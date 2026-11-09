using System;
using Funke.Pathfinding;

namespace Funke.Pathfinding3D;

/// <summary>World-space snapshot. Rebuild after obstacle changes; existing searches keep their old snapshot.</summary>
public sealed class NavigationMap3D : IDisposable
{
    private readonly PathMap m_Map;
    private readonly Vector3[]? m_Positions;
    private readonly bool[] m_Walkable;
    public Vector3 Origin { get; }
    public float CellSize { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public bool IsGround { get; }
    public int NodeCount => m_Walkable.Length;
    internal NavigationMap3D(PathMap map, Vector3 origin, float cellSize, int width, int height, int depth,
        bool ground, bool[] walkable, Vector3[]? positions = null)
    {
        m_Map = map; Origin = origin; CellSize = cellSize; Width = width; Height = height; Depth = depth;
        IsGround = ground; m_Walkable = walkable; m_Positions = positions;
    }
    public bool IsWalkable(int node) => node >= 0 && node < NodeCount && m_Walkable[node];
    public Vector3 Position(int node)
    {
        if (node < 0 || node >= NodeCount) throw new ArgumentOutOfRangeException(nameof(node));
        return m_Positions != null ? m_Positions[node] : Origin + new Vector3(
            (node % Width + 0.5f) * CellSize, (node / Width % Height + 0.5f) * CellSize,
            (node / (Width * Height) + 0.5f) * CellSize);
    }
    /// <summary>Ground maps select an XZ column and enforce vertical tolerance. No silent nearest-node snapping.</summary>
    public bool TryGetNode(Vector3 world, out int node, float groundHeightTolerance = 1)
    {
        node = -1;
        if (!BuildValidation.Finite(world) || !float.IsFinite(groundHeightTolerance) || groundHeightTolerance < 0) return false;
        double x = Math.Floor(((double)world.X - Origin.X) / CellSize);
        double y = IsGround ? 0 : Math.Floor(((double)world.Y - Origin.Y) / CellSize);
        double z = Math.Floor(((double)world.Z - Origin.Z) / CellSize);
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth) return false;
        int candidate = (int)x + Width * ((int)y + Height * (int)z);
        if (!IsWalkable(candidate) || (IsGround && Math.Abs((double)Position(candidate).Y - world.Y) > groundHeightTolerance)) return false;
        node = candidate; return true;
    }
    public NavigationSearch3D CreateSearch() => new(this, m_Map.CreateSearch());
    public void Dispose() => m_Map.Dispose();
}

public sealed class NavigationSearch3D : IDisposable
{
    private readonly NavigationMap3D m_Map;
    private readonly PathSearch m_Search;
    internal NavigationSearch3D(NavigationMap3D map, PathSearch search) { m_Map = map; m_Search = search; }
    public SearchStatus Status => m_Search.Status;
    public SearchStatistics Statistics => m_Search.Statistics;
    public SearchStatus Begin(int startNode, int goalNode, int expansionLimit = 0)
    {
        if (!m_Map.IsWalkable(startNode) || !m_Map.IsWalkable(goalNode))
            throw new ArgumentException("Both endpoints must be walkable nodes in this map.");
        return m_Search.Begin(startNode, goalNode, expansionLimit);
    }
    public SearchStatus Begin(Vector3 start, Vector3 goal, int expansionLimit = 0, float groundHeightTolerance = 1)
    {
        if (!m_Map.TryGetNode(start, out int a, groundHeightTolerance) || !m_Map.TryGetNode(goal, out int b, groundHeightTolerance))
            throw new ArgumentException("Endpoint is outside the map, blocked, or outside ground height tolerance.");
        return Begin(a, b, expansionLimit);
    }
    public SearchStatus Step(int expansionBudget = 256) => m_Search.Step(expansionBudget);
    public SearchStatus Cancel() => m_Search.Cancel();
    /// <summary>Both buffers must fit the entire path; otherwise neither is written.</summary>
    public int CopyPath(Span<int> nodeScratch, Span<Vector3> positions)
    {
        int capacity = Math.Min(nodeScratch.Length, positions.Length);
        int count = m_Search.CopyPath(nodeScratch.Slice(0, capacity));
        if (count > capacity) return count;
        for (int i = 0; i < count; ++i) positions[i] = m_Map.Position(nodeScratch[i]);
        return count;
    }
    public void Dispose() => m_Search.Dispose();
}
