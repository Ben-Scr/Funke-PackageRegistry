using System;
using System.Collections.Generic;

namespace Funke.Pathfinding3D;

public readonly struct NavigationBox
{
    public readonly Vector3 Min, Max;
    public NavigationBox(Vector3 min, Vector3 max)
    {
        if (!BuildValidation.Finite(min) || !BuildValidation.Finite(max) || min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            throw new ArgumentException("Obstacle bounds must be finite and ordered.");
        Min = min; Max = max;
    }
    internal bool Intersects(NavigationBox other) => Min.X <= other.Max.X && Max.X >= other.Min.X
        && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    internal static NavigationBox Union(NavigationBox a, NavigationBox b) => new(
        new Vector3(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
        new Vector3(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z)));
}

/// <summary>Immutable BVH of world AABBs. Conservative for objects whose complete geometry lies inside these bounds.</summary>
public sealed class ObstacleBoxes3D
{
    private readonly struct Node
    {
        internal readonly NavigationBox Bounds;
        internal readonly int Left, Right;
        internal Node(NavigationBox bounds, int left = -1, int right = -1) { Bounds = bounds; Left = left; Right = right; }
    }
    private readonly Node[] m_Nodes;
    private sealed class CenterComparer(int axis) : IComparer<NavigationBox>
    {
        public int Compare(NavigationBox a, NavigationBox b) => Center(a).CompareTo(Center(b));
        private double Center(NavigationBox box) => axis == 0 ? (double)box.Min.X + box.Max.X
            : axis == 1 ? (double)box.Min.Y + box.Max.Y : (double)box.Min.Z + box.Max.Z;
    }
    private static readonly IComparer<NavigationBox>[] s_Comparers = { new CenterComparer(0), new CenterComparer(1), new CenterComparer(2) };
    public ObstacleBoxes3D(ReadOnlySpan<NavigationBox> boxes)
    {
        if (boxes.Length > 1048576) throw new ArgumentException("Too many obstacle boxes.");
        m_Nodes = new Node[boxes.Length == 0 ? 0 : boxes.Length * 2 - 1];
        if (boxes.Length != 0) { var copy = boxes.ToArray(); int next = 0; Build(copy, 0, copy.Length, ref next); }
    }
    private int Build(NavigationBox[] boxes, int start, int count, ref int next)
    {
        int index = next++; NavigationBox bounds = boxes[start];
        for (int i = 1; i < count; ++i) bounds = NavigationBox.Union(bounds, boxes[start + i]);
        if (count == 1) { m_Nodes[index] = new Node(bounds); return index; }
        double x = (double)bounds.Max.X - bounds.Min.X, y = (double)bounds.Max.Y - bounds.Min.Y, z = (double)bounds.Max.Z - bounds.Min.Z;
        int axis = x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        Array.Sort(boxes, start, count, s_Comparers[axis]);
        int left = Build(boxes, start, count / 2, ref next), right = Build(boxes, start + count / 2, count - count / 2, ref next);
        m_Nodes[index] = new Node(bounds, left, right); return index;
    }
    public bool IsFree(NavigationBox volume) => m_Nodes.Length == 0 || !Intersects(0, volume);
    private bool Intersects(int index, NavigationBox volume)
    {
        var node = m_Nodes[index];
        if (!node.Bounds.Intersects(volume)) return false;
        return node.Left < 0 || Intersects(node.Left, volume) || Intersects(node.Right, volume);
    }
    public float CellCost(Vector3 center, float halfExtent)
    {
        if (!float.IsFinite(halfExtent) || halfExtent < 0) throw new ArgumentOutOfRangeException(nameof(halfExtent));
        return IsFree(new NavigationBox(center - new Vector3(halfExtent), center + new Vector3(halfExtent))) ? 1 : 0;
    }
    /// <summary>Conservative swept upright box. Include obstacles/ceilings, excluding the supporting walkable floor.</summary>
    public bool CanTraverse(Vector3 fromFeet, Vector3 toFeet, float radius, float height)
    {
        if (!float.IsFinite(radius) || radius < 0 || !float.IsFinite(height) || height <= 0)
            throw new ArgumentException("Invalid agent dimensions.");
        var footprint = new Vector3(radius, 0, radius);
        var from = new NavigationBox(fromFeet - footprint, fromFeet + footprint + Vector3.Up * height);
        var to = new NavigationBox(toFeet - footprint, toFeet + footprint + Vector3.Up * height);
        return IsFree(NavigationBox.Union(from, to));
    }
}
