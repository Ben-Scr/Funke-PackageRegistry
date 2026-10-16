#pragma once
#include "Export.hpp"
#include "Collider.hpp"

namespace FunkePhys {
    class FUNKE_PHYS_API CircleCollider final : public Collider
    {
    public:
        explicit CircleCollider(float radius);

        float GetRadius() const noexcept { return m_Radius; }
        void SetRadius(float radius);

        AABB ComputeAABB() const noexcept override;

    private:
        float m_Radius = 0.5f;
    };
}
