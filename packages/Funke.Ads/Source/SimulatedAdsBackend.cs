using System;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Funke.UI;

namespace Funke.Ads;

/// <summary>
/// Placeholder ads for the editor and desktop Development builds: loads succeed after a moment, full-screen ads show
/// an overlay that closes itself (or with its button), rewarded ads grant their reward after a short "video".
/// Messages go through AndroidBridge.Simulate, the same path Java events take on a phone.
/// </summary>
internal sealed class SimulatedAdsBackend : IAdsBackend
{
    private const int k_TopSortingOrder = 32000;
    private static readonly Regex s_AdUnitId = new(@"^ca-app-pub-\d{16}/\d{10}$", RegexOptions.CultureInvariant);

    private bool m_Disposed;
    private Entity? m_Banner;
    private Entity? m_FullScreen;
    private int m_FullScreenSession;

    internal static async void PostNextFrame(JsonObject message, Func<bool> stillWanted, int delayMs = 0)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        else await Task.Yield();
        if (stillWanted()) AndroidBridge.Simulate(Ads.Channel, message.ToJsonString());
    }

    private void Post(JsonObject message, int delayMs = 0) => PostNextFrame(message, () => !m_Disposed, delayMs);

    private static JsonObject Event(string name) => new() { ["event"] = name };

    private static JsonObject InvalidUnit(string name, string adUnitId)
        => new() { ["event"] = name, ["code"] = 1, ["message"] = $"Invalid ad unit ID '{adUnitId}' (expected ca-app-pub-<16 digits>/<10 digits>)" };

    public void Initialize(AdsConfig config)
    {
        Log.Info("[Ads] Simulating ads (editor / Development build); Android builds show real AdMob ads.");
        bool eea = config.DebugGeography == ConsentDebugGeography.EEA;
        Post(new JsonObject
        {
            ["event"] = "consent",
            ["status"] = (int)(eea ? ConsentStatus.Obtained : ConsentStatus.NotRequired),
            ["canRequestAds"] = true,
            ["privacyOptionsRequired"] = eea,
        }, 50);
        Post(Event("initialized"), 100);
    }

    public void ShowBanner(string adUnitId, BannerPosition position, BannerSize size)
    {
        DestroyBanner();
        if (!s_AdUnitId.IsMatch(adUnitId))
        {
            Post(InvalidUnit("bannerFailed", adUnitId));
            return;
        }
        (float width, float height) = size switch
        {
            BannerSize.Large => (640f, 200f),
            BannerSize.MediumRectangle => (600f, 500f),
            BannerSize.Leaderboard => (1456f, 180f),
            BannerSize.Adaptive => (1920f, 110f),
            _ => (640f, 100f),
        };
        m_Banner = TryBuild(() =>
        {
            float y = position == BannerPosition.Top ? 1f : 0f;
            Entity banner = Panel("Ads (simulated banner)", null, new Vector2(0.5f, y), new Vector2(0.5f, y), new Vector2(width, height),
                new Vector4(0.12f, 0.14f, 0.18f, 0.95f), k_TopSortingOrder);
            Label(banner, $"Test banner · {size}", 36f, k_TopSortingOrder + 1);
            return banner;
        });
        Post(new JsonObject { ["event"] = "bannerLoaded", ["height"] = MathF.Round(Application.ScreenHeight * height / 1080f) }, 300);
    }

    public void HideBanner()
    {
        if (DestroyBanner()) Post(Event("bannerHidden"));
    }

    public void Load(AdFormat format, string adUnitId)
    {
        string prefix = format == AdFormat.Interstitial ? "interstitial" : "rewarded";
        Post(s_AdUnitId.IsMatch(adUnitId) ? Event(prefix + "Loaded") : InvalidUnit(prefix + "Failed", adUnitId), 500);
    }

    public void Show(AdFormat format)
    {
        string prefix = format == AdFormat.Interstitial ? "interstitial" : "rewarded";
        CloseFullScreen(null);
        int session = ++m_FullScreenSession;
        bool rewarded = format == AdFormat.Rewarded;
        m_FullScreen = TryBuild(() =>
        {
            Entity dim = Panel("Ads (simulated " + prefix + ")", null, new Vector2(0.5f), new Vector2(0.5f), new Vector2(8000f, 8000f),
                new Vector4(0f, 0f, 0f, 0.8f), k_TopSortingOrder + 10);
            Entity card = Panel("Card", dim, new Vector2(0.5f), new Vector2(0.5f), new Vector2(1000f, 520f),
                new Vector4(0.12f, 0.14f, 0.18f, 1f), k_TopSortingOrder + 11);
            Label(card, rewarded ? "Test rewarded ad\nReward after 2.5 s" : "Test interstitial ad", 52f, k_TopSortingOrder + 12);
            Entity close = Panel("Close", card, new Vector2(1f), new Vector2(1f), new Vector2(110f, 110f),
                new Vector4(0.85f, 0.2f, 0.2f, 1f), k_TopSortingOrder + 13);
            close.GetComponent<RectTransform>()!.Pivot = new Vector2(1f);
            Label(close, "X", 56f, k_TopSortingOrder + 14);
            Button? button = close.AddComponent<Button>();
            if (button != null) button.OnClicked += _ => CloseFullScreen(prefix, session);
            return dim;
        });
        Post(Event(prefix + "Shown"));
        if (rewarded)
        {
            PostNextFrame(new JsonObject { ["event"] = "rewardEarned", ["type"] = "Reward", ["amount"] = 1 },
                () => !m_Disposed && m_FullScreenSession == session, 2500);
        }
        AutoClose(prefix, session, rewarded ? 4000 : 3000);
    }

    private async void AutoClose(string prefix, int session, int delayMs)
    {
        await Task.Delay(delayMs);
        CloseFullScreen(prefix, session);
    }

    private void CloseFullScreen(string? prefix, int session = -1)
    {
        if (m_Disposed || (session >= 0 && session != m_FullScreenSession)) return;
        bool wasOpen = session >= 0;
        m_FullScreenSession++;
        if (m_FullScreen) Entity.Destroy(m_FullScreen!);
        m_FullScreen = null;
        if (wasOpen && prefix != null) Post(Event(prefix + "Closed"));
    }

    public void ShowPrivacyOptions()
    {
        Log.Info("[Ads] (simulated) The privacy options form would open here.");
        Post(new JsonObject
        {
            ["event"] = "consent",
            ["status"] = (int)Ads.Consent.Status,
            ["canRequestAds"] = true,
            ["privacyOptionsRequired"] = Ads.Consent.PrivacyOptionsRequired,
        });
    }

    public void ResetConsent() => Log.Info("[Ads] (simulated) Consent reset.");
    public void SetMuted(bool muted) { }
    public void OpenAdInspector() => Log.Info("[Ads] (simulated) Ad Inspector only opens on Android test devices.");

    public void Dispose()
    {
        DestroyBanner();
        CloseFullScreen(null);
        m_Disposed = true;
    }

    private bool DestroyBanner()
    {
        bool had = m_Banner != null;
        if (m_Banner) Entity.Destroy(m_Banner!);
        m_Banner = null;
        return had;
    }

    // The placeholders are plain UI entities; a scene without UI support only loses the picture, not the events.
    private static Entity? TryBuild(Func<Entity> build)
    {
        try { return build(); }
        catch (Exception ex)
        {
            Log.Warn("[Ads] Could not draw the simulated ad: " + ex.Message);
            return null;
        }
    }

    private static Entity UiEntity(string name, Entity? parent)
    {
        Entity entity = parent != null ? Entity.Create(name, parent) : Entity.Create(name);
        // New entities get the world transform of the project's modules, which UI's RectTransform replaces.
        if (entity.HasComponent<Transform2D>()) entity.RemoveComponent<Transform2D>();
        if (entity.HasComponent<Transform3D>()) entity.RemoveComponent<Transform3D>();
        return entity;
    }

    private static Entity Panel(string name, Entity? parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector4 color, int order)
    {
        Entity entity = UiEntity(name, parent);
        RectTransform rect = entity.AddComponent<RectTransform>() ?? throw new InvalidOperationException("no RectTransform");
        rect.AnchorMin = anchorMin;
        rect.AnchorMax = anchorMax;
        rect.Pivot = new Vector2(0.5f, anchorMin.Y);
        rect.SizeDelta = size;
        Image image = entity.AddComponent<Image>() ?? throw new InvalidOperationException("no Image");
        image.Color = color;
        image.SortingOrder = order;
        return entity;
    }

    private static void Label(Entity parent, string text, float fontSize, int order)
    {
        Entity entity = UiEntity("Text", parent);
        RectTransform rect = entity.AddComponent<RectTransform>() ?? throw new InvalidOperationException("no RectTransform");
        rect.AnchorMin = rect.AnchorMax = rect.Pivot = new Vector2(0.5f);
        rect.SizeDelta = parent.GetComponent<RectTransform>()?.SizeDelta ?? new Vector2(400f, 100f);
        TextRenderer label = entity.AddComponent<TextRenderer>() ?? throw new InvalidOperationException("no TextRenderer");
        label.Text = text;
        label.FontSize = fontSize;
        label.Color = new Vector4(1f, 1f, 1f, 1f);
        label.Alignment = TextAlignment.Center;
        label.VerticalAlignment = TextVerticalAlignment.Middle;
        label.SortingOrder = order;
    }
}
