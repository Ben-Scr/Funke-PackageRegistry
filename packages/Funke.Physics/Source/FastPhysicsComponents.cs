using Funke.Physics;

namespace Funke;

public enum FastBodyType { Static = 0, Dynamic = 1, Kinematic = 2 }

/// <summary>Body simulated by Funke.Physics (boxes and circles, no rotation). Use Rigidbody2D for rotation, joints or CCD.</summary>
public class FastBody2D : Component
{
    public FastBodyType BodyType
    {
        get => (FastBodyType)NativeBindings.FastBody2D_GetBodyType(RequireComponent<FastBody2D>());
        set => NativeBindings.FastBody2D_SetBodyType(RequireComponent<FastBody2D>(), (int)value);
    }

    public float Mass
    {
        get => NativeBindings.FastBody2D_GetMass(RequireComponent<FastBody2D>());
        set => NativeBindings.FastBody2D_SetMass(RequireComponent<FastBody2D>(), value);
    }

    public bool UseGravity
    {
        get => NativeBindings.FastBody2D_GetUseGravity(RequireComponent<FastBody2D>()) != 0;
        set => NativeBindings.FastBody2D_SetUseGravity(RequireComponent<FastBody2D>(), value ? 1 : 0);
    }

    /// <summary>Clamps the body to the world bounds of the physics settings.</summary>
    public bool BoundaryCheck
    {
        get => NativeBindings.FastBody2D_GetBoundaryCheck(RequireComponent<FastBody2D>()) != 0;
        set => NativeBindings.FastBody2D_SetBoundaryCheck(RequireComponent<FastBody2D>(), value ? 1 : 0);
    }

    public unsafe Vector2 Velocity
    {
        get
        {
            float x, y;
            NativeBindings.FastBody2D_GetVelocity(RequireComponent<FastBody2D>(), &x, &y);
            return new Vector2(x, y);
        }
        set => NativeBindings.FastBody2D_SetVelocity(RequireComponent<FastBody2D>(), value.X, value.Y);
    }
}

/// <summary>Axis-aligned box collider; HalfExtents are multiplied by Transform2D.Scale.</summary>
public class FastBoxCollider2D : Component
{
    public unsafe Vector2 HalfExtents
    {
        get
        {
            float x, y;
            NativeBindings.FastBoxCollider2D_GetHalfExtents(RequireComponent<FastBoxCollider2D>(), &x, &y);
            return new Vector2(x, y);
        }
        set => NativeBindings.FastBoxCollider2D_SetHalfExtents(RequireComponent<FastBoxCollider2D>(), value.X, value.Y);
    }
}

/// <summary>Circle collider; Radius is scaled by the larger of |Scale.X| and |Scale.Y|.</summary>
public class FastCircleCollider2D : Component
{
    public float Radius
    {
        get => NativeBindings.FastCircleCollider2D_GetRadius(RequireComponent<FastCircleCollider2D>());
        set => NativeBindings.FastCircleCollider2D_SetRadius(RequireComponent<FastCircleCollider2D>(), value);
    }
}
