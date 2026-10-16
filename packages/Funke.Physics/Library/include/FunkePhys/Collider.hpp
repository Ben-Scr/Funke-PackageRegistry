#pragma once
#include "Export.hpp"
#include "ColliderType.hpp"
#include "AABB.hpp"

#include <cstdint>

namespace FunkePhys {
    class Body;
    class PhysicsWorld;

    class FUNKE_PHYS_API Collider
    {
    public:
        // Copies carry the shape but never the world registration or body pairing.
        Collider(const Collider& other) noexcept;
        Collider& operator=(const Collider& other) noexcept;
        // Unregisters itself so a world never keeps a dangling pointer.
        virtual ~Collider();

        virtual void Destroy() noexcept;

        ColliderType GetType() const noexcept { return m_Type; }

        Body* GetBody() noexcept { return m_Body; }
        const Body* GetBody() const noexcept { return m_Body; }

        void SetBody(Body* body) noexcept;

        // Opaque, pointer-sized user value so a query hit can be mapped back to the
        // owning game object. Store a direct entity pointer (fastest - no lookup) or
        // an integer id via reinterpret_cast. nullptr = unset.
        void  SetUserData(void* userData) noexcept { m_UserData = userData; }
        void* GetUserData() const noexcept { return m_UserData; }

        PhysicsWorld* GetWorld() const noexcept { return m_World; }

        virtual AABB ComputeAABB() const noexcept = 0;

    protected:
        explicit Collider(ColliderType type) noexcept;
        // Shape setters call this so the world rebuilds its query grid.
        void OnShapeChanged() noexcept;

    private:
        friend class PhysicsWorld;
        static constexpr std::uint32_t k_NoIndex = 0xFFFFFFFFu;

        ColliderType m_Type;
        Body* m_Body = nullptr;
        void* m_UserData = nullptr;

        PhysicsWorld* m_World = nullptr;
        std::uint32_t m_WorldIndex = k_NoIndex;
    };
}
