#pragma once

#include "FunkeTilemap2DApi.hpp"

#include "Scene/SceneSystem.hpp"

#include <cstddef>

namespace Funke { class Scene; }

namespace Funke::Tilemap2D {

	class FUNKE_PACKAGE_API TilemapRenderSystem : public Funke::SceneSystem {
	public:
		void OnPreRender(Funke::Scene& scene) override;

		std::size_t GetTilesEmittedLastFrame() const noexcept { return m_LastTilesEmitted; }
		std::size_t GetChunksWalkedLastFrame() const noexcept { return m_LastChunksWalked; }

	private:
		std::size_t m_LastTilesEmitted = 0;
		std::size_t m_LastChunksWalked = 0;
	};

} // namespace Funke::Tilemap2D
