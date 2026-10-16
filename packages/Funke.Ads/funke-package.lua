-- Funke.Ads — Google AdMob ads (banner, interstitial, rewarded) and Google's GDPR consent form for Android games.
-- C# API in Source/, the Java plugin and the AdMob / UMP dependencies in Android/. The editor and desktop
-- Development builds simulate ads with placeholder overlays.
--
-- The AdMob app ID comes from the Android build profile option `admobAppId`; without it the build uses Google's
-- sample app ID, which only serves test ads.

return {
    name        = "Funke.Ads",
    version     = "1.0.0",
    description = "Google AdMob ads for Android: banners, interstitials, rewarded ads and the GDPR consent form; simulated in the editor.",
    engine      = ">=2026.1.0",

    layers = {
        csharp = {
            sources = { "Source/**.cs" },
        },
    },

    android = {
        java     = { "Android/Java/**.java" },
        maven    = {
            "com.google.android.gms:play-services-ads:25.5.0",
            "com.google.android.ump:user-messaging-platform:4.0.0",
        },
        manifest = "Android/AndroidManifest.xml",
        placeholders = {
            admobAppId = "ca-app-pub-3940256099942544~3347511713",
        },
    },

    dependencies = {},
}
