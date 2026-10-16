using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Funke.Ads;

/// <summary>
/// Google AdMob ads: banners, interstitials and rewarded ads, plus Google's GDPR consent form. Real ads run in Android
/// builds; the editor and desktop Development builds simulate them (placeholder overlays, same events) so the game's
/// flow can be tested without a phone. Everything is called and reported on the main thread.
/// </summary>
/// <example>
/// <code>
/// Ads.Initialize();                                   // consent form where required, then the SDK
/// Ads.LoadRewarded("ca-app-pub-1234567890123456/1234567890");
/// if (Ads.IsRewardedReady)
///     Ads.ShowRewarded(reward => coins += reward.Amount);
/// </code>
/// </example>
public static class Ads
{
    internal const string Channel = "Funke.Ads";

    private sealed class FullScreenSlot
    {
        public readonly AdFormat Format;
        public string? AdUnitId;
        public bool Loading, Ready, Showing;
        public int Failures;
        public int Generation;
        public Action? OnClosed;
        public Action<AdReward>? OnReward;

        public FullScreenSlot(AdFormat format) => Format = format;
    }

    private static IAdsBackend? s_Backend;
    private static AdsConfig s_Config = new();
    private static bool s_Initializing;
    private static readonly List<Action> s_AfterInitialize = new();
    private static readonly FullScreenSlot s_Interstitial = new(AdFormat.Interstitial);
    private static readonly FullScreenSlot s_Rewarded = new(AdFormat.Rewarded);
    private static long s_LastInterstitialClosedMs = -1;
    private static string? s_BannerUnitId;

    /// <summary>True once the SDK has started (after consent); ads can load from now on.</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>False where ads cannot run: iOS, Web, and desktop Release builds. Calls are then ignored and loads fail.</summary>
    public static bool IsSupported => s_Backend is not UnsupportedAdsBackend;

    /// <summary>True in the editor and desktop Development builds, where ads are placeholders.</summary>
    public static bool IsSimulated => s_Backend is SimulatedAdsBackend;

    /// <summary>True when Google's test ad units replace the game's (see <see cref="AdsConfig.UseTestAds"/>).</summary>
    public static bool IsUsingTestAds { get; private set; }

    public static ConsentInfo Consent { get; private set; }

    public static bool IsBannerVisible { get; private set; }

    /// <summary>Height of the visible banner in screen pixels, to keep UI clear of it; 0 without one.</summary>
    public static float BannerHeight { get; private set; }

    public static bool IsInterstitialReady => s_Interstitial.Ready;
    public static bool IsRewardedReady => s_Rewarded.Ready;

    /// <summary>True while an interstitial or rewarded ad covers the game; pause gameplay and music meanwhile.</summary>
    public static bool IsShowingFullScreenAd => s_Interstitial.Showing || s_Rewarded.Showing;

    public static event Action? Initialized;
    public static event Action<ConsentInfo>? ConsentChanged;
    public static event Action? BannerLoaded;
    public static event Action<AdError>? BannerFailedToLoad;
    public static event Action<AdFormat>? Loaded;
    public static event Action<AdFormat, AdError>? FailedToLoad;
    /// <summary>A full-screen ad opened; pause the game here.</summary>
    public static event Action<AdFormat>? FullScreenAdShown;
    /// <summary>A full-screen ad closed (or failed to open); resume the game here.</summary>
    public static event Action<AdFormat>? FullScreenAdClosed;
    public static event Action<AdReward>? RewardEarned;
    public static event Action<AdFormat>? Clicked;
    /// <summary>Estimated revenue of an impression, for analytics.</summary>
    public static event Action<AdRevenue>? PaidEvent;

    // ── Setup ──────────────────────────────────────────────────────

    /// <summary>
    /// Starts ads: shows Google's consent form where the law requires it (EEA, UK), then initializes the SDK.
    /// Loads and banners requested before <see cref="Initialized"/> run once it fires. Calling it again raises
    /// <see cref="Initialized"/> again (next frame) once the SDK runs, so every scene can wait for it the same way.
    /// </summary>
    public static void Initialize(AdsConfig? config = null)
    {
        if (s_Initializing) return;
        if (IsInitialized)
        {
            RaiseInitializedNextFrame();
            return;
        }
        config ??= new AdsConfig();
        BuildConfiguration build = Engine.BuildConfiguration;
        bool testAds = config.UseTestAds ?? build != BuildConfiguration.Release;
        IAdsBackend backend = CreateBackend();
        if (testAds && backend is not UnsupportedAdsBackend)
            Log.Info($"[Ads] Using Google test ads ({build} build); Release builds show the game's own ad units.");
        Start(backend, config, testAds);
    }

    /// <summary>Initialize with a given backend; the seam the package's tests use.</summary>
    internal static void Start(IAdsBackend backend, AdsConfig config, bool useTestAds)
    {
        s_Config = config.Clone();
        IsUsingTestAds = useTestAds;
        s_Backend = backend;
        s_Initializing = true;
        AndroidBridge.Unsubscribe(Channel, OnMessage);
        AndroidBridge.Subscribe(Channel, OnMessage);
        // Static state outlives editor Play sessions; the next session starts afresh.
        Application.OnPlayModeExited -= Reset;
        Application.OnPlayModeExited += Reset;
        s_Backend.Initialize(s_Config);
    }

    private static async void RaiseInitializedNextFrame()
    {
        await Task.Yield();
        if (IsInitialized) Invoke(Initialized);
    }

    private static IAdsBackend CreateBackend()
    {
        if (AndroidBridge.IsAvailable)
            return new AndroidAdsBackend();
        if (Application.IsEditor || (Engine.BuildConfiguration != BuildConfiguration.Release
                                    && Engine.Platform is Platform.Windows or Platform.Linux or Platform.MacOS))
            return new SimulatedAdsBackend();
        return new UnsupportedAdsBackend();
    }

    // ── Banner ─────────────────────────────────────────────────────

    /// <summary>Shows a banner (replacing a visible one). It stays across scenes until <see cref="HideBanner"/>.</summary>
    public static void ShowBanner(string adUnitId, BannerPosition position = BannerPosition.Bottom, BannerSize size = BannerSize.Adaptive)
    {
        RequireInitializeCalled();
        s_BannerUnitId = UnitFor(AdFormat.Banner, adUnitId);
        string unit = s_BannerUnitId;
        WhenInitialized(() => s_Backend!.ShowBanner(unit, position, size));
    }

    public static void HideBanner()
    {
        s_BannerUnitId = null;
        IsBannerVisible = false;
        BannerHeight = 0;
        s_Backend?.HideBanner();
    }

    // ── Interstitial ───────────────────────────────────────────────

    /// <summary>Loads an interstitial for <see cref="ShowInterstitial"/>; <see cref="Loaded"/> reports when it is ready.</summary>
    public static void LoadInterstitial(string adUnitId) => Load(s_Interstitial, adUnitId);

    /// <summary>
    /// Shows the loaded interstitial. False (and no ad) when none is ready or the cooldown runs; onClosed then does
    /// not run. Show interstitials at natural breaks such as between levels, never during play.
    /// </summary>
    public static bool ShowInterstitial(Action? onClosed = null)
    {
        if (!s_Interstitial.Ready || IsShowingFullScreenAd) return false;
        float cooldown = s_Config.InterstitialCooldownSeconds;
        if (cooldown > 0 && s_LastInterstitialClosedMs >= 0
            && Environment.TickCount64 - s_LastInterstitialClosedMs < (long)(cooldown * 1000)) return false;
        return Show(s_Interstitial, onClosed, null);
    }

    // ── Rewarded ───────────────────────────────────────────────────

    public static void LoadRewarded(string adUnitId) => Load(s_Rewarded, adUnitId);

    /// <summary>
    /// Shows the loaded rewarded ad. onReward runs when the player has watched enough to earn it (before the ad
    /// closes); onClosed runs when it closes, rewarded or not. False when none is ready.
    /// </summary>
    public static bool ShowRewarded(Action<AdReward> onReward, Action? onClosed = null)
    {
        if (onReward == null) throw new ArgumentNullException(nameof(onReward));
        if (!s_Rewarded.Ready || IsShowingFullScreenAd) return false;
        return Show(s_Rewarded, onClosed, onReward);
    }

    // ── Consent and tools ──────────────────────────────────────────

    /// <summary>
    /// Opens the consent form again so players can change their choice. GDPR requires a way to do that: show a
    /// "Privacy settings" button in your menu while <see cref="ConsentInfo.PrivacyOptionsRequired"/> is true.
    /// </summary>
    public static void ShowPrivacyOptions()
    {
        RequireInitializeCalled();
        s_Backend!.ShowPrivacyOptions();
    }

    /// <summary>Forgets the stored consent so the form appears again on the next start; for testing.</summary>
    public static void ResetConsent() => s_Backend?.ResetConsent();

    /// <summary>Mutes or unmutes video ads, e.g. from the game's sound setting.</summary>
    public static void SetMuted(bool muted)
    {
        s_Config.MuteAds = muted;
        s_Backend?.SetMuted(muted);
    }

    /// <summary>Opens Google's Ad Inspector on a test device to check ad units and mediation.</summary>
    public static void OpenAdInspector() => s_Backend?.OpenAdInspector();

    // ── Internals ──────────────────────────────────────────────────

    private static void RequireInitializeCalled()
    {
        if (s_Backend == null) throw new InvalidOperationException("Call Ads.Initialize() before using ads.");
    }

    private static string UnitFor(AdFormat format, string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId)) throw new ArgumentException("An AdMob ad unit ID is required.", nameof(adUnitId));
        return IsUsingTestAds ? TestAdUnits.For(format) : adUnitId.Trim();
    }

    private static void WhenInitialized(Action action)
    {
        if (IsInitialized) action();
        else s_AfterInitialize.Add(action);
    }

    private static void Load(FullScreenSlot slot, string adUnitId)
    {
        RequireInitializeCalled();
        slot.AdUnitId = UnitFor(slot.Format, adUnitId);
        if (slot.Loading || slot.Ready || slot.Showing) return;
        slot.Loading = true;
        WhenInitialized(() => s_Backend!.Load(slot.Format, slot.AdUnitId!));
    }

    private static bool Show(FullScreenSlot slot, Action? onClosed, Action<AdReward>? onReward)
    {
        slot.Ready = false;
        slot.Showing = true;
        slot.OnClosed = onClosed;
        slot.OnReward = onReward;
        s_Backend!.Show(slot.Format);
        return true;
    }

    private static FullScreenSlot? SlotOf(string format) => format switch
    {
        "interstitial" => s_Interstitial,
        "rewarded" => s_Rewarded,
        _ => null,
    };

    private static void Closed(FullScreenSlot slot)
    {
        if (!slot.Showing) return;
        slot.Showing = false;
        if (slot.Format == AdFormat.Interstitial) s_LastInterstitialClosedMs = Environment.TickCount64;
        Action? closed = slot.OnClosed;
        slot.OnClosed = null;
        slot.OnReward = null;
        Invoke(closed);
        Raise(FullScreenAdClosed, slot.Format);
        if (s_Config.AutoReload && slot.AdUnitId != null) Load(slot, slot.AdUnitId);
    }

    private static void LoadFailed(FullScreenSlot slot, AdError error)
    {
        slot.Loading = false;
        slot.Ready = false;
        slot.Failures++;
        Log.Warn($"[Ads] {slot.Format} failed to load: {error}");
        Raise(FailedToLoad, slot.Format, error);
        if (!s_Config.AutoReload || slot.AdUnitId == null || !IsSupported) return;
        RetryLater(slot, slot.Generation, Math.Min(64, 1 << Math.Min(slot.Failures, 6)));
    }

    private static async void RetryLater(FullScreenSlot slot, int generation, int seconds)
    {
        await Task.Delay(seconds * 1000);
        if (slot.Generation == generation && !slot.Loading && !slot.Ready && !slot.Showing && slot.AdUnitId != null)
            Load(slot, slot.AdUnitId);
    }

    internal static void OnMessage(string json)
    {
        JsonElement message;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            message = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            Log.Warn("[Ads] Ignoring malformed message: " + json);
            return;
        }
        string name = Str(message, "event");
        string format = name.StartsWith("interstitial", StringComparison.Ordinal) ? "interstitial"
            : name.StartsWith("rewarded", StringComparison.Ordinal) ? "rewarded" : "";
        FullScreenSlot? slot = SlotOf(format);
        string what = format.Length > 0 ? name.Substring(format.Length) : name;

        switch (slot != null ? what : name)
        {
            case "initialized":
                s_Initializing = false;
                IsInitialized = true;
                if (s_Config.MuteAds) s_Backend?.SetMuted(true);
                var pending = s_AfterInitialize.ToArray();
                s_AfterInitialize.Clear();
                foreach (Action action in pending) Invoke(action);
                Invoke(Initialized);
                break;
            case "initializeFailed":
                s_Initializing = false;
                Log.Error("[Ads] Could not start: " + Str(message, "message"));
                break;
            case "consent":
                Consent = new ConsentInfo((ConsentStatus)Int(message, "status"), Bool(message, "canRequestAds"),
                    Bool(message, "privacyOptionsRequired"));
                if (message.TryGetProperty("message", out _))
                    Log.Warn($"[Ads] Consent: {Str(message, "message")} (code {Int(message, "code")})");
                Raise(ConsentChanged, Consent);
                break;
            case "bannerLoaded":
                if (s_BannerUnitId == null) break;
                IsBannerVisible = true;
                BannerHeight = (float)Num(message, "height");
                Invoke(BannerLoaded);
                break;
            case "bannerFailed":
                IsBannerVisible = false;
                BannerHeight = 0;
                Log.Warn($"[Ads] Banner failed to load: {Error(message)}");
                Raise(BannerFailedToLoad, Error(message));
                break;
            case "bannerHidden":
                break;
            case "bannerClicked":
                Raise(Clicked, AdFormat.Banner);
                break;
            case "paid":
                AdFormat paidFormat = Str(message, "format") switch
                {
                    "interstitial" => AdFormat.Interstitial,
                    "rewarded" => AdFormat.Rewarded,
                    _ => AdFormat.Banner,
                };
                Raise(PaidEvent, new AdRevenue(paidFormat, (long)Num(message, "valueMicros"), Str(message, "currency"), Int(message, "precision")));
                break;
            case "rewardEarned":
                var reward = new AdReward(Str(message, "type"), Int(message, "amount"));
                Action<AdReward>? onReward = s_Rewarded.OnReward;
                s_Rewarded.OnReward = null;
                if (onReward != null)
                {
                    try { onReward(reward); }
                    catch (Exception ex) { Log.Error($"[Ads] Reward handler threw: {ex}"); }
                }
                Raise(RewardEarned, reward);
                break;
            case "Loaded":
                slot!.Loading = false;
                slot.Ready = true;
                slot.Failures = 0;
                slot.Generation++;
                Raise(Loaded, slot.Format);
                break;
            case "Failed":
                LoadFailed(slot!, Error(message));
                break;
            case "Shown":
                Raise(FullScreenAdShown, slot!.Format);
                break;
            case "ShowFailed":
                Log.Warn($"[Ads] {slot!.Format} failed to show: {Error(message)}");
                Closed(slot);
                break;
            case "Closed":
                Closed(slot!);
                break;
            case "Clicked":
                Raise(Clicked, slot!.Format);
                break;
            case "Impression":
                break;
            case "error":
                Log.Error("[Ads] " + Str(message, "message"));
                break;
        }
    }

    private static AdError Error(JsonElement message) => new(Int(message, "code"), Str(message, "message"));

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static int Int(JsonElement e, string name) => (int)Num(e, name);

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static void Invoke(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Error($"[Ads] Handler threw: {ex}"); }
    }

    private static void Raise<T>(Action<T>? handler, T value)
    {
        try { handler?.Invoke(value); }
        catch (Exception ex) { Log.Error($"[Ads] Handler threw: {ex}"); }
    }

    private static void Raise<T1, T2>(Action<T1, T2>? handler, T1 a, T2 b)
    {
        try { handler?.Invoke(a, b); }
        catch (Exception ex) { Log.Error($"[Ads] Handler threw: {ex}"); }
    }

    /// <summary>Forgets everything (tests and the editor's next Play session).</summary>
    internal static void Reset()
    {
        s_Backend?.Dispose();
        AndroidBridge.Unsubscribe(Channel, OnMessage);
        s_Backend = null;
        s_Initializing = IsInitialized = false;
        s_AfterInitialize.Clear();
        foreach (FullScreenSlot slot in new[] { s_Interstitial, s_Rewarded })
        {
            slot.AdUnitId = null;
            slot.Loading = slot.Ready = slot.Showing = false;
            slot.Failures = 0;
            slot.Generation++;
            slot.OnClosed = null;
            slot.OnReward = null;
        }
        s_LastInterstitialClosedMs = -1;
        s_BannerUnitId = null;
        IsBannerVisible = false;
        BannerHeight = 0;
        Consent = default;
    }
}
