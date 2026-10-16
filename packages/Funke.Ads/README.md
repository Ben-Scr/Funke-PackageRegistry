# Funke.Ads

Google AdMob ads for Android games: banners, interstitials and rewarded ads, plus Google's GDPR consent form
(User Messaging Platform). In the editor and in desktop Development builds the ads are simulated with placeholder
overlays and the same events, so the game's flow can be tested without a phone. iOS, Web and desktop Release builds
show no ads; loads fail there, so games take their no-ad path.

## Install

Package Manager → **Funke.Ads** → Install, or `funke package add Funke.Ads`. C# only on the game side; the Java
plugin and the Google libraries are added to Android builds automatically (the first Android build downloads them
once from Google's Maven repository, pinned in `Android/maven.lock.json`).

## Set up AdMob

1. Create an AdMob account, add your app (Android) and create ad units (Banner, Interstitial, Rewarded).
2. Put the app ID (`ca-app-pub-…~…`) into the Android build profile:

   ```json
   "platformOptions": { "admobAppId": "ca-app-pub-1234567890123456~1234567890" }
   ```

   Without it the build uses Google's sample app ID, which only serves test ads (Release builds warn).
3. Use your ad unit IDs (`ca-app-pub-…/…`) in code. Development builds and the editor replace them with Google's
   test units automatically (`AdsConfig.UseTestAds`), so you never click your own live ads. Release builds show real
   ads.

## Use

```csharp
using Funke;
using Funke.Ads;

public class AdsSetup : EntityScript
{
    const string Interstitial = "ca-app-pub-1234567890123456/1111111111";
    const string Rewarded     = "ca-app-pub-1234567890123456/2222222222";

    public override void OnStart()
    {
        Ads.Initialized += () =>
        {
            Ads.LoadInterstitial(Interstitial);
            Ads.LoadRewarded(Rewarded);
        };
        Ads.Initialize(new AdsConfig { InterstitialCooldownSeconds = 60 });
    }
}

// Between levels:
Ads.ShowInterstitial(onClosed: () => LoadNextLevel());

// "Watch an ad for 50 coins" button (show it only while Ads.IsRewardedReady):
Ads.ShowRewarded(reward => coins += 50, onClosed: ResumeGame);

// Banner:
Ads.ShowBanner("ca-app-pub-1234567890123456/3333333333", BannerPosition.Bottom);
Ads.HideBanner();
```

- **Loading:** after an interstitial or rewarded ad closes the next one loads by itself; failed loads retry with
  back-off (`AdsConfig.AutoReload`). `Ads.Loaded` / `Ads.FailedToLoad` report it; error code 3 means "no fill"
  (no ad available right now), which is normal for new ad units in the first hours.
- **Pausing:** `Ads.FullScreenAdShown` / `Ads.FullScreenAdClosed` (and `Ads.IsShowingFullScreenAd`) tell you when to
  pause and resume gameplay and music.
- **Banner height:** `Ads.BannerHeight` (screen pixels) after `Ads.BannerLoaded`, to keep UI clear of the banner.
- **Revenue:** `Ads.PaidEvent` reports the estimated value of each impression for analytics.
- **Muting:** `Ads.SetMuted(true)` mutes video ads, e.g. when the game's sound is off.

## Consent (GDPR) and privacy

`Ads.Initialize()` shows Google's consent form where the law requires it (EEA, UK, Switzerland) before it starts the
SDK; elsewhere it starts right away. Create the form once in AdMob under *Privacy & messaging → GDPR*.

- Players must be able to change their choice: show a "Privacy settings" button while
  `Ads.Consent.PrivacyOptionsRequired` is true and call `Ads.ShowPrivacyOptions()`.
- Test the form outside the EEA with `new AdsConfig { DebugGeography = ConsentDebugGeography.EEA }` (on a phone, also
  add its hashed ID to `TestDeviceIds`; logcat prints it) and reset it with `Ads.ResetConsent()`.
- Games for children: `TagForChildDirectedTreatment = true`, `MaxAdContentRating = MaxAdContentRating.G`.

## Publishing on Google Play

- Build the Release profile: the Android builder writes `<Game>.aab` (upload this) next to `<Game>.apk` (for testing
  on a phone). Sign it with your own keystore (`keystore`, `keyAlias`, `keystorePassword` in the profile).
- Play Console → *App content*: declare **Contains ads**, the **Advertising ID** use (AdMob uses it; the permission is
  in the manifest), and fill in the **Data safety** form (AdMob collects device identifiers and diagnostics).
- Add an `app-ads.txt` to your developer website (AdMob → Apps → app-ads.txt) so advertisers buy your inventory.

## Ad Inspector

`Ads.OpenAdInspector()` opens Google's on-device inspector on a test device to check ad units and mediation.

## Versions

Google Mobile Ads SDK 25.5.0 and User Messaging Platform 4.0.0. To update them, change `android.maven` in
`funke-package.lua` and regenerate the lock with
`python <engine>/platforms/Funke.Platform.Android/Builder/android_maven.py lock <this folder>`.
