#pragma once
#include "Export.hpp"
#include "WorldSettings.hpp"
#include "Body.hpp"
#include "Collider.hpp"
#include "Contact.hpp"

#include <memory>
#include <vector>

namespace FunkePhys {
    namespace Detail {
        struct WorldScratch;
        class SpatialGrid;
    }

    class FUNKE_PHYS_API PhysicsWorld
    {
    public:
        PhysicsWorld();
        explicit PhysicsWorld(const WorldSettings& settings);
        // Leaves registered bodies and colliders alive but unregistered.
        ~PhysicsWorld();
        PhysicsWorld(const PhysicsWorld&) = delete;
        PhysicsWorld& operator=(const PhysicsWorld&) = delete;

        void SetSettings(const WorldSettings& settings) noexcept;
        const WorldSettings& GetSettings() const noexcept { return m_Settings; }

        // A body or collider belongs to at most one world; registering it in a second
        // one fails until it is unregistered. Both operations are O(1).
        bool RegisterBody(Body& body);
        bool UnregisterBody(Body& body);

        bool RegisterCollider(Collider& collider);
        bool UnregisterCollider(Collider& collider);

        // Pairs a registered body with a registered collider, keeping both
        // back-pointers in sync. If the body or collider is currently attached
        // to a different partner, the previous pairing is severed first.
        bool AttachCollider(Body& body, Collider& collider);
        void DetachCollider(Body& body);

        void Step(float dt);

        std::size_t GetBodyCount() const noexcept { return m_Bodies.size(); }
        std::size_t GetColliderCount() const noexcept { return m_Colliders.size(); }
        const std::vector<Body*>& GetBodies() const noexcept { return m_Bodies; }
        const std::vector<Collider*>& GetColliders() const noexcept { return m_Colliders; }
        const std::vector<Contact>& GetContacts() const noexcept { return m_Contacts; }

    private:
        friend class Body;
        friend class Collider;
        friend class Physics2D;

        static WorldSettings SanitizeSettings(const WorldSettings& settings) noexcept;
        void IntegrateBodies(float dt);
        void ApplyWorldBounds(Body& body) const noexcept;
        void DetectCollisions();
        void ResolveContacts();
        void RemoveContactsWith(const Body* body, const Collider* collider);
        float ChooseCellSize(float extentSum, std::size_t count) const noexcept;

        // Grid over every registered collider (proxy i == GetColliders()[i]); rebuilt
        // lazily after anything moved, so queries between steps stay exact.
        const Detail::SpatialGrid& GetQueryGrid();

        WorldSettings m_Settings;
        std::vector<Body*> m_Bodies;
        std::vector<Collider*> m_Colliders;
        std::vector<Contact> m_Contacts;
        std::unique_ptr<Detail::WorldScratch> m_Scratch;
        bool m_QueryDirty = true;
    };
}
