using System;
using Funke.Pathfinding;

namespace Funke.Pathfinding3D;

public static class FlightVolume
{
    /// <summary>Input describes fully free cells, not just free centers. Radius uses conservative cubic inflation.</summary>
    public static NavigationMap3D FromCosts(Vector3 origin, float cellSize, int width, int height, int depth,
        ReadOnlySpan<float> costs, float agentRadius = 0, bool diagonals = true)
    {
        int count = BuildValidation.Grid(origin, cellSize, width, height, depth, agentRadius);
        if (costs.Length != count) throw new ArgumentException("Cost count differs from grid size.");
        BuildValidation.Costs(costs);
        bool[] walkable = BuildValidation.Clearance(costs, width, height, depth, agentRadius, cellSize, false);
        var inflated = costs.ToArray();
        for (int i = 0; i < count; ++i) if (!walkable[i]) inflated[i] = 0;
        return new NavigationMap3D(PathMap.Grid(width, height, depth, inflated, diagonals),
            origin, cellSize, width, height, depth, false, walkable);
    }
    /// <summary>Sampler receives cell center and half-extent; return zero if any part of that cell is blocked.</summary>
    public static NavigationMap3D Bake(Vector3 origin, float cellSize, int width, int height, int depth,
        Func<Vector3, float, float> sampleCost, float agentRadius = 0, bool diagonals = true)
    {
        ArgumentNullException.ThrowIfNull(sampleCost);
        int count = BuildValidation.Grid(origin, cellSize, width, height, depth, agentRadius);
        var costs = new float[count];
        for (int z = 0; z < depth; ++z) for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x)
            costs[x + width * (y + height * z)] = sampleCost(origin + new Vector3(x + .5f, y + .5f, z + .5f) * cellSize, cellSize * .5f);
        return FromCosts(origin, cellSize, width, height, depth, costs, agentRadius, diagonals);
    }
}
