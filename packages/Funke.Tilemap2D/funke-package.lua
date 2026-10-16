-- Funke.Tilemap2D — chunked sparse 2D tilemap component, system, and editor inspector.
--
-- Two-layer package:
--   • native (C++) — Pkg.Funke.Tilemap2D.Native:
--       - Tile / Tilemap2DComponent (POD, chunked sparse storage)
--       - TilemapRenderSystem hooked into the active 2D draw path
--       - Properties::Make-driven inspector via the unified PropertyDrawer
--         (no per-package ImGui inspector code — auto-drawer renders every
--         field declared in the properties vector)
--       - extern "C" P/Invoke surface for the csharp layer
--   • csharp — Pkg.Funke.Tilemap2D:
--       - Idiomatic C# binding (Tile struct, Tilemap2DComponent class)
--       - Talks to Pkg.Funke.Tilemap2D.Native via DllImport (declared with
--         the loader's `pinvoke_dll` field, never as a project link).
--
-- The `native` layer depends on Funke-Engine implicitly (every `native` layer
-- links Funke-Engine via the loader's EditorRuntimeCommon dependency set),
-- so we don't list it in `dependencies` — that field is reserved for inter-
-- package deps.

return {
    name = "Funke.Tilemap2D",
    version = "0.2.0",
    description = "2D tilemap component (chunked sparse storage), render system, and editor inspector.",

    layers = {
        native = {
            sources = {
                "src/**.cpp",
                "src/**.hpp",
                "include/**.hpp",
            },
            includes = {
                "include",
            },
        },

        csharp = {
            sources = {
                "csharp/**.cs",
            },
            allow_unsafe = true, -- ReadOnlySpan<T> -> T* fixed pinning for SetTiles
            pinvoke_dll = "Pkg.Funke.Tilemap2D.Native",
        },
    },

    dependencies = {},
}
