#pragma once

#include "IndexTilemap2DApi.hpp"

#include "Scene/SceneSystem.hpp"

#include <cstddef>

namespace Index { class Scene; }

namespace Index::Tilemap2D {

	class INDEX_PACKAGE_API TilemapRenderSystem : public Index::SceneSystem {
	public:
		void OnPreRender(Index::Scene& scene) override;

		std::size_t GetTilesEmittedLastFrame() const noexcept { return m_LastTilesEmitted; }
		std::size_t GetChunksWalkedLastFrame() const noexcept { return m_LastChunksWalked; }

	private:
		std::size_t m_LastTilesEmitted = 0;
		std::size_t m_LastChunksWalked = 0;
	};

} // namespace Index::Tilemap2D
