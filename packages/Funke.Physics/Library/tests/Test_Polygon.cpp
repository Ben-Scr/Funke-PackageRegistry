#include "TestRunner.hpp"

#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"
#include "FunkePhys/Physics2D.hpp"
#include "FunkePhys/PolygonCollider.hpp"

#include <vector>

using namespace FunkePhys;

namespace {
    // CCW-wound triangle centered on local origin.
    std::vector<Vec2> Triangle(float size) {
        return {
            { -size,        -size * 0.5f },
            {  size,        -size * 0.5f },
            {  0.0f,         size }
        };
    }

    // CCW-wound square (4-vertex polygon) centered on local origin.
    std::vector<Vec2> Square(float half) {
        return {
            { -half, -half },
            {  half, -half },
            {  half,  half },
            { -half,  half }
        };
    }
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsTriangleOverlap)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.5f, 0.0f }); // overlapping
    PolygonCollider triA;
    triA.SetVertices(Triangle(1.0f));
    PolygonCollider triB;
    triB.SetVertices(Triangle(1.0f));
    a.SetCollider(&triA); triA.SetBody(&a);
    b.SetCollider(&triB); triB.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(triA, triB);
    EXPECT_TRUE(contact.has_value());
    EXPECT_TRUE(contact->penetration > 0.0f);
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsTriangleSeparated)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 5.0f, 0.0f }); // far apart
    PolygonCollider triA;
    triA.SetVertices(Triangle(1.0f));
    PolygonCollider triB;
    triB.SetVertices(Triangle(1.0f));
    a.SetCollider(&triA); triA.SetBody(&a);
    b.SetCollider(&triB); triB.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(triA, triB);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Polygon_AABBOverlapsButPolygonsDont)
{
    // Two CCW triangles whose AABBs overlap but whose filled areas are separated
    // by a diagonal gap. This is the case the prior AABB-only narrowphase got
    // wrong.
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.3f, 0.3f });

    // Triangle A: lower-left half of a square, y <= -x.
    PolygonCollider triA;
    triA.SetVertices(std::vector<Vec2>{
        { -1.0f, -1.0f },
        {  1.0f, -1.0f },
        { -1.0f,  1.0f }
    });

    // Triangle B: upper-right half of a square, shifted so its hypotenuse is
    // y = -x + 0.6 in world space.
    PolygonCollider triB;
    triB.SetVertices(std::vector<Vec2>{
        {  1.0f,  1.0f },
        { -1.0f,  1.0f },
        {  1.0f, -1.0f }
    });

    a.SetCollider(&triA); triA.SetBody(&a);
    b.SetCollider(&triB); triB.SetBody(&b);

    EXPECT_TRUE(triA.ComputeAABB().Intersects(triB.ComputeAABB()));

    auto contact = Physics2D::OverlapsWith(triA, triB);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsTriangleTangent)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.0f, 0.0f });

    PolygonCollider triA;
    triA.SetVertices(std::vector<Vec2>{
        { -1.0f, -1.0f },
        {  1.0f, -1.0f },
        { -1.0f,  1.0f }
    });

    PolygonCollider triB;
    triB.SetVertices(std::vector<Vec2>{
        {  1.0f,  1.0f },
        { -1.0f,  1.0f },
        {  1.0f, -1.0f }
    });

    a.SetCollider(&triA); triA.SetBody(&a);
    b.SetCollider(&triB); triB.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(triA, triB);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsCircleTangent)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.0f, 1.4f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f)); // tip at (0, 1)
    CircleCollider circle(0.4f);
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&circle); circle.SetBody(&b);

    auto polyCircle = Physics2D::OverlapsWith(tri, circle);
    auto circlePoly = Physics2D::OverlapsWith(circle, tri);
    EXPECT_FALSE(polyCircle.has_value());
    EXPECT_FALSE(circlePoly.has_value());
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsBoxTangent)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.0f, 1.5f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f)); // tip at (0, 1)
    BoxCollider box({ 0.5f, 0.5f });
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&box); box.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(tri, box);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsBox)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.8f, 0.0f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f));
    BoxCollider box({ 0.5f, 0.5f });
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&box); box.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(tri, box);
    EXPECT_TRUE(contact.has_value());
    EXPECT_TRUE(contact->penetration > 0.0f);
}

FUNKE_PHYS_TEST_CASE(Polygon_BoxVsTriangleNormalIsFlipped)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.8f, 0.0f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f));
    BoxCollider box({ 0.5f, 0.5f });
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&box); box.SetBody(&b);

    auto contactPolyBox = Physics2D::OverlapsWith(tri, box);
    auto contactBoxPoly = Physics2D::OverlapsWith(box, tri);
    EXPECT_TRUE(contactPolyBox.has_value());
    EXPECT_TRUE(contactBoxPoly.has_value());
    EXPECT_NEAR(contactPolyBox->normal.x, -contactBoxPoly->normal.x, 1e-5);
    EXPECT_NEAR(contactPolyBox->normal.y, -contactBoxPoly->normal.y, 1e-5);
    EXPECT_NEAR(contactPolyBox->penetration, contactBoxPoly->penetration, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsCircleHit)
{
    // Circle near triangle's tip; should overlap.
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.0f, 1.2f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f)); // tip at (0, 1)
    CircleCollider circle(0.4f);
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&circle); circle.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(tri, circle);
    EXPECT_TRUE(contact.has_value());
    EXPECT_TRUE(contact->penetration > 0.0f);
}

FUNKE_PHYS_TEST_CASE(Polygon_TriangleVsCircleClear)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.0f, 5.0f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f));
    CircleCollider circle(0.4f);
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&circle); circle.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(tri, circle);
    EXPECT_FALSE(contact.has_value());
}

FUNKE_PHYS_TEST_CASE(Polygon_CircleVsTriangleNormalIsFlipped)
{
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.0f, 1.2f });
    PolygonCollider tri;
    tri.SetVertices(Triangle(1.0f));
    CircleCollider circle(0.4f);
    a.SetCollider(&tri); tri.SetBody(&a);
    b.SetCollider(&circle); circle.SetBody(&b);

    auto polyCircle = Physics2D::OverlapsWith(tri, circle);
    auto circlePoly = Physics2D::OverlapsWith(circle, tri);
    EXPECT_TRUE(polyCircle.has_value());
    EXPECT_TRUE(circlePoly.has_value());
    EXPECT_NEAR(polyCircle->normal.x, -circlePoly->normal.x, 1e-5);
    EXPECT_NEAR(polyCircle->normal.y, -circlePoly->normal.y, 1e-5);
}

FUNKE_PHYS_TEST_CASE(Polygon_QuadVsQuadEquivalentToBox)
{
    // A 4-vertex polygon coinciding with a box should yield the same overlap
    // as box-vs-box. We don't compare contacts directly, just both detect it.
    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.8f, 0.0f });
    PolygonCollider quad;
    quad.SetVertices(Square(0.5f));
    BoxCollider box({ 0.5f, 0.5f });
    a.SetCollider(&quad); quad.SetBody(&a);
    b.SetCollider(&box); box.SetBody(&b);

    auto contact = Physics2D::OverlapsWith(quad, box);
    EXPECT_TRUE(contact.has_value());
    // Penetration should be ~0.2 along the X axis.
    EXPECT_NEAR(contact->penetration, 0.2f, 1e-4);
}
