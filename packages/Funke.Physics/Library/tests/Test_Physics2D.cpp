#include "TestRunner.hpp"

#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"
#include "FunkePhys/PhysicsWorld.hpp"
#include "FunkePhys/Physics2D.hpp"
#include "FunkePhys/PolygonCollider.hpp"

#include <limits>
#include <vector>

using namespace FunkePhys;

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapsWithReturnsValueLifetime)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.6f, 0.0f });
    CircleCollider colA(0.5f);
    CircleCollider colB(0.5f);
    a.SetCollider(&colA); colA.SetBody(&a);
    b.SetCollider(&colB); colB.SetBody(&b);

    auto contact1 = Physics2D::OverlapsWith(colA, colB);
    auto contact2 = Physics2D::OverlapsWith(colA, colB);
    EXPECT_TRUE(contact1.has_value());
    EXPECT_TRUE(contact2.has_value());
    // Both copies are independent: contact1 is not invalidated by contact2.
    EXPECT_NEAR(contact1->penetration, 0.4f, 1e-5);
    EXPECT_NEAR(contact2->penetration, 0.4f, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Physics2D_ContextOverlapFindsAny)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.6f, 0.0f });
    CircleCollider colA(0.5f);
    CircleCollider colB(0.5f);

    world.RegisterBody(a); world.RegisterCollider(colA); world.AttachCollider(a, colA);
    world.RegisterBody(b); world.RegisterCollider(colB); world.AttachCollider(b, colB);

    auto contact = Physics2D::OverlapsWith(colA);
    EXPECT_TRUE(contact.has_value());

    Physics2D::ClearContext();
    EXPECT_FALSE(Physics2D::OverlapsWith(colA).has_value());
}

FUNKE_PHYS_TEST_CASE(Physics2D_ContainsPointCircle)
{
    Body a; a.SetPosition({ 1.0f, 1.0f });
    CircleCollider col(0.5f);
    a.SetCollider(&col); col.SetBody(&a);

    EXPECT_TRUE(Physics2D::ContainsPoint(col, Vec2{ 1.0f, 1.0f }));
    EXPECT_TRUE(Physics2D::ContainsPoint(col, Vec2{ 1.4f, 1.0f }));
    EXPECT_FALSE(Physics2D::ContainsPoint(col, Vec2{ 1.6f, 1.0f }));
    // AABB-only logic would let (1.4, 1.4) through since it's inside the circle's AABB,
    // but distance sqrt(0.32) ~= 0.566 > 0.5 so it should be outside the actual circle.
    EXPECT_FALSE(Physics2D::ContainsPoint(col, Vec2{ 1.4f, 1.4f }));
}

FUNKE_PHYS_TEST_CASE(Physics2D_ContainsPointBox)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 1.0f, 1.0f });
    a.SetCollider(&col); col.SetBody(&a);

    EXPECT_TRUE(Physics2D::ContainsPoint(col, Vec2{ 0.5f, 0.5f }));
    EXPECT_FALSE(Physics2D::ContainsPoint(col, Vec2{ 1.5f, 0.5f }));
}

// ---- Raycast: circle --------------------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastCircleHit)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    CircleCollider col(0.5f);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_TRUE(hit.collider == &col);
    EXPECT_NEAR(hit.distance, 4.5f, 1e-4);
    EXPECT_NEAR(hit.point.x, 4.5f, 1e-4);
    EXPECT_NEAR(hit.point.y, 0.0f, 1e-4);
    EXPECT_NEAR(hit.normal.x, -1.0f, 1e-4);
    EXPECT_NEAR(hit.normal.y, 0.0f, 1e-4);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastCircleMissPastMaxDistance)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    CircleCollider col(0.5f);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 4.0f).hit);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastCircleMissDirection)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    CircleCollider col(0.5f);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { 0.0f, 1.0f }, 10.0f).hit);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastCircleOriginInside)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    CircleCollider col(1.0f);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_TRUE(hit.collider == &col);
    EXPECT_NEAR(hit.distance, 0.0f, 1e-5);
    EXPECT_NEAR(hit.normal.x, -1.0f, 1e-5);
    EXPECT_NEAR(hit.normal.y, 0.0f, 1e-5);

    Physics2D::ClearContext();
}

// ---- Raycast: box -----------------------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastBoxSideNormal)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    BoxCollider col({ 1.0f, 1.0f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_NEAR(hit.distance, 4.0f, 1e-4);
    EXPECT_NEAR(hit.point.x, 4.0f, 1e-4);
    EXPECT_NEAR(hit.normal.x, -1.0f, 1e-4);
    EXPECT_NEAR(hit.normal.y, 0.0f, 1e-4);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastBoxTopDownNormal)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 1.0f, 1.0f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 5.0f }, { 0.0f, -1.0f }, 10.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_NEAR(hit.distance, 4.0f, 1e-4);
    EXPECT_NEAR(hit.point.y, 1.0f, 1e-4);
    EXPECT_NEAR(hit.normal.x, 0.0f, 1e-4);
    EXPECT_NEAR(hit.normal.y, 1.0f, 1e-4);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastBoxParallelMiss)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 1.0f, 1.0f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    // Ray runs horizontally well above the box.
    EXPECT_FALSE(Physics2D::Raycast({ -5.0f, 5.0f }, { 1.0f, 0.0f }, 100.0f).hit);

    Physics2D::ClearContext();
}

// ---- Raycast: polygon -------------------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastPolygonHit)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    PolygonCollider col;
    col.SetVertices(std::vector<Vec2>{ { -1.0f, -1.0f }, { 1.0f, -1.0f }, { 1.0f, 1.0f }, { -1.0f, 1.0f } });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_NEAR(hit.distance, 4.0f, 1e-4);
    EXPECT_NEAR(hit.point.x, 4.0f, 1e-4);
    EXPECT_NEAR(hit.normal.x, -1.0f, 1e-4);
    EXPECT_NEAR(hit.normal.y, 0.0f, 1e-4);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastPolygonMiss)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    PolygonCollider col;
    col.SetVertices(std::vector<Vec2>{ { -1.0f, -1.0f }, { 1.0f, -1.0f }, { 1.0f, 1.0f }, { -1.0f, 1.0f } });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    // Pointing away from the polygon.
    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { -1.0f, 0.0f }, 10.0f).hit);

    Physics2D::ClearContext();
}

// ---- Raycast: aggregation & guards -----------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastReturnsNearest)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body near_; near_.SetPosition({ 3.0f, 0.0f });
    Body far_;  far_.SetPosition({ 6.0f, 0.0f });
    CircleCollider nearCol(0.5f);
    CircleCollider farCol(0.5f);
    world.RegisterBody(near_); world.RegisterCollider(nearCol); world.AttachCollider(near_, nearCol);
    world.RegisterBody(far_);  world.RegisterCollider(farCol);  world.AttachCollider(far_, farCol);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 100.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_TRUE(hit.collider == &nearCol);
    EXPECT_NEAR(hit.distance, 2.5f, 1e-4);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastNoContextMiss)
{
    Physics2D::ClearContext();
    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f).hit);
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastInvalidArgsMiss)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    CircleCollider col(0.5f);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { 0.0f, 0.0f }, 10.0f).hit);  // zero direction
    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 0.0f).hit);   // zero distance
    EXPECT_FALSE(Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, -1.0f).hit);  // negative distance

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_RaycastCheckMatchesRaycast)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 5.0f, 0.0f });
    CircleCollider col(0.5f);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_TRUE(Physics2D::RaycastCheck({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f));
    EXPECT_FALSE(Physics2D::RaycastCheck({ 0.0f, 0.0f }, { 0.0f, 1.0f }, 10.0f));

    Physics2D::ClearContext();
}

// ---- OverlapCircle ----------------------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapCircleFirstAndMiss)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_TRUE(Physics2D::OverlapCircle({ 0.6f, 0.0f }, 0.5f) == &col);
    EXPECT_TRUE(Physics2D::OverlapCircle({ 5.0f, 0.0f }, 0.5f) == nullptr);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapCircleAllAndCheck)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.3f, 0.0f });
    CircleCollider colA(0.5f);
    CircleCollider colB(0.5f);
    world.RegisterBody(a); world.RegisterCollider(colA); world.AttachCollider(a, colA);
    world.RegisterBody(b); world.RegisterCollider(colB); world.AttachCollider(b, colB);

    EXPECT_EQ(Physics2D::OverlapCircleAll({ 0.0f, 0.0f }, 0.5f).size(), std::size_t{ 2 });
    EXPECT_EQ(Physics2D::OverlapCircleAll({ 50.0f, 0.0f }, 0.5f).size(), std::size_t{ 0 });
    EXPECT_TRUE(Physics2D::OverlapCircleCheck({ 0.0f, 0.0f }, 0.5f));
    EXPECT_FALSE(Physics2D::OverlapCircleCheck({ 50.0f, 0.0f }, 0.5f));

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapCircleInvalid)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    const float nan = std::numeric_limits<float>::quiet_NaN();
    EXPECT_TRUE(Physics2D::OverlapCircle({ 0.0f, 0.0f }, 0.0f) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapCircle({ 0.0f, 0.0f }, -1.0f) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapCircle({ 0.0f, 0.0f }, nan) == nullptr);
    EXPECT_EQ(Physics2D::OverlapCircleAll({ 0.0f, 0.0f }, 0.0f).size(), std::size_t{ 0 });

    Physics2D::ClearContext();
}

// ---- OverlapBox -------------------------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapBoxFirstAndMiss)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_TRUE(Physics2D::OverlapBox({ 0.8f, 0.0f }, { 1.0f, 1.0f }, 0.0f) == &col);
    EXPECT_TRUE(Physics2D::OverlapBox({ 5.0f, 0.0f }, { 1.0f, 1.0f }, 0.0f) == nullptr);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapBoxRotated)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    // World box [0.55, 1.55] x [-0.5, 0.5]. An axis-aligned unit query box at the
    // origin reaches x = 0.5 and misses; rotating it 45 deg pushes a corner to
    // x ~= 0.707, which pokes inside the world box.
    Body b; b.SetPosition({ 1.05f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    EXPECT_TRUE(Physics2D::OverlapBox({ 0.0f, 0.0f }, { 1.0f, 1.0f }, 0.0f) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapBox({ 0.0f, 0.0f }, { 1.0f, 1.0f }, 45.0f) == &col);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapBoxInvalid)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    const float nan = std::numeric_limits<float>::quiet_NaN();
    EXPECT_TRUE(Physics2D::OverlapBox({ 0.0f, 0.0f }, { 0.0f, 1.0f }, 0.0f) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapBox({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 0.0f) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapBox({ 0.0f, 0.0f }, { nan, 1.0f }, 0.0f) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapBox({ 0.0f, 0.0f }, { 1.0f, 1.0f }, nan) == nullptr);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapBoxTangentSeparated)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    // Query box edge sits exactly on the world box edge -> touching, not overlapping.
    EXPECT_TRUE(Physics2D::OverlapBox({ 1.0f, 0.0f }, { 1.0f, 1.0f }, 0.0f) == nullptr);

    Physics2D::ClearContext();
}

// ---- OverlapPolygon ---------------------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapPolygonFirstAndCheck)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    Vec2 square[4] = { { -0.5f, -0.5f }, { 0.5f, -0.5f }, { 0.5f, 0.5f }, { -0.5f, 0.5f } };
    EXPECT_TRUE(Physics2D::OverlapPolygon({ 0.5f, 0.0f }, square, 4) == &col);
    EXPECT_TRUE(Physics2D::OverlapPolygonCheck({ 0.5f, 0.0f }, square, 4));
    EXPECT_TRUE(Physics2D::OverlapPolygon({ 5.0f, 0.0f }, square, 4) == nullptr);

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapPolygonAll)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.3f, 0.0f });
    CircleCollider colA(0.5f);
    CircleCollider colB(0.5f);
    world.RegisterBody(a); world.RegisterCollider(colA); world.AttachCollider(a, colA);
    world.RegisterBody(b); world.RegisterCollider(colB); world.AttachCollider(b, colB);

    Vec2 square[4] = { { -0.5f, -0.5f }, { 0.5f, -0.5f }, { 0.5f, 0.5f }, { -0.5f, 0.5f } };
    EXPECT_EQ(Physics2D::OverlapPolygonAll({ 0.0f, 0.0f }, square, 4).size(), std::size_t{ 2 });
    EXPECT_EQ(Physics2D::OverlapPolygonAll({ 50.0f, 0.0f }, square, 4).size(), std::size_t{ 0 });

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_OverlapPolygonInvalid)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body b; b.SetPosition({ 0.0f, 0.0f });
    BoxCollider col({ 0.5f, 0.5f });
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    const float nan = std::numeric_limits<float>::quiet_NaN();
    Vec2 line[2] = { { 0.0f, 0.0f }, { 1.0f, 0.0f } };
    Vec2 nanPoly[3] = { { 0.0f, 0.0f }, { 1.0f, 0.0f }, { nan, 1.0f } };
    EXPECT_TRUE(Physics2D::OverlapPolygon({ 0.0f, 0.0f }, line, 2) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapPolygon({ 0.0f, 0.0f }, nullptr, 4) == nullptr);
    EXPECT_TRUE(Physics2D::OverlapPolygon({ 0.0f, 0.0f }, nanPoly, 3) == nullptr);

    Physics2D::ClearContext();
}

// ---- ContainsPointAll & user data ------------------------------------------

FUNKE_PHYS_TEST_CASE(Physics2D_ContainsPointAll)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.2f, 0.0f });
    CircleCollider colA(0.5f);
    CircleCollider colB(0.5f);
    world.RegisterBody(a); world.RegisterCollider(colA); world.AttachCollider(a, colA);
    world.RegisterBody(b); world.RegisterCollider(colB); world.AttachCollider(b, colB);

    EXPECT_EQ(Physics2D::ContainsPointAll({ 0.0f, 0.0f }).size(), std::size_t{ 2 });
    EXPECT_EQ(Physics2D::ContainsPointAll({ -0.4f, 0.0f }).size(), std::size_t{ 1 });
    EXPECT_EQ(Physics2D::ContainsPointAll({ 5.0f, 0.0f }).size(), std::size_t{ 0 });

    Physics2D::ClearContext();
}

FUNKE_PHYS_TEST_CASE(Physics2D_ContainsPointAllNoContext)
{
    Physics2D::ClearContext();
    EXPECT_EQ(Physics2D::ContainsPointAll({ 0.0f, 0.0f }).size(), std::size_t{ 0 });
}

FUNKE_PHYS_TEST_CASE(Physics2D_ColliderUserDataRoundTrip)
{
    PhysicsWorld world;
    Physics2D::SetContext(world);

    int entity = 42;
    Body b; b.SetPosition({ 5.0f, 0.0f });
    CircleCollider col(0.5f);
    col.SetUserData(&entity);
    world.RegisterBody(b); world.RegisterCollider(col); world.AttachCollider(b, col);

    RaycastHit hit = Physics2D::Raycast({ 0.0f, 0.0f }, { 1.0f, 0.0f }, 10.0f);
    EXPECT_TRUE(hit.hit);
    EXPECT_TRUE(hit.collider != nullptr);
    EXPECT_TRUE(hit.collider->GetUserData() == &entity);

    Physics2D::ClearContext();
}
