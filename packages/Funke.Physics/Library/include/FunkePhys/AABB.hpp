#pragma once
#include "Vec2.hpp"

namespace FunkePhys {
    struct AABB
    {
        Vec2 min{ 0.0f, 0.0f };
        Vec2 max{ 0.0f, 0.0f };

        constexpr bool Intersects(const AABB& other) const noexcept
        {
            return !(max.x < other.min.x || min.x > other.max.x ||
                     max.y < other.min.y || min.y > other.max.y);
        }

        constexpr bool Contains(const Vec2& point) const noexcept
        {
            return point.x >= min.x && point.x <= max.x &&
                   point.y >= min.y && point.y <= max.y;
        }

        constexpr Vec2 GetSize() const noexcept
        {
            return max - min;
        }

        constexpr Vec2 GetExtents() const noexcept
        {
            return { (max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f };
        }

        constexpr Vec2 GetCenter() const noexcept
        {
            return { (min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f };
        }
    };
}
