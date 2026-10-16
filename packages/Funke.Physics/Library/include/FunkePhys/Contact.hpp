#pragma once
#include "Vec2.hpp"

namespace FunkePhys {
    class Body;
    class Collider;

    // A single point-of-overlap between two colliders.
    // `normal` points from A toward B; `penetration` is the depth along that normal.
    struct Contact
    {
        Body* bodyA = nullptr;
        Body* bodyB = nullptr;
        Collider* colliderA = nullptr;
        Collider* colliderB = nullptr;
        Vec2 normal{ 0.0f, 0.0f };
        float penetration = 0.0f;
    };
}
