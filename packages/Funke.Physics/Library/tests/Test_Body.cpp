#include "TestRunner.hpp"

#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"

using namespace FunkePhys;

FUNKE_PHYS_TEST_CASE(Body_DefaultsToDynamic)
{
    Body body;
    EXPECT_EQ(body.GetBodyType(), BodyType::Dynamic);
    EXPECT_TRUE(body.IsGravityEnabled());
    EXPECT_TRUE(body.IsBoundaryCheckEnabled());
}

FUNKE_PHYS_TEST_CASE(Body_StaticZerosVelocity)
{
    Body body;
    body.SetVelocity({ 5.0f, 5.0f });
    body.SetBodyType(BodyType::Static);
    EXPECT_NEAR(body.GetVelocity().x, 0.0f, 1e-6);
    EXPECT_NEAR(body.GetVelocity().y, 0.0f, 1e-6);
    EXPECT_FALSE(body.IsGravityEnabled());
    EXPECT_FALSE(body.IsBoundaryCheckEnabled());
}

FUNKE_PHYS_TEST_CASE(Body_KinematicKeepsVelocityNoGravity)
{
    Body body;
    body.SetVelocity({ 1.0f, -2.0f });
    body.SetBodyType(BodyType::Kinematic);
    EXPECT_NEAR(body.GetVelocity().x, 1.0f, 1e-6);
    EXPECT_NEAR(body.GetVelocity().y, -2.0f, 1e-6);
    EXPECT_FALSE(body.IsGravityEnabled());
    EXPECT_TRUE(body.IsBoundaryCheckEnabled());
}

FUNKE_PHYS_TEST_CASE(Body_RestitutionClamped)
{
    Body body;
    body.SetRestitution(2.0f);
    EXPECT_NEAR(body.GetRestitution(), 1.0f, 1e-6);
    body.SetRestitution(-0.5f);
    EXPECT_NEAR(body.GetRestitution(), 0.0f, 1e-6);
    body.SetRestitution(0.5f);
    EXPECT_NEAR(body.GetRestitution(), 0.5f, 1e-6);
}

FUNKE_PHYS_TEST_CASE(Body_FrictionClamped)
{
    Body body;
    EXPECT_NEAR(body.GetFriction(), 0.3f, 1e-6); // default
    body.SetFriction(2.0f);
    EXPECT_NEAR(body.GetFriction(), 1.0f, 1e-6);
    body.SetFriction(-1.0f);
    EXPECT_NEAR(body.GetFriction(), 0.0f, 1e-6);
}

FUNKE_PHYS_TEST_CASE(Body_MassClampsToOneOnInvalid)
{
    Body body;
    body.SetMass(-3.0f);
    EXPECT_NEAR(body.GetMass(), 1.0f, 1e-6);
    body.SetMass(0.0f);
    EXPECT_NEAR(body.GetMass(), 1.0f, 1e-6);
    body.SetMass(2.5f);
    EXPECT_NEAR(body.GetMass(), 2.5f, 1e-6);
}

FUNKE_PHYS_TEST_CASE(Body_DestroyDetachesCollider)
{
    Body body;
    BoxCollider collider({ 0.5f, 0.5f });
    body.SetCollider(&collider);
    collider.SetBody(&body);

    body.Destroy();
    EXPECT_TRUE(body.GetCollider() == nullptr);
    EXPECT_TRUE(collider.GetBody() == nullptr);
}
