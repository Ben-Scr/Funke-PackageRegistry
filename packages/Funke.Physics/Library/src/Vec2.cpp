#include "FunkePhys/Vec2.hpp"

#include <cmath>
#include <limits>

namespace FunkePhys {
    float Length(const Vec2& v) noexcept
    {
        return std::sqrt(LengthSq(v));
    }

    float Distance(const Vec2& a, const Vec2& b) noexcept
    {
        return std::sqrt(DistanceSq(a, b));
    }

    Vec2 Normalize(const Vec2& v) noexcept
    {
        const float length = Length(v);
        if (length <= std::numeric_limits<float>::epsilon()) {
            return {};
        }

        return v / length;
    }
}
