#pragma once
#include "Export.hpp"
#include "Collider.hpp"

namespace FunkePhys {
    class FUNKE_PHYS_API BoxCollider final : public Collider
    {
    public:
        BoxCollider() noexcept;
        explicit BoxCollider(const Vec2& halfExtents);

        const Vec2& GetHalfExtents() const noexcept { return m_HalfExtents; }
        void SetHalfExtents(const Vec2& halfExtents) noexcept;

        AABB ComputeAABB() const noexcept override;

    private:
        Vec2 m_HalfExtents;
    };
}
