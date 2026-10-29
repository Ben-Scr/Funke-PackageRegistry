return {
    name        = "Funke.Lighting2D",
    version     = "0.1.0",
    description = "2D ambient and point lights with colored falloff, rectangular shadow occluders, editor controls and C# bindings.",
    engine      = ">=2026.1.0",
    modules     = { "Funke.2D" },
    layers = {
        native = {
            sources = { "src/**.cpp", "src/**.hpp", "include/**.hpp" },
            includes = { "include" },
        },
        csharp = {
            sources = { "Source/**.cs" },
            allow_unsafe = true,
            pinvoke_dll = "Pkg.Funke.Lighting2D.Native",
        },
    },
    dependencies = {},
}
