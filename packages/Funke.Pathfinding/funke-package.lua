return {
    name        = "Funke.Pathfinding",
    version     = "0.1.0",
    description = "Native incremental A-star for weighted grids and waypoint graphs.",
    engine      = ">=2026.1.0",

    layers = {
        native_standalone = {
            sources = {
                "src/**.cpp",
                "include/**.hpp",
            },
            includes = { "include" },
        },

        csharp = {
            sources      = { "Source/**.cs" },
            allow_unsafe = true,
            pinvoke_dll  = "Pkg.Funke.Pathfinding.Native",
        },
    },

    dependencies = {},
}
