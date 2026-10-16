using System;
using System.Collections.Generic;

namespace Funke.Tween;

internal sealed class SequenceCore : TweenCore
{
    private enum OpKind : byte
    {
        Append,
        Prepend,
        Join,
        Insert,
        AppendInterval,
        PrependInterval,
        AppendCallback,
        InsertCallback,
    }

    private struct Op
    {
        public OpKind Kind;
        public TweenCore? Tween;
        public Action? Callback;
        public float Time;
    }

    private struct Child
    {
        public TweenCore Tween;
        public double Start;
    }

    private struct Callback
    {
        public Action Action;
        public double Time;
    }

    private readonly List<Op> m_Ops = new();
    private readonly List<Child> m_Children = new();
    private readonly List<Callback> m_Callbacks = new();
    private bool m_Resolved;
    private double m_ResolvedDuration;
    private double m_LastTime = -1.0;

    internal override double LoopDuration
    {
        get
        {
            Resolve();
            return m_ResolvedDuration;
        }
    }

    private void AddTween(OpKind kind, TweenCore tween, float time) => AddOp(new Op { Kind = kind, Tween = Adopt(tween), Time = time });

    internal void Append(TweenCore tween) => AddTween(OpKind.Append, tween, 0f);
    internal void Prepend(TweenCore tween) => AddTween(OpKind.Prepend, tween, 0f);
    internal void Join(TweenCore tween) => AddTween(OpKind.Join, tween, 0f);
    internal void Insert(float time, TweenCore tween) => AddTween(OpKind.Insert, tween, MathF.Max(0f, time));
    internal void AppendInterval(float seconds) => AddOp(new Op { Kind = OpKind.AppendInterval, Time = MathF.Max(0f, seconds) });
    internal void PrependInterval(float seconds) => AddOp(new Op { Kind = OpKind.PrependInterval, Time = MathF.Max(0f, seconds) });
    internal void AppendCallback(Action callback) => AddOp(new Op { Kind = OpKind.AppendCallback, Callback = callback });
    internal void InsertCallback(float time, Action callback) => AddOp(new Op { Kind = OpKind.InsertCallback, Callback = callback, Time = MathF.Max(0f, time) });

    private void AddOp(Op op)
    {
        if (Started) throw new InvalidOperationException("A sequence can't be changed after it started.");
        m_Ops.Add(op);
        m_Resolved = false;
    }

    private TweenCore Adopt(TweenCore tween)
    {
        if (Started) throw new InvalidOperationException("A sequence can't be changed after it started.");
        if (tween == this) throw new ArgumentException("A sequence can't contain itself.");
        if (tween.Parent != null) throw new ArgumentException("The tween is already in a sequence.");
        if (tween.Started) throw new ArgumentException("Add tweens to a sequence right after creating them, before they start.");
        if (tween.Loops < 0)
        {
            Log.Warn("[Funke.Tween] Endless loops inside a sequence play once; loop the sequence instead.");
            tween.Loops = 1;
        }
        tween.Parent = this;
        tween.Playing = true;
        TweenManager.Detach(tween);
        return tween;
    }

    // Placement is resolved lazily so loops and delays set on children after Append still count.
    private void Resolve()
    {
        if (m_Resolved) return;
        m_Resolved = true;
        m_Children.Clear();
        m_Callbacks.Clear();

        double end = 0.0, lastStart = 0.0;
        foreach (Op op in m_Ops)
        {
            switch (op.Kind)
            {
                case OpKind.Append:
                    lastStart = end;
                    end = Place(op.Tween!, end, end);
                    break;
                case OpKind.Prepend:
                {
                    double length = op.Tween!.FullDuration;
                    Shift(length);
                    end += length;
                    lastStart = 0.0;
                    Place(op.Tween!, 0.0, end);
                    break;
                }
                case OpKind.Join:
                    end = Place(op.Tween!, lastStart, end);
                    break;
                case OpKind.Insert:
                    lastStart = op.Time;
                    end = Place(op.Tween!, op.Time, end);
                    break;
                case OpKind.AppendInterval:
                    lastStart = end;
                    end += op.Time;
                    break;
                case OpKind.PrependInterval:
                    Shift(op.Time);
                    end += op.Time;
                    lastStart += op.Time;
                    break;
                case OpKind.AppendCallback:
                    lastStart = end;
                    m_Callbacks.Add(new Callback { Action = op.Callback!, Time = end });
                    break;
                case OpKind.InsertCallback:
                    lastStart = op.Time;
                    m_Callbacks.Add(new Callback { Action = op.Callback!, Time = op.Time });
                    end = Math.Max(end, op.Time);
                    break;
            }
        }

        SortByStart();
        m_ResolvedDuration = end;
    }

    private double Place(TweenCore tween, double start, double end)
    {
        m_Children.Add(new Child { Tween = tween, Start = start });
        return Math.Max(end, start + tween.FullDuration);
    }

    private void Shift(double amount)
    {
        for (int i = 0; i < m_Children.Count; i++)
        {
            Child child = m_Children[i];
            child.Start += amount;
            m_Children[i] = child;
        }
        for (int i = 0; i < m_Callbacks.Count; i++)
        {
            Callback callback = m_Callbacks[i];
            callback.Time += amount;
            m_Callbacks[i] = callback;
        }
    }

    // Stable insertion sort: joined tweens keep their order, later ones win on shared properties.
    private void SortByStart()
    {
        for (int i = 1; i < m_Children.Count; i++)
        {
            Child item = m_Children[i];
            int j = i - 1;
            while (j >= 0 && m_Children[j].Start > item.Start)
            {
                m_Children[j + 1] = m_Children[j];
                j--;
            }
            m_Children[j + 1] = item;
        }
        for (int i = 1; i < m_Callbacks.Count; i++)
        {
            Callback item = m_Callbacks[i];
            int j = i - 1;
            while (j >= 0 && m_Callbacks[j].Time > item.Time)
            {
                m_Callbacks[j + 1] = m_Callbacks[j];
                j--;
            }
            m_Callbacks[j + 1] = item;
        }
    }

    protected override bool Startup()
    {
        Resolve();
        m_LastTime = -1.0;
        return true;
    }

    protected override void OnLoopChanged(long previousLoop, long loop, bool forward, bool silent)
    {
        if (forward && loop > previousLoop)
        {
            bool previousReversed = LoopType == LoopType.Yoyo && (previousLoop & 1) == 1;
            MoveTo(previousReversed ? 0.0 : m_ResolvedDuration, silent);
            if (Killed || TargetLost) return;
            if (LoopType == LoopType.Yoyo) return;
        }
        RestartChildren();
    }

    protected override void Apply(float t01, float eased, long loopIndex, bool silent)
    {
        double time = m_ResolvedDuration > 0.0 ? Math.Clamp(eased * m_ResolvedDuration, 0.0, m_ResolvedDuration) : 0.0;
        MoveTo(time, silent);
    }

    private void MoveTo(double time, bool silent)
    {
        double from = m_LastTime;
        bool forward = time >= from;

        if (forward)
        {
            for (int i = 0; i < m_Children.Count; i++)
            {
                Child child = m_Children[i];
                if (time < child.Start) break;
                double target = Math.Min(time - child.Start, child.Tween.FullDuration);
                if (target == child.Tween.LastPosition) continue;
                child.Tween.Goto(target, silent);
                if (Killed) return;
            }
        }
        else
        {
            for (int i = m_Children.Count - 1; i >= 0; i--)
            {
                Child child = m_Children[i];
                if (!child.Tween.Started) continue;
                double target = Math.Clamp(time - child.Start, 0.0, child.Tween.FullDuration);
                if (target == child.Tween.LastPosition) continue;
                child.Tween.Goto(target, silent);
                if (Killed) return;
            }
        }

        m_LastTime = time;
        if (silent) return;

        // Half-open ranges, so a callback at a yoyo turning point fires once, not twice.
        if (forward)
        {
            for (int i = 0; i < m_Callbacks.Count; i++)
            {
                double at = m_Callbacks[i].Time;
                if (at > from && at <= time) Invoke(m_Callbacks[i].Action, "Sequence callback");
                if (Killed) return;
            }
        }
        else
        {
            for (int i = m_Callbacks.Count - 1; i >= 0; i--)
            {
                double at = m_Callbacks[i].Time;
                if (at >= time && at < from) Invoke(m_Callbacks[i].Action, "Sequence callback");
                if (Killed) return;
            }
        }
    }

    // Later children first, so the earliest child's start value is what remains on shared properties.
    private void RestartChildren()
    {
        for (int i = m_Children.Count - 1; i >= 0; i--)
        {
            TweenCore child = m_Children[i].Tween;
            if (child.Started) child.Goto(0.0, silent: true);
        }
        ResetChildFlags();
    }

    internal override void ResetChildFlags()
    {
        m_LastTime = -1.0;
        for (int i = 0; i < m_Children.Count; i++) m_Children[i].Tween.ResetFlags();
    }

    protected override void OnKilled()
    {
        foreach (Op op in m_Ops) op.Tween?.Kill(complete: false);
    }

    protected override void RecycleChildren()
    {
        foreach (Op op in m_Ops)
        {
            if (op.Tween != null && op.Tween.Alive && op.Tween.Parent == this) op.Tween.Recycle();
        }
    }

    protected override void ClearValues()
    {
        m_Ops.Clear();
        m_Children.Clear();
        m_Callbacks.Clear();
        m_Resolved = false;
        m_ResolvedDuration = 0.0;
        m_LastTime = -1.0;
    }

    protected override void ReturnToPool() => TweenPool<SequenceCore>.Return(this);
}
