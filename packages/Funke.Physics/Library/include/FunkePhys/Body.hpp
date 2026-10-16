#pragma once
#include "Export.hpp"
#include "BodyType.hpp"
#include "Vec2.hpp"

#include <cstdint>

namespace FunkePhys {
    class Collider;
    class PhysicsWorld;

    class FUNKE_PHYS_API Body
    {
    public:
        Body() noexcept;
        explicit Body(BodyType type) noexcept;
        // Copies carry the simulation state but never the world registration.
        Body(const Body& other) noexcept;
        Body& operator=(const Body& other) noexcept;
        // Unregisters itself so a world never keeps a dangling pointer.
        ~Body();
        void Destroy() noexcept;

        BodyType GetBodyType() const noexcept { return m_BodyType; }
        void SetBodyType(BodyType type) noexcept;

        const Vec2& GetPosition() const noexcept { return m_Position; }
        void SetPosition(const Vec2& p) noexcept;

        const Vec2& GetVelocity() const noexcept { return m_Velocity; }
        void SetVelocity(const Vec2& v) noexcept { m_Velocity = v; }

        float GetMass() const noexcept { return m_Mass; }
        void SetMass(float mass) noexcept { m_Mass = mass > 0.0f ? mass : 1.0f; }

        // Bounciness in [0, 1]. 0 = fully inelastic (no bounce), 1 = perfectly elastic.
        float GetRestitution() const noexcept { return m_Restitution; }
        void SetRestitution(float restitution) noexcept;

        // Coulomb friction coefficient in [0, 1]. The contact friction is the
        // geometric mean of the two participating bodies' coefficients.
        float GetFriction() const noexcept { return m_Friction; }
        void SetFriction(float friction) noexcept;

        bool IsBoundaryCheckEnabled() const noexcept { return m_BoundaryCheckEnabled; }
        void SetBoundaryCheckEnabled(bool enabled) noexcept { m_BoundaryCheckEnabled = enabled; }

        bool IsGravityEnabled() const noexcept { return m_GravityEnabled; }
        void SetGravityEnabled(bool enabled) noexcept { m_GravityEnabled = enabled; }

        Collider* GetCollider() noexcept { return m_Collider; }
        const Collider* GetCollider() const noexcept { return m_Collider; }

        // Raw setter — does not update the collider's back-pointer. Prefer
        // PhysicsWorld::AttachCollider, which keeps both sides in sync.
        void SetCollider(Collider* collider) noexcept;

        // Opaque, pointer-sized user value (e.g. the owning game object) so contacts
        // can be mapped back without a lookup. nullptr = unset.
        void  SetUserData(void* userData) noexcept { m_UserData = userData; }
        void* GetUserData() const noexcept { return m_UserData; }

        PhysicsWorld* GetWorld() const noexcept { return m_World; }

    private:
        friend class PhysicsWorld;
        static constexpr std::uint32_t k_NoIndex = 0xFFFFFFFFu;

        BodyType m_BodyType = BodyType::Dynamic;
        Vec2 m_Position{ 0.0f, 0.0f };
        Vec2 m_Velocity{ 0.0f, 0.0f };
        float m_Mass = 1.0f;
        float m_Restitution = 0.0f;
        float m_Friction = 0.3f;
        bool m_BoundaryCheckEnabled = true;
        bool m_GravityEnabled = true;
        Collider* m_Collider = nullptr;
        void* m_UserData = nullptr;

        PhysicsWorld* m_World = nullptr;
        std::uint32_t m_WorldIndex = k_NoIndex;
    };
}
