using System;

namespace Funke.Tween;

/// <summary>Robert Penner's easing equations. In = slow start, Out = slow end, InOut = both.</summary>
public enum Ease
{
    Linear,
    InSine, OutSine, InOutSine,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InQuart, OutQuart, InOutQuart,
    InQuint, OutQuint, InOutQuint,
    InExpo, OutExpo, InOutExpo,
    InCirc, OutCirc, InOutCirc,
    InBack, OutBack, InOutBack,
    InElastic, OutElastic, InOutElastic,
    InBounce, OutBounce, InOutBounce,
}

/// <summary>What a tween does at the end of each loop.</summary>
public enum LoopType
{
    /// <summary>Jumps back to the start value.</summary>
    Restart,
    /// <summary>Plays forward, then backward (a loop count of 2 is one round trip).</summary>
    Yoyo,
    /// <summary>Continues from the end value, so each loop adds the same change again (endless spinning, stairs).</summary>
    Incremental,
}

/// <summary>The easing functions behind <see cref="Ease"/>, for use outside tweens.</summary>
public static class Easing
{
    public const float DefaultOvershoot = 1.70158f;

    /// <summary>
    /// Eased progress for <paramref name="t"/> in [0, 1]. <paramref name="overshoot"/> is the Back overshoot;
    /// for Elastic it is the amplitude and <paramref name="period"/> the period (0 = default).
    /// </summary>
    public static float Evaluate(Ease ease, float t, float overshoot = DefaultOvershoot, float period = 0f)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;

        switch (ease)
        {
            case Ease.Linear: return t;

            case Ease.InSine: return 1f - MathF.Cos(t * MathF.PI * 0.5f);
            case Ease.OutSine: return MathF.Sin(t * MathF.PI * 0.5f);
            case Ease.InOutSine: return -(MathF.Cos(MathF.PI * t) - 1f) * 0.5f;

            case Ease.InQuad: return t * t;
            case Ease.OutQuad: return t * (2f - t);
            case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Pow2(-2f * t + 2f) * 0.5f;

            case Ease.InCubic: return t * t * t;
            case Ease.OutCubic: return 1f - Pow3(1f - t);
            case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Pow3(-2f * t + 2f) * 0.5f;

            case Ease.InQuart: return Pow2(t * t);
            case Ease.OutQuart: return 1f - Pow2(Pow2(1f - t));
            case Ease.InOutQuart: return t < 0.5f ? 8f * Pow2(t * t) : 1f - Pow2(Pow2(-2f * t + 2f)) * 0.5f;

            case Ease.InQuint: return Pow2(t * t) * t;
            case Ease.OutQuint: return 1f - Pow2(Pow2(1f - t)) * (1f - t);
            case Ease.InOutQuint:
                return t < 0.5f ? 16f * Pow2(t * t) * t : 1f - Pow2(Pow2(-2f * t + 2f)) * (-2f * t + 2f) * 0.5f;

            case Ease.InExpo: return MathF.Pow(2f, 10f * t - 10f);
            case Ease.OutExpo: return 1f - MathF.Pow(2f, -10f * t);
            case Ease.InOutExpo:
                return t < 0.5f ? MathF.Pow(2f, 20f * t - 10f) * 0.5f : (2f - MathF.Pow(2f, -20f * t + 10f)) * 0.5f;

            case Ease.InCirc: return 1f - MathF.Sqrt(1f - t * t);
            case Ease.OutCirc: return MathF.Sqrt(1f - Pow2(t - 1f));
            case Ease.InOutCirc:
                return t < 0.5f
                    ? (1f - MathF.Sqrt(1f - Pow2(2f * t))) * 0.5f
                    : (MathF.Sqrt(1f - Pow2(-2f * t + 2f)) + 1f) * 0.5f;

            case Ease.InBack: return (overshoot + 1f) * t * t * t - overshoot * t * t;
            case Ease.OutBack: return 1f + (overshoot + 1f) * Pow3(t - 1f) + overshoot * Pow2(t - 1f);
            case Ease.InOutBack:
            {
                float s = overshoot * 1.525f;
                return t < 0.5f
                    ? Pow2(2f * t) * ((s + 1f) * 2f * t - s) * 0.5f
                    : (Pow2(2f * t - 2f) * ((s + 1f) * (t * 2f - 2f) + s) + 2f) * 0.5f;
            }

            case Ease.InElastic: return 1f - Elastic(1f - t, overshoot, period > 0f ? period : 0.3f);
            case Ease.OutElastic: return Elastic(t, overshoot, period > 0f ? period : 0.3f);
            case Ease.InOutElastic:
            {
                float p = period > 0f ? period : 0.45f;
                return t < 0.5f
                    ? (1f - Elastic(1f - 2f * t, overshoot, p)) * 0.5f
                    : (1f + Elastic(2f * t - 1f, overshoot, p)) * 0.5f;
            }

            case Ease.InBounce: return 1f - Bounce(1f - t);
            case Ease.OutBounce: return Bounce(t);
            case Ease.InOutBounce: return t < 0.5f ? (1f - Bounce(1f - 2f * t)) * 0.5f : (1f + Bounce(2f * t - 1f)) * 0.5f;
        }
        return t;
    }

    private static float Pow2(float x) => x * x;
    private static float Pow3(float x) => x * x * x;

    // OutElastic; amplitudes below 1 would not reach the end value, so they behave like 1 (Penner).
    private static float Elastic(float t, float amplitude, float period)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        float a = amplitude, s;
        if (a < 1f) { a = 1f; s = period / 4f; }
        else s = period / (2f * MathF.PI) * MathF.Asin(1f / a);
        return a * MathF.Pow(2f, -10f * t) * MathF.Sin((t - s) * (2f * MathF.PI) / period) + 1f;
    }

    private static float Bounce(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1f / d) return n * t * t;
        if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
        if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
        t -= 2.625f / d;
        return n * t * t + 0.984375f;
    }
}
