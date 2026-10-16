using System;
using System.Text.Json.Nodes;

namespace Funke.Ads;

/// <summary>Where ad calls go. Every backend reports through the same JSON events (see FunkeAds.java).</summary>
internal interface IAdsBackend : IDisposable
{
    void Initialize(AdsConfig config);
    void ShowBanner(string adUnitId, BannerPosition position, BannerSize size);
    void HideBanner();
    void Load(AdFormat format, string adUnitId);
    void Show(AdFormat format);
    void ShowPrivacyOptions();
    void ResetConsent();
    void SetMuted(bool muted);
    void OpenAdInspector();
}

/// <summary>Google Mobile Ads through the package's Java plugin (Android/Java/dev/funke/ads/FunkeAds.java).</summary>
internal sealed class AndroidAdsBackend : IAdsBackend
{
    private const string JavaClass = "dev.funke.ads.FunkeAds";
    private bool m_ReportedMissing;

    public void Initialize(AdsConfig config)
    {
        var options = new JsonObject
        {
            ["requestConsent"] = config.RequestConsent,
            ["muted"] = config.MuteAds,
            ["debugGeography"] = config.DebugGeography switch
            {
                ConsentDebugGeography.EEA => "eea",
                ConsentDebugGeography.NotEEA => "other",
                _ => "",
            },
        };
        var devices = new JsonArray();
        foreach (string id in config.TestDeviceIds) devices.Add(id);
        options["testDeviceIds"] = devices;
        if (config.TagForChildDirectedTreatment is bool child) options["childDirected"] = child;
        if (config.TagForUnderAgeOfConsent is bool underAge) options["underAgeOfConsent"] = underAge;
        if (config.MaxAdContentRating != MaxAdContentRating.Unspecified) options["maxAdContentRating"] = config.MaxAdContentRating.ToString();
        Call("initialize", options, "initializeFailed");
    }

    public void ShowBanner(string adUnitId, BannerPosition position, BannerSize size)
    {
        string sizeName = size switch
        {
            BannerSize.Standard => "banner",
            BannerSize.Large => "large",
            BannerSize.MediumRectangle => "mediumRectangle",
            BannerSize.Leaderboard => "leaderboard",
            _ => "adaptive",
        };
        Call("showBanner", new JsonObject
        {
            ["adUnitId"] = adUnitId,
            ["position"] = position == BannerPosition.Top ? "top" : "bottom",
            ["size"] = sizeName,
        }, "bannerFailed");
    }

    public void HideBanner() => Call("hideBanner", new JsonObject(), null);

    public void Load(AdFormat format, string adUnitId)
        => Call(format == AdFormat.Interstitial ? "loadInterstitial" : "loadRewarded", new JsonObject { ["adUnitId"] = adUnitId },
            format == AdFormat.Interstitial ? "interstitialFailed" : "rewardedFailed");

    public void Show(AdFormat format)
        => Call(format == AdFormat.Interstitial ? "showInterstitial" : "showRewarded", new JsonObject(),
            format == AdFormat.Interstitial ? "interstitialShowFailed" : "rewardedShowFailed");

    public void ShowPrivacyOptions() => Call("showPrivacyOptions", new JsonObject(), null);
    public void ResetConsent() => Call("resetConsent", new JsonObject(), null);
    public void SetMuted(bool muted) => Call("setMuted", new JsonObject { ["muted"] = muted }, null);
    public void OpenAdInspector() => Call("openAdInspector", new JsonObject(), null);

    public void Dispose() { }

    private void Call(string method, JsonObject argument, string? failureEvent)
    {
        if (AndroidBridge.TryCall(JavaClass, method, argument.ToJsonString(), out _)) return;
        if (!m_ReportedMissing)
        {
            m_ReportedMissing = true;
            Log.Error("[Ads] The Java side of Funke.Ads is missing from the APK or failed (see logcat, tag Funke). "
                      + "Rebuild the Android player with the package installed.");
        }
        if (failureEvent != null)
            SimulatedAdsBackend.PostNextFrame(new JsonObject { ["event"] = failureEvent, ["code"] = -1, ["message"] = $"{method} failed" }, () => true);
    }
}

/// <summary>iOS, Web and desktop Release builds: ads never load, so games fall back to their no-ad path.</summary>
internal sealed class UnsupportedAdsBackend : IAdsBackend
{
    public void Initialize(AdsConfig config) => Log.Info($"[Ads] Ads are not available on {Engine.Platform}; calls are ignored.");
    public void ShowBanner(string adUnitId, BannerPosition position, BannerSize size) => Fail("bannerFailed");
    public void HideBanner() { }
    public void Load(AdFormat format, string adUnitId) => Fail(format == AdFormat.Interstitial ? "interstitialFailed" : "rewardedFailed");
    public void Show(AdFormat format) => Fail(format == AdFormat.Interstitial ? "interstitialShowFailed" : "rewardedShowFailed");
    public void ShowPrivacyOptions() { }
    public void ResetConsent() { }
    public void SetMuted(bool muted) { }
    public void OpenAdInspector() { }
    public void Dispose() { }

    private static void Fail(string failureEvent)
        => SimulatedAdsBackend.PostNextFrame(new JsonObject
        {
            ["event"] = failureEvent,
            ["code"] = -1,
            ["message"] = $"Ads are not supported on {Engine.Platform}",
        }, () => true);
}
