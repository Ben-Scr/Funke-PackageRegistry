-- Funke.Networking — multiplayer for every Funke platform. Desktop, Android and iOS talk UDP (LiteNetLib 2.1.4,
-- vendored under Source/ThirdParty), browsers join over WebSocket; a host or dedicated server accepts both at once.
-- Everything runs on the main thread, polled once per frame.
--
-- Android: the manifest adds the INTERNET permission sockets need.

return {
    name        = "Funke.Networking",
    version     = "1.0.0",
    description = "Multiplayer for every platform: host/server/client over UDP and WebSocket (browsers), typed messages, LAN discovery, networked objects and interpolated transforms.",
    engine      = ">=2026.1.0",

    layers = {
        csharp = {
            sources      = { "Source/**.cs" },
            allow_unsafe = true,
        },
    },

    android = {
        manifest = "Android/AndroidManifest.xml",
    },

    dependencies = {},
}
