using System;

namespace Funke.Pathfinding3D;

/// <summary>Waypoint motion only; the game's character controller must apply collision-safe movement.</summary>
public sealed class PathFollower3D
{
    private Vector3[] m_Points = Array.Empty<Vector3>();
    private int m_Count, m_Next;
    public bool IsFinished => m_Next >= m_Count;
    public void SetPath(ReadOnlySpan<Vector3> points)
    {
        foreach (var point in points) if (!BuildValidation.Finite(point)) throw new ArgumentException("Waypoints must be finite.");
        if (m_Points.Length < points.Length) m_Points = new Vector3[points.Length];
        points.CopyTo(m_Points); m_Count = points.Length; m_Next = 0;
    }
    public void Clear() { m_Count = 0; m_Next = 0; }
    /// <summary>Returns the next segment target; never skips a turn or remaining waypoint because of a large frame delta.</summary>
    public Vector3 NextPosition(Vector3 current, float speed, float deltaTime, float arrivalDistance = .01f)
    {
        if (!BuildValidation.Finite(current) || !float.IsFinite(speed) || speed < 0 || !float.IsFinite(deltaTime) || deltaTime < 0
            || !float.IsFinite(arrivalDistance) || arrivalDistance < 0)
            throw new ArgumentException("Movement parameters must be finite and nonnegative.");
        while (!IsFinished)
        {
            Vector3 target = m_Points[m_Next];
            double dx = (double)target.X - current.X, dy = (double)target.Y - current.Y, dz = (double)target.Z - current.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance <= arrivalDistance) { ++m_Next; continue; }
            double ratio = Math.Min(1, (double)speed * deltaTime / distance);
            return new Vector3((float)(current.X + dx * ratio), (float)(current.Y + dy * ratio), (float)(current.Z + dz * ratio));
        }
        return current;
    }
}
