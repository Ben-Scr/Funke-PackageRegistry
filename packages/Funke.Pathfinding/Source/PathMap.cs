using System;
using System.Runtime.InteropServices;

namespace Funke.Pathfinding;

public enum SearchStatus { Idle, Searching, Found, NoPath, LimitReached, Cancelled }
[StructLayout(LayoutKind.Sequential)]
public readonly struct PathEdge
{
    public readonly int From, To;
    public readonly float Cost;
    public PathEdge(int from, int to, float cost) { From = from; To = to; Cost = cost; }
}
[StructLayout(LayoutKind.Sequential)]
public struct SearchStatistics
{
    public int Expanded, Visited, OpenPeak, PathLength;
    public double Cost;
}

/// <summary>Immutable snapshot. Dispose maps and searches when their owning scene ends.</summary>
public sealed unsafe class PathMap : IDisposable
{
    private readonly MapHandle m_Handle;
    public int NodeCount { get; }
    private PathMap(IntPtr handle, int count) { m_Handle = new MapHandle(NativeBindings.Check(handle)); NodeCount = count; }

    /// <summary>X-fastest storage: x + width * (y + height * z). Zero blocks a cell; other costs must be >= 1.</summary>
    public static PathMap Grid(int width, int height, int depth, ReadOnlySpan<float> costs, bool diagonals = true)
    {
        NativeBindings.VerifyVersion();
        fixed (float* p = costs)
            return new PathMap(NativeBindings.Grid(width, height, depth, diagonals ? 1 : 0, p, costs.Length), costs.Length);
    }
    public static PathMap Grid2D(int width, int height, ReadOnlySpan<float> costs, bool diagonals = true)
        => Grid(width, height, 1, costs, diagonals);

    /// <summary>Directed edges; add reverse edges for two-way movement. Zero-cost links are supported.</summary>
    public static PathMap Graph(ReadOnlySpan<Vector3> positions, ReadOnlySpan<PathEdge> edges)
    {
        NativeBindings.VerifyVersion();
        fixed (Vector3* p = positions) fixed (PathEdge* e = edges)
            return new PathMap(NativeBindings.Graph(p, positions.Length, e, edges.Length), positions.Length);
    }
    public PathSearch CreateSearch() => new(new SearchHandle(NativeBindings.Check(NativeBindings.CreateSearch(m_Handle))));
    public void Dispose() => m_Handle.Dispose();
}
