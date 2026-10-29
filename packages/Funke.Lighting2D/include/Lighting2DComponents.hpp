#pragma once
#include "Collections/Color.hpp"
#include <glm/vec2.hpp>

namespace Funke::Lighting2D {
	struct Light2DComponent {
		bool Enabled = true;
		Funke::Color Color{ 1.0f, 0.85f, 0.6f, 1.0f };
		float Intensity = 1.5f;
		float Radius = 5.0f;
		float Falloff = 1.5f;
		bool CastShadows = true;
	};
	struct AmbientLight2DComponent {
		bool Enabled = true;
		Funke::Color Color{ 0.65f, 0.75f, 1.0f, 1.0f };
		float Intensity = 0.15f;
	};
	struct ShadowOccluder2DComponent {
		bool Enabled = true;
		glm::vec2 Size{ 1.0f, 1.0f };
		glm::vec2 Offset{ 0.0f, 0.0f };
		float Opacity = 1.0f;
	};
}
