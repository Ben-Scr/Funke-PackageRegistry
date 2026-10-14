# Index.Tilemap2D

`Index.Tilemap2D` provides a chunked sparse tile store, scene/prefab
serialization, C++ and C# APIs, an inspector, an editor grid gizmo, and active
Renderer2D rendering with chunk culling.

Rendering is automatic after the native package loads. The package registers a
Renderer2D instance contributor in `IndexPackage_OnLoad`; a scene does not need
to register `TilemapRenderSystem`. That system remains available only for its
last-frame diagnostic counters.

## Installation and native compatibility

Install the package from the editor's Package Manager. Installation places the
manifest and sources under:

```text
<project>/Packages/Index.Tilemap2D/
```

It also adds `Index.Tilemap2D` to the `packages` array in `project.json` and
regenerates the isolated package workspace. `project.json` is the project file;
the older `index-project.json` name is not used.

The package is source-first because its `native` layer consumes the engine's
C++ API. The editor rebuilds it against the active engine instead of treating a
registry DLL as permanently compatible. Project-local build output is written
under:

```text
<project>/.index/build/packages/bin/<Configuration>-<platform>-x86_64/
```

Every native build also writes
`Pkg.Index.Tilemap2D.Native.dll.indexabi` beside the DLL. The sidecar records the
SHA-256 of the exact `Index-Engine.dll` used for that build. The runtime rejects
an engine-linked prebuilt when this metadata is missing or its hash differs, so
recompiling the engine cannot silently load an incompatible Tilemap DLL.

For a manual source install, copy this folder into the project's `Packages/`
directory, add the allow-list entry, then generate the package workspace with
the engine's dedicated generator:

```powershell
C:\path\to\Index\vendor\bin\premake5.exe `
  --file=C:\path\to\Index\premake\project-packages.lua `
  vs2022 `
  --index-project=C:\absolute\path\to\MyProject
```

Opening or rebuilding the project in the editor handles the same generation and
build flow automatically.

## Add the component

Select an entity with `Transform2D`, choose **Add Component**, then select
**Rendering > Tilemap 2D**. The inspector exposes:

- `Color`: tint multiplied with each tile's color.
- `Tile Anchor`: local-space offset of the grid.
- `Cell Size`: rendered size of each tile.
- `Cell Space`: extra spacing between cell centres.
- `Tileset`: fallback texture for tiles whose texture GUID is zero.

Painting is currently API-driven; the inspector does not yet provide a brush
tool. The selected entity's viewport gizmo visualizes occupied cells and the
grid around them.

## C# example

```csharp
using System;
using Index;
using Index.Tilemap2D;

public sealed class RoomSpawner : EntityScript
{
    [ShowInEditor] private Texture? floorTexture;

    public override void OnStart()
    {
        Tilemap2D? tilemap = GetComponent<Tilemap2D>();
        if (tilemap is null)
            return;

        tilemap.Tileset = floorTexture;
        Tile floor = new(floorTexture?.UUID ?? 0, Color.White);

        tilemap.SetTile(new Vector2Int(0, 0), floor);

        Span<Vector2Int> cells = stackalloc Vector2Int[16];
        for (int x = 0; x < cells.Length; ++x)
            cells[x] = new Vector2Int(x, 1);
        tilemap.SetTiles(cells, floor);

        Vector2Int selectedCell = tilemap.WorldToCell(CameraUtility.GetMousePosition2D());
        tilemap.ClearTile(selectedCell);
    }
}
```

`Tilemap2D` is a thin wrapper over the native component. Reads, writes, batch
operations, color, geometry, and texture changes go through P/Invoke; there is
no duplicate managed tile store.

## C++ example

```cpp
#include "Scene/Entity.hpp"
#include "Tilemap2DComponent.hpp"

void FillFloor(Index::Entity entity) {
    auto& tilemap = entity.GetComponent<Index::Tilemap2D::Tilemap2DComponent>();

    Index::Tilemap2D::Tile floor{};
    floor.Texture = tilemap.TilesetTexture;
    floor.TileColor = Index::Color::White();

    for (int x = 0; x < 16; ++x)
        tilemap.SetTile({ x, 0 }, floor);
}
```

The package header is available to native package code through the manifest's
`include` directory. A standalone C++ application must add that directory to
its includes and link/load the package through the same engine configuration;
including the header alone does not make an engine-linked package ABI-stable.

## Serialization and rendering

Tilemaps round-trip through `.scene` and `.prefab` serialization under the
serialized component name `Tilemap2D`. Storage is split into 16 x 16 chunks;
empty chunks are removed.

The renderer visits occupied bits only, performs chunk-level viewport culling,
uses each tile's texture or the tilemap fallback texture, and multiplies the
per-tile color by the tilemap-wide tint. `Transform2D` position, scale, and
rotation affect the grid.

## Troubleshooting

| Symptom | Check |
|---|---|
| `Tilemap 2D` is absent from Add Component | Confirm `project.json` lists `Index.Tilemap2D`, then let the editor regenerate and rebuild project packages. |
| The native DLL is skipped as incompatible | Rebuild the package against the active `Index-Engine.dll`; do not copy or edit `.indexabi` manually. |
| Tiles do not render | Confirm the entity has `Transform2D`, non-zero `Cell Size`, and either a per-tile texture or `Tileset`. No scene system registration is required. |
| Painted data disappears after reload | Inspect the entity's `Tilemap2D` value in the saved scene and check package load/build diagnostics. |
| A C# call reports `DllNotFoundException` | Confirm both managed and native package projects built for the current configuration and the native ABI sidecar is present. |

## Source map

- `index-package.lua`: canonical `native` plus `csharp` layer manifest.
- `include/Tilemap2DComponent.hpp`: storage and geometry contract.
- `src/PackageEntry.cpp`: component, serializer, gizmo, conflicts, and renderer registration.
- `src/Tilemap2DSerialization.cpp`: scene/prefab payload conversion.
- `csharp/Tilemap2D.cs`: managed component API.
