#pragma once
#include "Collections/Vec2.hpp"
#include "Physics/Collision2D.hpp"
#include "Scene/EntityHandle.hpp"

#include <FunkePhys/Body.hpp>
#include <FunkePhys/BoxCollider.hpp>
#include <FunkePhys/CircleCollider.hpp>
#include <FunkePhys/PhysicsWorld.hpp>

#include <cstdint>
#include <functional>
#include <memory>
#include <unordered_map>
#include <vector>

namespace Funke {
	class Scene;
}

namespace Funke::FastPhysics {

	// One Funke-Physics world shared by every loaded scene. Bodies and colliders are keyed
	// by (scene, entity) because EnTT handles are only unique within one registry.
	class FastPhysicsWorld {
	public:
		enum class ColliderKind : uint8_t { Box, Circle };

		// Scene and entity a body or collider belongs to; set as its user data.
		struct Owner {
			Scene* scene = nullptr;
			EntityHandle entity = entt::null;
		};

		using CollisionHandler = std::function<void(const Collision2D&)>;

		static FastPhysicsWorld& Get();

		FunkePhys::PhysicsWorld& GetWorld() { return m_World; }

		FunkePhys::Body* CreateBody(Scene* scene, EntityHandle entity, FunkePhys::BodyType type);
		void DestroyBody(Scene* scene, EntityHandle entity);

		FunkePhys::BoxCollider* CreateBoxCollider(Scene* scene, EntityHandle entity, const Vec2& halfExtents);
		FunkePhys::CircleCollider* CreateCircleCollider(Scene* scene, EntityHandle entity, float radius);
		void DestroyCollider(Scene* scene, EntityHandle entity, ColliderKind kind);

		// Drops everything keyed by the scene (unload, teardown).
		void PurgeScene(Scene* scene);
		// Drops everything (physics shutdown, package unload).
		void Clear();

		void Step(float dt);
		// Raises Enter/Stay/Exit for this step's contacts compared with the previous step's.
		void DispatchCollisions(const CollisionHandler& onEnter, const CollisionHandler& onStay, const CollisionHandler& onExit);

		// Owner of the body a query hit belongs to; null for a collider without a body.
		static const Owner* GetOwner(const FunkePhys::Collider* collider);

		std::size_t GetBodyCount() const { return m_World.GetBodyCount(); }
		std::size_t GetColliderCount() const { return m_World.GetColliderCount(); }

	private:
		struct Key {
			Scene* scene;
			uint32_t entity;
			bool operator==(const Key& other) const noexcept { return scene == other.scene && entity == other.entity; }
			bool operator<(const Key& other) const noexcept {
				return scene != other.scene ? std::less<Scene*>{}(scene, other.scene) : entity < other.entity;
			}
		};
		struct KeyHash {
			size_t operator()(const Key& key) const noexcept {
				const size_t a = std::hash<Scene*>{}(key.scene);
				return a ^ (static_cast<size_t>(key.entity) + 0x9e3779b9u + (a << 6) + (a >> 2));
			}
		};

		struct BodyRecord {
			Owner owner;
			FunkePhys::Body body;
			bool hasAttached = false;
			ColliderKind attached = ColliderKind::Box;
		};
		struct ColliderRecord {
			Owner owner;
			std::unique_ptr<FunkePhys::Collider> collider;
		};
		struct ColliderSlots {
			std::unique_ptr<ColliderRecord> box;
			std::unique_ptr<ColliderRecord> circle;
			std::unique_ptr<ColliderRecord>& Get(ColliderKind kind) { return kind == ColliderKind::Box ? box : circle; }
		};

		struct ContactPair {
			Key a;
			Key b;
			bool operator==(const ContactPair& o) const noexcept { return a == o.a && b == o.b; }
			bool operator<(const ContactPair& o) const noexcept { return a == o.a ? b < o.b : a < o.a; }
		};

		static Key MakeKey(Scene* scene, EntityHandle entity) { return { scene, static_cast<uint32_t>(entity) }; }
		void AttachIfFree(const Key& key, ColliderKind kind);
		FunkePhys::Collider* AddCollider(Scene* scene, EntityHandle entity, ColliderKind kind,
			std::unique_ptr<FunkePhys::Collider> collider);

		FunkePhys::PhysicsWorld m_World;
		std::unordered_map<Key, std::unique_ptr<BodyRecord>, KeyHash> m_Bodies;
		std::unordered_map<Key, ColliderSlots, KeyHash> m_Colliders;
		// Sorted pairs touching after the previous step, for Enter/Stay/Exit.
		std::vector<ContactPair> m_PreviousContacts;
	};

}
