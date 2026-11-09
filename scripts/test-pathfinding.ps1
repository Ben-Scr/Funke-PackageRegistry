[CmdletBinding()]
param(
    [string] $EngineRoot = (Join-Path $PSScriptRoot '../../Index'),
    [string] $OutputRoot = (Join-Path $PSScriptRoot '../dist/pathfinding-validation')
)
$ErrorActionPreference = 'Stop'
$engine = (Resolve-Path -LiteralPath $EngineRoot).Path
$registry = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
$marker = Join-Path $output '.pathfinding-validation'
if ((Test-Path -LiteralPath $output) -and !(Test-Path -LiteralPath $marker)) {
    throw 'Choose a new output directory or an existing pathfinding validation directory.'
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
Set-Content -LiteralPath $marker -Value 'Funke.Pathfinding validation output'
function Invoke-Checked([string] $Executable, [string[]] $Arguments, [string] $LogName) {
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $output $LogName)
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit $LASTEXITCODE; see $LogName" }
}
$native = Join-Path $output 'native'
Invoke-Checked 'cmake' @('-S', "$registry/packages/Funke.Pathfinding/Tests", '-B', $native, '-G', 'Visual Studio 17 2022', '-A', 'x64') 'configure.log'
Invoke-Checked 'cmake' @('--build', $native, '--config', 'Release', '--parallel', '4') 'build.log'
Invoke-Checked "$native/Release/PathfindingTests.exe" @() 'native-tests.log'
foreach ($repeat in 1..3) {
    Invoke-Checked "$native/Release/PathfindingBenchmark.exe" @("$output/raw-$repeat.csv") "benchmark-$repeat.csv"
}
$fixture = Join-Path $output 'fixture'
New-Item -ItemType Directory -Force -Path "$fixture/Packages" | Out-Null
@{ name = 'PathfindingValidation'; entityBits = 20; packages = @('Funke.Pathfinding3D'); modules = @('Funke.3D', 'Funke.Physics3D') } |
    ConvertTo-Json | Set-Content -LiteralPath "$fixture/project.json"
foreach ($name in @('Funke.Pathfinding', 'Funke.Pathfinding3D')) {
    New-Item -ItemType Directory -Force -Path "$fixture/Packages/$name" | Out-Null
    Copy-Item -Path "$registry/packages/$name/*" -Destination "$fixture/Packages/$name" -Recurse -Force
}
Invoke-Checked 'python' @("$registry/tests/Funke.Pathfinding.Tests/check_packages.py", $engine, $fixture, $output) 'static-exports.log'
Invoke-Checked "$engine/vendor/bin/premake5.exe" @("--file=$engine/premake/project-packages.lua", "--funke-project=$fixture", 'vs2022') 'premake.log'
$workspace = "$fixture/.funke/build/packages"
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products * -version '[17.0,18.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio 2022 MSBuild is required.' }
Invoke-Checked $msbuild @("$workspace/generated/Pkg.Funke.Pathfinding.Native/Pkg.Funke.Pathfinding.Native.vcxproj", '/t:Build', '/m:1', '/p:Configuration=Release', '/p:Platform=x64', '/nr:false', '/v:minimal', '/nologo') 'package-native.log'
Invoke-Checked 'dotnet' @('build', "$workspace/generated/Pkg.Funke.Pathfinding3D/Pkg.Funke.Pathfinding3D.csproj", '-c', 'Release', '-p:Platform=x64', '--nologo', '-v', 'minimal') 'package-managed.log'
$packageBin = "$workspace/bin/Release-windows-x86_64"
$testProject = "$registry/tests/Funke.Pathfinding.PackageTests/Funke.Pathfinding.PackageTests.csproj"
Invoke-Checked 'dotnet' @('build', $testProject, '-c', 'Release', "-p:FunkeEngineRoot=$engine", "-p:PathfindingPackageBin=$packageBin", '--nologo', '-v', 'minimal') 'integration-build.log'
Invoke-Checked 'dotnet' @("$registry/tests/Funke.Pathfinding.PackageTests/bin/Release/net10.0/Funke.Pathfinding.PackageTests.dll", "$packageBin/Pkg.Funke.Pathfinding.Native/Pkg.Funke.Pathfinding.Native.dll") 'integration.log'
$sourceFiles = foreach ($name in @('Funke.Pathfinding', 'Funke.Pathfinding3D')) {
    Get-ChildItem -LiteralPath "$registry/packages/$name" -Recurse -File | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($registry, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
}
@{
    recordedAt = [DateTime]::UtcNow.ToString('o'); platform = [Environment]::OSVersion.VersionString
    cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)
    configuration = 'Release'; seed = 20261109; warmups = 20; queriesPerWorkload = 200; repetitions = 3
    timingScope = 'Native Begin and Step until completion; map/scratch construction and path copy excluded'
    sourceFiles = @($sourceFiles)
    nativeDllSha256 = (Get-FileHash -LiteralPath "$packageBin/Pkg.Funke.Pathfinding.Native/Pkg.Funke.Pathfinding.Native.dll" -Algorithm SHA256).Hash
    benchmarkSha256 = (Get-FileHash -LiteralPath "$native/Release/PathfindingBenchmark.exe" -Algorithm SHA256).Hash
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output/metadata.json"
Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -match '\.(csv|log|json)$' -and $_.Name -ne 'hashes.json' } |
    Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash |
    ConvertTo-Json | Set-Content -LiteralPath "$output/hashes.json"
Write-Host "Pathfinding validation passed. Evidence: $output"
