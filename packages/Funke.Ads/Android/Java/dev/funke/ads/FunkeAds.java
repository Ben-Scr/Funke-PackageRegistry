package dev.funke.ads;

import android.app.Activity;
import android.os.Build;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.DisplayCutout;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.widget.FrameLayout;

import com.google.android.gms.ads.AdError;
import com.google.android.gms.ads.AdInspectorError;
import com.google.android.gms.ads.AdListener;
import com.google.android.gms.ads.AdRequest;
import com.google.android.gms.ads.AdSize;
import com.google.android.gms.ads.AdValue;
import com.google.android.gms.ads.AdView;
import com.google.android.gms.ads.FullScreenContentCallback;
import com.google.android.gms.ads.LoadAdError;
import com.google.android.gms.ads.MobileAds;
import com.google.android.gms.ads.OnAdInspectorClosedListener;
import com.google.android.gms.ads.OnPaidEventListener;
import com.google.android.gms.ads.OnUserEarnedRewardListener;
import com.google.android.gms.ads.RequestConfiguration;
import com.google.android.gms.ads.initialization.InitializationStatus;
import com.google.android.gms.ads.initialization.OnInitializationCompleteListener;
import com.google.android.gms.ads.interstitial.InterstitialAd;
import com.google.android.gms.ads.interstitial.InterstitialAdLoadCallback;
import com.google.android.gms.ads.rewarded.RewardItem;
import com.google.android.gms.ads.rewarded.RewardedAd;
import com.google.android.gms.ads.rewarded.RewardedAdLoadCallback;
import com.google.android.ump.ConsentDebugSettings;
import com.google.android.ump.ConsentForm;
import com.google.android.ump.ConsentInformation;
import com.google.android.ump.ConsentRequestParameters;
import com.google.android.ump.FormError;
import com.google.android.ump.UserMessagingPlatform;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

import dev.funke.player.FunkeBridge;

/**
 * Google Mobile Ads (AdMob) for Funke.Ads. C# calls the static String(String) methods through AndroidBridge;
 * results arrive as JSON events on the "Funke.Ads" channel. All SDK work runs on the UI thread.
 */
public final class FunkeAds {
    private static final String TAG = "Funke.Ads";
    private static final String CHANNEL = "Funke.Ads";

    private static final AtomicBoolean sSdkStarted = new AtomicBoolean(false);
    private static AdView sBanner;
    private static String sBannerPosition = "bottom";
    private static InterstitialAd sInterstitial;
    private static RewardedAd sRewarded;

    private FunkeAds() {}

    // ── Calls from C# ──────────────────────────────────────────────

    /** {requestConsent, testDeviceIds[], childDirected, underAgeOfConsent, maxAdContentRating, debugGeography, muted} */
    public static String initialize(String json) throws JSONException {
        final JSONObject options = new JSONObject(json.isEmpty() ? "{}" : json);
        onUi(new Runnable() { @Override public void run() {
            Activity activity = FunkeBridge.getActivity();
            if (activity == null) {
                send(event("initializeFailed").put("message", "no activity"));
                return;
            }
            MobileAds.setRequestConfiguration(requestConfiguration(options));
            if (options.optBoolean("requestConsent", true)) {
                requestConsent(activity, options);
            } else {
                startSdk(activity);
            }
        }});
        return "";
    }

    /** {adUnitId, position: "top"|"bottom", size: "adaptive"|"banner"|"large"|"mediumRectangle"|"leaderboard"} */
    public static String showBanner(String json) throws JSONException {
        final JSONObject options = new JSONObject(json);
        onUi(new Runnable() { @Override public void run() {
            final Activity activity = FunkeBridge.getActivity();
            if (activity == null) return;
            destroyBanner();
            final AdView banner = new AdView(activity);
            banner.setAdUnitId(options.optString("adUnitId"));
            banner.setAdSize(bannerSize(activity, options.optString("size", "adaptive")));
            sBannerPosition = options.optString("position", "bottom");
            banner.setAdListener(new AdListener() {
                @Override public void onAdLoaded() {
                    send(event("bannerLoaded").put("height", banner.getAdSize() != null ? banner.getAdSize().getHeightInPixels(activity) : 0));
                }
                @Override public void onAdFailedToLoad(LoadAdError error) { send(error(event("bannerFailed"), error)); }
                @Override public void onAdClicked() { send(event("bannerClicked")); }
                @Override public void onAdImpression() { send(event("bannerImpression")); }
            });
            banner.setOnPaidEventListener(paid("banner"));
            FrameLayout.LayoutParams layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER_HORIZONTAL | ("top".equals(sBannerPosition) ? Gravity.TOP : Gravity.BOTTOM));
            activity.addContentView(banner, layout);
            keepClearOfCutouts(banner);
            sBanner = banner;
            banner.loadAd(new AdRequest.Builder().build());
        }});
        return "";
    }

    public static String hideBanner(String json) {
        onUi(new Runnable() { @Override public void run() { destroyBanner(); }});
        return "";
    }

    /** {adUnitId} */
    public static String loadInterstitial(String json) throws JSONException {
        final String adUnitId = new JSONObject(json).optString("adUnitId");
        onUi(new Runnable() { @Override public void run() {
            Activity activity = FunkeBridge.getActivity();
            if (activity == null) return;
            InterstitialAd.load(activity, adUnitId, new AdRequest.Builder().build(), new InterstitialAdLoadCallback() {
                @Override public void onAdLoaded(InterstitialAd ad) {
                    sInterstitial = ad;
                    ad.setOnPaidEventListener(paid("interstitial"));
                    send(event("interstitialLoaded"));
                }
                @Override public void onAdFailedToLoad(LoadAdError error) {
                    sInterstitial = null;
                    send(error(event("interstitialFailed"), error));
                }
            });
        }});
        return "";
    }

    public static String showInterstitial(String json) {
        onUi(new Runnable() { @Override public void run() {
            Activity activity = FunkeBridge.getActivity();
            InterstitialAd ad = sInterstitial;
            sInterstitial = null;
            if (activity == null || ad == null) {
                send(event("interstitialShowFailed").put("message", "no interstitial is loaded"));
                return;
            }
            ad.setFullScreenContentCallback(fullScreen("interstitial"));
            ad.show(activity);
        }});
        return "";
    }

    /** {adUnitId} */
    public static String loadRewarded(String json) throws JSONException {
        final String adUnitId = new JSONObject(json).optString("adUnitId");
        onUi(new Runnable() { @Override public void run() {
            Activity activity = FunkeBridge.getActivity();
            if (activity == null) return;
            RewardedAd.load(activity, adUnitId, new AdRequest.Builder().build(), new RewardedAdLoadCallback() {
                @Override public void onAdLoaded(RewardedAd ad) {
                    sRewarded = ad;
                    ad.setOnPaidEventListener(paid("rewarded"));
                    send(event("rewardedLoaded"));
                }
                @Override public void onAdFailedToLoad(LoadAdError error) {
                    sRewarded = null;
                    send(error(event("rewardedFailed"), error));
                }
            });
        }});
        return "";
    }

    public static String showRewarded(String json) {
        onUi(new Runnable() { @Override public void run() {
            Activity activity = FunkeBridge.getActivity();
            RewardedAd ad = sRewarded;
            sRewarded = null;
            if (activity == null || ad == null) {
                send(event("rewardedShowFailed").put("message", "no rewarded ad is loaded"));
                return;
            }
            ad.setFullScreenContentCallback(fullScreen("rewarded"));
            ad.show(activity, new OnUserEarnedRewardListener() {
                @Override public void onUserEarnedReward(RewardItem reward) {
                    send(event("rewardEarned").put("type", reward.getType()).put("amount", reward.getAmount()));
                }
            });
        }});
        return "";
    }

    /** The privacy options form (GDPR: lets players change their consent); required when consent asks for it. */
    public static String showPrivacyOptions(String json) {
        onUi(new Runnable() { @Override public void run() {
            final Activity activity = FunkeBridge.getActivity();
            if (activity == null) return;
            UserMessagingPlatform.showPrivacyOptionsForm(activity, new ConsentForm.OnConsentFormDismissedListener() {
                @Override public void onConsentFormDismissed(FormError error) {
                    send(consentEvent(activity, error));
                }
            });
        }});
        return "";
    }

    /** Forgets the stored consent so the next start asks again; for testing. */
    public static String resetConsent(String json) {
        Activity activity = FunkeBridge.getActivity();
        if (activity != null) UserMessagingPlatform.getConsentInformation(activity).reset();
        return "";
    }

    public static String setMuted(String json) throws JSONException {
        final boolean muted = new JSONObject(json).optBoolean("muted");
        onUi(new Runnable() { @Override public void run() { MobileAds.setAppMuted(muted); }});
        return "";
    }

    /** Opens Google's Ad Inspector (test devices only), to check mediation and ad unit setup on the phone. */
    public static String openAdInspector(String json) {
        onUi(new Runnable() { @Override public void run() {
            Activity activity = FunkeBridge.getActivity();
            if (activity != null) MobileAds.openAdInspector(activity, new OnAdInspectorClosedListener() {
                @Override public void onAdInspectorClosed(AdInspectorError error) {
                    if (error != null) Log.w(TAG, "Ad Inspector: " + error.getMessage());
                }
            });
        }});
        return "";
    }

    // ── Consent and SDK start ──────────────────────────────────────

    private static void requestConsent(final Activity activity, JSONObject options) {
        ConsentRequestParameters.Builder parameters = new ConsentRequestParameters.Builder()
            .setTagForUnderAgeOfConsent(options.optBoolean("underAgeOfConsent", false));
        String geography = options.optString("debugGeography", "");
        JSONArray testDevices = options.optJSONArray("testDeviceIds");
        if (!geography.isEmpty() || (testDevices != null && testDevices.length() > 0)) {
            ConsentDebugSettings.Builder debug = new ConsentDebugSettings.Builder(activity);
            if ("eea".equals(geography)) debug.setDebugGeography(ConsentDebugSettings.DebugGeography.DEBUG_GEOGRAPHY_EEA);
            else if ("other".equals(geography)) debug.setDebugGeography(ConsentDebugSettings.DebugGeography.DEBUG_GEOGRAPHY_OTHER);
            for (int i = 0; testDevices != null && i < testDevices.length(); i++) debug.addTestDeviceHashedId(testDevices.optString(i));
            parameters.setConsentDebugSettings(debug.build());
        }
        final ConsentInformation consent = UserMessagingPlatform.getConsentInformation(activity);
        consent.requestConsentInfoUpdate(activity, parameters.build(),
            new ConsentInformation.OnConsentInfoUpdateSuccessListener() {
                @Override public void onConsentInfoUpdateSuccess() {
                    UserMessagingPlatform.loadAndShowConsentFormIfRequired(activity, new ConsentForm.OnConsentFormDismissedListener() {
                        @Override public void onConsentFormDismissed(FormError error) {
                            send(consentEvent(activity, error));
                            if (consent.canRequestAds()) startSdk(activity);
                        }
                    });
                }
            },
            new ConsentInformation.OnConsentInfoUpdateFailureListener() {
                @Override public void onConsentInfoUpdateFailure(FormError error) {
                    send(consentEvent(activity, error));
                    if (consent.canRequestAds()) startSdk(activity);
                }
            });
        // Consent from an earlier session is enough to start right away.
        if (consent.canRequestAds()) startSdk(activity);
    }

    private static void startSdk(final Activity activity) {
        if (!sSdkStarted.compareAndSet(false, true)) return;
        // Google recommends initializing off the main thread; the listener runs on the UI thread.
        new Thread(new Runnable() { @Override public void run() {
            MobileAds.initialize(activity, new OnInitializationCompleteListener() {
                @Override public void onInitializationComplete(InitializationStatus status) {
                    send(event("initialized"));
                }
            });
        }}, "Funke.Ads init").start();
    }

    private static RequestConfiguration requestConfiguration(JSONObject options) {
        RequestConfiguration.Builder builder = MobileAds.getRequestConfiguration().toBuilder();
        List<String> devices = new ArrayList<String>();
        JSONArray ids = options.optJSONArray("testDeviceIds");
        for (int i = 0; ids != null && i < ids.length(); i++) devices.add(ids.optString(i));
        builder.setTestDeviceIds(devices);
        if (options.has("childDirected")) {
            builder.setTagForChildDirectedTreatment(options.optBoolean("childDirected")
                ? RequestConfiguration.TAG_FOR_CHILD_DIRECTED_TREATMENT_TRUE : RequestConfiguration.TAG_FOR_CHILD_DIRECTED_TREATMENT_FALSE);
        }
        if (options.has("underAgeOfConsent")) {
            builder.setTagForUnderAgeOfConsent(options.optBoolean("underAgeOfConsent")
                ? RequestConfiguration.TAG_FOR_UNDER_AGE_OF_CONSENT_TRUE : RequestConfiguration.TAG_FOR_UNDER_AGE_OF_CONSENT_FALSE);
        }
        String rating = options.optString("maxAdContentRating", "");
        if ("G".equals(rating)) builder.setMaxAdContentRating(RequestConfiguration.MAX_AD_CONTENT_RATING_G);
        else if ("PG".equals(rating)) builder.setMaxAdContentRating(RequestConfiguration.MAX_AD_CONTENT_RATING_PG);
        else if ("T".equals(rating)) builder.setMaxAdContentRating(RequestConfiguration.MAX_AD_CONTENT_RATING_T);
        else if ("MA".equals(rating)) builder.setMaxAdContentRating(RequestConfiguration.MAX_AD_CONTENT_RATING_MA);
        if (options.has("muted")) MobileAds.setAppMuted(options.optBoolean("muted"));
        return builder.build();
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static AdSize bannerSize(Activity activity, String size) {
        if ("banner".equals(size)) return AdSize.BANNER;
        if ("large".equals(size)) return AdSize.LARGE_BANNER;
        if ("mediumRectangle".equals(size)) return AdSize.MEDIUM_RECTANGLE;
        if ("leaderboard".equals(size)) return AdSize.LEADERBOARD;
        DisplayMetrics metrics = activity.getResources().getDisplayMetrics();
        int widthDp = (int) (metrics.widthPixels / metrics.density);
        return AdSize.getCurrentOrientationAnchoredAdaptiveBannerAdSize(activity, widthDp);
    }

    /** Fullscreen games draw under the notch; keep the banner beside it like the engine's safe area does. */
    private static void keepClearOfCutouts(final View banner) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.P) return;
        banner.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override public WindowInsets onApplyWindowInsets(View view, WindowInsets insets) {
                DisplayCutout cutout = insets.getDisplayCutout();
                ViewGroup.MarginLayoutParams margins = (ViewGroup.MarginLayoutParams) view.getLayoutParams();
                int top = cutout != null && "top".equals(sBannerPosition) ? cutout.getSafeInsetTop() : 0;
                int bottom = cutout != null && !"top".equals(sBannerPosition) ? cutout.getSafeInsetBottom() : 0;
                if (margins.topMargin != top || margins.bottomMargin != bottom) {
                    margins.topMargin = top;
                    margins.bottomMargin = bottom;
                    view.setLayoutParams(margins);
                }
                return insets;
            }
        });
        banner.requestApplyInsets();
    }

    private static void destroyBanner() {
        if (sBanner == null) return;
        ViewGroup parent = (ViewGroup) sBanner.getParent();
        if (parent != null) parent.removeView(sBanner);
        sBanner.destroy();
        sBanner = null;
        send(event("bannerHidden"));
    }

    private static FullScreenContentCallback fullScreen(final String format) {
        return new FullScreenContentCallback() {
            @Override public void onAdShowedFullScreenContent() { send(event(format + "Shown")); }
            @Override public void onAdDismissedFullScreenContent() { send(event(format + "Closed")); }
            @Override public void onAdFailedToShowFullScreenContent(AdError error) {
                send(event(format + "ShowFailed").put("code", error.getCode()).put("message", error.getMessage()));
            }
            @Override public void onAdClicked() { send(event(format + "Clicked")); }
            @Override public void onAdImpression() { send(event(format + "Impression")); }
        };
    }

    private static OnPaidEventListener paid(final String format) {
        return new OnPaidEventListener() {
            @Override public void onPaidEvent(AdValue value) {
                send(event("paid").put("format", format).put("valueMicros", value.getValueMicros())
                    .put("currency", value.getCurrencyCode()).put("precision", value.getPrecisionType()));
            }
        };
    }

    private static Json consentEvent(Activity activity, FormError error) {
        ConsentInformation consent = UserMessagingPlatform.getConsentInformation(activity);
        Json json = event("consent").put("status", consent.getConsentStatus()).put("canRequestAds", consent.canRequestAds())
            .put("privacyOptionsRequired", consent.getPrivacyOptionsRequirementStatus()
                == ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED);
        if (error != null) json.put("code", error.getErrorCode()).put("message", error.getMessage());
        return json;
    }

    private static Json error(Json json, LoadAdError error) {
        return json.put("code", error.getCode()).put("message", error.getMessage());
    }

    private static Json event(String name) {
        return new Json().put("event", name);
    }

    private static void send(Json json) {
        FunkeBridge.send(CHANNEL, json.toString());
    }

    private static void onUi(final Runnable work) {
        Activity activity = FunkeBridge.getActivity();
        Runnable guarded = new Runnable() { @Override public void run() {
            try {
                work.run();
            } catch (RuntimeException e) {
                Log.e(TAG, "Ad call failed", e);
                send(event("error").put("message", String.valueOf(e)));
            }
        }};
        if (activity != null) activity.runOnUiThread(guarded);
        else Log.w(TAG, "No activity yet; ad call dropped");
    }

    /** JSONObject whose put never throws, so event building reads as one expression. */
    private static final class Json {
        private final JSONObject object = new JSONObject();

        Json put(String key, Object value) {
            try {
                object.put(key, value);
            } catch (JSONException ignored) {
                // Only thrown for NaN numbers and null keys.
            }
            return this;
        }

        @Override public String toString() {
            return object.toString();
        }
    }
}
