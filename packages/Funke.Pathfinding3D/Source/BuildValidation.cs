using System;

namespace Funke.Pathfinding3D;

internal static class BuildValidation
{
    internal static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    internal static int Grid(Vector3 origin, float cellSize, int width, int height, int depth, float radius)
    {
        if (!Finite(origin) || !float.IsFinite(cellSize) || cellSize <= 0 || !float.IsFinite(radius) || radius < 0)
            throw new ArgumentException("Origin, positive cell size, and nonnegative radius must be finite.");
        if (width <= 0 || height <= 0 || depth <= 0 || (long)width * height > 8388608 || depth > 8388608L / ((long)width * height))
            throw new ArgumentOutOfRangeException(nameof(width), "Grid must contain 1..8388608 cells.");
        Vector3 end = origin + new Vector3(width * cellSize, height * cellSize, depth * cellSize);
        if (!Finite(end) || end.X <= origin.X || end.Y <= origin.Y || end.Z <= origin.Z)
            throw new ArgumentException("Grid extent cannot be represented at this origin.");
        return width * height * depth;
    }
    internal static void Costs(ReadOnlySpan<float> costs)
    {
        foreach (float cost in costs)
            if (!float.IsFinite(cost) || (cost != 0 && cost < 1)) throw new ArgumentException("Cost must be zero or finite and >= 1.");
    }
    internal static bool[] Clearance(ReadOnlySpan<float> costs, int width, int height, int depth, float radius, float cellSize, bool ground)
    {
        var a = new bool[costs.Length];
        for (int i = 0; i < a.Length; ++i) a[i] = costs[i] != 0;
        if (radius == 0) return a;
        double cells = Math.Ceiling((double)radius / cellSize);
        if (cells >= width || cells >= depth || (!ground && cells >= height)) return new bool[a.Length];
        int r = (int)cells;
        var b = new bool[a.Length];
        Pass(a, b, width, 1, r);
        if (!ground) { Pass(b, a, height, width, r); Pass(a, b, depth, width * height, r); }
        else { Pass(b, a, depth, width, r); return a; }
        return b;
    }
    private static void Pass(bool[] source, bool[] target, int length, int stride, int radius)
    {
        for (int block = 0; block < source.Length; block += length * stride)
        for (int lane = 0; lane < stride; ++lane)
        {
            int blocked = 0;
            for (int k = 0; k <= radius; ++k) if (!source[block + lane + k * stride]) ++blocked;
            for (int i = 0; i < length; ++i)
            {
                target[block + lane + i * stride] = i >= radius && i + radius < length && blocked == 0;
                int remove = i - radius, add = i + radius + 1;
                if (remove >= 0 && !source[block + lane + remove * stride]) --blocked;
                if (add < length && !source[block + lane + add * stride]) ++blocked;
            }
        }
    }
}
