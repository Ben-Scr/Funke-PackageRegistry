using System;

namespace Funke.Tween;

/// <summary>
/// DOTween-style tweening. Component shortcuts (<c>transform.MoveTo(...)</c>, <c>image.FadeTo(...)</c>) cover the common
/// cases; <see cref="To(Func{float}, Action{float}, float, float)"/> animates anything with a getter and setter.
/// <code>
/// transform.MoveTo(new Vector2(3f, 0f), 0.5f).SetEase(Ease.OutBack);
/// Tween.Sequence().Append(panel.ScaleTo(1f, 0.2f)).AppendInterval(2f).Append(panel.ScaleTo(0f, 0.2f));
/// </code>
/// Tweens start next frame, update before the scripts' OnUpdate, and recycle their memory when they end.
/// </summary>
public static class Tween
{
    /// <summary>Ease for new tweens (default OutQuad).</summary>
    public static Ease DefaultEase { get; set; } = Ease.OutQuad;

    /// <summary>Speed multiplier for every tween, on top of <c>Time.TimeScale</c> for scaled tweens.</summary>
    public static float GlobalTimeScale
    {
        get => TweenManager.GlobalTimeScale;
        set => TweenManager.GlobalTimeScale = MathF.Max(0f, value);
    }

    /// <summary>
    /// True (default): tweens update themselves every frame. False: nothing moves until you call <see cref="Update"/>,
    /// e.g. from a fixed-step simulation.
    /// </summary>
    public static bool AutoUpdate
    {
        get => TweenManager.AutoUpdate;
        set => TweenManager.AutoUpdate = value;
    }

    /// <summary>Running tweens and sequences (not counting tweens inside sequences).</summary>
    public static int ActiveCount => TweenManager.ActiveCount;

    /// <summary>Advances every tween by hand; for <see cref="AutoUpdate"/> = false.</summary>
    public static void Update(float deltaTime, float unscaledDeltaTime) => TweenManager.Update(deltaTime, unscaledDeltaTime);

    /// <summary>Fills the pools up front so the first tweens of a level allocate nothing.</summary>
    public static void Prewarm(int tweens = 32, int sequences = 8)
    {
        TweenPool<FloatTweener>.Prewarm(tweens);
        TweenPool<Vector2Tweener>.Prewarm(tweens);
        TweenPool<Vector3Tweener>.Prewarm(tweens);
        TweenPool<Vector4Tweener>.Prewarm(tweens);
        TweenPool<SequenceCore>.Prewarm(sequences);
    }

    public static TweenHandle To(Func<float> getter, Action<float> setter, float endValue, float duration)
        => Create<FloatTweener, float>(getter, setter, endValue, duration);

    public static TweenHandle To(Func<Vector2> getter, Action<Vector2> setter, Vector2 endValue, float duration)
        => Create<Vector2Tweener, Vector2>(getter, setter, endValue, duration);

    public static TweenHandle To(Func<Vector3> getter, Action<Vector3> setter, Vector3 endValue, float duration)
        => Create<Vector3Tweener, Vector3>(getter, setter, endValue, duration);

    public static TweenHandle To(Func<Vector4> getter, Action<Vector4> setter, Vector4 endValue, float duration)
        => Create<Vector4Tweener, Vector4>(getter, setter, endValue, duration);

    public static TweenHandle To(Func<Color> getter, Action<Color> setter, Color endValue, float duration)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        return Create<Vector4Tweener, Vector4>(() => getter(), value => setter(value), endValue, duration);
    }

    /// <summary>Animates a number from <paramref name="from"/> to <paramref name="to"/> and hands each value to <paramref name="onUpdate"/>.</summary>
    public static TweenHandle Value(float from, float to, float duration, Action<float> onUpdate)
        => FixedValue<FloatTweener, float>(from, to, duration, onUpdate);

    public static TweenHandle Value(Vector2 from, Vector2 to, float duration, Action<Vector2> onUpdate)
        => FixedValue<Vector2Tweener, Vector2>(from, to, duration, onUpdate);

    public static TweenHandle Value(Vector3 from, Vector3 to, float duration, Action<Vector3> onUpdate)
        => FixedValue<Vector3Tweener, Vector3>(from, to, duration, onUpdate);

    public static TweenHandle Value(Color from, Color to, float duration, Action<Color> onUpdate)
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        return FixedValue<Vector4Tweener, Vector4>(from, to, duration, value => onUpdate(value));
    }

    /// <summary>Calls <paramref name="callback"/> after <paramref name="seconds"/>; kill the handle to cancel it.</summary>
    public static TweenHandle Delay(float seconds, Action callback, bool unscaledTime = false)
    {
        ArgumentNullException.ThrowIfNull(callback);
        CallbackTweener core = TweenManager.Spawn<CallbackTweener>(seconds);
        core.Ease = Ease.Linear;
        core.CompleteCallback = callback;
        core.Unscaled = unscaledTime;
        return new TweenHandle(core);
    }

    /// <summary>An empty sequence; fill it with Append / Join / Insert right away.</summary>
    public static Sequence Sequence()
    {
        SequenceCore core = TweenManager.Spawn<SequenceCore>(0f);
        core.Ease = Ease.Linear;
        return new Sequence(core);
    }

    /// <summary>Random jitter around the current value that settles back (<paramref name="randomness"/> in degrees, 0..180).</summary>
    public static TweenHandle Shake(Func<Vector3> getter, Action<Vector3> setter, float duration, Vector3 strength,
        int vibrato = 10, float randomness = 90f, bool fadeOut = true)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ShakeTweener core = TweenManager.Spawn<ShakeTweener>(duration);
        core.Getter = getter;
        core.Setter = setter;
        core.Configure(false, strength, vibrato, randomness, fadeOut, 1f, 3);
        return new TweenHandle(core);
    }

    public static TweenHandle Shake(Func<float> getter, Action<float> setter, float duration, float strength,
        int vibrato = 10, bool fadeOut = true)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ShakeTweener core = TweenManager.Spawn<ShakeTweener>(duration);
        core.Getter = () => new Vector3(getter(), 0f, 0f);
        core.Setter = value => setter(value.X);
        core.Configure(false, new Vector3(strength, 0f, 0f), vibrato, 0f, fadeOut, 1f, 1);
        return new TweenHandle(core);
    }

    /// <summary>A springy kick towards <paramref name="punch"/> that swings back (<paramref name="elasticity"/> 0..1 = how far past the start).</summary>
    public static TweenHandle Punch(Func<Vector3> getter, Action<Vector3> setter, Vector3 punch, float duration,
        int vibrato = 10, float elasticity = 1f)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ShakeTweener core = TweenManager.Spawn<ShakeTweener>(duration);
        core.Getter = getter;
        core.Setter = setter;
        core.Configure(true, punch, vibrato, 0f, true, elasticity, 3);
        return new TweenHandle(core);
    }

    public static TweenHandle Punch(Func<float> getter, Action<float> setter, float punch, float duration,
        int vibrato = 10, float elasticity = 1f)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ShakeTweener core = TweenManager.Spawn<ShakeTweener>(duration);
        core.Getter = () => new Vector3(getter(), 0f, 0f);
        core.Setter = value => setter(value.X);
        core.Configure(true, new Vector3(punch, 0f, 0f), vibrato, 0f, true, elasticity, 1);
        return new TweenHandle(core);
    }

    /// <summary>
    /// Kills every tween whose id equals <paramref name="target"/>, that animates <paramref name="target"/> (a component),
    /// or that is linked to it (an entity). Returns how many were killed.
    /// </summary>
    public static int Kill(object target, bool complete = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        return complete
            ? TweenManager.ForEachMatching(target, static tween => tween.Kill(complete: true))
            : TweenManager.ForEachMatching(target, static tween => tween.Kill(complete: false));
    }

    /// <summary>Jumps the matching tweens to their end (see <see cref="Kill(object, bool)"/> for what matches).</summary>
    public static int Complete(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return TweenManager.ForEachMatching(target, static tween => new TweenHandle(tween).Complete());
    }

    public static int Pause(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return TweenManager.ForEachMatching(target, static tween => tween.Playing = false);
    }

    public static int Play(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return TweenManager.ForEachMatching(target, static tween => tween.Playing = !tween.Completed);
    }

    /// <summary>True while any tween matches <paramref name="target"/> (see <see cref="Kill(object, bool)"/>).</summary>
    public static bool IsTweening(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return TweenManager.ForEachMatching(target, static _ => { }) > 0;
    }

    public static void KillAll(bool complete = false)
    {
        if (complete) TweenManager.ForEachRoot(static tween => tween.Kill(complete: true));
        else TweenManager.ForEachRoot(static tween => tween.Kill(complete: false));
    }

    public static void PauseAll() => TweenManager.ForEachRoot(static tween => tween.Playing = false);

    public static void PlayAll() => TweenManager.ForEachRoot(static tween => tween.Playing = !tween.Completed);

    internal static TweenHandle Create<TTweener, TValue>(Func<TValue> getter, Action<TValue> setter, TValue endValue, float duration)
        where TTweener : ValueTweener<TValue>, new()
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        TTweener core = TweenManager.Spawn<TTweener>(duration);
        core.Getter = getter;
        core.Setter = setter;
        core.EndParam = endValue;
        return new TweenHandle(core);
    }

    private static TweenHandle FixedValue<TTweener, TValue>(TValue from, TValue to, float duration, Action<TValue> onUpdate)
        where TTweener : ValueTweener<TValue>, new()
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        TTweener core = TweenManager.Spawn<TTweener>(duration);
        core.Setter = onUpdate;
        core.StartValue = from;
        core.EndValue = to;
        core.EndParam = to;
        core.ValuesFixed = true;
        return new TweenHandle(core);
    }

    /// <summary>Shortcut plumbing: the setter delegates are static, so creating a component tween allocates nothing once pools are warm.</summary>
    internal static TweenHandle Target<TTweener, TValue>(Component target, Func<object, TValue> getter, Action<object, TValue> setter,
        TValue endValue, float duration)
        where TTweener : ValueTweener<TValue>, new()
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(target);
        TTweener core = TweenManager.Spawn<TTweener>(duration);
        core.Target = target;
        core.Link = target.Entity;
        core.TargetGetter = getter;
        core.TargetSetter = setter;
        core.EndParam = endValue;
        return new TweenHandle(core);
    }

    internal static TweenHandle TargetShake(Component target, Func<object, Vector3> getter, Action<object, Vector3> setter,
        float duration, bool punch, Vector3 strength, int vibrato, float randomness, bool fadeOut, float elasticity, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ShakeTweener core = TweenManager.Spawn<ShakeTweener>(duration);
        core.Target = target;
        core.Link = target.Entity;
        core.TargetGetter = getter;
        core.TargetSetter = setter;
        core.Configure(punch, strength, vibrato, randomness, fadeOut, elasticity, dimensions);
        return new TweenHandle(core);
    }
}
