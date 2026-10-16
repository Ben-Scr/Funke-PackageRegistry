using System;
using System.Collections.Generic;

namespace Funke.Networking.Internal;

/// <summary>Position, Euler rotation in degrees and scale; 2D uses X/Y and rotation Z.</summary>
internal struct TransformState
{
    internal Vector3 Position;
    internal Vector3 Rotation;
    internal Vector3 Scale;

    internal static TransformState Lerp(in TransformState a, in TransformState b, float t) => new()
    {
        Position = a.Position + (b.Position - a.Position) * t,
        Rotation = new Vector3(LerpAngle(a.Rotation.X, b.Rotation.X, t), LerpAngle(a.Rotation.Y, b.Rotation.Y, t), LerpAngle(a.Rotation.Z, b.Rotation.Z, t)),
        Scale = a.Scale + (b.Scale - a.Scale) * t,
    };

    internal static float DeltaAngle(float from, float to)
    {
        float delta = (to - from) % 360f;
        if (delta > 180f) delta -= 360f;
        else if (delta < -180f) delta += 360f;
        return delta;
    }

    private static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * t;
}

/// <summary>
/// Maps a peer's clock to ours. The smallest observed (local - remote) difference is one-way latency plus clock
/// offset at its best; late packets only raise the estimate slowly, so jitter doesn't shake the playback.
/// </summary>
internal sealed class ClockSync
{
    private const double k_Rise = 0.02;
    private double m_Offset;
    private bool m_HasOffset;

    internal void Observe(double localNow, double remoteTime)
    {
        double sample = localNow - remoteTime;
        if (!m_HasOffset || sample < m_Offset)
        {
            m_Offset = sample;
            m_HasOffset = true;
        }
        else
        {
            m_Offset += (sample - m_Offset) * k_Rise;
        }
    }

    internal bool HasOffset => m_HasOffset;

    /// <summary>The remote clock "now", as far as packets let us know it.</summary>
    internal double RemoteNow(double localNow) => localNow - m_Offset;
}

/// <summary>Received states of one object in sender time; sampled a little in the past so there is always a pair to blend.</summary>
internal sealed class SnapshotBuffer
{
    private const int k_Capacity = 32;
    private readonly List<(double Time, TransformState State)> m_Items = new(k_Capacity);
    private double m_LastTime = double.NegativeInfinity;

    internal int Count => m_Items.Count;
    internal int Source { get; private set; } = -1;

    internal void Clear()
    {
        m_Items.Clear();
        m_LastTime = double.NegativeInfinity;
        Source = -1;
    }

    /// <summary>Older or duplicate states (reordered UDP) are dropped.</summary>
    internal bool Add(int source, double time, in TransformState state, double sendInterval)
    {
        if (source != Source)
        {
            Clear();   // authority moved: the old sender's clock means nothing for the new one
            Source = source;
        }
        if (time <= m_LastTime) return false;
        // After a pause the sender resumes from a state it stopped sending; repeat it one interval earlier,
        // otherwise the first blend would stretch across the whole pause.
        if (m_Items.Count > 0 && time - m_LastTime > sendInterval * 3)
            m_Items.Add((time - sendInterval, m_Items[^1].State));
        m_Items.Add((time, state));
        m_LastTime = time;
        while (m_Items.Count > k_Capacity) m_Items.RemoveAt(0);
        return true;
    }

    /// <summary>State at <paramref name="renderTime"/>; holds the newest state when the buffer runs dry.</summary>
    internal bool Sample(double renderTime, float teleportDistance, out TransformState state)
    {
        state = default;
        if (m_Items.Count == 0) return false;
        while (m_Items.Count >= 2 && m_Items[1].Time <= renderTime) m_Items.RemoveAt(0);
        (double timeA, TransformState a) = m_Items[0];
        if (m_Items.Count == 1 || renderTime <= timeA)
        {
            state = a;
            return true;
        }
        (double timeB, TransformState b) = m_Items[1];
        if (teleportDistance > 0 && Vector3.Distance(a.Position, b.Position) > teleportDistance)
        {
            state = a;   // jumps to b once render time reaches it
            return true;
        }
        float t = (float)Math.Clamp((renderTime - timeA) / (timeB - timeA), 0, 1);
        state = TransformState.Lerp(a, b, t);
        return true;
    }
}
