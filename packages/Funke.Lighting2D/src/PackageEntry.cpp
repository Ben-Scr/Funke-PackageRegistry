#include "Lighting2DComponents.hpp"
#include "LightingShader.hpp"
#include "Components/General/Transform2DComponent.hpp"
#include "Components/Tags.hpp"
#include "Core/Log.hpp"
#include "Graphics/FullscreenEffect.hpp"
#include "Graphics/Gizmo.hpp"
#include "Graphics/Renderer2D.hpp"
#include "Inspector/PropertyRegistration.hpp"
#include "Packages/PackageApi.hpp"
#include "Packages/PackageRegistration.hpp"
#include "Scene/Scene.hpp"
#include "Serialization/Json.hpp"
#include <algorithm>
#include <array>
#include <cmath>
#include <memory>
#include <vector>
#include <glm/gtc/matrix_inverse.hpp>

namespace Funke::Lighting2D {
	namespace {
		struct GpuLight { glm::vec4 Geometry{}, Color{}, Options{}; };
		struct GpuOccluder { glm::vec4 Geometry{}, Options{}; };
		struct Uniforms {
			glm::mat4 InverseVP{ 1.0f };
			glm::vec4 Ambient{};
			glm::uvec4 Counts{};
			std::array<GpuLight, 32> Lights{};
			std::array<GpuOccluder, 64> Occluders{};
		};
		static_assert(sizeof(Uniforms) == 3680);
		std::unique_ptr<FullscreenEffect> g_Effect;
		uint32_t g_EffectToken = 0;
		bool g_WarnedLimit = false;

		float ReadFloat(const Json::Value& value, const char* name, float fallback, float minimum, float maximum) {
			const auto* member = value.FindMember(name);
			const float result = member ? static_cast<float>(member->AsDoubleOr(fallback)) : fallback;
			return std::isfinite(result) ? std::clamp(result, minimum, maximum) : fallback;
		}
		bool ReadBool(const Json::Value& value, const char* name, bool fallback) {
			const auto* member = value.FindMember(name);
			return member ? member->AsBoolOr(fallback) : fallback;
		}
		template<class T> Json::Value SerializeLight(const T& light) {
			auto value = Json::Value::MakeObject();
			value.AddMember("enabled", Json::Value(light.Enabled));
			value.AddMember("r", Json::Value(light.Color.r));
			value.AddMember("g", Json::Value(light.Color.g));
			value.AddMember("b", Json::Value(light.Color.b));
			value.AddMember("a", Json::Value(light.Color.a));
			value.AddMember("intensity", Json::Value(light.Intensity));
			if constexpr (std::is_same_v<T, Light2DComponent>) {
				value.AddMember("radius", Json::Value(light.Radius));
				value.AddMember("falloff", Json::Value(light.Falloff));
				value.AddMember("castShadows", Json::Value(light.CastShadows));
			}
			return value;
		}
		template<class T> void DeserializeLight(T& light, const Json::Value& value) {
			const T defaults{};
			light.Enabled = ReadBool(value, "enabled", defaults.Enabled);
			light.Color = Color{ ReadFloat(value,"r",defaults.Color.r,0,1), ReadFloat(value,"g",defaults.Color.g,0,1),
				ReadFloat(value,"b",defaults.Color.b,0,1), ReadFloat(value,"a",defaults.Color.a,0,1) };
			light.Intensity = ReadFloat(value, "intensity", defaults.Intensity, 0, 100);
			if constexpr (std::is_same_v<T, Light2DComponent>) {
				light.Radius = ReadFloat(value, "radius", defaults.Radius, 0.001f, 100000);
				light.Falloff = ReadFloat(value, "falloff", defaults.Falloff, 0.05f, 10);
				light.CastShadows = ReadBool(value, "castShadows", defaults.CastShadows);
			}
		}
		Json::Value SerializeOccluder(const ShadowOccluder2DComponent& box) {
			auto value = Json::Value::MakeObject();
			value.AddMember("enabled", Json::Value(box.Enabled));
			value.AddMember("sizeX", Json::Value(box.Size.x));
			value.AddMember("sizeY", Json::Value(box.Size.y));
			value.AddMember("offsetX", Json::Value(box.Offset.x));
			value.AddMember("offsetY", Json::Value(box.Offset.y));
			value.AddMember("opacity", Json::Value(box.Opacity));
			return value;
		}
		void DeserializeOccluder(ShadowOccluder2DComponent& box, const Json::Value& value) {
			box.Enabled = ReadBool(value,"enabled",true);
			box.Size = { ReadFloat(value,"sizeX",1,0.001f,100000), ReadFloat(value,"sizeY",1,0.001f,100000) };
			box.Offset = { ReadFloat(value,"offsetX",0,-100000,100000), ReadFloat(value,"offsetY",0,-100000,100000) };
			box.Opacity = ReadFloat(value,"opacity",1,0,1);
		}
		bool IsActive(const Renderer2D::SceneProvider& scenes) {
			bool active = false;
			scenes([&](Scene& scene) {
				for (auto [entity, light] : scene.GetRegistry().view<Light2DComponent>(entt::exclude<DisabledTag>).each()) active |= light.Enabled;
				for (auto [entity, light] : scene.GetRegistry().view<AmbientLight2DComponent>(entt::exclude<DisabledTag>).each()) active |= light.Enabled;
			});
			return active;
		}
		const Framebuffer* Render(const Renderer2D::SceneProvider& scenes, const glm::mat4& vp,
			const AABB& bounds, const Framebuffer& source) {
			if (!g_Effect) g_Effect = std::make_unique<FullscreenEffect>();
			if (!g_Effect->IsInitialized() && !g_Effect->Initialize(k_Shader, sizeof(Uniforms))) return nullptr;
			Uniforms uniforms{};
			uniforms.InverseVP = glm::inverse(vp);
			std::vector<GpuLight> lights;
			std::vector<GpuOccluder> occluders;
			bool hasAmbient = false;
			scenes([&](Scene& scene) {
				auto& registry = scene.GetRegistry();
				for (auto [entity, ambient] : registry.view<AmbientLight2DComponent>(entt::exclude<DisabledTag>).each()) {
					if (!ambient.Enabled) continue;
					hasAmbient = true;
					uniforms.Ambient += glm::vec4(ambient.Color.r, ambient.Color.g, ambient.Color.b, 0)
						* std::clamp(ambient.Intensity, 0.0f, 100.0f) * std::clamp(ambient.Color.a, 0.0f, 1.0f);
				}
				for (auto [entity, transform, light] : registry.view<Transform2DComponent, Light2DComponent>(entt::exclude<DisabledTag>).each()) {
					if (!light.Enabled || !std::isfinite(light.Radius) || !std::isfinite(light.Intensity)) continue;
					const float radius = std::clamp(light.Radius, 0.001f, 100000.0f);
					if (!AABB::Intersects(bounds, { transform.Position - Vec2(radius), transform.Position + Vec2(radius) })) continue;
					lights.push_back({ {transform.Position.x, transform.Position.y, radius, std::clamp(light.Intensity,0.0f,100.0f)},
						{light.Color.r, light.Color.g, light.Color.b, light.Color.a},
						{std::clamp(light.Falloff,0.05f,10.0f), light.CastShadows ? 1.0f : 0.0f,0,0} });
				}
				for (auto [entity, transform, box] : registry.view<Transform2DComponent, ShadowOccluder2DComponent>(entt::exclude<DisabledTag>).each()) {
					if (!box.Enabled || box.Opacity <= 0) continue;
					const auto center = transform.TransformPoint({box.Offset.x, box.Offset.y});
					occluders.push_back({ {center.x, center.y, std::max(0.0001f,std::abs(box.Size.x*transform.Scale.x)*0.5f),
						std::max(0.0001f,std::abs(box.Size.y*transform.Scale.y)*0.5f)},
						{std::cos(transform.Rotation),std::sin(transform.Rotation),std::clamp(box.Opacity,0.0f,1.0f),0} });
				}
			});
			if (!hasAmbient) uniforms.Ambient = glm::vec4(0.1f,0.1f,0.1f,0);
			const glm::vec2 center{ (bounds.Min.x+bounds.Max.x)*0.5f, (bounds.Min.y+bounds.Max.y)*0.5f };
			auto nearer = [&](const auto& a, const auto& b) {
				const glm::vec2 da = glm::vec2(a.Geometry) - center, db = glm::vec2(b.Geometry) - center;
				return glm::dot(da,da) < glm::dot(db,db);
			};
			std::stable_sort(lights.begin(),lights.end(),nearer);
			std::stable_sort(occluders.begin(),occluders.end(),nearer);
			if (!g_WarnedLimit && (lights.size()>uniforms.Lights.size() || occluders.size()>uniforms.Occluders.size())) {
				FNK_CORE_WARN_TAG("Lighting2D", "View exceeds 32 lights or 64 occluders; using those nearest the view center");
				g_WarnedLimit = true;
			}
			uniforms.Counts.x = static_cast<uint32_t>(std::min(lights.size(),uniforms.Lights.size()));
			uniforms.Counts.y = static_cast<uint32_t>(std::min(occluders.size(),uniforms.Occluders.size()));
			std::copy_n(lights.begin(),uniforms.Counts.x,uniforms.Lights.begin());
			std::copy_n(occluders.begin(),uniforms.Counts.y,uniforms.Occluders.begin());
			return g_Effect->Render(source,&uniforms,sizeof(uniforms));
		}
		template<class T> void RegisterLight(const char* name, const char* display, const char* icon) {
			std::vector<PropertyDescriptor> props{
				Properties::Make("Enabled","Enabled",&T::Enabled),
				Properties::Make("Color","Color",&T::Color),
				Properties::Make("Intensity","Intensity",&T::Intensity,PropertyMetadata{}.WithDragClamp(0,100)) };
			if constexpr (std::is_same_v<T, Light2DComponent>) {
				props.push_back(Properties::Make("Radius","Radius",&T::Radius,PropertyMetadata{}.WithDragClamp(0.001,100000)));
				props.push_back(Properties::Make("Falloff","Falloff",&T::Falloff,PropertyMetadata{}.WithDragClamp(0.05,10)));
				props.push_back(Properties::Make("CastShadows","Cast Shadows",&T::CastShadows));
			}
			Package::RegisterSerializableComponent<T>(display,"Lighting 2D",name,nullptr,
				SerializeLight<T>,DeserializeLight<T>,ComponentCategory::Component,std::move(props),icon);
			auto* info = SceneManager::GetComponentRegistry().GetInfo<T>();
			info->engineModule = EngineModule::TwoD;
			info->dependsOn.push_back(typeid(Transform2DComponent));
			Package::RegisterComponentEntityPreset<T>("2D Entity/Lighting",display);
		}
	}
}

extern "C" FUNKE_PACKAGE_API int FunkePackage_OnLoad() {
	using namespace Funke;
	using namespace Funke::Lighting2D;
	RegisterLight<Light2DComponent>("Light2D","Light 2D","light_point");
	RegisterLight<AmbientLight2DComponent>("AmbientLight2D","Ambient Light 2D","light_sun");
	Package::RegisterSerializableComponent<ShadowOccluder2DComponent>("Shadow Occluder 2D","Lighting 2D","ShadowOccluder2D",nullptr,
		SerializeOccluder,DeserializeOccluder,ComponentCategory::Component,{
			Properties::Make("Enabled","Enabled",&ShadowOccluder2DComponent::Enabled),
			Properties::Make("Size","Size",&ShadowOccluder2DComponent::Size,PropertyMetadata{}.WithDragClamp(0.001,100000)),
			Properties::Make("Offset","Offset",&ShadowOccluder2DComponent::Offset),
			Properties::Make("Opacity","Opacity",&ShadowOccluder2DComponent::Opacity,Properties::Meta::Clamp(0,1)) },"collider_box_2d");
	auto* info = SceneManager::GetComponentRegistry().GetInfo<ShadowOccluder2DComponent>();
	info->engineModule = EngineModule::TwoD;
	info->dependsOn.push_back(typeid(Transform2DComponent));
	Package::RegisterComponentEntityPreset<ShadowOccluder2DComponent>("2D Entity/Lighting","Shadow Occluder 2D");
	Package::SetEditorGizmo<Light2DComponent>([](Entity entity) {
		if (!entity.HasComponent<Transform2DComponent>()) return;
		const auto& light = entity.GetComponent<Light2DComponent>();
		Gizmo::SetColor(light.Color);
		Gizmo::DrawWireCircle(entity.GetComponent<Transform2DComponent>().Position,light.Radius,64);
	});
	Package::SetEditorGizmo<ShadowOccluder2DComponent>([](Entity entity) {
		if (!entity.HasComponent<Transform2DComponent>()) return;
		const auto& t = entity.GetComponent<Transform2DComponent>();
		const auto& box = entity.GetComponent<ShadowOccluder2DComponent>();
		Gizmo::SetColor(Color{0.8f,0.5f,1.0f,1.0f});
		Gizmo::DrawWireSquare(t.TransformPoint({box.Offset.x,box.Offset.y}),
			{box.Size.x*t.Scale.x,box.Size.y*t.Scale.y},t.GetRotationDegrees());
	});
	g_EffectToken = Renderer2D::RegisterSceneEffect({IsActive,Render,[] { g_Effect.reset(); }});
	return g_EffectToken ? 0 : 1;
}

extern "C" FUNKE_PACKAGE_API void FunkePackage_OnUnload() {
	Funke::Renderer2D::UnregisterSceneEffect(Funke::Lighting2D::g_EffectToken);
	Funke::Lighting2D::g_Effect.reset();
}
