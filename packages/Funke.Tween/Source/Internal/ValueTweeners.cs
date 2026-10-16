using System;

namespace Funke.Tween;

/// <summary>Animates one value from a start to an end, through user delegates or the static shortcut delegates plus <see cref="TweenCore.Target"/>.</summary>
internal abstract class ValueTweener<T> : TweenCore where T : struct
{
    internal Func<T>? Getter;
    internal Action<T>? Setter;
    internal Func<object, T>? TargetGetter;
    internal Action<object, T>? TargetSetter;
    internal T EndParam;
    internal T StartValue;
    internal T EndValue;
    internal bool Relative;
    internal bool ValuesFixed;

    protected abstract T Add(T a, T b);
    protected abstract T Subtract(T a, T b);
    protected abstract T Scale(T value, float factor);

    internal T ReadCurrent()
    {
        if (TargetGetter != null) return TargetGetter(Target!);
        if (Getter != null) return Getter();
        return StartValue;
    }

    private void Write(T value)
    {
        if (TargetSetter != null) TargetSetter(Target!, value);
        else Setter?.Invoke(value);
    }

    protected override bool Startup()
    {
        if (ValuesFixed) return true;
        try
        {
            StartValue = ReadCurrent();
        }
        catch (Exception ex)
        {
            OnApplyFailed(ex);
            return false;
        }
        EndValue = Relative ? Add(StartValue, EndParam) : EndParam;
        return true;
    }

    protected override void Apply(float t01, float eased, long loopIndex, bool silent)
    {
        T change = Subtract(EndValue, StartValue);
        T value = Add(StartValue, Scale(change, eased));
        if (LoopType == LoopType.Incremental && loopIndex > 0) value = Add(value, Scale(change, loopIndex));
        try { Write(value); }
        catch (Exception ex) { OnApplyFailed(ex); }
    }

    /// <summary>Plays from the end value (or current + end when relative) back to the current value, applied at once.</summary>
    internal void FromCurrent()
    {
        T current = ReadCurrent();
        StartValue = Relative ? Add(current, EndParam) : EndParam;
        EndValue = current;
        FixAndApplyStart();
    }

    /// <summary>Plays from <paramref name="start"/> to the end value, applied at once.</summary>
    internal void FromValue(T start)
    {
        T current = ReadCurrent();
        StartValue = start;
        EndValue = Relative ? Add(current, EndParam) : EndParam;
        FixAndApplyStart();
    }

    private void FixAndApplyStart()
    {
        ValuesFixed = true;
        try { Write(StartValue); }
        catch (Exception ex) { OnApplyFailed(ex); }
    }

    protected override void ClearValues()
    {
        Getter = null;
        Setter = null;
        TargetGetter = null;
        TargetSetter = null;
        EndParam = default;
        StartValue = default;
        EndValue = default;
        Relative = false;
        ValuesFixed = false;
    }
}

internal sealed class FloatTweener : ValueTweener<float>
{
    protected override float Add(float a, float b) => a + b;
    protected override float Subtract(float a, float b) => a - b;
    protected override float Scale(float value, float factor) => value * factor;
    protected override void ReturnToPool() => TweenPool<FloatTweener>.Return(this);
}

internal sealed class Vector2Tweener : ValueTweener<Vector2>
{
    protected override Vector2 Add(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    protected override Vector2 Subtract(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    protected override Vector2 Scale(Vector2 value, float factor) => new(value.X * factor, value.Y * factor);
    protected override void ReturnToPool() => TweenPool<Vector2Tweener>.Return(this);
}

internal sealed class Vector3Tweener : ValueTweener<Vector3>
{
    protected override Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    protected override Vector3 Subtract(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    protected override Vector3 Scale(Vector3 value, float factor) => new(value.X * factor, value.Y * factor, value.Z * factor);
    protected override void ReturnToPool() => TweenPool<Vector3Tweener>.Return(this);
}

/// <summary>Also carries colors (RGBA); the engine's color properties are <see cref="Vector4"/>.</summary>
internal sealed class Vector4Tweener : ValueTweener<Vector4>
{
    protected override Vector4 Add(Vector4 a, Vector4 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    protected override Vector4 Subtract(Vector4 a, Vector4 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    protected override Vector4 Scale(Vector4 value, float factor) => new(value.X * factor, value.Y * factor, value.Z * factor, value.W * factor);
    protected override void ReturnToPool() => TweenPool<Vector4Tweener>.Return(this);
}

/// <summary>A delayed call: no value, just time and an OnComplete.</summary>
internal sealed class CallbackTweener : TweenCore
{
    protected override bool Startup() => true;
    protected override void Apply(float t01, float eased, long loopIndex, bool silent) { }
    protected override void ClearValues() { }
    protected override void ReturnToPool() => TweenPool<CallbackTweener>.Return(this);
}
