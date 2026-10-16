#pragma once
#include "Collections/Vec2.hpp"
#include "Scene/EntityHandle.hpp"

#include <FunkePhys/Body.hpp>
#include <FunkePhys/BoxCollider.hpp>
#include <FunkePhys/CircleCollider.hpp>

#include <algorithm>
#include <cmath>

namespace Funke::FastPhysics {

	inline FunkePhys::Vec2 ToPhys(const Vec2& v) { return { v.x, v.y }; }
	inline Vec2 FromPhys(const FunkePhys::Vec2& v) { return { v.x, v.y }; }

	// Body simulated by Funke-Physics: box and circle shapes, no rotation. For rotation,
	// joints or continuous collision use Rigidbody2D (Box2D) instead.
	struct FastBody2DComponent {
		FunkePhys::BodyType Type = FunkePhys::BodyType::Dynamic;
		float Mass = 1.0f;
		bool UseGravity = true;
		bool BoundaryCheck = false;

		// Runtime state owned by the scene hooks; never serialized.
		FunkePhys::Body* m_Body = nullptr;

		bool IsValid() const { return m_Body != nullptr; }
		Vec2 GetVelocity() const { return m_Body ? FromPhys(m_Body->GetVelocity()) : Vec2{}; }
		void SetVelocity(const Vec2& velocity) { if (m_Body) m_Body->SetVelocity(ToPhys(velocity)); }

		void ApplySettings() {
			if (!m_Body) return;
			m_Body->SetBodyType(Type);
			m_Body->SetMass(Mass);
			m_Body->SetGravityEnabled(UseGravity);
			m_Body->SetBoundaryCheckEnabled(BoundaryCheck);
		}
	};

	// Axis-aligned box; HalfExtents are multiplied by Transform2D.Scale.
	struct FastBoxCollider2DComponent {
		Vec2 HalfExtents{ 0.5f, 0.5f };

		FunkePhys::BoxCollider* m_Collider = nullptr;
		Vec2 m_LastAppliedScale{ 1.0f, 1.0f };

		bool IsValid() const { return m_Collider != nullptr; }
		Vec2 GetWorldHalfExtents() const {
			return m_Collider ? FromPhys(m_Collider->GetHalfExtents())
				: Vec2{ HalfExtents.x * m_LastAppliedScale.x, HalfExtents.y * m_LastAppliedScale.y };
		}

		void SetHalfExtents(const Vec2& halfExtents) {
			HalfExtents = halfExtents;
			Apply();
		}

		void SyncScale(const Vec2& scale) {
			if (scale == m_LastAppliedScale) return;
			m_LastAppliedScale = scale;
			Apply();
		}

		void Apply() {
			if (m_Collider) {
				m_Collider->SetHalfExtents({ HalfExtents.x * m_LastAppliedScale.x, HalfExtents.y * m_LastAppliedScale.y });
			}
		}
	};

	// Circle; Radius is scaled by max(|Scale.x|, |Scale.y|) since a circle cannot stretch.
	struct FastCircleCollider2DComponent {
		float Radius = 0.5f;

		FunkePhys::CircleCollider* m_Collider = nullptr;
		float m_LastAppliedScale = 1.0f;

		bool IsValid() const { return m_Collider != nullptr; }
		float GetWorldRadius() const { return m_Collider ? m_Collider->GetRadius() : Radius * m_LastAppliedScale; }

		void SetRadius(float radius) {
			Radius = radius;
			Apply();
		}

		void SyncScale(const Vec2& scale) {
			const float uniform = std::max(std::abs(scale.x), std::abs(scale.y));
			if (uniform == m_LastAppliedScale) return;
			m_LastAppliedScale = uniform;
			Apply();
		}

		void Apply() {
			if (m_Collider) m_Collider->SetRadius(Radius * m_LastAppliedScale);
		}
	};

}
