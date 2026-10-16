#include "FunkePhys/Body.hpp"
#include "FunkePhys/Collider.hpp"
#include "FunkePhys/PhysicsWorld.hpp"

#include <algorithm>

namespace FunkePhys {
    Body::Body() noexcept
    {
        SetBodyType(BodyType::Dynamic);
    }

    Body::Body(BodyType type) noexcept
    {
        SetBodyType(type);
    }

    Body::Body(const Body& other) noexcept
        : m_BodyType(other.m_BodyType)
        , m_Position(other.m_Position)
        , m_Velocity(other.m_Velocity)
        , m_Mass(other.m_Mass)
        , m_Restitution(other.m_Restitution)
        , m_Friction(other.m_Friction)
        , m_BoundaryCheckEnabled(other.m_BoundaryCheckEnabled)
        , m_GravityEnabled(other.m_GravityEnabled)
        , m_UserData(other.m_UserData)
    {}

    Body& Body::operator=(const Body& other) noexcept
    {
        if (this != &other) {
            m_BodyType = other.m_BodyType;
            m_Velocity = other.m_Velocity;
            m_Mass = other.m_Mass;
            m_Restitution = other.m_Restitution;
            m_Friction = other.m_Friction;
            m_BoundaryCheckEnabled = other.m_BoundaryCheckEnabled;
            m_GravityEnabled = other.m_GravityEnabled;
            m_UserData = other.m_UserData;
            SetPosition(other.m_Position);
        }
        return *this;
    }

    Body::~Body()
    {
        if (m_World != nullptr) {
            m_World->UnregisterBody(*this);
        }
        Destroy();
    }

    void Body::Destroy() noexcept
    {
        if (m_Collider != nullptr) {
            if (m_Collider->GetBody() == this) {
                m_Collider->SetBody(nullptr);
            }
            m_Collider = nullptr;
        }
    }

    void Body::SetBodyType(BodyType type) noexcept
    {
        m_BodyType = type;

        switch (m_BodyType) {
        case BodyType::Static:
            SetGravityEnabled(false);
            SetBoundaryCheckEnabled(false);
            SetVelocity({ 0.0f, 0.0f });
            break;
        case BodyType::Kinematic:
            SetGravityEnabled(false);
            SetBoundaryCheckEnabled(true);
            break;
        case BodyType::Dynamic:
            SetGravityEnabled(true);
            SetBoundaryCheckEnabled(true);
            break;
        }
    }

    void Body::SetPosition(const Vec2& p) noexcept
    {
        m_Position = p;
        if (m_World != nullptr) {
            m_World->m_QueryDirty = true;
        }
    }

    void Body::SetRestitution(float restitution) noexcept
    {
        m_Restitution = std::clamp(restitution, 0.0f, 1.0f);
    }

    void Body::SetFriction(float friction) noexcept
    {
        m_Friction = std::clamp(friction, 0.0f, 1.0f);
    }

    void Body::SetCollider(Collider* collider) noexcept
    {
        m_Collider = collider;
        if (m_World != nullptr) {
            m_World->m_QueryDirty = true;
        }
    }
}
