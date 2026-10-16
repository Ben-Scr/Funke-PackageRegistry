using System;
using System.Collections.Generic;
using Funke.Ads;
using Xunit;

namespace Funke.Ads.Tests;

internal sealed class RecordingBackend : IAdsBackend
{
    public readonly List<string> Calls = new();
    public AdsConfig? Config;

    public void Initialize(AdsConfig config) { Config = config; Calls.Add("initialize"); }
    public void ShowBanner(string adUnitId, BannerPosition position, BannerSize size) => Calls.Add($"banner {adUnitId} {position} {size}");
    public void HideBanner() => Calls.Add("hideBanner");
    public void Load(AdFormat format, string adUnitId) => Calls.Add($"load {format} {adUnitId}");
    public void Show(AdFormat format) => Calls.Add($"show {format}");
    public void ShowPrivacyOptions() => Calls.Add("privacy");
    public void ResetConsent() => Calls.Add("resetConsent");
    public void SetMuted(bool muted) => Calls.Add($"muted {muted}");
    public void OpenAdInspector() => Calls.Add("inspector");
    public void Dispose() { }
}

// Ads is static: run these one at a time.
[CollectionDefinition("Ads", DisableParallelization = true)]
public class AdsCollection { }

[Collection("Ads")]
public class AdsTests : IDisposable
{
    private const string Interstitial = "ca-app-pub-1234567890123456/1111111111";
    private const string Rewarded = "ca-app-pub-1234567890123456/2222222222";
    private readonly RecordingBackend m_Backend = new();

    public void Dispose() => Ads.Reset();

    private void Start(AdsConfig? config = null, bool testAds = false)
        => Ads.Start(m_Backend, config ?? new AdsConfig(), testAds);

    private static void Send(string json) => AndroidBridge.Simulate(Ads.Channel, json);

    [Fact]
    public void Requests_wait_for_the_sdk_and_then_run_in_order()
    {
        bool initialized = false;
        void OnInitialized() => initialized = true;
        Ads.Initialized += OnInitialized;
        try
        {
            Start();
            Ads.LoadInterstitial(Interstitial);
            Ads.ShowBanner("ca-app-pub-1234567890123456/3333333333", BannerPosition.Top);
            Assert.Equal(new[] { "initialize" }, m_Backend.Calls);

            Send("{\"event\":\"consent\",\"status\":3,\"canRequestAds\":true,\"privacyOptionsRequired\":true}");
            Assert.Equal(new ConsentInfo(ConsentStatus.Obtained, true, true), Ads.Consent);

            Send("{\"event\":\"initialized\"}");
            Assert.True(initialized);
            Assert.True(Ads.IsInitialized);
            Assert.Equal(new[] { "initialize", $"load Interstitial {Interstitial}",
                "banner ca-app-pub-1234567890123456/3333333333 Top Adaptive" }, m_Backend.Calls);
        }
        finally { Ads.Initialized -= OnInitialized; }
    }

    [Fact]
    public void Test_ads_replace_the_games_ad_units()
    {
        Start(testAds: true);
        Send("{\"event\":\"initialized\"}");
        Ads.LoadRewarded(Rewarded);
        Assert.Contains($"load Rewarded {TestAdUnits.Rewarded}", m_Backend.Calls);
        Assert.True(Ads.IsUsingTestAds);
    }

    [Fact]
    public void Interstitial_closes_runs_the_callback_and_loads_the_next_one()
    {
        Start();
        Send("{\"event\":\"initialized\"}");
        Assert.False(Ads.ShowInterstitial());

        Ads.LoadInterstitial(Interstitial);
        Send("{\"event\":\"interstitialLoaded\"}");
        Assert.True(Ads.IsInterstitialReady);

        int closed = 0;
        Assert.True(Ads.ShowInterstitial(() => closed++));
        Assert.True(Ads.IsShowingFullScreenAd);
        Assert.False(Ads.IsInterstitialReady);
        Send("{\"event\":\"interstitialShown\"}");
        Send("{\"event\":\"interstitialClosed\"}");
        Send("{\"event\":\"interstitialClosed\"}");

        Assert.Equal(1, closed);
        Assert.False(Ads.IsShowingFullScreenAd);
        Assert.Equal(2, m_Backend.Calls.FindAll(c => c == $"load Interstitial {Interstitial}").Count);
    }

    [Fact]
    public void Cooldown_blocks_interstitials_shown_too_soon()
    {
        Start(new AdsConfig { InterstitialCooldownSeconds = 60 });
        Send("{\"event\":\"initialized\"}");
        Ads.LoadInterstitial(Interstitial);
        Send("{\"event\":\"interstitialLoaded\"}");
        Assert.True(Ads.ShowInterstitial());
        Send("{\"event\":\"interstitialClosed\"}");
        Send("{\"event\":\"interstitialLoaded\"}");
        Assert.True(Ads.IsInterstitialReady);
        Assert.False(Ads.ShowInterstitial());
    }

    [Fact]
    public void Rewarded_grants_the_reward_before_closing_and_only_once()
    {
        Start();
        Send("{\"event\":\"initialized\"}");
        Ads.LoadRewarded(Rewarded);
        Send("{\"event\":\"rewardedLoaded\"}");

        var order = new List<string>();
        Assert.True(Ads.ShowRewarded(r => order.Add($"reward {r.Type} {r.Amount}"), () => order.Add("closed")));
        Send("{\"event\":\"rewardEarned\",\"type\":\"coins\",\"amount\":50}");
        Send("{\"event\":\"rewardEarned\",\"type\":\"coins\",\"amount\":50}");
        Send("{\"event\":\"rewardedClosed\"}");
        Assert.Equal(new[] { "reward coins 50", "closed" }, order);
    }

    [Fact]
    public void Failures_are_reported_with_googles_error_code()
    {
        Start(new AdsConfig { AutoReload = false });
        Send("{\"event\":\"initialized\"}");
        AdError? error = null;
        void OnFailed(AdFormat format, AdError e) { if (format == AdFormat.Rewarded) error = e; }
        Ads.FailedToLoad += OnFailed;
        try
        {
            Ads.LoadRewarded(Rewarded);
            Send("{\"event\":\"rewardedFailed\",\"code\":3,\"message\":\"No fill.\"}");
            Assert.Equal(new AdError(3, "No fill."), error);
            Assert.False(Ads.IsRewardedReady);
            Assert.False(Ads.ShowRewarded(_ => { }));
        }
        finally { Ads.FailedToLoad -= OnFailed; }
    }

    [Fact]
    public void A_show_that_fails_still_ends_the_full_screen_state()
    {
        Start(new AdsConfig { AutoReload = false });
        Send("{\"event\":\"initialized\"}");
        Ads.LoadInterstitial(Interstitial);
        Send("{\"event\":\"interstitialLoaded\"}");
        bool closed = false;
        Ads.ShowInterstitial(() => closed = true);
        Send("{\"event\":\"interstitialShowFailed\",\"code\":-1,\"message\":\"no activity\"}");
        Assert.True(closed);
        Assert.False(Ads.IsShowingFullScreenAd);
    }

    [Fact]
    public void Banner_and_revenue_events_update_state()
    {
        Start();
        Send("{\"event\":\"initialized\"}");
        Ads.ShowBanner("ca-app-pub-1234567890123456/3333333333");
        Send("{\"event\":\"bannerLoaded\",\"height\":147}");
        Assert.True(Ads.IsBannerVisible);
        Assert.Equal(147f, Ads.BannerHeight);

        AdRevenue? revenue = null;
        void OnPaid(AdRevenue r) => revenue = r;
        Ads.PaidEvent += OnPaid;
        try
        {
            Send("{\"event\":\"paid\",\"format\":\"rewarded\",\"valueMicros\":1500,\"currency\":\"EUR\",\"precision\":2}");
            Assert.Equal(new AdRevenue(AdFormat.Rewarded, 1500, "EUR", 2), revenue);
            Assert.Equal(0.0015, revenue!.Value.Value, 6);
        }
        finally { Ads.PaidEvent -= OnPaid; }

        Ads.HideBanner();
        Assert.False(Ads.IsBannerVisible);
        Assert.Equal(0f, Ads.BannerHeight);
        Send("{\"event\":\"bannerLoaded\",\"height\":147}");
        Assert.False(Ads.IsBannerVisible);
    }

    [Fact]
    public void Calls_before_initialize_explain_themselves_and_bad_ids_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Ads.LoadInterstitial(Interstitial));
        Start();
        Assert.Throws<ArgumentException>(() => Ads.LoadInterstitial(" "));
        Assert.Throws<ArgumentNullException>(() => Ads.ShowRewarded(null!));
        Send("not json");
    }

    [Fact]
    public void Config_is_copied_and_reset_forgets_everything()
    {
        var config = new AdsConfig { MuteAds = true };
        config.TestDeviceIds.Add("ABC");
        Start(config);
        config.TestDeviceIds.Add("changed later");
        Assert.Equal(new[] { "ABC" }, m_Backend.Config!.TestDeviceIds);

        Send("{\"event\":\"initialized\"}");
        Assert.Contains("muted True", m_Backend.Calls);
        Ads.Reset();
        Assert.False(Ads.IsInitialized);
        Send("{\"event\":\"initialized\"}");
        Assert.False(Ads.IsInitialized);
    }
}
