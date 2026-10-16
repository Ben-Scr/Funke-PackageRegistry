using System;

namespace Funke.Physics;

/// <summary>
/// Queries against the Funke.Physics world (FastBody2D / FastBoxCollider2D / FastCircleCollider2D).
/// Physics2D queries the Box2D world instead. Colliders without a FastBody2D are never hit.
/// Main thread only.
/// </summary>
public static unsafe class FastPhysics2D
{
    public static int MaxPolygonVertices = 8;

    private const int k_First = 0;

    public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float maxDistance = Mathf.Infinity)
    {
        RaycastHit2D result = new();
        if (direction.LengthSquared() <= Mathf.Epsilon * Mathf.Epsilon || !(maxDistance > 0.0f))
            return result;

        ulong id;
        float hitX, hitY, normalX, normalY, distance;
        if (NativeBindings.Raycast(origin.X, origin.Y, direction.X, direction.Y, maxDistance,
                &id, &hitX, &hitY, &normalX, &normalY, &distance) == 0)
            return result;

        result.Hit = true;
        result.Entity = Entity.FromID(id);
        result.Point = new Vector2(hitX, hitY);
        result.Normal = new Vector2(normalX, normalY);
        result.Distance = distance;
        return result;
    }

    public static bool RaycastCheck(Vector2 origin, Vector2 direction, float maxDistance = Mathf.Infinity)
        => Raycast(origin, direction, maxDistance).Hit;

    public static Entity? OverlapCircle(Vector2 origin, float radius)
    {
        if (!(radius > 0.0f))
            return null;
        ulong id;
        return ToEntity(NativeBindings.OverlapCircle(origin.X, origin.Y, radius, k_First, &id), id);
    }

    public static bool OverlapCircleCheck(Vector2 origin, float radius)
        => OverlapCircle(origin, radius) != null;

    public static Entity[] OverlapCircleAll(Vector2 origin, float radius)
    {
        if (!(radius > 0.0f))
            return Array.Empty<Entity>();
        return Collect((ids, max) => NativeBindings.OverlapCircleAll(origin.X, origin.Y, radius, ids, max));
    }

    /// <summary><paramref name="size"/> is the full width and height; <paramref name="rotation"/> in degrees.</summary>
    public static Entity? OverlapBox(Vector2 origin, Vector2 size, float rotation = 0)
    {
        if (!IsValidBox(size, rotation))
            return null;
        ulong id;
        return ToEntity(NativeBindings.OverlapBox(origin.X, origin.Y, size.X * 0.5f, size.Y * 0.5f, rotation, k_First, &id), id);
    }

    public static bool OverlapBoxCheck(Vector2 origin, Vector2 size, float rotation = 0)
        => OverlapBox(origin, size, rotation) != null;

    public static Entity[] OverlapBoxAll(Vector2 origin, Vector2 size, float rotation = 0)
    {
        if (!IsValidBox(size, rotation))
            return Array.Empty<Entity>();
        float halfX = size.X * 0.5f, halfY = size.Y * 0.5f;
        return Collect((ids, max) => NativeBindings.OverlapBoxAll(origin.X, origin.Y, halfX, halfY, rotation, ids, max));
    }

    /// <summary>Convex polygon as x,y pairs relative to <paramref name="origin"/>; 3 to MaxPolygonVertices points.</summary>
    public static Entity? OverlapPolygon(Vector2 origin, params float[] points)
    {
        if (!IsValidPolygon(points))
            return null;
        ulong id;
        fixed (float* p = points)
            return ToEntity(NativeBindings.OverlapPolygon(origin.X, origin.Y, p, points.Length / 2, k_First, &id), id);
    }

    public static bool OverlapPolygonCheck(Vector2 origin, params float[] points)
        => OverlapPolygon(origin, points) != null;

    public static Entity[] OverlapPolygonAll(Vector2 origin, params float[] points)
    {
        if (!IsValidPolygon(points))
            return Array.Empty<Entity>();
        return Collect((ids, max) =>
        {
            fixed (float* p = points)
                return NativeBindings.OverlapPolygonAll(origin.X, origin.Y, p, points.Length / 2, ids, max);
        });
    }

    public static Entity? ContainsPoint(Vector2 point)
    {
        ulong id;
        return ToEntity(NativeBindings.ContainsPoint(point.X, point.Y, k_First, &id), id);
    }

    public static bool ContainsPointCheck(Vector2 point)
        => ContainsPoint(point) != null;

    public static Entity[] ContainsPointAll(Vector2 point)
        => Collect((ids, max) => NativeBindings.ContainsPointAll(point.X, point.Y, ids, max));

    private delegate int AllQuery(ulong* ids, int maxOut);

    private static Entity? ToEntity(int found, ulong id) => found != 0 && id != 0 ? Entity.FromID(id) : null;

    private static bool IsValidBox(Vector2 size, float rotation)
        => size.X > 0.0f && size.Y > 0.0f && float.IsFinite(rotation);

    private static bool IsValidPolygon(float[]? points)
    {
        if (points == null || points.Length < 6 || points.Length % 2 != 0 || points.Length / 2 > MaxPolygonVertices)
            return false;
        foreach (float value in points)
        {
            if (!float.IsFinite(value))
                return false;
        }
        return true;
    }

    // The native side reports the total count, so a too-small buffer is retried once at the right size.
    private static Entity[] Collect(AllQuery query)
    {
        ulong[] buffer = new ulong[64];
        int count;
        while (true)
        {
            fixed (ulong* ids = buffer)
                count = query(ids, buffer.Length);
            if (count <= buffer.Length)
                break;
            buffer = new ulong[count];
        }
        if (count <= 0)
            return Array.Empty<Entity>();

        Entity[] entities = new Entity[count];
        for (int i = 0; i < count; i++)
            entities[i] = Entity.FromID(buffer[i]);
        return entities;
    }
}
