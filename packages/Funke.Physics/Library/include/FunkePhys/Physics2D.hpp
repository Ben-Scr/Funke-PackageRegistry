#pragma once
#include "Export.hpp"
#include "Vec2.hpp"
#include "Contact.hpp"

#include <cstddef>
#include <limits>
#include <optional>
#include <vector>

namespace FunkePhys {
    class Collider;
    class PhysicsWorld;

    // Result of Physics2D::Raycast. On a miss `hit` is false and the remaining
    // fields are unset. Map `collider` back to a game object via GetUserData().
    struct FUNKE_PHYS_API RaycastHit
    {
        bool            hit      = false;
        const Collider* collider = nullptr;
        Vec2            point    { 0.0f, 0.0f };
        Vec2            normal   { 0.0f, 0.0f };
        float           distance = 0.0f;
    };

    class FUNKE_PHYS_API Physics2D
    {
    public:
        // Optional global context. When set, the single-argument query overloads
        // (OverlapsWith / ContainsPoint) operate against this world. The two-argument
        // overloads are pure pair tests and work without a context.
        static void SetContext(PhysicsWorld& world) noexcept;
        static void ClearContext() noexcept;
        static PhysicsWorld* GetContext() noexcept;

        // First contact between `collider` and any other collider in the active
        // world. Returns nullopt if no overlap or no context set.
        static std::optional<Contact> OverlapsWith(Collider& collider);

        // Pair test. Returns nullopt if the two colliders do not overlap.
        // Performs precise narrowphase for circle/circle, circle/box, box/box,
        // and convex polygon pairs.
        static std::optional<Contact> OverlapsWith(Collider& colliderA, Collider& colliderB);

        // Returns the first collider in the active world whose shape contains the
        // given world-space point, or nullptr if none.
        static const Collider* ContainsPoint(const Vec2& point);

        // True if the collider's shape contains the given world-space point.
        // Uses precise tests per shape (circle distance, polygon ray cast); falls
        // back to AABB for unknown shapes.
        static bool ContainsPoint(Collider& collider, const Vec2& point);

        // ---- Raycast & shape-overlap queries (run against the active context) -----

        // Nearest hit along the ray. `direction` need not be normalized; `distance`
        // is measured along it. Returns a miss when the direction is degenerate,
        // `maxDistance` <= 0 or NaN, or no context is set. A ray that starts inside a
        // shape hits it at distance 0 with normal = -direction.
        static RaycastHit Raycast(const Vec2& origin, const Vec2& direction,
                                  float maxDistance = std::numeric_limits<float>::infinity());
        static bool       RaycastCheck(const Vec2& origin, const Vec2& direction,
                                       float maxDistance = std::numeric_limits<float>::infinity());

        // Overlap a circle placed at `origin`. *Check returns a bool; *All returns
        // every overlapping collider. Invalid radius (<= 0 or NaN) -> none.
        static const Collider*              OverlapCircle(const Vec2& origin, float radius);
        static bool                         OverlapCircleCheck(const Vec2& origin, float radius);
        static std::vector<const Collider*> OverlapCircleAll(const Vec2& origin, float radius);

        // Overlap a box placed at `origin`. `size` is the FULL width/height;
        // `rotationDegrees` rotates it CCW about `origin`. Invalid size -> none.
        static const Collider*              OverlapBox(const Vec2& origin, const Vec2& size, float rotationDegrees = 0.0f);
        static bool                         OverlapBoxCheck(const Vec2& origin, const Vec2& size, float rotationDegrees = 0.0f);
        static std::vector<const Collider*> OverlapBoxAll(const Vec2& origin, const Vec2& size, float rotationDegrees = 0.0f);

        // Overlap a convex polygon whose `points` are RELATIVE to `origin`.
        // Needs count >= 3 finite points, else -> none.
        static const Collider*              OverlapPolygon(const Vec2& origin, const Vec2* points, std::size_t count);
        static bool                         OverlapPolygonCheck(const Vec2& origin, const Vec2* points, std::size_t count);
        static std::vector<const Collider*> OverlapPolygonAll(const Vec2& origin, const Vec2* points, std::size_t count);

        // Every collider whose shape contains the world-space point.
        static std::vector<const Collider*> ContainsPointAll(const Vec2& point);

    private:
        // Grid-accelerated searches of the active world. "First" means the lowest
        // registration slot, so results do not depend on grid traversal order.
        static const Collider* FirstOverlap(Collider& probe, const Collider* skip, Contact* outContact);
        static std::vector<const Collider*> AllOverlaps(Collider& probe);
        static const Collider* FirstContaining(const Vec2& point);
        static std::vector<const Collider*> AllContaining(const Vec2& point);

        static PhysicsWorld* s_Context;
    };
}
