using System;
using System.Collections.Generic;

namespace Funke.Ads;

/// <summary>Where a banner sits; it stays clear of the notch / camera cutout.</summary>
public enum BannerPosition
{
    Bottom,
    Top,
}

/// <summary>Banner formats. <see cref="Adaptive"/> spans the screen width with the height Google picks for it.</summary>
public enum BannerSize
{
    Adaptive,
    /// <summary>320×50 dp.</summary>
    Standard,
    /// <summary>320×100 dp.</summary>
    Large,
    /// <summary>300×250 dp.</summary>
    MediumRectangle,
    /// <summary>728×90 dp, tablets.</summary>
    Leaderboard,
}

public enum AdFormat
{
    Banner,
    Interstitial,
    Rewarded,
}

/// <summary>GDPR consent state from Google's User Messaging Platform. Values match <c>ConsentInformation.ConsentStatus</c>.</summary>
public enum ConsentStatus
{
    Unknown = 0,
    /// <summary>The player is outside the regions that need consent (EEA, UK, Switzerland).</summary>
    NotRequired = 1,
    Required = 2,
    Obtained = 3,
}

/// <summary>Highest content rating of the ads the game accepts.</summary>
public enum MaxAdContentRating
{
    Unspecified,
    /// <summary>General audiences.</summary>
    G,
    /// <summary>Parental guidance.</summary>
    PG,
    /// <summary>Teen.</summary>
    T,
    /// <summary>Mature audiences.</summary>
    MA,
}

/// <summary>Region the consent form pretends the device is in, for testing it outside the EEA.</summary>
public enum ConsentDebugGeography
{
    Disabled,
    EEA,
    NotEEA,
}

/// <summary>What a rewarded ad grants, as set up for the ad unit in AdMob.</summary>
public readonly record struct AdReward(string Type, int Amount);

/// <summary>Why an ad did not load or show. Codes are Google's (e.g. 3 = no fill: no ad available right now).</summary>
public readonly record struct AdError(int Code, string Message)
{
    public override string ToString() => $"{Message} (code {Code})";
}

/// <summary>Estimated earnings of one impression, reported by AdMob; for analytics.</summary>
public readonly record struct AdRevenue(AdFormat Format, long ValueMicros, string CurrencyCode, int Precision)
{
    public double Value => ValueMicros / 1_000_000.0;
}

/// <summary>Consent state after the form or a stored answer; <see cref="CanRequestAds"/> gates every ad request.</summary>
public readonly record struct ConsentInfo(ConsentStatus Status, bool CanRequestAds, bool PrivacyOptionsRequired);

/// <summary>Settings for <see cref="Ads.Initialize(AdsConfig?)"/>.</summary>
public sealed class AdsConfig
{
    /// <summary>
    /// Replace every ad unit with Google's test units. Null (default) = test ads everywhere except Release builds, so
    /// development never clicks real ads (AdMob suspends accounts for that).
    /// </summary>
    public bool? UseTestAds { get; set; }

    /// <summary>Hashed device IDs that get test ads even with real ad units; logcat prints a phone's ID on the first request.</summary>
    public List<string> TestDeviceIds { get; set; } = new();

    /// <summary>Ask for GDPR consent (Google's form, shown only where the law requires it) before starting the SDK.</summary>
    public bool RequestConsent { get; set; } = true;

    /// <summary>Shows the consent form as if the device were in the EEA; for testing. Needs the device in TestDeviceIds on real phones.</summary>
    public ConsentDebugGeography DebugGeography { get; set; } = ConsentDebugGeography.Disabled;

    /// <summary>The game is directed at children (COPPA). Null leaves the AdMob account default.</summary>
    public bool? TagForChildDirectedTreatment { get; set; }

    /// <summary>Treat players as under the age of consent (GDPR). Null leaves the default.</summary>
    public bool? TagForUnderAgeOfConsent { get; set; }

    public MaxAdContentRating MaxAdContentRating { get; set; } = MaxAdContentRating.Unspecified;

    /// <summary>Mute video ads, e.g. when the game's sound is off.</summary>
    public bool MuteAds { get; set; }

    /// <summary>Load the next interstitial / rewarded ad as soon as one closes, and retry failed loads with back-off.</summary>
    public bool AutoReload { get; set; } = true;

    /// <summary>Minimum seconds between two interstitials; ShowInterstitial returns false sooner. 0 = no limit.</summary>
    public float InterstitialCooldownSeconds { get; set; }

    internal AdsConfig Clone()
    {
        var copy = (AdsConfig)MemberwiseClone();
        copy.TestDeviceIds = new List<string>(TestDeviceIds ?? new List<string>());
        return copy;
    }
}

/// <summary>Google's sample ad units: always fill with test ads, never pay. Use them while developing.</summary>
public static class TestAdUnits
{
    public const string AppId = "ca-app-pub-3940256099942544~3347511713";
    public const string Banner = "ca-app-pub-3940256099942544/9214589741";
    public const string Interstitial = "ca-app-pub-3940256099942544/1033173712";
    public const string Rewarded = "ca-app-pub-3940256099942544/5224354917";

    public static string For(AdFormat format) => format switch
    {
        AdFormat.Banner => Banner,
        AdFormat.Interstitial => Interstitial,
        _ => Rewarded,
    };
}
