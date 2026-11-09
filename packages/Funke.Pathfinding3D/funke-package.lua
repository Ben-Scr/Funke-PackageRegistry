return {
    name        = "Funke.Pathfinding3D",
    version     = "0.1.0",
    description = "Ground and flying navigation with world-space paths and physics sampling.",
    engine      = ">=2026.1.0",
    modules     = { "Funke.3D", "Funke.Physics3D" },

    layers = {
        csharp = {
            sources = { "Source/**.cs" },
        },
    },

    dependencies = { "Funke.Pathfinding@^0.1.0" },
}
