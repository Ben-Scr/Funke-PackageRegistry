using System;

namespace Funke.Tween;

/// <summary>Fluent settings and controls for <see cref="TweenHandle"/> and <see cref="Sequence"/>; each returns the same handle.</summary>
public static class TweenSettings
{
    public static T SetEase<T>(this T tween, Ease ease) where T : struct, ITween
    {
        if (tween.Core is { } core)
        {
            core.Ease = ease;
            core.CustomEase = null;
            core.Curve = null;
        }
        return tween;
    }

    /// <summary>Back eases: how far they overshoot (default 1.70158).</summary>
    public static T SetEase<T>(this T tween, Ease ease, float overshoot) where T : struct, ITween
    {
        if (tween.Core is { } core) core.Overshoot = overshoot;
        return tween.SetEase(ease);
    }

    /// <summary>Elastic eases: amplitude (1 = default) and period (0 = default).</summary>
    public static T SetEase<T>(this T tween, Ease ease, float amplitude, float period) where T : struct, ITween
    {
        if (tween.Core is { } core)
        {
            core.Overshoot = amplitude;
            core.Period = period;
        }
        return tween.SetEase(ease);
    }

    /// <summary>Your own easing: maps progress 0..1 to eased progress (0 = start value, 1 = end value).</summary>
    public static T SetEase<T>(this T tween, Func<float, float> ease) where T : struct, ITween
    {
        ArgumentNullException.ThrowIfNull(ease);
        if (tween.Core is { } core)
        {
            core.CustomEase = ease;
            core.Curve = null;
        }
        return tween;
    }

    /// <summary>A curve from the Inspector (a <see cref="Graph"/> script field) as the easing; author it from (0, 0) to (1, 1).</summary>
    public static T SetEase<T>(this T tween, Graph curve) where T : struct, ITween
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (tween.Core is { } core)
        {
            core.Curve = curve;
            core.CustomEase = null;
        }
        return tween;
    }

    /// <summary>Waits this many seconds before starting; start values are read when the delay is over.</summary>
    public static T SetDelay<T>(this T tween, float seconds) where T : struct, ITween
    {
        if (tween.Core is { Started: false } core) core.Delay = MathF.Max(0f, seconds);
        return tween;
    }

    /// <summary>Plays <paramref name="loops"/> times (-1 = endlessly). Yoyo counts each direction as one loop.</summary>
    public static T SetLoops<T>(this T tween, int loops, LoopType type = LoopType.Restart) where T : struct, ITween
    {
        if (tween.Core is { } core)
        {
            core.Loops = loops < 0 ? -1 : Math.Max(1, loops);
            core.LoopType = type;
        }
        return tween;
    }

    /// <summary>Runs on real time, unaffected by <c>Time.TimeScale</c> (pause menus, fades while the game is frozen).</summary>
    public static T SetUnscaledTime<T>(this T tween, bool unscaled = true) where T : struct, ITween
    {
        if (tween.Core is { } core) core.Unscaled = unscaled;
        return tween;
    }

    /// <summary>Speed multiplier for this tween alone.</summary>
    public static T SetTimeScale<T>(this T tween, float timeScale) where T : struct, ITween
    {
        if (tween.Core is { } core) core.TimeScale = MathF.Max(0f, timeScale);
        return tween;
    }

    /// <summary>Treats the end value as an offset from the start value (<c>MoveBy</c>, "rotate 90 more").</summary>
    public static T SetRelative<T>(this T tween, bool relative = true) where T : struct, ITween
    {
        switch (tween.Core)
        {
            case FloatTweener t: t.Relative = relative; break;
            case Vector2Tweener t: t.Relative = relative; break;
            case Vector3Tweener t: t.Relative = relative; break;
            case Vector4Tweener t: t.Relative = relative; break;
        }
        return tween;
    }

    /// <summary>Tags the tween so <see cref="Tween.Kill(object, bool)"/> and friends find it by this id.</summary>
    public static T SetId<T>(this T tween, object? id) where T : struct, ITween
    {
        if (tween.Core is { } core) core.Id = id;
        return tween;
    }

    /// <summary>Kills the tween once <paramref name="entity"/> is destroyed. Component shortcuts link their entity already.</summary>
    public static T SetLink<T>(this T tween, Entity? entity) where T : struct, ITween
    {
        if (tween.Core is { } core) core.Link = entity;
        return tween;
    }

    /// <summary>False keeps a finished tween alive (paused at its end) so it can be restarted; kill it yourself.</summary>
    public static T SetAutoKill<T>(this T tween, bool autoKill) where T : struct, ITween
    {
        if (tween.Core is { } core) core.AutoKill = autoKill;
        return tween;
    }

    /// <summary>Called once, when the tween starts after its delay.</summary>
    public static T OnStart<T>(this T tween, Action? callback) where T : struct, ITween
    {
        if (tween.Core is { } core) core.StartCallback = callback;
        return tween;
    }

    /// <summary>Called every frame the tween updates, after the value was set.</summary>
    public static T OnUpdate<T>(this T tween, Action? callback) where T : struct, ITween
    {
        if (tween.Core is { } core) core.UpdateCallback = callback;
        return tween;
    }

    /// <summary>Called at the end of every loop.</summary>
    public static T OnStepComplete<T>(this T tween, Action? callback) where T : struct, ITween
    {
        if (tween.Core is { } core) core.StepCompleteCallback = callback;
        return tween;
    }

    /// <summary>Called when the last loop finishes (never for endless loops, unless completed by hand).</summary>
    public static T OnComplete<T>(this T tween, Action? callback) where T : struct, ITween
    {
        if (tween.Core is { } core) core.CompleteCallback = callback;
        return tween;
    }

    /// <summary>Called when the tween is killed, by hand, after finishing or because its entity was destroyed.</summary>
    public static T OnKill<T>(this T tween, Action? callback) where T : struct, ITween
    {
        if (tween.Core is { } core) core.KillCallback = callback;
        return tween;
    }

    public static T Pause<T>(this T tween) where T : struct, ITween
    {
        if (Root(tween) is { } core) core.Playing = false;
        return tween;
    }

    public static T Play<T>(this T tween) where T : struct, ITween
    {
        if (Root(tween) is { Completed: false } core) core.Playing = true;
        return tween;
    }

    public static T TogglePause<T>(this T tween) where T : struct, ITween
    {
        if (Root(tween) is { } core) core.Playing = !core.Playing && !core.Completed;
        return tween;
    }

    /// <summary>Back to the start values (delay included) and plays again.</summary>
    public static T Restart<T>(this T tween, bool includeDelay = true) where T : struct, ITween
    {
        if (Root(tween) is not { } core) return tween;
        core.Rewind();
        if (!includeDelay) core.Elapsed = core.Delay;
        core.Playing = true;
        return tween;
    }

    /// <summary>Back to the start values, paused.</summary>
    public static T Rewind<T>(this T tween) where T : struct, ITween
    {
        if (Root(tween) is not { } core) return tween;
        core.Rewind();
        core.Playing = false;
        return tween;
    }

    /// <summary>Jumps to <paramref name="time"/> seconds (delay included), firing the callbacks on the way.</summary>
    public static T Goto<T>(this T tween, float time, bool andPlay = false) where T : struct, ITween
    {
        if (Root(tween) is not { } core) return tween;
        core.Elapsed = Math.Max(0.0, time);
        core.Goto(core.Elapsed, silent: false);
        core.Playing = andPlay && !core.Completed;
        return tween;
    }

    /// <summary>Jumps to the end values and fires OnComplete; an endless tween ends its current loop.</summary>
    public static void Complete<T>(this T tween) where T : struct, ITween
    {
        if (Root(tween) is not { } core) return;
        core.CompleteNow();
        if (!core.Completed || core.Killed) return;
        core.Playing = false;
        if (core.AutoKill) core.Kill(complete: false);
    }

    /// <summary>Stops the tween where it is (or at its end with <paramref name="complete"/>). Safe on inactive handles.</summary>
    public static void Kill<T>(this T tween, bool complete = false) where T : struct, ITween
    {
        Root(tween)?.Kill(complete);
    }

    // Tweens inside a sequence follow the sequence's clock; controlling them directly would desync it.
    private static TweenCore? Root<T>(T tween) where T : struct, ITween
    {
        TweenCore? core = tween.Core;
        if (core == null || core.Killed) return null;
        if (core.Parent != null)
        {
            Log.Warn("[Funke.Tween] This tween is part of a sequence; control the sequence instead.");
            return null;
        }
        return core;
    }
}
