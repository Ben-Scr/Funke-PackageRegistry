using System;
using Funke.Coroutines;

namespace Funke.Tween;

/// <summary>Implemented by <see cref="TweenHandle"/> and <see cref="Sequence"/>; the fluent settings in <see cref="TweenSettings"/> work on both.</summary>
public interface ITween
{
    internal TweenCore? Core { get; }
}

/// <summary>
/// A running tween. Handles are cheap values: once the tween is killed or finished the handle simply reads as inactive and
/// every call on it does nothing, so a stored handle never controls a recycled tween by accident.
/// </summary>
public readonly struct TweenHandle : ITween, IEquatable<TweenHandle>
{
    private readonly TweenCore? m_Core;
    private readonly int m_Version;

    internal TweenHandle(TweenCore core)
    {
        m_Core = core;
        m_Version = core.Version;
    }

    TweenCore? ITween.Core => Resolve();

    internal TweenCore? Resolve() => m_Core != null && m_Core.Alive && m_Core.Version == m_Version ? m_Core : null;

    /// <summary>False once the tween was killed or finished (and auto-killed); <c>default</c> handles are inactive too.</summary>
    public bool IsActive => Resolve() is { Killed: false };

    public bool IsPlaying => Resolve() is { Killed: false, Playing: true, Completed: false };

    public bool IsComplete => Resolve() is { Completed: true };

    /// <summary>Seconds of one loop, without the delay.</summary>
    public float Duration => Resolve() is { } core ? (float)core.LoopDuration : 0f;

    /// <summary>Seconds played so far, delay included.</summary>
    public float Elapsed => Resolve() is { } core ? (float)core.Elapsed : 0f;

    public int CompletedLoops => Resolve() is { } core ? (int)Math.Min(int.MaxValue, core.StepsFired) : 0;

    /// <summary>0..1 over the whole tween (all loops, delay included); the current loop's progress for endless loops.</summary>
    public float Progress
    {
        get
        {
            if (Resolve() is not { } core) return 0f;
            if (core.Completed) return 1f;
            double full = core.FullDuration;
            if (double.IsPositiveInfinity(full))
            {
                double loop = core.LoopDuration;
                double played = core.Elapsed - core.Delay;
                return loop > 0.0 && played > 0.0 ? (float)(played % loop / loop) : 0f;
            }
            return full > 0.0 ? (float)Math.Clamp(core.Elapsed / full, 0.0, 1.0) : 0f;
        }
    }

    /// <summary>Plays from the given end value back to the current one; the start is applied at once. Chain it last.</summary>
    public TweenHandle From()
    {
        switch (Resolve())
        {
            case FloatTweener t: t.FromCurrent(); break;
            case Vector2Tweener t: t.FromCurrent(); break;
            case Vector3Tweener t: t.FromCurrent(); break;
            case Vector4Tweener t: t.FromCurrent(); break;
            case null: break;
            default: throw new InvalidOperationException("From() works on value tweens, not on sequences, shakes or delays.");
        }
        return this;
    }

    /// <summary>Starts at <paramref name="value"/> instead of the current value, applied at once; uniform for Vector2/Vector3 tweens (scale pop-ins).</summary>
    public TweenHandle From(float value)
    {
        switch (Resolve())
        {
            case Vector2Tweener t: t.FromValue(new Vector2(value, value)); return this;
            case Vector3Tweener t: t.FromValue(new Vector3(value, value, value)); return this;
            default: return FromValue<FloatTweener, float>(value);
        }
    }

    public TweenHandle From(Vector2 value) => FromValue<Vector2Tweener, Vector2>(value);

    public TweenHandle From(Vector3 value) => FromValue<Vector3Tweener, Vector3>(value);

    public TweenHandle From(Color value) => FromValue<Vector4Tweener, Vector4>(value);

    private TweenHandle FromValue<TTweener, TValue>(TValue value)
        where TTweener : ValueTweener<TValue>
        where TValue : struct
    {
        TweenCore? core = Resolve();
        if (core == null) return this;
        if (core is not TTweener tweener)
            throw new InvalidOperationException($"From({typeof(TValue).Name}) doesn't match this tween's value type.");
        tweener.FromValue(value);
        return this;
    }

    /// <summary>For coroutines: <c>await handle.WaitForCompletion();</c> resumes once the tween finished or was killed.</summary>
    public WaitUntil WaitForCompletion()
    {
        TweenHandle self = this;
        return new WaitUntil(() => self.Resolve() is not { Killed: false, Completed: false });
    }

    public bool Equals(TweenHandle other) => m_Core == other.m_Core && m_Version == other.m_Version;
    public override bool Equals(object? obj) => obj is TweenHandle other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(m_Core, m_Version);
    public static bool operator ==(TweenHandle a, TweenHandle b) => a.Equals(b);
    public static bool operator !=(TweenHandle a, TweenHandle b) => !a.Equals(b);
}

/// <summary>
/// Tweens and callbacks on one timeline. Build it right after <see cref="Tween.Sequence"/>, before it starts playing next
/// frame; a sequence is a tween itself, so it can loop, ease, delay and nest.
/// </summary>
public readonly struct Sequence : ITween, IEquatable<Sequence>
{
    private readonly SequenceCore? m_Core;
    private readonly int m_Version;

    internal Sequence(SequenceCore core)
    {
        m_Core = core;
        m_Version = core.Version;
    }

    TweenCore? ITween.Core => Resolve();

    private SequenceCore? Resolve() => m_Core != null && m_Core.Alive && m_Core.Version == m_Version ? m_Core : null;

    public bool IsActive => Handle.IsActive;
    public bool IsPlaying => Handle.IsPlaying;
    public bool IsComplete => Handle.IsComplete;
    public float Duration => Handle.Duration;
    public float Elapsed => Handle.Elapsed;
    public int CompletedLoops => Handle.CompletedLoops;
    public float Progress => Handle.Progress;

    /// <summary>The sequence as a plain handle, e.g. to store it next to other tweens.</summary>
    public TweenHandle Handle => Resolve() is { } core ? new TweenHandle(core) : default;

    /// <summary>Adds <paramref name="tween"/> after everything added so far.</summary>
    public Sequence Append(TweenHandle tween) => Add(tween, static (s, t) => s.Append(t));

    /// <summary>Adds <paramref name="tween"/> at the very start, pushing everything else back.</summary>
    public Sequence Prepend(TweenHandle tween) => Add(tween, static (s, t) => s.Prepend(t));

    /// <summary>Plays <paramref name="tween"/> together with the previously added tween, interval or callback.</summary>
    public Sequence Join(TweenHandle tween) => Add(tween, static (s, t) => s.Join(t));

    /// <summary>Plays <paramref name="tween"/> at <paramref name="time"/> seconds into the sequence.</summary>
    public Sequence Insert(float time, TweenHandle tween)
    {
        if (Resolve() is { } core && Child(tween) is { } child) core.Insert(time, child);
        return this;
    }

    public Sequence AppendInterval(float seconds)
    {
        Resolve()?.AppendInterval(seconds);
        return this;
    }

    public Sequence PrependInterval(float seconds)
    {
        Resolve()?.PrependInterval(seconds);
        return this;
    }

    public Sequence AppendCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Resolve()?.AppendCallback(callback);
        return this;
    }

    public Sequence InsertCallback(float time, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Resolve()?.InsertCallback(time, callback);
        return this;
    }

    private Sequence Add(TweenHandle tween, Action<SequenceCore, TweenCore> add)
    {
        if (Resolve() is { } core && Child(tween) is { } child) add(core, child);
        return this;
    }

    private static TweenCore? Child(TweenHandle tween)
    {
        TweenCore? child = tween.Resolve();
        if (child is { Killed: true }) return null;
        return child;
    }

    public static implicit operator TweenHandle(Sequence sequence) => sequence.Handle;

    public bool Equals(Sequence other) => m_Core == other.m_Core && m_Version == other.m_Version;
    public override bool Equals(object? obj) => obj is Sequence other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(m_Core, m_Version);
    public static bool operator ==(Sequence a, Sequence b) => a.Equals(b);
    public static bool operator !=(Sequence a, Sequence b) => !a.Equals(b);
}
