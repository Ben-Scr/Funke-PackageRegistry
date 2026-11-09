using System;
using Funke.Physics;

namespace Funke.Pathfinding3D;

public readonly struct GroundHit
{
    public readonly Vector3 Point, Normal;
    public GroundHit(Vector3 point, Vector3 normal) { Point = point; Normal = normal; }
}
public delegate bool GroundRaycast(Vector3 origin, Vector3 direction, float distance, out GroundHit hit);

public static class PhysicsGroundBaker
{
    private static bool Cast(Vector3 origin, Vector3 direction, float distance, out GroundHit hit)
    {
        bool found = Physics3D.Raycast(origin, direction, out var result, distance);
        hit = new GroundHit(result.Point, result.Normal); return found;
    }
    /// <summary>Call on the scene thread in Play mode after physics sync. Clearance must validate the agent's swept volume.</summary>
    public static NavigationMap3D Bake(Vector3 minimum, float cellSize, int width, int depth,
        float probeTop, float probeBottom, float agentHeight, Func<Vector3, Vector3, bool> clearance,
        float maxStep = .4f, float maxSlopeDegrees = 45, float agentRadius = 0, GroundRaycast? raycast = null)
    {
        ArgumentNullException.ThrowIfNull(clearance);
        int count = BuildValidation.Grid(minimum, cellSize, width, 1, depth, agentRadius);
        if (!float.IsFinite(probeTop) || !float.IsFinite(probeBottom) || probeTop <= probeBottom || !float.IsFinite(probeTop - probeBottom)
            || !float.IsFinite(agentHeight) || agentHeight <= 0 || !float.IsFinite(maxSlopeDegrees) || maxSlopeDegrees < 0 || maxSlopeDegrees >= 90
            || !float.IsFinite(maxStep) || maxStep < 0)
            throw new ArgumentException("Invalid ground probing parameters.");
        raycast ??= Cast;
        var heights = new float[count]; var costs = new float[count];
        double slopeCos = Math.Cos(maxSlopeDegrees * Math.PI / 180);
        for (int z = 0; z < depth; ++z) for (int x = 0; x < width; ++x)
        {
            int i = x + z * width;
            var origin = new Vector3(minimum.X + (x + .5f) * cellSize, probeTop, minimum.Z + (z + .5f) * cellSize);
            if (!raycast(origin, Vector3.Down, probeTop - probeBottom, out var hit)) continue;
            if (!BuildValidation.Finite(hit.Point) || !BuildValidation.Finite(hit.Normal)) continue;
            double length = Math.Sqrt((double)hit.Normal.X * hit.Normal.X + (double)hit.Normal.Y * hit.Normal.Y + (double)hit.Normal.Z * hit.Normal.Z);
            if (length == 0 || hit.Normal.Y / length < slopeCos || hit.Point.Y < probeBottom || hit.Point.Y > probeTop) continue;
            heights[i] = hit.Point.Y;
            var headOrigin = hit.Point + Vector3.Up * .01f;
            if (!BuildValidation.Finite(headOrigin + Vector3.Up * agentHeight)) continue;
            if (raycast(headOrigin, Vector3.Up, agentHeight, out _)) continue;
            if (clearance(hit.Point, hit.Point)) costs[i] = 1;
        }
        return GroundSurface.FromHeights(minimum, cellSize, width, depth, heights, costs, maxStep, maxSlopeDegrees, agentRadius, clearance);
    }
}
