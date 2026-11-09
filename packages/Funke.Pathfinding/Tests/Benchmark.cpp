#include "Pathfinding.hpp"
#include <algorithm>
#include <chrono>
#include <cstdlib>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <new>
#include <random>

namespace { size_t g_Allocations = 0; bool g_Count = false; }
void* operator new(std::size_t size) { if (g_Count) ++g_Allocations; if (void* p = std::malloc(size ? size : 1)) return p; throw std::bad_alloc(); }
void* operator new[](std::size_t size) { return ::operator new(size); }
void operator delete(void* p) noexcept { std::free(p); }
void operator delete[](void* p) noexcept { std::free(p); }
void operator delete(void* p, std::size_t) noexcept { std::free(p); }
void operator delete[](void* p, std::size_t) noexcept { std::free(p); }
using namespace Funke::Pathfinding;
int main(int argc, char** argv) {
    std::ofstream raw;
    if (argc > 1) {
        raw.open(argv[1]);
        if (!raw) return 2;
        raw << "workload,query,start,goal,status,ms,expanded\n";
    }
    std::cout << "workload,nodes,queries,found,mean_ms,p50_ms,p95_ms,max_ms,mean_expanded,search_allocations\n";
    for (int kind = 0; kind < 3; ++kind) {
        int w = kind == 2 ? 64 : 256, h = kind == 2 ? 32 : 256, d = kind == 2 ? 64 : 1;
        std::vector<float> costs(w * h * d, 1);
        std::mt19937 random(20261109);
        if (kind != 0) for (auto& cost : costs) cost = random() % 10 == 0 ? 0.0f : 1.0f;
        auto map = Map::Grid(w, h, d, true, costs); Search search(map);
        std::vector<double> samples; samples.reserve(200);
        long long expanded = 0; int found = 0; g_Allocations = 0;
        for (int run = -20; run < 200; ++run) {
            int start, goal;
            do { start = int(random() % costs.size()); } while (costs[start] == 0);
            do { goal = int(random() % costs.size()); } while (costs[goal] == 0);
            const auto begin = std::chrono::steady_clock::now();
            g_Count = run >= 0;
            search.Begin(start, goal);
            while (search.GetStatus() == Status::Searching) search.Step(512);
            g_Count = false;
            double ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - begin).count();
            if (run >= 0) {
                samples.push_back(ms); expanded += search.GetStatistics().Expanded; found += search.GetStatus() == Status::Found;
                if (raw) raw << kind << ',' << run << ',' << start << ',' << goal << ',' << int(search.GetStatus())
                    << ',' << std::setprecision(12) << ms << ',' << search.GetStatistics().Expanded << '\n';
            }
        }
        double total = 0; for (auto time : samples) total += time;
        std::sort(samples.begin(), samples.end());
        std::cout << (kind == 0 ? "open2d" : kind == 1 ? "obstacles2d" : "obstacles3d") << ',' << costs.size()
            << ",200," << found << ',' << std::setprecision(8) << total / 200 << ',' << (samples[99] + samples[100]) / 2
            << ',' << samples[189] << ',' << samples.back() << ',' << double(expanded) / 200 << ',' << g_Allocations << '\n';
        if (g_Allocations != 0) return 1;
    }
}
