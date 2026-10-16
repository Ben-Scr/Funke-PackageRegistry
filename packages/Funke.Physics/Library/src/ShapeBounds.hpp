#pragma once
#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"

namespace FunkePhys::Detail {
    // World-space AABB without a virtual call for the two common shapes.
    inline AABB ComputeShapeAABB(const Collider& collider) noexcept
    {
        switch (collider.GetType()) {
        case ColliderType::Box: {
            const Body* body = collider.GetBody();
            const Vec2 center = body ? body->GetPosition() : Vec2{};
            const Vec2 half = static_cast<const BoxCollider&>(collider).GetHalfExtents();
            return { center - half, center + half };
        }
        case ColliderType::Circle: {
            const Body* body = collider.GetBody();
            const Vec2 center = body ? body->GetPosition() : Vec2{};
            const float r = static_cast<const CircleCollider&>(collider).GetRadius();
            return { { center.x - r, center.y - r }, { center.x + r, center.y + r } };
        }
        default:
            return collider.ComputeAABB();
        }
    }
}
