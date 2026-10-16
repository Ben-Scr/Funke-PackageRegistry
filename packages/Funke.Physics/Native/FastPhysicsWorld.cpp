#include "FastPhysicsWorld.hpp"

#include "Core/Log.hpp"
#include "Scene/Scene.hpp"

#include <algorithm>

namespace Funke::FastPhysics {

	FastPhysicsWorld& FastPhysicsWorld::Get() {
		static FastPhysicsWorld s_World;
		return s_World;
	}

	FunkePhys::Body* FastPhysicsWorld::CreateBody(Scene* scene, EntityHandle entity, FunkePhys::BodyType type) {
		const Key key = MakeKey(scene, entity);
		if (auto it = m_Bodies.find(key); it != m_Bodies.end()) {
			FNK_CORE_WARN_TAG("FastPhysics", "Entity already has a Fast Body 2D body");
			return &it->second->body;
		}

		auto record = std::make_unique<BodyRecord>();
		record->owner = { scene, entity };
		record->body.SetBodyType(type);
		record->body.SetUserData(&record->owner);
		m_World.RegisterBody(record->body);
		FunkePhys::Body* body = &record->body;
		m_Bodies.emplace(key, std::move(record));

		// A collider added before the body (deserialization order) attaches now.
		AttachIfFree(key, ColliderKind::Box);
		AttachIfFree(key, ColliderKind::Circle);
		return body;
	}

	void FastPhysicsWorld::DestroyBody(Scene* scene, EntityHandle entity) {
		const Key key = MakeKey(scene, entity);
		auto it = m_Bodies.find(key);
		if (it == m_Bodies.end()) return;

		// Colliders belong to their components; only detach them here.
		m_World.UnregisterBody(it->second->body);
		m_Bodies.erase(it);

		// A pair with a destroyed body must not produce a phantom Exit next step.
		std::erase_if(m_PreviousContacts, [&](const ContactPair& pair) { return pair.a == key || pair.b == key; });
	}

	void FastPhysicsWorld::AttachIfFree(const Key& key, ColliderKind kind) {
		auto bodyIt = m_Bodies.find(key);
		auto colliderIt = m_Colliders.find(key);
		if (bodyIt == m_Bodies.end() || colliderIt == m_Colliders.end() || bodyIt->second->hasAttached) return;
		const std::unique_ptr<ColliderRecord>& record = colliderIt->second.Get(kind);
		if (!record) return;
		m_World.AttachCollider(bodyIt->second->body, *record->collider);
		bodyIt->second->hasAttached = true;
		bodyIt->second->attached = kind;
	}

	FunkePhys::Collider* FastPhysicsWorld::AddCollider(Scene* scene, EntityHandle entity, ColliderKind kind,
		std::unique_ptr<FunkePhys::Collider> collider) {
		// Replace only this kind, so a coexisting collider of the other kind keeps its pointer.
		DestroyCollider(scene, entity, kind);

		const Key key = MakeKey(scene, entity);
		auto record = std::make_unique<ColliderRecord>();
		record->owner = { scene, entity };
		record->collider = std::move(collider);
		record->collider->SetUserData(&record->owner);
		m_World.RegisterCollider(*record->collider);
		FunkePhys::Collider* raw = record->collider.get();
		m_Colliders[key].Get(kind) = std::move(record);

		// Only the first collider attaches: a body has one shape, a second one just stays registered.
		AttachIfFree(key, kind);
		return raw;
	}

	FunkePhys::BoxCollider* FastPhysicsWorld::CreateBoxCollider(Scene* scene, EntityHandle entity, const Vec2& halfExtents) {
		return static_cast<FunkePhys::BoxCollider*>(AddCollider(scene, entity, ColliderKind::Box,
			std::make_unique<FunkePhys::BoxCollider>(FunkePhys::Vec2{ halfExtents.x, halfExtents.y })));
	}

	FunkePhys::CircleCollider* FastPhysicsWorld::CreateCircleCollider(Scene* scene, EntityHandle entity, float radius) {
		return static_cast<FunkePhys::CircleCollider*>(AddCollider(scene, entity, ColliderKind::Circle,
			std::make_unique<FunkePhys::CircleCollider>(radius)));
	}

	void FastPhysicsWorld::DestroyCollider(Scene* scene, EntityHandle entity, ColliderKind kind) {
		const Key key = MakeKey(scene, entity);
		auto colliderIt = m_Colliders.find(key);
		if (colliderIt == m_Colliders.end()) return;
		std::unique_ptr<ColliderRecord>& slot = colliderIt->second.Get(kind);
		if (!slot) return;

		auto bodyIt = m_Bodies.find(key);
		const bool wasAttached = bodyIt != m_Bodies.end() && bodyIt->second->hasAttached && bodyIt->second->attached == kind;
		m_World.UnregisterCollider(*slot->collider);
		slot.reset();
		if (wasAttached) {
			bodyIt->second->hasAttached = false;
			// The other kind, if present, keeps the body colliding.
			AttachIfFree(key, kind == ColliderKind::Box ? ColliderKind::Circle : ColliderKind::Box);
		}
		if (!colliderIt->second.box && !colliderIt->second.circle) {
			m_Colliders.erase(colliderIt);
		}
	}

	void FastPhysicsWorld::PurgeScene(Scene* scene) {
		if (!scene) return;
		for (auto it = m_Bodies.begin(); it != m_Bodies.end();) {
			if (it->first.scene == scene) {
				m_World.UnregisterBody(it->second->body);
				it = m_Bodies.erase(it);
			}
			else {
				++it;
			}
		}
		for (auto it = m_Colliders.begin(); it != m_Colliders.end();) {
			if (it->first.scene == scene) {
				if (it->second.box) m_World.UnregisterCollider(*it->second.box->collider);
				if (it->second.circle) m_World.UnregisterCollider(*it->second.circle->collider);
				it = m_Colliders.erase(it);
			}
			else {
				++it;
			}
		}
		std::erase_if(m_PreviousContacts, [&](const ContactPair& pair) { return pair.a.scene == scene || pair.b.scene == scene; });
	}

	void FastPhysicsWorld::Clear() {
		m_Bodies.clear();
		m_Colliders.clear();
		m_PreviousContacts.clear();
	}

	void FastPhysicsWorld::Step(float dt) {
		m_World.Step(dt);
	}

	const FastPhysicsWorld::Owner* FastPhysicsWorld::GetOwner(const FunkePhys::Collider* collider) {
		// A collider without a body is not simulated and has no position, so it never counts as a hit.
		const FunkePhys::Body* body = collider ? collider->GetBody() : nullptr;
		return body ? static_cast<const Owner*>(body->GetUserData()) : nullptr;
	}

	void FastPhysicsWorld::DispatchCollisions(const CollisionHandler& onEnter, const CollisionHandler& onStay,
		const CollisionHandler& onExit) {
		struct Resolved {
			ContactPair pair;
			Collision2D collision;
		};
		std::vector<Resolved> current;
		const auto& contacts = m_World.GetContacts();
		current.reserve(contacts.size());

		for (const FunkePhys::Contact& contact : contacts) {
			const auto* ownerA = contact.bodyA ? static_cast<const Owner*>(contact.bodyA->GetUserData()) : nullptr;
			const auto* ownerB = contact.bodyB ? static_cast<const Owner*>(contact.bodyB->GetUserData()) : nullptr;
			if (!ownerA || !ownerB) continue;

			Key a = MakeKey(ownerA->scene, ownerA->entity);
			Key b = MakeKey(ownerB->scene, ownerB->entity);
			if (b < a) std::swap(a, b);

			// The contact point is the midpoint of the two bodies, as on the Box2D path.
			const FunkePhys::Vec2 pa = contact.bodyA->GetPosition();
			const FunkePhys::Vec2 pb = contact.bodyB->GetPosition();

			Collision2D collision{};
			collision.entityA = static_cast<EntityHandle>(static_cast<entt::entity>(a.entity));
			collision.entityB = static_cast<EntityHandle>(static_cast<entt::entity>(b.entity));
			collision.sceneA = a.scene;
			collision.sceneB = b.scene;
			collision.contactPoint = { (pa.x + pb.x) * 0.5f, (pa.y + pb.y) * 0.5f };
			current.push_back({ { a, b }, collision });
		}

		std::sort(current.begin(), current.end(), [](const Resolved& l, const Resolved& r) { return l.pair < r.pair; });
		current.erase(std::unique(current.begin(), current.end(),
			[](const Resolved& l, const Resolved& r) { return l.pair == r.pair; }), current.end());

		std::vector<ContactPair> currentPairs;
		currentPairs.reserve(current.size());
		for (const Resolved& resolved : current) currentPairs.push_back(resolved.pair);

		// Snapshot the exits first: a callback that destroys an entity edits m_PreviousContacts.
		std::vector<ContactPair> exited;
		std::set_difference(m_PreviousContacts.begin(), m_PreviousContacts.end(),
			currentPairs.begin(), currentPairs.end(), std::back_inserter(exited));
		const std::vector<ContactPair> previous = m_PreviousContacts;

		for (const Resolved& resolved : current) {
			const bool wasTouching = std::binary_search(previous.begin(), previous.end(), resolved.pair);
			const CollisionHandler& handler = wasTouching ? onStay : onEnter;
			if (handler) handler(resolved.collision);
		}

		if (onExit) {
			for (const ContactPair& pair : exited) {
				Collision2D collision{};
				collision.entityA = static_cast<EntityHandle>(static_cast<entt::entity>(pair.a.entity));
				collision.entityB = static_cast<EntityHandle>(static_cast<entt::entity>(pair.b.entity));
				collision.sceneA = pair.a.scene;
				collision.sceneB = pair.b.scene;
				onExit(collision);
			}
		}

		// Keep only pairs whose bodies survived the callbacks.
		std::erase_if(currentPairs, [&](const ContactPair& pair) {
			return !m_Bodies.contains(pair.a) || !m_Bodies.contains(pair.b);
		});
		m_PreviousContacts = std::move(currentPairs);
	}

}
