#pragma once

namespace FunkePhys {
    struct Vec2
    {
        float x = 0.0f;
        float y = 0.0f;

        constexpr Vec2() noexcept = default;
        constexpr Vec2(float xValue, float yValue) noexcept : x(xValue), y(yValue) {}

        constexpr Vec2 operator+(const Vec2& o) const noexcept { return { x + o.x, y + o.y }; }
        constexpr Vec2 operator-(const Vec2& o) const noexcept { return { x - o.x, y - o.y }; }
        constexpr Vec2 operator*(float s) const noexcept { return { x * s, y * s }; }
        constexpr Vec2 operator/(float s) const noexcept { return { x / s, y / s }; }
        constexpr Vec2 operator-() const noexcept { return { -x, -y }; }

        constexpr Vec2& operator+=(const Vec2& o) noexcept { x += o.x; y += o.y; return *this; }
        constexpr Vec2& operator-=(const Vec2& o) noexcept { x -= o.x; y -= o.y; return *this; }
        constexpr Vec2& operator*=(float s) noexcept { x *= s; y *= s; return *this; }
        constexpr Vec2& operator/=(float s) noexcept { x /= s; y /= s; return *this; }

        constexpr bool operator==(const Vec2& o) const noexcept { return x == o.x && y == o.y; }
        constexpr bool operator!=(const Vec2& o) const noexcept { return !(*this == o); }
    };

    constexpr Vec2 operator*(float s, const Vec2& v) noexcept { return { v.x * s, v.y * s }; }

    constexpr float Dot(const Vec2& a, const Vec2& b) noexcept
    {
        return a.x * b.x + a.y * b.y;
    }

    constexpr float LengthSq(const Vec2& v) noexcept
    {
        return Dot(v, v);
    }

    constexpr float DistanceSq(const Vec2& a, const Vec2& b) noexcept
    {
        const Vec2 d{ a.x - b.x, a.y - b.y };
        return Dot(d, d);
    }

    float Length(const Vec2& v) noexcept;
    float Distance(const Vec2& a, const Vec2& b) noexcept;
    Vec2 Normalize(const Vec2& v) noexcept;
}
