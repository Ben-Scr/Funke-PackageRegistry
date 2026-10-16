#include "TestRunner.hpp"

#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"
#include "FunkePhys/PhysicsWorld.hpp"

using namespace FunkePhys;

FUNKE_PHYS_TEST_CASE(World_GravityAccelDynamic)
{
    PhysicsWorld world;
    Body body;
    body.SetBodyType(BodyType::Dynamic);
    body.SetPosition({ 0.0f, 0.0f });
    world.RegisterBody(body);

    // Default gravity = (0, -9.81).
    world.Step(1.0f);
    EXPECT_NEAR(body.GetVelocity().y, -9.81f, 1e-4);
    EXPECT_NEAR(body.GetPosition().y, -9.81f, 1e-4);
}

FUNKE_PHYS_TEST_CASE(World_StaticBodyDoesNotMove)
{
    PhysicsWorld world;
    Body body(BodyType::Static);
    body.SetPosition({ 1.0f, 2.0f });
    world.RegisterBody(body);

    world.Step(0.5f);
    EXPECT_NEAR(body.GetPosition().x, 1.0f, 1e-6);
    EXPECT_NEAR(body.GetPosition().y, 2.0f, 1e-6);
}

FUNKE_PHYS_TEST_CASE(World_AttachColliderUpdatesBothSides)
{
    PhysicsWorld world;
    Body body;
    BoxCollider collider({ 0.5f, 0.5f });
    world.RegisterBody(body);
    world.RegisterCollider(collider);

    EXPECT_TRUE(world.AttachCollider(body, collider));
    EXPECT_TRUE(body.GetCollider() == &collider);
    EXPECT_TRUE(collider.GetBody() == &body);
}

FUNKE_PHYS_TEST_CASE(World_AttachColliderReassignSeversPrevious)
{
    PhysicsWorld world;
    Body body1;
    Body body2;
    BoxCollider collider({ 0.5f, 0.5f });
    world.RegisterBody(body1);
    world.RegisterBody(body2);
    world.RegisterCollider(collider);

    EXPECT_TRUE(world.AttachCollider(body1, collider));
    EXPECT_TRUE(world.AttachCollider(body2, collider));
    // Old body should no longer reference the collider.
    EXPECT_TRUE(body1.GetCollider() == nullptr);
    EXPECT_TRUE(body2.GetCollider() == &collider);
    EXPECT_TRUE(collider.GetBody() == &body2);
}

FUNKE_PHYS_TEST_CASE(World_DynamicBodyRestsOnStaticGround)
{
    // Configure a small world with no gravity and zero-restitution; the dynamic
    // body should be pushed back out of the ground after a single step.
    WorldSettings settings;
    settings.gravity = { 0.0f, 0.0f };
    PhysicsWorld world(settings);

    Body ground(BodyType::Static);
    ground.SetPosition({ 0.0f, 0.0f });
    BoxCollider groundCollider({ 5.0f, 0.5f });

    Body dyn(BodyType::Dynamic);
    dyn.SetPosition({ 0.0f, 0.4f }); // halfExtents 0.5 + 0.5 = 1.0; centers 0.4 apart -> overlap 0.6
    dyn.SetVelocity({ 0.0f, -1.0f });
    BoxCollider dynCollider({ 0.5f, 0.5f });

    world.RegisterBody(ground); world.RegisterCollider(groundCollider);
    world.AttachCollider(ground, groundCollider);
    world.RegisterBody(dyn); world.RegisterCollider(dynCollider);
    world.AttachCollider(dyn, dynCollider);

    world.Step(0.016f);

    // After one step, the dynamic body must be at or above ground.top + halfHeight = 0.5 + 0.5 = 1.0.
    EXPECT_TRUE(dyn.GetPosition().y >= 1.0f - 1e-4f);
    // Velocity along contact normal should be killed (no bounce).
    EXPECT_NEAR(dyn.GetVelocity().y, 0.0f, 1e-4);
}

FUNKE_PHYS_TEST_CASE(World_RestitutionBouncesDynamicBody)
{
    WorldSettings settings;
    settings.gravity = { 0.0f, 0.0f };
    PhysicsWorld world(settings);

    Body ground(BodyType::Static);
    ground.SetPosition({ 0.0f, 0.0f });
    BoxCollider groundCollider({ 5.0f, 0.5f });

    Body dyn(BodyType::Dynamic);
    dyn.SetPosition({ 0.0f, 0.95f });   // overlap 0.05
    dyn.SetVelocity({ 0.0f, -2.0f });
    dyn.SetRestitution(1.0f);
    ground.SetRestitution(1.0f);
    BoxCollider dynCollider({ 0.5f, 0.5f });

    world.RegisterBody(ground); world.RegisterCollider(groundCollider);
    world.AttachCollider(ground, groundCollider);
    world.RegisterBody(dyn); world.RegisterCollider(dynCollider);
    world.AttachCollider(dyn, dynCollider);

    world.Step(0.016f);
    // With restitution = 1, the y velocity should reverse.
    EXPECT_TRUE(dyn.GetVelocity().y > 0.0f);
}

FUNKE_PHYS_TEST_CASE(World_UnregisterBodyClearsContacts)
{
    WorldSettings settings;
    settings.gravity = { 0.0f, 0.0f };
    PhysicsWorld world(settings);

    Body a; a.SetPosition({ 0.0f, 0.0f });
    Body b; b.SetPosition({ 0.5f, 0.0f });
    BoxCollider colA({ 0.5f, 0.5f });
    BoxCollider colB({ 0.5f, 0.5f });
    world.RegisterBody(a); world.RegisterCollider(colA); world.AttachCollider(a, colA);
    world.RegisterBody(b); world.RegisterCollider(colB); world.AttachCollider(b, colB);

    world.Step(0.016f);
    EXPECT_TRUE(world.GetContacts().size() >= 1);

    world.UnregisterBody(a);
    EXPECT_EQ(world.GetBodyCount(), 1u);
    for (const Contact& c : world.GetContacts()) {
        EXPECT_FALSE(c.bodyA == &a || c.bodyB == &a);
    }
}

FUNKE_PHYS_TEST_CASE(World_BoundsClampPosition)
{
    WorldSettings settings;
    settings.gravity = { 0.0f, 0.0f };
    settings.enableWorldBounds = true;
    settings.worldMin = { -1.0f, -1.0f };
    settings.worldMax = { 1.0f, 1.0f };
    PhysicsWorld world(settings);

    Body body(BodyType::Dynamic);
    body.SetPosition({ 0.0f, 0.0f });
    body.SetVelocity({ 100.0f, 0.0f });
    world.RegisterBody(body);

    world.Step(1.0f);
    EXPECT_NEAR(body.GetPosition().x, 1.0f, 1e-6);
    EXPECT_NEAR(body.GetVelocity().x, 0.0f, 1e-6);
}
