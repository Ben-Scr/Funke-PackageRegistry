using System;
using System.Collections.Generic;
using Funke.Pathfinding;

namespace Funke.Pathfinding3D;

public static class GroundSurface
{
    /// <summary>One world-space height per XZ cell; zero cost blocks it. Links are cardinal and checked in both directions.</summary>
    public static NavigationMap3D FromHeights(Vector3 origin, float cellSize, int width, int depth,
        ReadOnlySpan<float> heights, ReadOnlySpan<float> costs, float maxStep = .4f,
        float maxSlopeDegrees = 45, float agentRadius = 0, Func<Vector3, Vector3, bool>? canTraverse = null)
    {
        int count = BuildValidation.Grid(origin, cellSize, width, 1, depth, agentRadius);
        if (heights.Length != count || costs.Length != count) throw new ArgumentException("Height/cost count differs from grid size.");
        if (!float.IsFinite(maxStep) || maxStep < 0 || !float.IsFinite(maxSlopeDegrees) || maxSlopeDegrees < 0 || maxSlopeDegrees >= 90)
            throw new ArgumentException("Step must be nonnegative; slope must be in [0,90).");
        BuildValidation.Costs(costs);
        var positions = new Vector3[count];
        for (int i = 0; i < count; ++i)
        {
            if (!float.IsFinite(heights[i])) throw new ArgumentException("Heights must be finite, including blocked cells.");
            positions[i] = new Vector3(origin.X + (i % width + .5f) * cellSize, heights[i], origin.Z + (i / width + .5f) * cellSize);
        }
        var walkable = BuildValidation.Clearance(costs, width, 1, depth, agentRadius, cellSize, true);
        var edges = new List<PathEdge>();
        double riseLimit = Math.Min(maxStep, Math.Tan(maxSlopeDegrees * Math.PI / 180) * cellSize);
        for (int z = 0; z < depth; ++z) for (int x = 0; x < width; ++x)
        {
            int a = x + z * width;
            if (!walkable[a]) continue;
            if (x + 1 < width) Connect(a, a + 1, positions, costs, walkable, riseLimit, canTraverse, edges);
            if (z + 1 < depth) Connect(a, a + width, positions, costs, walkable, riseLimit, canTraverse, edges);
        }
        return new NavigationMap3D(PathMap.Graph(positions, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edges)),
            origin, cellSize, width, 1, depth, true, walkable, positions);
    }
    private static void Connect(int a, int b, Vector3[] positions, ReadOnlySpan<float> costs, bool[] walkable,
        double riseLimit, Func<Vector3, Vector3, bool>? canTraverse, List<PathEdge> edges)
    {
        if (!walkable[b] || Math.Abs((double)positions[a].Y - positions[b].Y) > riseLimit) return;
        var delta = positions[a] - positions[b];
        double distance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Y * delta.Y + (double)delta.Z * delta.Z);
        float forward = (float)(distance * costs[b]), reverse = (float)(distance * costs[a]);
        if (!float.IsFinite(forward) || !float.IsFinite(reverse)) throw new ArgumentException("World-space edge cost exceeds float range.");
        if (canTraverse == null || canTraverse(positions[a], positions[b])) edges.Add(new PathEdge(a, b, forward));
        if (canTraverse == null || canTraverse(positions[b], positions[a])) edges.Add(new PathEdge(b, a, reverse));
    }
}
