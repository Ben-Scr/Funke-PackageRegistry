#include "Lighting2DComponents.hpp"
#include "Packages/PackageApi.hpp"
#include "Scene/Scene.hpp"
#include "Scene/SceneManager.hpp"
#include "Scene/SceneExecutionContext.hpp"
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {
	using namespace Funke;
	using namespace Funke::Lighting2D;
	template<class T> T* Find(uint64_t id) {
		if (id == 0) return nullptr;
		if (Scene* scene = GetCurrentExecutionScene()) {
			EntityHandle entity = entt::null;
			if (scene->TryResolveEntityRef(id, entity)) return scene->GetRegistry().try_get<T>(entity);
		}
		T* result = nullptr;
		SceneManager::ForeachLoadedScene([&](Scene& scene) {
			if (result) return;
			EntityHandle entity = entt::null;
			if (scene.TryResolveEntityRef(id, entity)) result = scene.GetRegistry().try_get<T>(entity);
		});
		return result;
	}
}

extern "C" FUNKE_PACKAGE_API int Funke_Lighting2D_Read(uint64_t entityId, int kind, float* values, int count) {
	try {
		if (!values) return 0;
		if (kind == 0 && count == 9) {
			const auto* c = Find<Light2DComponent>(entityId);
			if (!c) return 0;
			values[0]=c->Enabled?1.0f:0.0f; values[1]=c->Color.r; values[2]=c->Color.g; values[3]=c->Color.b;
			values[4]=c->Color.a; values[5]=c->Intensity; values[6]=c->Radius; values[7]=c->Falloff; values[8]=c->CastShadows?1.0f:0.0f;
			return 1;
		}
		if (kind == 1 && count == 6) {
			const auto* c = Find<AmbientLight2DComponent>(entityId);
			if (!c) return 0;
			values[0]=c->Enabled?1.0f:0.0f; values[1]=c->Color.r; values[2]=c->Color.g; values[3]=c->Color.b;
			values[4]=c->Color.a; values[5]=c->Intensity;
			return 1;
		}
		if (kind == 2 && count == 6) {
			const auto* c = Find<ShadowOccluder2DComponent>(entityId);
			if (!c) return 0;
			values[0]=c->Enabled?1.0f:0.0f; values[1]=c->Size.x; values[2]=c->Size.y;
			values[3]=c->Offset.x; values[4]=c->Offset.y; values[5]=c->Opacity;
			return 1;
		}
	} catch (...) { return 0; }
	return 0;
}

extern "C" FUNKE_PACKAGE_API int Funke_Lighting2D_Write(uint64_t entityId, int kind, const float* values, int count) {
	try {
		if (!values || count < 0 || count > 9) return 0;
		for (int i=0;i<count;++i) if (!std::isfinite(values[i])) return 0;
		if (kind == 0 && count == 9) {
			auto* c = Find<Light2DComponent>(entityId);
			if (!c) return 0;
			c->Enabled=values[0]!=0;
			c->Color=Color{std::clamp(values[1],0.0f,1.0f),std::clamp(values[2],0.0f,1.0f),std::clamp(values[3],0.0f,1.0f),std::clamp(values[4],0.0f,1.0f)};
			c->Intensity=std::clamp(values[5],0.0f,100.0f); c->Radius=std::clamp(values[6],0.001f,100000.0f);
			c->Falloff=std::clamp(values[7],0.05f,10.0f); c->CastShadows=values[8]!=0;
			return 1;
		}
		if (kind == 1 && count == 6) {
			auto* c = Find<AmbientLight2DComponent>(entityId);
			if (!c) return 0;
			c->Enabled=values[0]!=0;
			c->Color=Color{std::clamp(values[1],0.0f,1.0f),std::clamp(values[2],0.0f,1.0f),std::clamp(values[3],0.0f,1.0f),std::clamp(values[4],0.0f,1.0f)};
			c->Intensity=std::clamp(values[5],0.0f,100.0f);
			return 1;
		}
		if (kind == 2 && count == 6) {
			auto* c = Find<ShadowOccluder2DComponent>(entityId);
			if (!c) return 0;
			c->Enabled=values[0]!=0; c->Size={std::clamp(values[1],0.001f,100000.0f),std::clamp(values[2],0.001f,100000.0f)};
			c->Offset={values[3],values[4]}; c->Opacity=std::clamp(values[5],0.0f,1.0f);
			return 1;
		}
	} catch (...) { return 0; }
	return 0;
}
