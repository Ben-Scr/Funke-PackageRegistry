#include "FunkePhys/PolygonCollider.hpp"

#include "FunkePhys/Body.hpp"

#include <algorithm>
#include <limits>
#include <utility>

namespace FunkePhys {
    namespace {
        constexpr std::size_t kMinPolygonVertices = 3;
    }

    PolygonCollider::PolygonCollider()
        : Collider(ColliderType::Polygon)
    {}

    void PolygonCollider::SetVertices(const Vec2* vertices, std::size_t count)
    {
        if (vertices == nullptr || count < kMinPolygonVertices) {
            SetVertices(std::vector<Vec2>{});
            return;
        }
        SetVertices(std::vector<Vec2>(vertices, vertices + count));
    }

    void PolygonCollider::SetVertices(std::vector<Vec2> vertices)
    {
        if (vertices.size() < kMinPolygonVertices) {
            vertices.clear();
        }
        m_Vertices = std::move(vertices);

        m_LocalMin = { 0.0f, 0.0f };
        m_LocalMax = { 0.0f, 0.0f };
        if (!m_Vertices.empty()) {
            m_LocalMin = { std::numeric_limits<float>::max(), std::numeric_limits<float>::max() };
            m_LocalMax = { std::numeric_limits<float>::lowest(), std::numeric_limits<float>::lowest() };
            for (const Vec2& v : m_Vertices) {
                m_LocalMin.x = std::min(m_LocalMin.x, v.x);
                m_LocalMin.y = std::min(m_LocalMin.y, v.y);
                m_LocalMax.x = std::max(m_LocalMax.x, v.x);
                m_LocalMax.y = std::max(m_LocalMax.y, v.y);
            }
        }
        OnShapeChanged();
    }

    AABB PolygonCollider::ComputeAABB() const noexcept
    {
        const Vec2 bodyPosition = GetBody() ? GetBody()->GetPosition() : Vec2{};
        if (m_Vertices.empty()) {
            return { bodyPosition, bodyPosition };
        }
        return { bodyPosition + m_LocalMin, bodyPosition + m_LocalMax };
    }
}
