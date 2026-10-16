#pragma once
#include "Export.hpp"
#include "Vec2.hpp"

namespace FunkePhys {
    struct FUNKE_PHYS_API WorldSettings
    {
        Vec2 gravity{ 0.0f, -9.81f };
        Vec2 worldMin{ -1000.0f, -1000.0f };
        Vec2 worldMax{ 1000.0f,  1000.0f };
        // Edge length of a broadphase grid cell. <= 0 picks it every step from the
        // average collider size, which suits scenes whose bodies are similar in size.
        float broadphaseCellSize = 5.0f;
        // How many velocity-solver passes to run per Step. Higher values
        // reduce jitter in stacks at the cost of CPU. Clamped to >= 1.
        int solverIterations = 8;
        bool enableWorldBounds = false;
    };
}
