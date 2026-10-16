// Funke-Physics benchmark. Built twice by CMake: against this library and, with
// FUNKE_PHYS_BENCH_BASELINE, against the pre-optimization Index-Physics sources.
#ifdef FUNKE_PHYS_BENCH_BASELINE
#include "PhysicsWorld.hpp"
#include "Physics2D.hpp"
#include "Body.hpp"
#include "BoxCollider.hpp"
#include "CircleCollider.hpp"
namespace Phys = IndexPhys;
#else
#include "FunkePhys/PhysicsWorld.hpp"
#include "FunkePhys/Physics2D.hpp"
#include "FunkePhys/Body.hpp"
#include "FunkePhys/BoxCollider.hpp"
#include "FunkePhys/CircleCollider.hpp"
namespace Phys = FunkePhys;
#endif

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <numeric>
#include <random>
#include <string>
#include <vector>

namespace {
    using Clock = std::chrono::steady_clock;

    double Ms(Clock::duration d) { return std::chrono::duration<double, std::milli>(d).count(); }

    struct Scene {
        Phys::PhysicsWorld world;
        std::vector<std::unique_ptr<Phys::Body>> bodies;
        std::vector<std::unique_ptr<Phys::Collider>> colliders;

        Phys::Body& Add(Phys::BodyType type, Phys::Vec2 pos, bool circle, float size) {
            auto body = std::make_unique<Phys::Body>(type);
            body->SetPosition(pos);
            std::unique_ptr<Phys::Collider> collider;
            if (circle) collider = std::make_unique<Phys::CircleCollider>(size);
            else collider = std::make_unique<Phys::BoxCollider>(Phys::Vec2{ size, size });
            world.RegisterBody(*body);
            world.RegisterCollider(*collider);
            world.AttachCollider(*body, *collider);
            Phys::Body& ref = *body;
            bodies.push_back(std::move(body));
            colliders.push_back(std::move(collider));
            return ref;
        }
    };

    // N dynamic boxes/circles dropped on a static floor; measures a settling pile.
    void Pile(int count, int steps) {
        Scene scene;
        const int columns = std::max(1, static_cast<int>(std::sqrt(static_cast<float>(count)) * 2.0f));
        auto& ground = scene.Add(Phys::BodyType::Static, { 0.0f, -1.0f }, false, 0.5f);
        static_cast<Phys::BoxCollider*>(scene.colliders.back().get())->SetHalfExtents({ columns * 0.6f + 5.0f, 0.5f });
        (void)ground;
        for (int i = 0; i < count; ++i) {
            const float x = (i % columns - columns * 0.5f) * 1.1f;
            const float y = 0.6f + (i / columns) * 1.1f;
            scene.Add(Phys::BodyType::Dynamic, { x, y }, (i & 1) != 0, 0.5f);
        }

        std::size_t contacts = 0;
        const auto start = Clock::now();
        for (int s = 0; s < steps; ++s) {
            scene.world.Step(1.0f / 60.0f);
            contacts += scene.world.GetContacts().size();
        }
        const double total = Ms(Clock::now() - start);
        std::printf("pile      n=%6d  %8.3f ms/step  %7.0f contacts/step\n", count, total / steps,
                    static_cast<double>(contacts) / steps);
    }

    // N kinematic movers spread over a large area: broadphase-dominated.
    void Sparse(int count, int steps) {
        Scene scene;
        std::mt19937 rng(42);
        const float extent = std::sqrt(static_cast<float>(count)) * 4.0f;
        std::uniform_real_distribution<float> pos(-extent, extent);
        std::uniform_real_distribution<float> vel(-3.0f, 3.0f);
        for (int i = 0; i < count; ++i) {
            auto& body = scene.Add(Phys::BodyType::Kinematic, { pos(rng), pos(rng) }, (i & 1) != 0, 0.5f);
            body.SetVelocity({ vel(rng), vel(rng) });
        }
        std::size_t contacts = 0;
        const auto start = Clock::now();
        for (int s = 0; s < steps; ++s) {
            scene.world.Step(1.0f / 60.0f);
            contacts += scene.world.GetContacts().size();
        }
        const double total = Ms(Clock::now() - start);
        std::printf("sparse    n=%6d  %8.3f ms/step  %7.0f contacts/step\n", count, total / steps,
                    static_cast<double>(contacts) / steps);
    }

    void Queries(int count, int queries) {
        Scene scene;
        std::mt19937 rng(7);
        const float extent = std::sqrt(static_cast<float>(count)) * 3.0f;
        std::uniform_real_distribution<float> pos(-extent, extent);
        std::uniform_real_distribution<float> angle(0.0f, 6.2831853f);
        for (int i = 0; i < count; ++i) {
            scene.Add(Phys::BodyType::Static, { pos(rng), pos(rng) }, (i & 1) != 0, 0.5f);
        }
        scene.world.Step(1.0f / 60.0f);
        Phys::Physics2D::SetContext(scene.world);

        std::size_t hits = 0;
        auto start = Clock::now();
        for (int q = 0; q < queries; ++q) {
            const float a = angle(rng);
            hits += Phys::Physics2D::Raycast({ pos(rng), pos(rng) }, { std::cos(a), std::sin(a) }, 20.0f).hit ? 1 : 0;
        }
        const double ray = Ms(Clock::now() - start) * 1000.0 / queries;

        start = Clock::now();
        for (int q = 0; q < queries; ++q) {
            hits += Phys::Physics2D::OverlapCircle({ pos(rng), pos(rng) }, 1.5f) != nullptr ? 1 : 0;
        }
        const double circle = Ms(Clock::now() - start) * 1000.0 / queries;

        start = Clock::now();
        for (int q = 0; q < queries; ++q) {
            hits += Phys::Physics2D::OverlapBoxAll({ pos(rng), pos(rng) }, { 4.0f, 4.0f }).size();
        }
        const double boxAll = Ms(Clock::now() - start) * 1000.0 / queries;

        start = Clock::now();
        for (int q = 0; q < queries; ++q) {
            hits += Phys::Physics2D::ContainsPoint(Phys::Vec2{ pos(rng), pos(rng) }) != nullptr ? 1 : 0;
        }
        const double point = Ms(Clock::now() - start) * 1000.0 / queries;
        Phys::Physics2D::ClearContext();

        std::printf("queries   n=%6d  raycast %8.2f us  overlapCircle %8.2f us  overlapBoxAll %8.2f us  point %8.2f us  (hits %zu)\n",
                    count, ray, circle, boxAll, point, hits);
    }

    void Churn(int count) {
        Scene scene;
        for (int i = 0; i < count; ++i) {
            scene.Add(Phys::BodyType::Dynamic, { static_cast<float>(i), 0.0f }, false, 0.5f);
        }
        std::vector<int> order(count);
        std::iota(order.begin(), order.end(), 0);
        std::shuffle(order.begin(), order.end(), std::mt19937(3));

        const auto start = Clock::now();
        for (int index : order) {
            scene.world.UnregisterCollider(*scene.colliders[index]);
            scene.world.UnregisterBody(*scene.bodies[index]);
        }
        for (int i = 0; i < count; ++i) {
            scene.world.RegisterBody(*scene.bodies[i]);
            scene.world.RegisterCollider(*scene.colliders[i]);
            scene.world.AttachCollider(*scene.bodies[i], *scene.colliders[i]);
        }
        std::printf("churn     n=%6d  %8.3f ms (unregister + register all)\n", count, Ms(Clock::now() - start));
    }
}

int main(int argc, char** argv) {
    const bool quick = argc > 1 && std::string(argv[1]) == "--quick";
    const int steps = quick ? 60 : 300;
    for (int n : { 1000, 4000 }) Pile(n, steps);
    for (int n : { 5000, 20000 }) Sparse(n, steps);
    for (int n : { 2000, 20000 }) Queries(n, quick ? 2000 : 10000);
    for (int n : { 5000, 20000 }) Churn(n);
    return 0;
}
