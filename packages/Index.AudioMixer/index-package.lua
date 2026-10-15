return {
    name        = "Index.AudioMixer",
    version     = "1.0.0",
    description = "Volume groups (Master, Music, SFX and your own) with mute, fades, snapshots, saved settings and slider binding.",
    engine      = ">=2026.1.0",

    layers = {
        csharp = {
            sources = { "Source/**.cs" },
        },
    },
}
