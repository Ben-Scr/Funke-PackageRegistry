#include "FunkePhys/Collider.hpp"
#include "FunkePhys/Body.hpp"
#include "FunkePhys/PhysicsWorld.hpp"

namespace FunkePhys {
    Collider::Collider(ColliderType type) noexcept
        : m_Type(type)
    {}

    Collider::Collider(const Collider& other) noexcept
        : m_Type(other.m_Type)
        , m_UserData(other.m_UserData)
    {}

    Collider& Collider::operator=(const Collider& other) noexcept
    {
        if (this != &other) {
            m_UserData = other.m_UserData;
            OnShapeChanged();
        }
        return *this;
    }

    Collider::~Collider()
    {
        if (m_World != nullptr) {
            m_World->UnregisterCollider(*this);
        }
        Destroy();
    }

    void Collider::Destroy() noexcept
    {
        if (m_Body != nullptr) {
            if (m_Body->GetCollider() == this) {
                m_Body->SetCollider(nullptr);
            }
            m_Body = nullptr;
        }
    }

    void Collider::SetBody(Body* body) noexcept
    {
        m_Body = body;
        OnShapeChanged();
    }

    void Collider::OnShapeChanged() noexcept
    {
        if (m_World != nullptr) {
            m_World->m_QueryDirty = true;
        }
    }
}
