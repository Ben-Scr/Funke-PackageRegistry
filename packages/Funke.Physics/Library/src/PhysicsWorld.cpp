#include "FunkePhys/PhysicsWorld.hpp"
#include "FunkePhys/Physics2D.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"

#include "ShapeBounds.hpp"
#include "SpatialGrid.hpp"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>

namespace FunkePhys {
    namespace Detail {
        struct ContactConstraint
        {
            Body* bodyA;
            Body* bodyB;
            Vec2 normal;
            float invMassA;
            float invMassB;
            float invMassSum;
            float restitution;
            float friction;
            bool moveA;
            bool moveB;
        };

        struct WorldScratch
        {
            SpatialGrid stepGrid;
            SpatialGrid queryGrid;
            std::vector<AABB> boxes;
            std::vector<std::uint32_t> proxyBody;
            std::vector<ContactConstraint> constraints;
        };
    }

    namespace {
        float Clamp(float value, float minValue, float maxValue) noexcept
        {
            return std::max(minValue, std::min(value, maxValue));
        }

        Vec2 Clamp(const Vec2& value, const Vec2& minValue, const Vec2& maxValue) noexcept
        {
            return {
                Clamp(value.x, minValue.x, maxValue.x),
                Clamp(value.y, minValue.y, maxValue.y)
            };
        }

        float ComputeInverseMass(const Body& body) noexcept
        {
            const float mass = body.GetMass();
            if (mass <= std::numeric_limits<float>::epsilon()) {
                return 0.0f;
            }
            return 1.0f / mass;
        }

        void DetachBodyAndCollider(Body& body) noexcept
        {
            Collider* collider = body.GetCollider();
            if (collider == nullptr) {
                return;
            }
            collider->SetBody(nullptr);
            body.SetCollider(nullptr);
        }
    }

    PhysicsWorld::PhysicsWorld()
        : m_Scratch(std::make_unique<Detail::WorldScratch>())
    {}

    PhysicsWorld::PhysicsWorld(const WorldSettings& settings)
        : m_Settings(SanitizeSettings(settings))
        , m_Scratch(std::make_unique<Detail::WorldScratch>())
    {}

    PhysicsWorld::~PhysicsWorld()
    {
        for (Body* body : m_Bodies) {
            body->m_World = nullptr;
            body->m_WorldIndex = Body::k_NoIndex;
        }
        for (Collider* collider : m_Colliders) {
            collider->m_World = nullptr;
            collider->m_WorldIndex = Collider::k_NoIndex;
        }
        if (Physics2D::GetContext() == this) {
            Physics2D::ClearContext();
        }
    }

    WorldSettings PhysicsWorld::SanitizeSettings(const WorldSettings& settings) noexcept
    {
        WorldSettings sanitized = settings;

        constexpr float kMinCellSize = 1e-3f;
        if (sanitized.broadphaseCellSize > 0.0f) {
            sanitized.broadphaseCellSize = std::max(sanitized.broadphaseCellSize, kMinCellSize);
        }
        sanitized.solverIterations = std::max(1, sanitized.solverIterations);

        if (sanitized.worldMin.x > sanitized.worldMax.x) {
            std::swap(sanitized.worldMin.x, sanitized.worldMax.x);
        }
        if (sanitized.worldMin.y > sanitized.worldMax.y) {
            std::swap(sanitized.worldMin.y, sanitized.worldMax.y);
        }

        return sanitized;
    }

    void PhysicsWorld::SetSettings(const WorldSettings& settings) noexcept
    {
        m_Settings = SanitizeSettings(settings);
        m_QueryDirty = true;
    }

    bool PhysicsWorld::RegisterBody(Body& body)
    {
        if (body.m_World != nullptr) {
            return false;
        }
        body.m_World = this;
        body.m_WorldIndex = static_cast<std::uint32_t>(m_Bodies.size());
        m_Bodies.push_back(&body);
        m_QueryDirty = true;
        return true;
    }

    bool PhysicsWorld::UnregisterBody(Body& body)
    {
        if (body.m_World != this) {
            return false;
        }

        DetachBodyAndCollider(body);
        Body* last = m_Bodies.back();
        m_Bodies[body.m_WorldIndex] = last;
        last->m_WorldIndex = body.m_WorldIndex;
        m_Bodies.pop_back();
        body.m_World = nullptr;
        body.m_WorldIndex = Body::k_NoIndex;
        RemoveContactsWith(&body, nullptr);
        m_QueryDirty = true;
        return true;
    }

    bool PhysicsWorld::RegisterCollider(Collider& collider)
    {
        if (collider.m_World != nullptr) {
            return false;
        }
        collider.m_World = this;
        collider.m_WorldIndex = static_cast<std::uint32_t>(m_Colliders.size());
        m_Colliders.push_back(&collider);
        m_QueryDirty = true;
        return true;
    }

    bool PhysicsWorld::UnregisterCollider(Collider& collider)
    {
        if (collider.m_World != this) {
            return false;
        }

        if (Body* body = collider.GetBody()) {
            body->SetCollider(nullptr);
            collider.SetBody(nullptr);
        }

        Collider* last = m_Colliders.back();
        m_Colliders[collider.m_WorldIndex] = last;
        last->m_WorldIndex = collider.m_WorldIndex;
        m_Colliders.pop_back();
        collider.m_World = nullptr;
        collider.m_WorldIndex = Collider::k_NoIndex;
        RemoveContactsWith(nullptr, &collider);
        m_QueryDirty = true;
        return true;
    }

    void PhysicsWorld::RemoveContactsWith(const Body* body, const Collider* collider)
    {
        if (m_Contacts.empty()) {
            return;
        }
        m_Contacts.erase(
            std::remove_if(m_Contacts.begin(), m_Contacts.end(), [body, collider](const Contact& contact) {
                return (body != nullptr && (contact.bodyA == body || contact.bodyB == body))
                    || (collider != nullptr && (contact.colliderA == collider || contact.colliderB == collider));
            }),
            m_Contacts.end());
    }

    bool PhysicsWorld::AttachCollider(Body& body, Collider& collider)
    {
        if (body.m_World != this || collider.m_World != this) {
            return false;
        }

        // Sever any prior partners so we never leave half-attached state.
        if (Body* existing = collider.GetBody(); existing != nullptr && existing != &body) {
            existing->SetCollider(nullptr);
        }
        if (Collider* existing = body.GetCollider(); existing != nullptr && existing != &collider) {
            existing->SetBody(nullptr);
        }

        body.SetCollider(&collider);
        collider.SetBody(&body);
        return true;
    }

    void PhysicsWorld::DetachCollider(Body& body)
    {
        DetachBodyAndCollider(body);
    }

    void PhysicsWorld::Step(float dt)
    {
        if (dt <= 0.0f) {
            return;
        }

        IntegrateBodies(dt);
        DetectCollisions();
        ResolveContacts();
        m_QueryDirty = true;
    }

    void PhysicsWorld::IntegrateBodies(float dt)
    {
        const Vec2 gravityStep = m_Settings.gravity * dt;
        for (Body* body : m_Bodies) {
            if (body->m_BodyType == BodyType::Static) {
                continue;
            }

            if (body->m_BodyType == BodyType::Dynamic && body->m_GravityEnabled) {
                body->m_Velocity = body->m_Velocity + gravityStep;
            }

            body->m_Position = body->m_Position + (body->m_Velocity * dt);

            if (m_Settings.enableWorldBounds && body->m_BoundaryCheckEnabled) {
                ApplyWorldBounds(*body);
            }
        }
    }

    void PhysicsWorld::ApplyWorldBounds(Body& body) const noexcept
    {
        const Vec2 position = Clamp(body.m_Position, m_Settings.worldMin, m_Settings.worldMax);
        Vec2 velocity = body.m_Velocity;

        if (position.x == m_Settings.worldMin.x || position.x == m_Settings.worldMax.x) {
            velocity.x = 0.0f;
        }
        if (position.y == m_Settings.worldMin.y || position.y == m_Settings.worldMax.y) {
            velocity.y = 0.0f;
        }

        body.m_Position = position;
        body.m_Velocity = velocity;
    }

    float PhysicsWorld::ChooseCellSize(float extentSum, std::size_t count) const noexcept
    {
        if (m_Settings.broadphaseCellSize > 0.0f) {
            return m_Settings.broadphaseCellSize;
        }
        // Twice the average AABB edge keeps most proxies in one to four cells.
        const float average = count > 0 ? extentSum / static_cast<float>(count * 2) : 1.0f;
        return std::max(2.0f * average, 1e-3f);
    }

    void PhysicsWorld::DetectCollisions()
    {
        m_Contacts.clear();
        Detail::WorldScratch& scratch = *m_Scratch;
        scratch.boxes.clear();
        scratch.proxyBody.clear();

        float extentSum = 0.0f;
        for (std::uint32_t i = 0; i < m_Bodies.size(); ++i) {
            const Collider* collider = m_Bodies[i]->m_Collider;
            if (collider == nullptr) {
                continue;
            }
            const AABB box = Detail::ComputeShapeAABB(*collider);
            extentSum += (box.max.x - box.min.x) + (box.max.y - box.min.y);
            scratch.boxes.push_back(box);
            scratch.proxyBody.push_back(i);
        }
        if (scratch.boxes.size() < 2) {
            return;
        }

        const auto count = static_cast<std::uint32_t>(scratch.boxes.size());
        scratch.stepGrid.Build(scratch.boxes.data(), count, ChooseCellSize(extentSum, count));
        scratch.stepGrid.ForEachOverlappingPair([&](std::uint32_t proxyA, std::uint32_t proxyB) {
            std::uint32_t indexA = scratch.proxyBody[proxyA];
            std::uint32_t indexB = scratch.proxyBody[proxyB];
            if (indexA > indexB) {
                std::swap(indexA, indexB);
            }
            Body* bodyA = m_Bodies[indexA];
            Body* bodyB = m_Bodies[indexB];
            // Two static bodies never move; their overlap is not a contact.
            if (bodyA->m_BodyType == BodyType::Static && bodyB->m_BodyType == BodyType::Static) {
                return;
            }
            if (auto contact = Physics2D::OverlapsWith(*bodyA->m_Collider, *bodyB->m_Collider)) {
                m_Contacts.push_back(*contact);
            }
        });
    }

    void PhysicsWorld::ResolveContacts()
    {
        auto& constraints = m_Scratch->constraints;
        constraints.clear();

        // Positional correction is applied once while the constraints are built; each
        // contact's penetration was measured at the start of the frame, so repeating it
        // with stale data would over-correct.
        for (const Contact& contact : m_Contacts) {
            Body* bodyA = contact.bodyA;
            Body* bodyB = contact.bodyB;
            if (bodyA == nullptr || bodyB == nullptr) {
                continue;
            }

            const bool moveA = bodyA->m_BodyType == BodyType::Dynamic;
            const bool moveB = bodyB->m_BodyType == BodyType::Dynamic;
            if (!moveA && !moveB) {
                continue;
            }

            const float invMassA = moveA ? ComputeInverseMass(*bodyA) : 0.0f;
            const float invMassB = moveB ? ComputeInverseMass(*bodyB) : 0.0f;
            const float invMassSum = invMassA + invMassB;
            if (invMassSum <= 0.0f) {
                continue;
            }

            if (contact.penetration > 0.0f) {
                const Vec2 correction = contact.normal * contact.penetration;
                if (moveA) {
                    bodyA->m_Position = bodyA->m_Position - (correction * (invMassA / invMassSum));
                }
                if (moveB) {
                    bodyB->m_Position = bodyB->m_Position + (correction * (invMassB / invMassSum));
                }
            }

            constraints.push_back({
                bodyA, bodyB, contact.normal,
                invMassA, invMassB, invMassSum,
                std::min(bodyA->m_Restitution, bodyB->m_Restitution),
                std::sqrt(bodyA->m_Friction * bodyB->m_Friction),
                moveA, moveB });
        }

        // Velocity solver: looped to converge in stacks. Each iteration applies a normal
        // impulse (with restitution) plus a tangential impulse clamped by Coulomb friction.
        // An iteration that applies nothing leaves every later one with nothing to do.
        const int iterations = std::max(1, m_Settings.solverIterations);
        for (int it = 0; it < iterations; ++it) {
            bool applied = false;
            for (const Detail::ContactConstraint& c : constraints) {
                Vec2& velocityA = c.bodyA->m_Velocity;
                Vec2& velocityB = c.bodyB->m_Velocity;

                const Vec2 relativeVelocity = velocityB - velocityA;
                const float velocityAlongNormal = Dot(relativeVelocity, c.normal);
                if (velocityAlongNormal >= 0.0f) {
                    continue;
                }
                applied = true;

                const float jn = -(1.0f + c.restitution) * velocityAlongNormal / c.invMassSum;
                const Vec2 normalImpulse = c.normal * jn;
                if (c.moveA) velocityA = velocityA - (normalImpulse * c.invMassA);
                if (c.moveB) velocityB = velocityB + (normalImpulse * c.invMassB);

                const Vec2 postRelative = velocityB - velocityA;
                Vec2 tangent = postRelative - c.normal * Dot(postRelative, c.normal);
                const float tangentLenSq = LengthSq(tangent);
                if (tangentLenSq <= 1e-10f) {
                    continue;
                }
                tangent = tangent / std::sqrt(tangentLenSq);

                const float jtRaw = -Dot(postRelative, tangent) / c.invMassSum;
                const float jtMax = c.friction * jn;
                const float jt = std::clamp(jtRaw, -jtMax, jtMax);
                const Vec2 frictionImpulse = tangent * jt;
                if (c.moveA) velocityA = velocityA - (frictionImpulse * c.invMassA);
                if (c.moveB) velocityB = velocityB + (frictionImpulse * c.invMassB);
            }
            if (!applied) {
                break;
            }
        }
    }

    const Detail::SpatialGrid& PhysicsWorld::GetQueryGrid()
    {
        Detail::WorldScratch& scratch = *m_Scratch;
        if (m_QueryDirty) {
            scratch.boxes.clear();
            float extentSum = 0.0f;
            for (const Collider* collider : m_Colliders) {
                const AABB box = Detail::ComputeShapeAABB(*collider);
                extentSum += (box.max.x - box.min.x) + (box.max.y - box.min.y);
                scratch.boxes.push_back(box);
            }
            const auto count = static_cast<std::uint32_t>(scratch.boxes.size());
            scratch.queryGrid.Build(scratch.boxes.data(), count, ChooseCellSize(extentSum, count));
            m_QueryDirty = false;
        }
        return scratch.queryGrid;
    }
}
