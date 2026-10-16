using System.Runtime.InteropServices;

namespace Funke.Physics;

// DllImport surface of Pkg.Funke.Physics.Native; names and signatures mirror Native/PInvoke.cpp.
// Pointers instead of out/bool so the signatures stay blittable for the Mono and Web pinvoke tables.
internal static unsafe class NativeBindings
{
    private const string Lib = "Pkg.Funke.Physics.Native";

    // ── Components ─────────────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_GetBodyType", CallingConvention = CallingConvention.Cdecl)]
    public static extern int FastBody2D_GetBodyType(ulong entityID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_SetBodyType", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBody2D_SetBodyType(ulong entityID, int type);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_GetMass", CallingConvention = CallingConvention.Cdecl)]
    public static extern float FastBody2D_GetMass(ulong entityID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_SetMass", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBody2D_SetMass(ulong entityID, float mass);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_GetUseGravity", CallingConvention = CallingConvention.Cdecl)]
    public static extern int FastBody2D_GetUseGravity(ulong entityID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_SetUseGravity", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBody2D_SetUseGravity(ulong entityID, int enabled);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_GetBoundaryCheck", CallingConvention = CallingConvention.Cdecl)]
    public static extern int FastBody2D_GetBoundaryCheck(ulong entityID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_SetBoundaryCheck", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBody2D_SetBoundaryCheck(ulong entityID, int enabled);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_GetVelocity", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBody2D_GetVelocity(ulong entityID, float* outX, float* outY);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBody2D_SetVelocity", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBody2D_SetVelocity(ulong entityID, float x, float y);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBoxCollider2D_GetHalfExtents", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBoxCollider2D_GetHalfExtents(ulong entityID, float* outX, float* outY);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastBoxCollider2D_SetHalfExtents", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastBoxCollider2D_SetHalfExtents(ulong entityID, float x, float y);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastCircleCollider2D_GetRadius", CallingConvention = CallingConvention.Cdecl)]
    public static extern float FastCircleCollider2D_GetRadius(ulong entityID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_FastCircleCollider2D_SetRadius", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FastCircleCollider2D_SetRadius(ulong entityID, float radius);

    // ── Queries ────────────────────────────────────────────────────────

    [DllImport(Lib, EntryPoint = "Funke_Physics_Raycast", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Raycast(float originX, float originY, float dirX, float dirY, float distance,
        ulong* hitID, float* hitX, float* hitY, float* normalX, float* normalY, float* hitDistance);

    [DllImport(Lib, EntryPoint = "Funke_Physics_OverlapCircle", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OverlapCircle(float x, float y, float radius, int mode, ulong* outID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_OverlapBox", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OverlapBox(float x, float y, float halfX, float halfY, float degrees, int mode, ulong* outID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_OverlapPolygon", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OverlapPolygon(float x, float y, float* points, int pointCount, int mode, ulong* outID);

    [DllImport(Lib, EntryPoint = "Funke_Physics_ContainsPoint", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ContainsPoint(float x, float y, int mode, ulong* outID);

    // *All exports return the total hit count and write at most maxOut ids.

    [DllImport(Lib, EntryPoint = "Funke_Physics_OverlapCircleAll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OverlapCircleAll(float x, float y, float radius, ulong* outIDs, int maxOut);

    [DllImport(Lib, EntryPoint = "Funke_Physics_OverlapBoxAll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OverlapBoxAll(float x, float y, float halfX, float halfY, float degrees, ulong* outIDs, int maxOut);

    [DllImport(Lib, EntryPoint = "Funke_Physics_OverlapPolygonAll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int OverlapPolygonAll(float x, float y, float* points, int pointCount, ulong* outIDs, int maxOut);

    [DllImport(Lib, EntryPoint = "Funke_Physics_ContainsPointAll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ContainsPointAll(float x, float y, ulong* outIDs, int maxOut);
}
