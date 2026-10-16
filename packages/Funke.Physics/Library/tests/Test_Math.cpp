#include "TestRunner.hpp"

#include "FunkePhys/AABB.hpp"
#include "FunkePhys/Vec2.hpp"

using namespace FunkePhys;

FUNKE_PHYS_TEST_CASE(Vec2_DotAndLength)
{
    const Vec2 a{ 3.0f, 4.0f };
    EXPECT_NEAR(LengthSq(a), 25.0f, 1e-6);
    EXPECT_NEAR(Length(a), 5.0f, 1e-5);
    EXPECT_NEAR(Dot(a, Vec2{ 1.0f, 0.0f }), 3.0f, 1e-6);
}

FUNKE_PHYS_TEST_CASE(Vec2_Distance)
{
    const Vec2 a{ 1.0f, 2.0f };
    const Vec2 b{ 4.0f, 6.0f };
    EXPECT_NEAR(DistanceSq(a, b), 25.0f, 1e-6);
    EXPECT_NEAR(Distance(a, b), 5.0f, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Vec2_NormalizeZeroSafe)
{
    const Vec2 zero{ 0.0f, 0.0f };
    const Vec2 n = Normalize(zero);
    EXPECT_NEAR(n.x, 0.0f, 1e-6);
    EXPECT_NEAR(n.y, 0.0f, 1e-6);
}

FUNKE_PHYS_TEST_CASE(AABB_Intersects)
{
    const AABB a{ { 0, 0 }, { 1, 1 } };
    const AABB b{ { 0.5f, 0.5f }, { 1.5f, 1.5f } };
    const AABB c{ { 2.0f, 2.0f }, { 3.0f, 3.0f } };
    EXPECT_TRUE(a.Intersects(b));
    EXPECT_FALSE(a.Intersects(c));
}

FUNKE_PHYS_TEST_CASE(AABB_ContainsPoint)
{
    const AABB box{ { -1.0f, -1.0f }, { 1.0f, 1.0f } };
    EXPECT_TRUE(box.Contains(Vec2{ 0.0f, 0.0f }));
    EXPECT_TRUE(box.Contains(Vec2{ 1.0f, 0.0f }));   // inclusive boundary
    EXPECT_FALSE(box.Contains(Vec2{ 1.5f, 0.0f }));
}

FUNKE_PHYS_TEST_CASE(AABB_CenterAndExtents)
{
    const AABB box{ { -2.0f, -1.0f }, { 4.0f, 3.0f } };
    const Vec2 center = box.GetCenter();
    const Vec2 extents = box.GetExtents();
    EXPECT_NEAR(center.x, 1.0f, 1e-6);
    EXPECT_NEAR(center.y, 1.0f, 1e-6);
    EXPECT_NEAR(extents.x, 3.0f, 1e-6);
    EXPECT_NEAR(extents.y, 2.0f, 1e-6);
}
