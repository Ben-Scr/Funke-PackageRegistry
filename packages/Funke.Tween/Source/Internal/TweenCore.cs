using System;

namespace Funke.Tween;

/// <summary>Pooled state behind a <see cref="TweenHandle"/>; handles compare <see cref="Version"/> so a recycled core reads as dead.</summary>
internal abstract class TweenCore
{
    internal int Version;
    internal bool Alive;
    internal bool Killed;
    internal bool Playing;
    internal bool InActiveList;
    internal SequenceCore? Parent;

    internal float Delay;
    internal float Duration;
    internal int Loops;
    internal LoopType LoopType;
    internal Ease Ease;
    internal float Overshoot;
    internal float Period;
    internal Func<float, float>? CustomEase;
    internal Graph? Curve;
    internal bool Unscaled;
    internal float TimeScale;
    internal bool AutoKill;
    internal object? Id;
    internal object? Target;
    internal Entity? Link;

    internal Action? StartCallback;
    internal Action? UpdateCallback;
    internal Action? StepCompleteCallback;
    internal Action? CompleteCallback;
    internal Action? KillCallback;

    internal double Elapsed;
    internal bool Started;
    internal bool Completed;
    internal bool TargetLost;
    internal double LastPosition;
    internal long LastLoop;
    internal long StepsFired;

    internal virtual double LoopDuration => Duration;
    internal double FullDuration => Loops < 0 ? double.PositiveInfinity : Delay + LoopDuration * Loops;

    /// <summary>Captures start values; false when the target is already gone.</summary>
    protected abstract bool Startup();

    protected abstract void Apply(float t01, float eased, long loopIndex, bool silent);

    protected virtual void OnLoopChanged(long previousLoop, long loop, bool forward, bool silent) { }

    protected virtual void OnKilled() { }

    protected abstract void ReturnToPool();

    internal void Activate(float duration)
    {
        Alive = true;
        Killed = false;
        Playing = true;
        Duration = MathF.Max(0f, duration);
        Loops = 1;
        LoopType = LoopType.Restart;
        Ease = Tween.DefaultEase;
        Overshoot = Easing.DefaultOvershoot;
        Period = 0f;
        TimeScale = 1f;
        AutoKill = true;
        LastPosition = -1.0;
    }

    internal void Recycle()
    {
        RecycleChildren();
        Version++;
        Alive = false;
        Killed = false;
        Playing = false;
        InActiveList = false;
        Parent = null;
        Delay = 0f;
        Duration = 0f;
        CustomEase = null;
        Curve = null;
        Unscaled = false;
        Id = null;
        Target = null;
        Link = null;
        StartCallback = UpdateCallback = StepCompleteCallback = CompleteCallback = KillCallback = null;
        Elapsed = 0.0;
        Started = false;
        Completed = false;
        TargetLost = false;
        LastLoop = 0;
        StepsFired = 0;
        ClearValues();
        ReturnToPool();
    }

    protected virtual void RecycleChildren() { }

    protected abstract void ClearValues();

    internal float EvaluateEase(float t01)
    {
        if (Curve != null) return Curve.Evaluate(t01);
        if (CustomEase != null) return CustomEase(t01);
        return Easing.Evaluate(Ease, t01, Overshoot, Period);
    }

    /// <summary>Moves a root tween forward by <paramref name="deltaTime"/> seconds; true once it is finished and should be killed.</summary>
    internal bool Advance(float deltaTime)
    {
        Elapsed += deltaTime;
        Goto(Elapsed, silent: false);
        if (TargetLost) return true;
        if (!Completed || Killed) return Killed;
        Playing = false;
        return AutoKill;
    }

    /// <summary>Puts the tween at <paramref name="position"/> seconds (delay included) and fires the callbacks crossed on the way.</summary>
    internal void Goto(double position, bool silent)
    {
        // Inside a sequence a lost target stays skipped instead of throwing again every frame.
        if (TargetLost)
        {
            LastPosition = position;
            return;
        }
        bool forward = position >= LastPosition;
        double p = position - Delay;
        if (p < 0.0)
        {
            if (!Started)
            {
                LastPosition = position;
                return;
            }
            p = 0.0;
        }

        if (!Started)
        {
            Started = true;
            if (!Startup())
            {
                TargetLost = true;
                return;
            }
            if (!silent) Invoke(StartCallback, "OnStart");
            if (Killed) return;
        }

        double loopDuration = LoopDuration;
        long loopIndex;
        double local;
        bool atEnd;
        if (loopDuration <= 0.0)
        {
            atEnd = true;
            loopIndex = Loops > 0 ? Loops - 1 : 0;
            local = 0.0;
        }
        else if (Loops >= 0 && p >= loopDuration * Loops)
        {
            atEnd = true;
            loopIndex = Math.Max(0, Loops - 1);
            local = loopDuration;
        }
        else
        {
            atEnd = false;
            loopIndex = (long)(p / loopDuration);
            local = p - loopIndex * loopDuration;
        }

        if (loopIndex != LastLoop && LastPosition >= 0.0)
        {
            OnLoopChanged(LastLoop, loopIndex, forward, silent);
            if (Killed || TargetLost) return;
        }

        float t01 = loopDuration > 0.0 ? (float)(local / loopDuration) : 1f;
        if (LoopType == LoopType.Yoyo && (loopIndex & 1) == 1) t01 = 1f - t01;
        Apply(t01, EvaluateEase(t01), loopIndex, silent);
        LastLoop = loopIndex;
        LastPosition = position;
        if (TargetLost || Killed) return;

        if (!silent) Invoke(UpdateCallback, "OnUpdate");

        long steps = atEnd ? Math.Max(0, Loops) : loopIndex;
        if (!silent && forward)
        {
            for (long i = StepsFired; i < steps && !Killed; i++) Invoke(StepCompleteCallback, "OnStepComplete");
        }
        StepsFired = steps;

        if (atEnd)
        {
            if (!Completed)
            {
                Completed = true;
                if (!silent && forward) Invoke(CompleteCallback, "OnComplete");
            }
        }
        else
        {
            Completed = false;
        }
    }

    /// <summary>Jumps to the end; an endless tween finishes its current loop.</summary>
    internal void CompleteNow()
    {
        if (Completed) return;
        if (Loops >= 0)
        {
            Elapsed = FullDuration;
            Goto(Elapsed, silent: false);
            return;
        }

        if (!Started) Goto(Delay, silent: false);
        if (Killed || TargetLost) return;
        long loop = Math.Max(0, LastLoop);
        float t01 = LoopType == LoopType.Yoyo && (loop & 1) == 1 ? 0f : 1f;
        Apply(t01, EvaluateEase(t01), loop, silent: false);
        if (Killed || TargetLost) return;
        Completed = true;
        Invoke(CompleteCallback, "OnComplete");
    }

    internal void Kill(bool complete)
    {
        if (!Alive || Killed) return;
        if (complete) CompleteNow();
        if (Killed) return;
        Killed = true;
        Playing = false;
        OnKilled();
        Invoke(KillCallback, "OnKill");
    }

    internal void Rewind()
    {
        if (Started) Goto(Delay, silent: true);
        Elapsed = 0.0;
        Completed = false;
        StepsFired = 0;
        LastLoop = 0;
        LastPosition = -1.0;
        ResetChildFlags();
    }

    /// <summary>Re-arms callbacks and loop counters so the next pass fires them again.</summary>
    internal virtual void ResetChildFlags() { }

    internal void ResetFlags()
    {
        Completed = false;
        StepsFired = 0;
        LastLoop = 0;
        LastPosition = -1.0;
        ResetChildFlags();
    }

    internal void Invoke(Action? callback, string name)
    {
        if (callback == null) return;
        try { callback(); }
        catch (Exception ex) { Log.Error($"[Funke.Tween] {name} callback threw: {ex}"); }
    }

    /// <summary>Ends the tween after its getter or setter threw; a target whose entity or component is gone ends it quietly.</summary>
    protected void OnApplyFailed(Exception ex)
    {
        TargetLost = true;
        if (ex is InvalidOperationException && Target != null) return;
        Log.Error($"[Funke.Tween] A tween's getter or setter threw, the tween was killed: {ex}");
    }
}
