# Creates a source-first Index package archive and prints its registry entry.
#
# By default Bin/ is omitted from the archive. The editor generates the
# project-local package workspace and rebuilds the package against the active
# engine after installation.
#
# -EngineRoot builds optional precompiled artifacts in an isolated temporary
# project and implies -IncludePrebuilt. A native artifact is publishable only
# with its adjacent <dll>.indexabi metadata file.
#
# -IncludePrebuilt includes already-built artifacts without rebuilding them.
# The script validates that every declared layer has its expected artifact and
# that a native DLL has a valid ABI sidecar.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PackageDir,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [string] $EngineRoot,
    [ValidateSet("Debug", "Release", "Dist")] [string] $Configuration = "Release",
    [switch] $IncludePrebuilt,
    [switch] $CreateRelease,
    [string] $RepoSlug = "Ben-Scr/Index-PackageRegistry"
)

$ErrorActionPreference = "Stop"

function Read-IndexAbiMetadata {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }

        $separator = $trimmed.IndexOf('=')
        if ($separator -le 0) {
            throw "Invalid ABI metadata line in '$Path': $trimmed"
        }

        $key = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        if ($values.ContainsKey($key)) {
            throw "Duplicate ABI metadata key '$key' in '$Path'."
        }
        $values[$key] = $value
    }
    return $values
}

$pkgDir = (Resolve-Path -LiteralPath $PackageDir).Path
if (Test-Path -LiteralPath $OutDir) {
    $outDirFull = (Resolve-Path -LiteralPath $OutDir).Path
} else {
    $outDirFull = (New-Item -ItemType Directory -Path $OutDir).FullName
}

$manifestPath = Join-Path $pkgDir "index-package.lua"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "No index-package.lua at '$manifestPath'. PackageDir must be the package folder itself."
}

$manifestText = Get-Content -LiteralPath $manifestPath -Raw
$nameMatch = [regex]::Match($manifestText, '\bname\s*=\s*"([^"]+)"')
$versionMatch = [regex]::Match($manifestText, '\bversion\s*=\s*"([^"]+)"')
if (-not $nameMatch.Success -or -not $versionMatch.Success) {
    throw "Could not parse name/version from '$manifestPath'."
}

$pkgName = $nameMatch.Groups[1].Value
$pkgVersion = $versionMatch.Groups[1].Value
$packageFolderName = Split-Path $pkgDir -Leaf
if ($packageFolderName -cne $pkgName) {
    throw "Package folder '$packageFolderName' must match manifest name '$pkgName'."
}

$layers = @()
$hasEngineNative = $manifestText -match '(?ms)(?:^|[\s,;{])(?:native|engine_core)\s*=\s*\{'
$hasStandaloneNative = $manifestText -match '(?ms)(?:^|[\s,;{])(?:native_standalone|standalone_cpp)\s*=\s*\{'
$hasCsharp = $manifestText -match '(?ms)(?:^|[\s,;{])csharp\s*=\s*\{'
if ($hasEngineNative -and $hasStandaloneNative) {
    throw "A package cannot combine native and native_standalone layers."
}
if ($hasEngineNative) { $layers += "native" }
if ($hasStandaloneNative) { $layers += "native_standalone" }
if ($hasCsharp) { $layers += "csharp" }
if ($layers.Count -eq 0) {
    throw "Manifest '$manifestPath' does not declare a supported package layer."
}

$hasNative = $hasEngineNative -or $hasStandaloneNative
$expectedBoundary = if ($hasEngineNative) { "engine_cpp" } else { "standalone_c" }
$includePrebuiltEffective = $IncludePrebuilt.IsPresent -or -not [string]::IsNullOrWhiteSpace($EngineRoot)
$builtEngineDll = $null

if ($EngineRoot) {
    $engineRootFull = (Resolve-Path -LiteralPath $EngineRoot).Path
    $premake = Join-Path $engineRootFull "vendor\bin\premake5.exe"
    $packageGenerator = Join-Path $engineRootFull "premake\project-packages.lua"
    if (-not (Test-Path -LiteralPath $premake)) {
        throw "premake5.exe not found at '$premake'."
    }
    if (-not (Test-Path -LiteralPath $packageGenerator)) {
        throw "Project package generator not found at '$packageGenerator'."
    }

    $msbuild = $null
    if ($hasNative) {
        $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (-not (Test-Path -LiteralPath $vswhere)) {
            $vswhere = "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
        }
        if (-not (Test-Path -LiteralPath $vswhere)) {
            throw "vswhere.exe was not found."
        }
        $msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if (-not $msbuild) {
            throw "MSBuild was not found via vswhere."
        }
    }
    if ($hasCsharp -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet was not found."
    }

    if ($hasEngineNative) {
        $builtEngineDll = Join-Path $engineRootFull "bin\$Configuration-windows-x86_64\Index-Engine\Index-Engine.dll"
        if (-not (Test-Path -LiteralPath $builtEngineDll)) {
            throw "The matching engine DLL must be built first: '$builtEngineDll'."
        }
    }

    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $tempProject = Join-Path $tempRoot ("IndexPackagePublish-" + [guid]::NewGuid().ToString("N"))
    Write-Host ""
    Write-Host "=== Isolated package build via $engineRootFull ===" -ForegroundColor Cyan

    try {
        $tempPackagesDir = Join-Path $tempProject "Packages"
        New-Item -ItemType Directory -Path $tempPackagesDir -Force | Out-Null
        Copy-Item -LiteralPath $pkgDir -Destination $tempPackagesDir -Recurse -Force

        $projectJson = [ordered]@{
            name = "RegistryPublish"
            packages = @($pkgName)
        } | ConvertTo-Json -Depth 3
        [IO.File]::WriteAllText(
            (Join-Path $tempProject "project.json"),
            $projectJson,
            [Text.UTF8Encoding]::new($false))

        Push-Location $engineRootFull
        try {
            & $premake "--file=$packageGenerator" vs2022 "--index-project=$tempProject"
            if ($LASTEXITCODE -ne 0) {
                throw "Package workspace generation failed (exit $LASTEXITCODE)."
            }
        } finally {
            Pop-Location
        }

        $generatedRoot = Join-Path $tempProject ".index\build\packages\generated"
        $projects = @()
        if ($hasNative) {
            $nativeProjectName = "Pkg.$pkgName.Native"
            $projects += [pscustomobject]@{
                Name = $nativeProjectName
                Path = Join-Path $generatedRoot "$nativeProjectName\$nativeProjectName.vcxproj"
                Native = $true
            }
        }
        if ($hasCsharp) {
            $managedProjectName = "Pkg.$pkgName"
            $projects += [pscustomobject]@{
                Name = $managedProjectName
                Path = Join-Path $generatedRoot "$managedProjectName\$managedProjectName.csproj"
                Native = $false
            }
        }

        foreach ($project in $projects) {
            if (-not (Test-Path -LiteralPath $project.Path)) {
                throw "Generated project not found: '$($project.Path)'."
            }
            Write-Host "Building $($project.Name) ($Configuration)..."
            if ($project.Native) {
                & $msbuild $project.Path "/p:Configuration=$Configuration" "/p:Platform=x64" `
                    /p:PreferredToolArchitecture=x64 /m:1 /nr:false /p:CL_MPCount=1 `
                    /p:UseMultiToolTask=false /nologo /v:minimal
            } else {
                & dotnet build $project.Path -c $Configuration -p:Platform=x64 `
                    --nologo -v minimal --nodeReuse:false -p:UseSharedCompilation=false
            }
            if ($LASTEXITCODE -ne 0) {
                throw "Build failed for $($project.Name) (exit $LASTEXITCODE)."
            }
        }

        $binDir = Join-Path $pkgDir "Bin\Windows-x64"
        New-Item -ItemType Directory -Path $binDir -Force | Out-Null
        $workspaceBin = Join-Path $tempProject ".index\build\packages\bin\$Configuration-windows-x86_64"
        foreach ($project in $projects) {
            $sourceDir = Join-Path $workspaceBin $project.Name
            foreach ($artifactName in @(
                "$($project.Name).dll",
                "$($project.Name).pdb",
                "$($project.Name).dll.indexabi"
            )) {
                $source = Join-Path $sourceDir $artifactName
                if (Test-Path -LiteralPath $source) {
                    Copy-Item -LiteralPath $source -Destination $binDir -Force
                    Write-Host "  $artifactName -> $binDir"
                }
            }
        }
    } finally {
        $resolvedTempProject = [IO.Path]::GetFullPath($tempProject)
        if ($resolvedTempProject.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
            (Split-Path $resolvedTempProject -Leaf).StartsWith("IndexPackagePublish-")) {
            Remove-Item -LiteralPath $resolvedTempProject -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$binDir = Join-Path $pkgDir "Bin\Windows-x64"
if ($includePrebuiltEffective) {
    $missing = @()
    if ($hasCsharp) {
        $managedDll = Join-Path $binDir "Pkg.$pkgName.dll"
        if (-not (Test-Path -LiteralPath $managedDll)) { $missing += $managedDll }
    }

    if ($hasNative) {
        $nativeDll = Join-Path $binDir "Pkg.$pkgName.Native.dll"
        $nativeAbi = "$nativeDll.indexabi"
        if (-not (Test-Path -LiteralPath $nativeDll)) { $missing += $nativeDll }
        if (-not (Test-Path -LiteralPath $nativeAbi)) { $missing += $nativeAbi }

        if ($missing.Count -eq 0) {
            $abi = Read-IndexAbiMetadata -Path $nativeAbi
            if ($abi["schema"] -ne "1") {
                throw "Unsupported native ABI metadata schema in '$nativeAbi'. Expected schema=1."
            }
            if ($abi["boundary"] -ne $expectedBoundary) {
                throw "Native ABI boundary in '$nativeAbi' is '$($abi['boundary'])'; expected '$expectedBoundary'."
            }
            if ($hasEngineNative) {
                $engineHash = $abi["engine_sha256"]
                if ($engineHash -notmatch '^[0-9a-f]{64}$') {
                    throw "Native ABI metadata '$nativeAbi' needs a lowercase 64-character engine_sha256."
                }
                if ($builtEngineDll) {
                    $expectedHash = (Get-FileHash -LiteralPath $builtEngineDll -Algorithm SHA256).Hash.ToLowerInvariant()
                    if ($engineHash -cne $expectedHash) {
                        throw "Native ABI metadata does not match '$builtEngineDll'."
                    }
                }
            }
        }
    }

    if ($missing.Count -gt 0) {
        Write-Host ""
        Write-Host "Missing prebuilt artifact(s):" -ForegroundColor Red
        $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Cannot include incomplete prebuilt package artifacts. Rebuild with -EngineRoot or publish source-first without -IncludePrebuilt."
    }
} else {
    Write-Host "Source-first archive: Bin/ artifacts are omitted and rebuilt after install." -ForegroundColor Cyan
}

$zipName = "$pkgName-$pkgVersion.zip"
$zipPath = Join-Path $outDirFull $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath }

Add-Type -Assembly System.IO.Compression
Add-Type -Assembly System.IO.Compression.FileSystem
$pkgDirFull = [IO.Path]::GetFullPath($pkgDir)
$binDirFull = [IO.Path]::GetFullPath($binDir).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$publishedPrebuiltNames = @(
    "Pkg.$pkgName.dll",
    "Pkg.$pkgName.pdb",
    "Pkg.$pkgName.Native.dll",
    "Pkg.$pkgName.Native.pdb",
    "Pkg.$pkgName.Native.dll.indexabi"
)
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $pkgDir -Recurse -File) {
        if ($file.FullName.StartsWith($binDirFull, [StringComparison]::OrdinalIgnoreCase)) {
            if (-not $includePrebuiltEffective -or
                $publishedPrebuiltNames -notcontains $file.Name) {
                continue
            }
        }

        $relativePath = $file.FullName.Substring($pkgDirFull.Length).TrimStart('\', '/').Replace('\', '/')
        $entryName = "$packageFolderName/$relativePath"
        $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        try {
            $fileStream = [IO.File]::OpenRead($file.FullName)
            try { $fileStream.CopyTo($entryStream) } finally { $fileStream.Dispose() }
        } finally {
            $entryStream.Dispose()
        }
    }
} finally {
    $archive.Dispose()
}

$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $zipPath).Length

Write-Host ""
Write-Host "Packaged: $zipPath" -ForegroundColor Green
Write-Host "SHA-256:  $sha"
Write-Host "Size:     $size bytes"

$tag = "$pkgName-$pkgVersion"
$downloadUrl = "https://github.com/$RepoSlug/releases/download/$tag/$zipName"
$entry = [pscustomobject]@{
    name = $pkgName
    version = $pkgVersion
    description = ""
    downloadUrl = $downloadUrl
    sha256 = $sha
    sizeBytes = $size
    layers = $layers
    dependencies = @()
}

Write-Host ""
Write-Host "index.json entry:" -ForegroundColor Cyan
$entry | ConvertTo-Json -Depth 4

if ($CreateRelease) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "gh CLI not found. Install it from https://cli.github.com to use -CreateRelease."
    }
    Write-Host ""
    Write-Host "Creating GitHub Release $tag on $RepoSlug..." -ForegroundColor Yellow
    & gh release create $tag $zipPath --repo $RepoSlug --title $tag --notes "Auto-published by publish-package.ps1"
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub release creation failed (exit $LASTEXITCODE)."
    }
}
