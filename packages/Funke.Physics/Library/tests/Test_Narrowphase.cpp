#include "TestRunner.hpp"

#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"
#include "FunkePhys/Physics2D.hpp"

using namespace FunkePhys;

namespace {
    struct ColliderFixture
    {
        Body body;
        explicit ColliderFixture(const Vec2& pos) {
            body.SetBodyType(BodyType::Dynamic);
            body.SetPosition(pos);
        }
    };
}

FUNKE_PHYS_TEST_CASE(Narrowphase_CircleVsCircleNoOverlap)
{
    ColliderFixture a({ 0.0f, 0.0f });
    ColliderFixture b({ 5.0f, 0.0f });
    CircleCollider circleA(0.5f);
    CircleCollider circleB(0.5f);
    a.body.SetCollider(&circleA); circleA.SetBody(&a.body);
    b.body.SetCollider(&circleB); circleB.SetBody(&b.body);

    auto contact = Physics2D::OverlapsWith(circleA, circleB);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Narrowphase_CircleVsCircleOverlap)
{
    ColliderFixture a({ 0.0f, 0.0f });
    ColliderFixture b({ 0.6f, 0.0f });
    CircleCollider circleA(0.5f);
    CircleCollider circleB(0.5f);
    a.body.SetCollider(&circleA); circleA.SetBody(&a.body);
    b.body.SetCollider(&circleB); circleB.SetBody(&b.body);

    auto contact = Physics2D::OverlapsWith(circleA, circleB);
    EXPECT_TRUE(contact.has_value());
    EXPECT_NEAR(contact->penetration, 0.4f, 1e-5);
    EXPECT_NEAR(contact->normal.x, 1.0f, 1e-5);
    EXPECT_NEAR(contact->normal.y, 0.0f, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Narrowphase_CircleAABBOverlapButCirclesDontTouch)
{
    // Two circles whose AABBs overlap but actual disks do not.
    // Centers at the diagonal corners of an overlapping AABB region.
    ColliderFixture a({ 0.0f, 0.0f });
    ColliderFixture b({ 0.9f, 0.9f });
    CircleCollider circleA(0.5f);
    CircleCollider circleB(0.5f);
    a.body.SetCollider(&circleA); circleA.SetBody(&a.body);
    b.body.SetCollider(&circleB); circleB.SetBody(&b.body);

    // AABBs overlap (each AABB is a 1x1 square; they share the center quadrant).
    EXPECT_TRUE(circleA.ComputeAABB().Intersects(circleB.ComputeAABB()));

    // But circles only touch when centers are within 1.0; here distance = sqrt(0.9^2+0.9^2) ~= 1.27 > 1.0.
    auto contact = Physics2D::OverlapsWith(circleA, circleB);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Narrowphase_CircleVsBoxOutside)
{
    ColliderFixture circleBody({ 1.2f, 0.0f });
    ColliderFixture boxBody({ 0.0f, 0.0f });
    CircleCollider circle(0.5f);
    BoxCollider box({ 1.0f, 1.0f });
    circleBody.body.SetCollider(&circle); circle.SetBody(&circleBody.body);
    boxBody.body.SetCollider(&box); box.SetBody(&boxBody.body);

    auto contact = Physics2D::OverlapsWith(circle, box);
    EXPECT_TRUE(contact.has_value());
    EXPECT_NEAR(contact->penetration, 0.3f, 1e-5);
    EXPECT_NEAR(contact->normal.x, -1.0f, 1e-5);
    EXPECT_NEAR(contact->normal.y, 0.0f, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Narrowphase_CircleVsBoxOutsideTangent)
{
    // Tangent: circle touches box but does not penetrate.
    ColliderFixture circleBody({ 1.5f, 0.0f });
    ColliderFixture boxBody({ 0.0f, 0.0f });
    CircleCollider circle(0.5f);
    BoxCollider box({ 1.0f, 1.0f });
    circleBody.body.SetCollider(&circle); circle.SetBody(&circleBody.body);
    boxBody.body.SetCollider(&box); box.SetBody(&boxBody.body);

    auto contact = Physics2D::OverlapsWith(circle, box);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Narrowphase_BoxVsCircleNormalIsFlipped)
{
    ColliderFixture circleBody({ 1.2f, 0.0f });
    ColliderFixture boxBody({ 0.0f, 0.0f });
    CircleCollider circle(0.5f);
    BoxCollider box({ 1.0f, 1.0f });
    circleBody.body.SetCollider(&circle); circle.SetBody(&circleBody.body);
    boxBody.body.SetCollider(&box); box.SetBody(&boxBody.body);

    auto contact = Physics2D::OverlapsWith(box, circle);
    EXPECT_TRUE(contact.has_value());
    EXPECT_NEAR(contact->normal.x, 1.0f, 1e-5);
    EXPECT_NEAR(contact->normal.y, 0.0f, 1e-5);
    EXPECT_TRUE(contact->colliderA == &box);
    EXPECT_TRUE(contact->colliderB == &circle);
}

FUNKE_PHYS_TEST_CASE(Narrowphase_CircleCenterInsideBox)
{
    // Circle center at (0.4, 0) inside box centered at origin halfExtents (0.5, 0.5).
    // Closest edges: right (0.1) and along Y the center is 0.5 away from top/bottom edges.
    // distRight = 0.1 < distLeft (0.9), distBottom (0.5), distTop (0.5).
    ColliderFixture circleBody({ 0.4f, 0.0f });
    ColliderFixture boxBody({ 0.0f, 0.0f });
    CircleCollider circle(0.2f);
    BoxCollider box({ 0.5f, 0.5f });
    circleBody.body.SetCollider(&circle); circle.SetBody(&circleBody.body);
    boxBody.body.SetCollider(&box); box.SetBody(&boxBody.body);

    auto contact = Physics2D::OverlapsWith(circle, box);
    EXPECT_TRUE(contact.has_value());
    // Should pop out through right edge: normal direction such that resolver moves circle in +X.
    EXPECT_NEAR(contact->normal.x, -1.0f, 1e-5);
    EXPECT_NEAR(contact->normal.y, 0.0f, 1e-5);
    // Penetration = distRight (0.1) + radius (0.2) = 0.3.
    EXPECT_NEAR(contact->penetration, 0.3f, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Narrowphase_BoxVsBoxAABB)
{
    ColliderFixture a({ 0.0f, 0.0f });
    ColliderFixture b({ 0.8f, 0.0f });
    BoxCollider boxA({ 0.5f, 0.5f });
    BoxCollider boxB({ 0.5f, 0.5f });
    a.body.SetCollider(&boxA); boxA.SetBody(&a.body);
    b.body.SetCollider(&boxB); boxB.SetBody(&b.body);

    auto contact = Physics2D::OverlapsWith(boxA, boxB);
    EXPECT_TRUE(contact.has_value());
    EXPECT_NEAR(contact->penetration, 0.2f, 1e-5);
    EXPECT_NEAR(contact->normal.x, 1.0f, 1e-5);
    EXPECT_NEAR(contact->normal.y, 0.0f, 1e-5);
}
