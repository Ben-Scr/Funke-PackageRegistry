#include "TestRunner.hpp"

#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/PhysicsWorld.hpp"

using namespace FunkePhys;

FUNKE_PHYS_TEST_CASE(Solver_FrictionDecaysSlidingVelocity)
{
    WorldSettings settings;
    settings.gravity = { 0.0f, -9.81f };
    PhysicsWorld world(settings);

    Body ground(BodyType::Static);
    ground.SetPosition({ 0.0f, 0.0f });
    ground.SetFriction(0.5f);
    BoxCollider groundCollider({ 100.0f, 0.5f });

    Body box(BodyType::Dynamic);
    box.SetPosition({ 0.0f, 0.99f });   // overlap by 0.01 with the ground top
    box.SetVelocity({ 5.0f, 0.0f });
    box.SetFriction(0.5f);
    BoxCollider dynCollider({ 0.5f, 0.5f });

    world.RegisterBody(ground); world.RegisterCollider(groundCollider);
    world.AttachCollider(ground, groundCollider);
    world.RegisterBody(box); world.RegisterCollider(dynCollider);
    world.AttachCollider(box, dynCollider);

    const float vx0 = box.GetVelocity().x;
    for (int i = 0; i < 60; ++i) world.Step(1.0f / 60.0f);
    EXPECT_TRUE(box.GetVelocity().x < vx0);
    EXPECT_TRUE(box.GetVelocity().x >= 0.0f);
}

FUNKE_PHYS_TEST_CASE(Solver_NoFrictionPreservesSlidingVelocity)
{
    // With both bodies' friction = 0 the tangential velocity should not decay
    // even though the bodies stay in contact under gravity.
    WorldSettings settings;
    settings.gravity = { 0.0f, -9.81f };
    PhysicsWorld world(settings);

    Body ground(BodyType::Static);
    ground.SetPosition({ 0.0f, 0.0f });
    ground.SetFriction(0.0f);
    BoxCollider groundCollider({ 100.0f, 0.5f });

    Body box(BodyType::Dynamic);
    box.SetPosition({ 0.0f, 0.99f });
    box.SetVelocity({ 5.0f, 0.0f });
    box.SetFriction(0.0f);
    BoxCollider dynCollider({ 0.5f, 0.5f });

    world.RegisterBody(ground); world.RegisterCollider(groundCollider);
    world.AttachCollider(ground, groundCollider);
    world.RegisterBody(box); world.RegisterCollider(dynCollider);
    world.AttachCollider(box, dynCollider);

    for (int i = 0; i < 60; ++i) world.Step(1.0f / 60.0f);
    // No friction: vx should be unchanged.
    EXPECT_NEAR(box.GetVelocity().x, 5.0f, 1e-3);
}

FUNKE_PHYS_TEST_CASE(Solver_StackOfThreeBoxesSettles)
{
    // A 3-box dynamic stack on a static ground. With iterative resolution the
    // top body's vertical velocity must eventually settle close to zero.
    WorldSettings settings;
    settings.gravity = { 0.0f, -9.81f };
    settings.solverIterations = 12;
    PhysicsWorld world(settings);

    Body ground(BodyType::Static);
    ground.SetPosition({ 0.0f, 0.0f });
    BoxCollider groundCollider({ 10.0f, 0.5f });
    world.RegisterBody(ground); world.RegisterCollider(groundCollider);
    world.AttachCollider(ground, groundCollider);

    Body b1(BodyType::Dynamic); b1.SetPosition({ 0.0f, 1.0f });
    Body b2(BodyType::Dynamic); b2.SetPosition({ 0.0f, 2.0f });
    Body b3(BodyType::Dynamic); b3.SetPosition({ 0.0f, 3.0f });
    BoxCollider c1({ 0.5f, 0.5f });
    BoxCollider c2({ 0.5f, 0.5f });
    BoxCollider c3({ 0.5f, 0.5f });
    world.RegisterBody(b1); world.RegisterCollider(c1); world.AttachCollider(b1, c1);
    world.RegisterBody(b2); world.RegisterCollider(c2); world.AttachCollider(b2, c2);
    world.RegisterBody(b3); world.RegisterCollider(c3); world.AttachCollider(b3, c3);

    for (int i = 0; i < 240; ++i) world.Step(1.0f / 60.0f);

    // After ~4s of simulation the top box should be roughly at rest.
    EXPECT_TRUE(std::fabs(b3.GetVelocity().y) < 1.0f);
    // And it must not have fallen through.
    EXPECT_TRUE(b3.GetPosition().y > 1.5f);
}

FUNKE_PHYS_TEST_CASE(Solver_IterationsClampedToOne)
{
    // Setting solverIterations to 0 must still produce a valid step (treated
    // as 1 internally), and a basic ground-rest scenario should still work.
    WorldSettings settings;
    settings.gravity = { 0.0f, 0.0f };
    settings.solverIterations = 0;
    PhysicsWorld world(settings);
    EXPECT_TRUE(world.GetSettings().solverIterations == 1);

    Body ground(BodyType::Static);
    ground.SetPosition({ 0.0f, 0.0f });
    BoxCollider groundCollider({ 5.0f, 0.5f });

    Body box(BodyType::Dynamic);
    box.SetPosition({ 0.0f, 0.95f });
    box.SetVelocity({ 0.0f, -2.0f });
    BoxCollider dynCollider({ 0.5f, 0.5f });

    world.RegisterBody(ground); world.RegisterCollider(groundCollider);
    world.AttachCollider(ground, groundCollider);
    world.RegisterBody(box); world.RegisterCollider(dynCollider);
    world.AttachCollider(box, dynCollider);

    world.Step(0.016f);
    // Should still have processed at least one velocity pass.
    EXPECT_TRUE(box.GetVelocity().y >= 0.0f);
}
