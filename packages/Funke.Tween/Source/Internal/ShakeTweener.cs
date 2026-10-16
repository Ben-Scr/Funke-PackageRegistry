using System;

namespace Funke.Tween;

/// <summary>
/// Shake (random jitter) or punch (springy kick) applied as an offset on top of the current value, so the target can
/// move meanwhile and ends where it would be without the effect.
/// </summary>
internal sealed class ShakeTweener : TweenCore
{
    internal Func<Vector3>? Getter;
    internal Action<Vector3>? Setter;
    internal Func<object, Vector3>? TargetGetter;
    internal Action<object, Vector3>? TargetSetter;
    internal bool IsPunch;
    internal Vector3 Strength;
    internal int Vibrato;
    internal float Randomness;
    internal bool FadeOut;
    internal float Elasticity;
    internal int Dimensions;
    internal uint Seed;

    private Vector3 m_LastOffset;
    private Vector3 m_LastWritten;
    private bool m_HasWritten;
    private Vector3 m_Axis;

    private static uint s_NextSeed = 0x9E3779B9u;

    internal void Configure(bool punch, Vector3 strength, int vibrato, float randomness, bool fadeOut, float elasticity, int dimensions)
    {
        IsPunch = punch;
        Strength = strength;
        Vibrato = Math.Max(1, vibrato);
        Randomness = Math.Clamp(randomness, 0f, 180f);
        FadeOut = fadeOut;
        Elasticity = Math.Clamp(elasticity, 0f, 1f);
        Dimensions = Math.Clamp(dimensions, 1, 3);
        s_NextSeed = s_NextSeed * 1664525u + 1013904223u;
        Seed = s_NextSeed;
        Ease = Ease.Linear;
    }

    private Vector3 Read() => TargetGetter != null ? TargetGetter(Target!) : Getter != null ? Getter() : default;

    private void Write(Vector3 value)
    {
        if (TargetSetter != null) TargetSetter(Target!, value);
        else Setter?.Invoke(value);
    }

    protected override bool Startup()
    {
        m_HasWritten = false;
        m_Axis = RandomUnit(0xA5A5u);
        return true;
    }

    protected override void Apply(float t01, float eased, long loopIndex, bool silent)
    {
        Vector3 offset = IsPunch ? PunchOffset(eased) : ShakeOffset(eased);
        try
        {
            Vector3 current = Read();
            // Someone else moved the target since the last write: its new value is the base, not current minus our offset.
            Vector3 basis = m_HasWritten && Near(current, m_LastWritten) ? Sub(current, m_LastOffset) : current;
            Vector3 written = Add(basis, offset);
            Write(written);
            m_LastWritten = written;
            m_LastOffset = offset;
            m_HasWritten = true;
        }
        catch (Exception ex)
        {
            OnApplyFailed(ex);
        }
    }

    protected override void OnKilled()
    {
        if (!m_HasWritten || TargetLost) return;
        try
        {
            Vector3 current = Read();
            if (Near(current, m_LastWritten)) Write(Sub(current, m_LastOffset));
        }
        catch (Exception)
        {
            // The target went away together with the tween; nothing to restore.
        }
        m_HasWritten = false;
    }

    private Vector3 PunchOffset(float t)
    {
        float wave = MathF.Sin(t * MathF.PI * Vibrato);
        if (wave < 0f) wave *= Elasticity;
        float amount = wave * (1f - t);
        return new Vector3(Strength.X * amount, Strength.Y * amount, Strength.Z * amount);
    }

    private Vector3 ShakeOffset(float t)
    {
        float position = t * Vibrato;
        int step = Math.Min((int)position, Vibrato - 1);
        float blend = position - step;
        blend = blend * blend * (3f - 2f * blend);
        Vector3 a = Direction(step);
        Vector3 b = Direction(step + 1);
        float envelope = FadeOut ? 1f - t : 1f;
        return new Vector3(
            (a.X + (b.X - a.X) * blend) * Strength.X * envelope,
            (a.Y + (b.Y - a.Y) * blend) * Strength.Y * envelope,
            (a.Z + (b.Z - a.Z) * blend) * Strength.Z * envelope);
    }

    // Step 0 and the last step are the rest position, so the shake starts and ends without a jump.
    private Vector3 Direction(int step)
    {
        if (step <= 0 || step >= Vibrato) return default;
        float sign = (step & 1) == 1 ? 1f : -1f;
        float jitter = (Random01((uint)step) * 2f - 1f) * Randomness * (MathF.PI / 180f);
        float reach = 0.6f + 0.4f * Random01((uint)step + 7919u);
        switch (Dimensions)
        {
            case 1:
                return new Vector3(sign * reach, 0f, 0f);
            case 2:
            {
                float baseAngle = m_Axis.X * MathF.PI;
                float angle = baseAngle + (sign > 0f ? 0f : MathF.PI) + jitter;
                return new Vector3(MathF.Cos(angle) * reach, MathF.Sin(angle) * reach, 0f);
            }
            default:
            {
                Vector3 random = RandomUnit((uint)step * 2654435761u);
                float w = Randomness / 180f;
                Vector3 d = new(
                    m_Axis.X * sign * (1f - w) + random.X * w,
                    m_Axis.Y * sign * (1f - w) + random.Y * w,
                    m_Axis.Z * sign * (1f - w) + random.Z * w);
                float length = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                if (length < 1e-4f) return new Vector3(m_Axis.X * sign * reach, m_Axis.Y * sign * reach, m_Axis.Z * sign * reach);
                float s = reach / length;
                return new Vector3(d.X * s, d.Y * s, d.Z * s);
            }
        }
    }

    private float Random01(uint salt)
    {
        uint h = Seed ^ (salt * 0x27D4EB2Du);
        h ^= h >> 15;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    private Vector3 RandomUnit(uint salt)
    {
        float z = Random01(salt) * 2f - 1f;
        float angle = Random01(salt + 1u) * MathF.PI * 2f;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, z);
    }

    private static Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static Vector3 Sub(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static bool Near(Vector3 a, Vector3 b)
    {
        float tolerance = 1e-4f * MathF.Max(1f, MathF.Max(MathF.Abs(a.X), MathF.Max(MathF.Abs(a.Y), MathF.Abs(a.Z))));
        return MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance && MathF.Abs(a.Z - b.Z) <= tolerance;
    }

    protected override void ClearValues()
    {
        Getter = null;
        Setter = null;
        TargetGetter = null;
        TargetSetter = null;
        m_HasWritten = false;
        m_LastOffset = default;
        m_LastWritten = default;
    }

    protected override void ReturnToPool() => TweenPool<ShakeTweener>.Return(this);
}
