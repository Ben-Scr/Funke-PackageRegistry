// extern "C" surface for the csharp layer (Pkg.Funke.Physics). Entity ids are the ones
// scripts hold (Entity.ID). Exceptions must not unwind into managed frames, so every
// export catches.
#include "FastPhysicsComponents.hpp"
#include "FastPhysicsWorld.hpp"

#include "Core/Application.hpp"
#include "Core/Log.hpp"
#include "Packages/PackageApi.hpp"
#include "Physics/PhysicsSystem2D.hpp"
#include "Scene/Scene.hpp"
#include "Scripting/ScriptEntityReference.hpp"

#include <FunkePhys/Physics2D.hpp>

#include <cstdint>
#include <limits>
#include <optional>
#include <vector>

#define FUNKE_PHYSICS_TRY try
#define FUNKE_PHYSICS_CATCH(fallback)                                                            \
	catch (const std::exception& e) {                                                            \
		FNK_CORE_ERROR_TAG("Funke.Physics", "exception in {}: {}", __func__, e.what());          \
		return fallback;                                                                         \
	}                                                                                            \
	catch (...) {                                                                                \
		FNK_CORE_ERROR_TAG("Funke.Physics", "unknown exception in {}", __func__);                \
		return fallback;                                                                         \
	}

namespace {
	using namespace Funke;
	using namespace Funke::FastPhysics;

	template <typename T>
	T* FindComponent(uint64_t entityID) {
		Scene* scene = nullptr;
		EntityHandle handle = entt::null;
		if (!ResolveEntityReference(entityID, scene, handle) || !scene->IsValid(handle)) return nullptr;
		return scene->GetRegistry().try_get<T>(handle);
	}

	// Points the library's query context at the live world. Null (a miss for the caller)
	// off the main thread or while 2D physics is off.
	FastPhysicsWorld* AcquireWorld() {
		if (!Application::IsMainThread()) {
			FNK_CORE_ERROR_TAG("Funke.Physics", "FastPhysics2D queries must run on the main thread.");
			return nullptr;
		}
		if (!PhysicsSystem2D::IsInitialized() || !PhysicsSystem2D::IsEnabled()) return nullptr;
		FastPhysicsWorld& world = FastPhysicsWorld::Get();
		FunkePhys::Physics2D::SetContext(world.GetWorld());
		return &world;
	}

	uint64_t ScriptId(const FunkePhys::Collider* collider) {
		const FastPhysicsWorld::Owner* owner = FastPhysicsWorld::GetOwner(collider);
		if (!owner || !owner->scene || !owner->scene->IsValid(owner->entity)) return 0;
		return GetEntityScriptId(*owner->scene, owner->entity);
	}

	// mode 0 = first registered hit, 1 = hit whose body is nearest to `center`.
	int WriteOne(const std::vector<const FunkePhys::Collider*>& hits, float cx, float cy, int mode, uint64_t* outID) {
		if (!outID) return 0;
		*outID = 0;
		float best = std::numeric_limits<float>::max();
		for (const FunkePhys::Collider* collider : hits) {
			const uint64_t id = ScriptId(collider);
			if (id == 0) continue;
			if (mode != 1) {
				*outID = id;
				return 1;
			}
			const FunkePhys::Vec2 p = collider->GetBody()->GetPosition();
			const float d = (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy);
			if (d < best) {
				best = d;
				*outID = id;
			}
		}
		return *outID != 0 ? 1 : 0;
	}

	// Returns the total hit count; writes up to maxOut ids, so the caller can retry with a larger buffer.
	int WriteAll(const std::vector<const FunkePhys::Collider*>& hits, uint64_t* outIDs, int maxOut) {
		int count = 0;
		for (const FunkePhys::Collider* collider : hits) {
			const uint64_t id = ScriptId(collider);
			if (id == 0) continue;
			if (outIDs && count < maxOut) outIDs[count] = id;
			++count;
		}
		return count;
	}

	std::vector<FunkePhys::Vec2> ReadPolygon(const float* points, int pointCount) {
		std::vector<FunkePhys::Vec2> polygon;
		if (!points || pointCount < 3) return polygon;
		polygon.reserve(static_cast<size_t>(pointCount));
		for (int i = 0; i < pointCount; ++i) polygon.push_back({ points[i * 2], points[i * 2 + 1] });
		return polygon;
	}
}

extern "C" {

// ── Components ────────────────────────────────────────────────────────

FUNKE_PACKAGE_API int Funke_Physics_FastBody2D_GetBodyType(uint64_t id) {
	FUNKE_PHYSICS_TRY { auto* c = FindComponent<FastBody2DComponent>(id); return c ? static_cast<int>(c->Type) : 1; }
	FUNKE_PHYSICS_CATCH(1)
}

FUNKE_PACKAGE_API void Funke_Physics_FastBody2D_SetBodyType(uint64_t id, int type) {
	FUNKE_PHYSICS_TRY {
		if (auto* c = FindComponent<FastBody2DComponent>(id)) {
			c->Type = static_cast<FunkePhys::BodyType>(type);
			c->ApplySettings();
		}
	}
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API float Funke_Physics_FastBody2D_GetMass(uint64_t id) {
	FUNKE_PHYSICS_TRY { auto* c = FindComponent<FastBody2DComponent>(id); return c ? c->Mass : 1.0f; }
	FUNKE_PHYSICS_CATCH(1.0f)
}

FUNKE_PACKAGE_API void Funke_Physics_FastBody2D_SetMass(uint64_t id, float mass) {
	FUNKE_PHYSICS_TRY {
		if (auto* c = FindComponent<FastBody2DComponent>(id)) {
			c->Mass = mass;
			if (c->m_Body) c->m_Body->SetMass(mass);
		}
	}
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API int Funke_Physics_FastBody2D_GetUseGravity(uint64_t id) {
	FUNKE_PHYSICS_TRY { auto* c = FindComponent<FastBody2DComponent>(id); return c && c->UseGravity ? 1 : 0; }
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API void Funke_Physics_FastBody2D_SetUseGravity(uint64_t id, int enabled) {
	FUNKE_PHYSICS_TRY {
		if (auto* c = FindComponent<FastBody2DComponent>(id)) {
			c->UseGravity = enabled != 0;
			if (c->m_Body) c->m_Body->SetGravityEnabled(c->UseGravity);
		}
	}
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API int Funke_Physics_FastBody2D_GetBoundaryCheck(uint64_t id) {
	FUNKE_PHYSICS_TRY { auto* c = FindComponent<FastBody2DComponent>(id); return c && c->BoundaryCheck ? 1 : 0; }
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API void Funke_Physics_FastBody2D_SetBoundaryCheck(uint64_t id, int enabled) {
	FUNKE_PHYSICS_TRY {
		if (auto* c = FindComponent<FastBody2DComponent>(id)) {
			c->BoundaryCheck = enabled != 0;
			if (c->m_Body) c->m_Body->SetBoundaryCheckEnabled(c->BoundaryCheck);
		}
	}
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API void Funke_Physics_FastBody2D_GetVelocity(uint64_t id, float* outX, float* outY) {
	FUNKE_PHYSICS_TRY {
		Vec2 v{};
		if (auto* c = FindComponent<FastBody2DComponent>(id)) v = c->GetVelocity();
		if (outX) *outX = v.x;
		if (outY) *outY = v.y;
	}
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API void Funke_Physics_FastBody2D_SetVelocity(uint64_t id, float x, float y) {
	FUNKE_PHYSICS_TRY { if (auto* c = FindComponent<FastBody2DComponent>(id)) c->SetVelocity({ x, y }); }
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API void Funke_Physics_FastBoxCollider2D_GetHalfExtents(uint64_t id, float* outX, float* outY) {
	FUNKE_PHYSICS_TRY {
		Vec2 he{ 0.5f, 0.5f };
		if (auto* c = FindComponent<FastBoxCollider2DComponent>(id)) he = c->HalfExtents;
		if (outX) *outX = he.x;
		if (outY) *outY = he.y;
	}
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API void Funke_Physics_FastBoxCollider2D_SetHalfExtents(uint64_t id, float x, float y) {
	FUNKE_PHYSICS_TRY { if (auto* c = FindComponent<FastBoxCollider2DComponent>(id)) c->SetHalfExtents({ x, y }); }
	FUNKE_PHYSICS_CATCH()
}

FUNKE_PACKAGE_API float Funke_Physics_FastCircleCollider2D_GetRadius(uint64_t id) {
	FUNKE_PHYSICS_TRY { auto* c = FindComponent<FastCircleCollider2DComponent>(id); return c ? c->Radius : 0.5f; }
	FUNKE_PHYSICS_CATCH(0.5f)
}

FUNKE_PACKAGE_API void Funke_Physics_FastCircleCollider2D_SetRadius(uint64_t id, float radius) {
	FUNKE_PHYSICS_TRY { if (auto* c = FindComponent<FastCircleCollider2DComponent>(id)) c->SetRadius(radius); }
	FUNKE_PHYSICS_CATCH()
}

// ── Queries ───────────────────────────────────────────────────────────

FUNKE_PACKAGE_API int Funke_Physics_Raycast(float originX, float originY, float dirX, float dirY, float distance,
	uint64_t* hitID, float* hitX, float* hitY, float* normalX, float* normalY, float* hitDistance) {
	FUNKE_PHYSICS_TRY {
		if (!hitID || !hitX || !hitY || !normalX || !normalY || !hitDistance) return 0;
		*hitID = 0; *hitX = 0; *hitY = 0; *normalX = 0; *normalY = 0; *hitDistance = 0;
		if (!AcquireWorld() || !(distance > 0.0f) || (dirX == 0.0f && dirY == 0.0f)) return 0;

		const FunkePhys::RaycastHit hit = FunkePhys::Physics2D::Raycast({ originX, originY }, { dirX, dirY }, distance);
		const uint64_t id = hit.hit ? ScriptId(hit.collider) : 0;
		if (id == 0) return 0;
		*hitID = id;
		*hitX = hit.point.x; *hitY = hit.point.y;
		*normalX = hit.normal.x; *normalY = hit.normal.y;
		*hitDistance = hit.distance;
		return 1;
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_OverlapCircle(float x, float y, float radius, int mode, uint64_t* outID) {
	FUNKE_PHYSICS_TRY {
		if (outID) *outID = 0;
		if (!AcquireWorld() || !(radius > 0.0f)) return 0;
		return WriteOne(FunkePhys::Physics2D::OverlapCircleAll({ x, y }, radius), x, y, mode, outID);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_OverlapBox(float x, float y, float halfX, float halfY, float degrees, int mode, uint64_t* outID) {
	FUNKE_PHYSICS_TRY {
		if (outID) *outID = 0;
		if (!AcquireWorld() || !(halfX > 0.0f) || !(halfY > 0.0f)) return 0;
		return WriteOne(FunkePhys::Physics2D::OverlapBoxAll({ x, y }, { halfX * 2.0f, halfY * 2.0f }, degrees), x, y, mode, outID);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_OverlapPolygon(float x, float y, const float* points, int pointCount, int mode, uint64_t* outID) {
	FUNKE_PHYSICS_TRY {
		if (outID) *outID = 0;
		const std::vector<FunkePhys::Vec2> polygon = ReadPolygon(points, pointCount);
		if (!AcquireWorld() || polygon.size() < 3) return 0;
		return WriteOne(FunkePhys::Physics2D::OverlapPolygonAll({ x, y }, polygon.data(), polygon.size()), x, y, mode, outID);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_ContainsPoint(float x, float y, int mode, uint64_t* outID) {
	FUNKE_PHYSICS_TRY {
		if (outID) *outID = 0;
		if (!AcquireWorld()) return 0;
		return WriteOne(FunkePhys::Physics2D::ContainsPointAll({ x, y }), x, y, mode, outID);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_OverlapCircleAll(float x, float y, float radius, uint64_t* outIDs, int maxOut) {
	FUNKE_PHYSICS_TRY {
		if (!AcquireWorld() || !(radius > 0.0f)) return 0;
		return WriteAll(FunkePhys::Physics2D::OverlapCircleAll({ x, y }, radius), outIDs, maxOut);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_OverlapBoxAll(float x, float y, float halfX, float halfY, float degrees, uint64_t* outIDs, int maxOut) {
	FUNKE_PHYSICS_TRY {
		if (!AcquireWorld() || !(halfX > 0.0f) || !(halfY > 0.0f)) return 0;
		return WriteAll(FunkePhys::Physics2D::OverlapBoxAll({ x, y }, { halfX * 2.0f, halfY * 2.0f }, degrees), outIDs, maxOut);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_OverlapPolygonAll(float x, float y, const float* points, int pointCount, uint64_t* outIDs, int maxOut) {
	FUNKE_PHYSICS_TRY {
		const std::vector<FunkePhys::Vec2> polygon = ReadPolygon(points, pointCount);
		if (!AcquireWorld() || polygon.size() < 3) return 0;
		return WriteAll(FunkePhys::Physics2D::OverlapPolygonAll({ x, y }, polygon.data(), polygon.size()), outIDs, maxOut);
	}
	FUNKE_PHYSICS_CATCH(0)
}

FUNKE_PACKAGE_API int Funke_Physics_ContainsPointAll(float x, float y, uint64_t* outIDs, int maxOut) {
	FUNKE_PHYSICS_TRY {
		if (!AcquireWorld()) return 0;
		return WriteAll(FunkePhys::Physics2D::ContainsPointAll({ x, y }), outIDs, maxOut);
	}
	FUNKE_PHYSICS_CATCH(0)
}

}
