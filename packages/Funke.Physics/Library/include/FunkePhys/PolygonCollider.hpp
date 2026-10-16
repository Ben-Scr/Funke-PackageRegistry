#pragma once
#include "Export.hpp"
#include "Collider.hpp"

#include <vector>

namespace FunkePhys {
    class FUNKE_PHYS_API PolygonCollider final : public Collider
    {
    public:
        PolygonCollider();

        // Vertices are interpreted as a local-space convex polygon. Collision
        // queries use SAT and do not support concave polygons.
        void SetVertices(const Vec2* vertices, std::size_t count);
        void SetVertices(std::vector<Vec2> vertices);
        std::size_t GetVertexCount() const noexcept { return m_Vertices.size(); }
        const Vec2* GetVertices() const noexcept { return m_Vertices.empty() ? nullptr : m_Vertices.data(); }

        AABB ComputeAABB() const noexcept override;

    private:
        std::vector<Vec2> m_Vertices;
        // Local-space bounds, cached when the vertices change.
        Vec2 m_LocalMin{ 0.0f, 0.0f };
        Vec2 m_LocalMax{ 0.0f, 0.0f };
    };
}
