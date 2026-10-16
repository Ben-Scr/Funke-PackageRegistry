// Registers the Fast* components and plugs Funke-Physics into the engine: per-scene
// component hooks (SceneExtensions) and a physics backend run next to Box2D
// (PhysicsSystem2D extensions).
#include "FastPhysicsComponents.hpp"
#include "FastPhysicsWorld.hpp"

#include "Components/General/Transform2DComponent.hpp"
#include "Components/Physics/BoxCollider2DComponent.hpp"
#include "Components/Physics/CircleCollider2DComponent.hpp"
#include "Components/Physics/PolygonCollider2DComponent.hpp"
#include "Components/Physics/Rigidbody2DComponent.hpp"
#include "Components/Tags.hpp"
#include "Graphics/Gizmo.hpp"
#include "Inspector/PropertyRegistration.hpp"
#include "Packages/PackageApi.hpp"
#include "Packages/PackageRegistration.hpp"
#include "Physics/PhysicsSystem2D.hpp"
#include "Scene/Entity.hpp"
#include "Scene/Scene.hpp"
#include "Scene/SceneExtensions.hpp"
#include "Serialization/Json.hpp"

#include <entt/core/type_info.hpp>

#include <cmath>

namespace Funke::FastPhysics {
	namespace {
		constexpr const char* k_Name = "Funke.Physics";
		const Color k_ColliderColor{ 0.10f, 0.85f, 0.85f, 1.0f };

		FastPhysicsWorld& World() { return FastPhysicsWorld::Get(); }

		bool IsSimulated(const Scene& scene) { return !scene.IsDetached() && !scene.IsLifecycleSuspended(); }

		Vec2 ScaleOf(const entt::registry& registry, EntityHandle entity) {
			const auto* transform = registry.try_get<Transform2DComponent>(entity);
			return transform ? transform->Scale : Vec2{ 1.0f, 1.0f };
		}

		// ── Component hooks ─────────────────────────────────────────────

		void OnBodyConstruct(Scene& scene, entt::registry& registry, EntityHandle entity) {
			if (!IsSimulated(scene)) return;
			auto& body = registry.get<FastBody2DComponent>(entity);
			body.m_Body = World().CreateBody(&scene, entity, body.Type);
			body.ApplySettings();
			if (const auto* transform = registry.try_get<Transform2DComponent>(entity)) {
				body.m_Body->SetPosition(ToPhys(transform->Position));
			}
		}

		void OnBodyDestroy(Scene& scene, entt::registry&, EntityHandle entity) {
			if (scene.IsDetached()) return;
			World().DestroyBody(&scene, entity);
		}

		void OnBoxConstruct(Scene& scene, entt::registry& registry, EntityHandle entity) {
			if (!IsSimulated(scene)) return;
			auto& box = registry.get<FastBoxCollider2DComponent>(entity);
			const Vec2 scale = ScaleOf(registry, entity);
			box.m_LastAppliedScale = scale;
			box.m_Collider = World().CreateBoxCollider(&scene, entity, { box.HalfExtents.x * scale.x, box.HalfExtents.y * scale.y });
		}

		void OnBoxDestroy(Scene& scene, entt::registry&, EntityHandle entity) {
			if (scene.IsDetached()) return;
			World().DestroyCollider(&scene, entity, FastPhysicsWorld::ColliderKind::Box);
		}

		void OnCircleConstruct(Scene& scene, entt::registry& registry, EntityHandle entity) {
			if (!IsSimulated(scene)) return;
			auto& circle = registry.get<FastCircleCollider2DComponent>(entity);
			const Vec2 scale = ScaleOf(registry, entity);
			circle.m_LastAppliedScale = std::max(std::abs(scale.x), std::abs(scale.y));
			circle.m_Collider = World().CreateCircleCollider(&scene, entity, circle.Radius * circle.m_LastAppliedScale);
		}

		void OnCircleDestroy(Scene& scene, entt::registry&, EntityHandle entity) {
			if (scene.IsDetached()) return;
			World().DestroyCollider(&scene, entity, FastPhysicsWorld::ColliderKind::Circle);
		}

		// ── Scene extension ─────────────────────────────────────────────

		void OnSceneCreated(Scene& scene) {
			auto& registry = scene.GetRegistry();
			registry.on_construct<FastBody2DComponent>().connect<&OnBodyConstruct>(scene);
			registry.on_destroy<FastBody2DComponent>().connect<&OnBodyDestroy>(scene);
			registry.on_construct<FastBoxCollider2DComponent>().connect<&OnBoxConstruct>(scene);
			registry.on_destroy<FastBoxCollider2DComponent>().connect<&OnBoxDestroy>(scene);
			registry.on_construct<FastCircleCollider2DComponent>().connect<&OnCircleConstruct>(scene);
			registry.on_destroy<FastCircleCollider2DComponent>().connect<&OnCircleDestroy>(scene);
		}

		void OnRuntimeStateInitialized(Scene& scene) {
			if (!IsSimulated(scene)) return;
			auto& registry = scene.GetRegistry();
			for (EntityHandle entity : registry.view<FastBody2DComponent>()) OnBodyConstruct(scene, registry, entity);
			for (EntityHandle entity : registry.view<FastBoxCollider2DComponent>()) OnBoxConstruct(scene, registry, entity);
			for (EntityHandle entity : registry.view<FastCircleCollider2DComponent>()) OnCircleConstruct(scene, registry, entity);
		}

		void OnSceneDestroyed(Scene& scene) {
			World().PurgeScene(&scene);
			auto& registry = scene.GetRegistry();
			for (auto [entity, body] : registry.view<FastBody2DComponent>().each()) body.m_Body = nullptr;
			for (auto [entity, box] : registry.view<FastBoxCollider2DComponent>().each()) box.m_Collider = nullptr;
			for (auto [entity, circle] : registry.view<FastCircleCollider2DComponent>().each()) circle.m_Collider = nullptr;
			registry.on_construct<FastBody2DComponent>().disconnect<&OnBodyConstruct>(scene);
			registry.on_destroy<FastBody2DComponent>().disconnect<&OnBodyDestroy>(scene);
			registry.on_construct<FastBoxCollider2DComponent>().disconnect<&OnBoxConstruct>(scene);
			registry.on_destroy<FastBoxCollider2DComponent>().disconnect<&OnBoxDestroy>(scene);
			registry.on_construct<FastCircleCollider2DComponent>().disconnect<&OnCircleConstruct>(scene);
			registry.on_destroy<FastCircleCollider2DComponent>().disconnect<&OnCircleDestroy>(scene);
		}

		// ── Physics backend ─────────────────────────────────────────────

		void SyncToPhysics(Scene& scene) {
			auto& registry = scene.GetRegistry();
			// Scale is compared every frame: deserialization writes Transform2D without marking it dirty.
			for (auto [entity, box, transform] : registry.view<FastBoxCollider2DComponent, Transform2DComponent>(entt::exclude<DisabledTag>).each()) {
				box.SyncScale(transform.Scale);
			}
			for (auto [entity, circle, transform] : registry.view<FastCircleCollider2DComponent, Transform2DComponent>(entt::exclude<DisabledTag>).each()) {
				circle.SyncScale(transform.Scale);
			}
			for (auto [entity, body, transform] : registry.view<FastBody2DComponent, Transform2DComponent>(entt::exclude<DisabledTag>).each()) {
				if (transform.IsDirty() && body.m_Body) {
					body.m_Body->SetPosition(ToPhys(transform.Position));
					transform.ClearDirty();
				}
			}
			for (auto [entity, box, transform] : registry.view<FastBoxCollider2DComponent, Transform2DComponent>(
				entt::exclude<DisabledTag, FastBody2DComponent, Rigidbody2DComponent>).each()) {
				if (transform.IsDirty()) transform.ClearDirty();
			}
			for (auto [entity, circle, transform] : registry.view<FastCircleCollider2DComponent, Transform2DComponent>(
				entt::exclude<DisabledTag, FastBody2DComponent, Rigidbody2DComponent>).each()) {
				if (transform.IsDirty()) transform.ClearDirty();
			}
		}

		void Step(float dt) {
			World().Step(dt);
		}

		void SyncFromPhysics(Scene& scene) {
			auto& registry = scene.GetRegistry();
			for (auto [entity, body, transform] : registry.view<FastBody2DComponent, Transform2DComponent>(entt::exclude<DisabledTag>).each()) {
				if (!body.m_Body) continue;
				transform.Position = FromPhys(body.m_Body->GetPosition());
				PhysicsSystem2D::DecomposeWorldToLocal(registry, entity, transform);
				transform.ClearDirty();
			}
		}

		void DispatchCollisions(const PhysicsExtension2D::CollisionHandler& onEnter,
			const PhysicsExtension2D::CollisionHandler& onStay, const PhysicsExtension2D::CollisionHandler& onExit) {
			World().DispatchCollisions(onEnter, onStay, onExit);
		}

		void DrawOutlineBox(const Vec2& center, const Vec2& half, float width) {
			const Vec2 c[4] = {
				{ center.x - half.x, center.y - half.y }, { center.x + half.x, center.y - half.y },
				{ center.x + half.x, center.y + half.y }, { center.x - half.x, center.y + half.y },
			};
			for (int i = 0; i < 4; ++i) Gizmo::DrawThickLine(c[i], c[(i + 1) % 4], width);
		}

		void DrawOutlineCircle(const Vec2& center, float radius, float width) {
			constexpr int k_Segments = 32;
			Vec2 previous{ center.x + radius, center.y };
			for (int i = 1; i <= k_Segments; ++i) {
				const float angle = static_cast<float>(i) / k_Segments * 6.28318530718f;
				const Vec2 point{ center.x + std::cos(angle) * radius, center.y + std::sin(angle) * radius };
				Gizmo::DrawThickLine(previous, point, width);
				previous = point;
			}
		}

		// The shapes never rotate, so the outlines are axis-aligned regardless of Transform2D.Rotation.
		void DrawDebug(Scene& scene, float lineWidth) {
			auto& registry = scene.GetRegistry();
			Gizmo::SetColor(k_ColliderColor);
			for (auto [entity, box, transform] : registry.view<FastBoxCollider2DComponent, Transform2DComponent>().each()) {
				if (!box.IsValid()) continue;
				const Vec2 he = box.GetWorldHalfExtents();
				DrawOutlineBox(transform.Position, { std::abs(he.x), std::abs(he.y) }, lineWidth);
			}
			for (auto [entity, circle, transform] : registry.view<FastCircleCollider2DComponent, Transform2DComponent>().each()) {
				if (!circle.IsValid()) continue;
				DrawOutlineCircle(transform.Position, circle.GetWorldRadius(), lineWidth);
			}
		}

		void Shutdown() {
			World().Clear();
		}

		// ── Editor gizmos ───────────────────────────────────────────────

		void DrawBoxGizmo(Entity entity) {
			if (!entity.HasComponent<Transform2DComponent>()) return;
			const auto& transform = entity.GetComponent<Transform2DComponent>();
			const auto& box = entity.GetComponent<FastBoxCollider2DComponent>();
			const Vec2 he = box.IsValid() ? box.GetWorldHalfExtents()
				: Vec2{ box.HalfExtents.x * transform.Scale.x, box.HalfExtents.y * transform.Scale.y };
			Gizmo::SetColor(k_ColliderColor);
			Gizmo::SetLineWidth(2.0f);
			Gizmo::DrawWireSquare(transform.Position, { std::abs(he.x) * 2.0f, std::abs(he.y) * 2.0f });
		}

		void DrawCircleGizmo(Entity entity) {
			if (!entity.HasComponent<Transform2DComponent>()) return;
			const auto& transform = entity.GetComponent<Transform2DComponent>();
			const auto& circle = entity.GetComponent<FastCircleCollider2DComponent>();
			const float radius = circle.IsValid() ? circle.GetWorldRadius()
				: circle.Radius * std::max(std::abs(transform.Scale.x), std::abs(transform.Scale.y));
			Gizmo::SetColor(k_ColliderColor);
			Gizmo::SetLineWidth(2.0f);
			Gizmo::DrawWireCircle(transform.Position, radius);
		}

		// ── Serialization (same scene keys as when the components were built in) ──

		float GetFloat(const Json::Value& value, const char* key, float fallback) {
			const Json::Value* member = value.FindMember(key);
			return member ? static_cast<float>(member->AsDoubleOr(fallback)) : fallback;
		}

		Json::Value SerializeBody(const FastBody2DComponent& body) {
			Json::Value value = Json::Value::MakeObject();
			value.AddMember("type", Json::Value(static_cast<int>(body.Type)));
			value.AddMember("mass", Json::Value(body.Mass));
			value.AddMember("useGravity", Json::Value(body.UseGravity));
			value.AddMember("boundaryCheck", Json::Value(body.BoundaryCheck));
			return value;
		}

		void DeserializeBody(FastBody2DComponent& body, const Json::Value& value) {
			const Json::Value* type = value.FindMember("type");
			body.Type = static_cast<FunkePhys::BodyType>(type ? type->AsIntOr(1) : 1);
			body.Mass = GetFloat(value, "mass", 1.0f);
			const Json::Value* gravity = value.FindMember("useGravity");
			body.UseGravity = gravity ? gravity->AsBoolOr(true) : true;
			const Json::Value* boundary = value.FindMember("boundaryCheck");
			body.BoundaryCheck = boundary ? boundary->AsBoolOr(false) : false;
			body.ApplySettings();
		}

		Json::Value SerializeBox(const FastBoxCollider2DComponent& box) {
			Json::Value value = Json::Value::MakeObject();
			value.AddMember("halfX", Json::Value(box.HalfExtents.x));
			value.AddMember("halfY", Json::Value(box.HalfExtents.y));
			return value;
		}

		void DeserializeBox(FastBoxCollider2DComponent& box, const Json::Value& value) {
			// SetHalfExtents keeps the Transform2D scale the construct hook applied.
			box.SetHalfExtents({ GetFloat(value, "halfX", 0.5f), GetFloat(value, "halfY", 0.5f) });
		}

		Json::Value SerializeCircle(const FastCircleCollider2DComponent& circle) {
			Json::Value value = Json::Value::MakeObject();
			value.AddMember("radius", Json::Value(circle.Radius));
			return value;
		}

		void DeserializeCircle(FastCircleCollider2DComponent& circle, const Json::Value& value) {
			circle.SetRadius(GetFloat(value, "radius", 0.5f));
		}

		std::vector<PropertyDescriptor> BodyProperties() {
			return {
				Properties::MakeWith<FunkePhys::BodyType>("Type", "Body Type",
					[](const Entity& e) { return e.GetComponent<FastBody2DComponent>().Type; },
					[](Entity& e, FunkePhys::BodyType v) {
						auto& body = e.GetComponent<FastBody2DComponent>();
						body.Type = v;
						body.ApplySettings();
					}),
				Properties::MakeWith<float>("Mass", "Mass",
					[](const Entity& e) { return e.GetComponent<FastBody2DComponent>().Mass; },
					[](Entity& e, float v) {
						auto& body = e.GetComponent<FastBody2DComponent>();
						body.Mass = v;
						if (body.m_Body) body.m_Body->SetMass(v);
					},
					Properties::Meta::DragSpeed(0.1f)),
				Properties::MakeWith<bool>("UseGravity", "Use Gravity",
					[](const Entity& e) { return e.GetComponent<FastBody2DComponent>().UseGravity; },
					[](Entity& e, bool v) {
						auto& body = e.GetComponent<FastBody2DComponent>();
						body.UseGravity = v;
						if (body.m_Body) body.m_Body->SetGravityEnabled(v);
					}),
				Properties::MakeWith<bool>("BoundaryCheck", "Boundary Check",
					[](const Entity& e) { return e.GetComponent<FastBody2DComponent>().BoundaryCheck; },
					[](Entity& e, bool v) {
						auto& body = e.GetComponent<FastBody2DComponent>();
						body.BoundaryCheck = v;
						if (body.m_Body) body.m_Body->SetBoundaryCheckEnabled(v);
					}),
			};
		}

		std::vector<PropertyDescriptor> BoxProperties() {
			return {
				Properties::MakeWith<Vec2>("HalfExtents", "Half Extents",
					[](const Entity& e) { return e.GetComponent<FastBoxCollider2DComponent>().HalfExtents; },
					[](Entity& e, const Vec2& v) { e.GetComponent<FastBoxCollider2DComponent>().SetHalfExtents(v); },
					Properties::Meta::DragSpeed(0.05f).WithHideStepperButtons(true)),
			};
		}

		std::vector<PropertyDescriptor> CircleProperties() {
			return {
				Properties::MakeWith<float>("Radius", "Radius",
					[](const Entity& e) { return e.GetComponent<FastCircleCollider2DComponent>().Radius; },
					[](Entity& e, float v) { e.GetComponent<FastCircleCollider2DComponent>().SetRadius(v); },
					Properties::Meta::DragSpeed(0.05f)),
			};
		}
	}
}

extern "C" FUNKE_PACKAGE_API int FunkePackage_OnLoad() {
	using namespace Funke;
	using namespace Funke::FastPhysics;

	Package::RegisterSerializableComponent<FastBody2DComponent>("Fast Body 2D", "Physics", "FastBody2D",
		nullptr, &SerializeBody, &DeserializeBody, ComponentCategory::Component, BodyProperties(), "rigidbody_2d");
	Package::RegisterSerializableComponent<FastBoxCollider2DComponent>("Fast Box Collider 2D", "Physics", "FastBoxCollider2D",
		nullptr, &SerializeBox, &DeserializeBox, ComponentCategory::Component, BoxProperties(), "collider_box_2d");
	Package::RegisterSerializableComponent<FastCircleCollider2DComponent>("Fast Circle Collider 2D", "Physics", "FastCircleCollider2D",
		nullptr, &SerializeCircle, &DeserializeCircle, ComponentCategory::Component, CircleProperties(), "collider_circle_2d");

	Package::SetEditorGizmo<FastBoxCollider2DComponent>(&DrawBoxGizmo);
	Package::SetEditorGizmo<FastCircleCollider2DComponent>(&DrawCircleGizmo);

	// Box2D and Funke-Physics must not be mixed on one entity, and a fast body has one shape.
	Package::DeclareComponentConflict<Rigidbody2DComponent, FastBody2DComponent>();
	Package::DeclareComponentConflict<BoxCollider2DComponent, FastBoxCollider2DComponent>();
	Package::DeclareComponentConflict<BoxCollider2DComponent, FastCircleCollider2DComponent>();
	Package::DeclareComponentConflict<CircleCollider2DComponent, FastBoxCollider2DComponent>();
	Package::DeclareComponentConflict<CircleCollider2DComponent, FastCircleCollider2DComponent>();
	Package::DeclareComponentConflict<PolygonCollider2DComponent, FastBoxCollider2DComponent>();
	Package::DeclareComponentConflict<PolygonCollider2DComponent, FastCircleCollider2DComponent>();
	Package::DeclareComponentConflict<FastBoxCollider2DComponent, FastCircleCollider2DComponent>();

	PhysicsExtension2D physics;
	physics.Name = k_Name;
	physics.BodyComponent = entt::type_hash<FastBody2DComponent>::value();
	physics.SyncToPhysics = &SyncToPhysics;
	physics.Step = &Step;
	physics.SyncFromPhysics = &SyncFromPhysics;
	physics.DispatchCollisions = &DispatchCollisions;
	physics.DrawDebug = &DrawDebug;
	physics.Shutdown = &Shutdown;
	PhysicsSystem2D::RegisterExtension(std::move(physics));

	SceneExtension scenes;
	scenes.Name = k_Name;
	scenes.OnSceneCreated = &OnSceneCreated;
	scenes.OnRuntimeStateInitialized = &OnRuntimeStateInitialized;
	scenes.OnSceneDestroyed = &OnSceneDestroyed;
	SceneExtensions::Register(std::move(scenes));
	return 0;
}

extern "C" FUNKE_PACKAGE_API void FunkePackage_OnUnload() {
	using namespace Funke;
	SceneExtensions::Unregister(Funke::FastPhysics::k_Name);
	PhysicsSystem2D::UnregisterExtension(Funke::FastPhysics::k_Name);
}
