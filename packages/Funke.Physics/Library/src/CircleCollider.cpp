#include "FunkePhys/CircleCollider.hpp"
#include "FunkePhys/Body.hpp"

namespace FunkePhys {
    namespace {
        constexpr float kDefaultRadius = 0.5f;
    }

    CircleCollider::CircleCollider(float radius)
        : Collider(ColliderType::Circle),
        m_Radius(radius > 0.0f ? radius : kDefaultRadius)
    {}

    void CircleCollider::SetRadius(float radius)
    {
        m_Radius = radius > 0.0f ? radius : kDefaultRadius;
        OnShapeChanged();
    }

    AABB CircleCollider::ComputeAABB() const noexcept
    {
        const Vec2 center = GetBody() ? GetBody()->GetPosition() : Vec2{};
        const Vec2 extents{ m_Radius, m_Radius };
        return { center - extents, center + extents };
    }
}
