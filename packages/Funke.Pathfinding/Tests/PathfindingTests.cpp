#include "Pathfinding.hpp"
#include <algorithm>
#include <cmath>
#include <future>
#include <iostream>
#include <limits>
#include <queue>
#include <random>
#include <stdexcept>
#include <string>

using namespace Funke::Pathfinding;
namespace {
int g_Checks = 0;
void Check(bool value, const char* message) { ++g_Checks; if (!value) throw std::runtime_error(message); }
template<class F> void Reject(F fn) { bool rejected = false; try { fn(); } catch (const std::invalid_argument&) { rejected = true; } Check(rejected, "Invalid input accepted"); }
Status Finish(Search& search, int budget = 17) { while (search.GetStatus() == Status::Searching) search.Step(budget); return search.GetStatus(); }
double Dijkstra(int count, const std::vector<Edge>& edges, int start, int goal) {
    std::vector<std::vector<std::pair<int, double>>> arcs(count);
    for (auto e : edges) arcs[e.From].push_back({e.To, e.Cost});
    std::vector<double> distance(count, std::numeric_limits<double>::infinity());
    using Item = std::pair<double, int>;
    std::priority_queue<Item, std::vector<Item>, std::greater<Item>> open;
    distance[start] = 0; open.push({0, start});
    while (!open.empty()) {
        auto [cost, node] = open.top(); open.pop();
        if (cost != distance[node]) continue;
        if (node == goal) return cost;
        for (auto [to, edgeCost] : arcs[node]) if (cost + edgeCost < distance[to]) {
            distance[to] = cost + edgeCost; open.push({distance[to], to});
        }
    }
    return distance[goal];
}
std::vector<Edge> GridEdges(int w, int h, int d, const std::vector<float>& costs, bool diagonals) {
    std::vector<Edge> edges;
    for (int z = 0; z < d; ++z) for (int y = 0; y < h; ++y) for (int x = 0; x < w; ++x) {
        int from = x + w * (y + h * z);
        if (costs[from] == 0) continue;
        for (int nz = std::max(0, z - 1); nz <= std::min(d - 1, z + 1); ++nz)
        for (int ny = std::max(0, y - 1); ny <= std::min(h - 1, y + 1); ++ny)
        for (int nx = std::max(0, x - 1); nx <= std::min(w - 1, x + 1); ++nx) {
            int axes = (nx != x) + (ny != y) + (nz != z);
            if (axes == 0 || (!diagonals && axes > 1)) continue;
            bool clear = true;
            for (int cz = std::min(z, nz); cz <= std::max(z, nz); ++cz)
            for (int cy = std::min(y, ny); cy <= std::max(y, ny); ++cy)
            for (int cx = std::min(x, nx); cx <= std::max(x, nx); ++cx)
                if (costs[cx + w * (cy + h * cz)] == 0) clear = false;
            int to = nx + w * (ny + h * nz);
            if (clear) edges.push_back({from, to, float(std::sqrt(double(axes)) * costs[to])});
        }
    }
    return edges;
}
void Compare(const std::shared_ptr<const Map>& map, const std::vector<Edge>& edges, int start, int goal) {
    Search search(map); search.Begin(start, goal);
    const double expected = Dijkstra(map->Count(), edges, start, goal);
    auto status = Finish(search, 1);
    Check((status == Status::Found) == std::isfinite(expected), "Reachability differs from Dijkstra");
    if (status != Status::Found) return;
    Check(std::abs(search.GetStatistics().Cost - expected) < 1e-5 * std::max(1.0, expected), "Suboptimal path");
    std::vector<int32_t> path(search.GetStatistics().PathLength);
    Check(search.CopyPath(path) == int(path.size()), "Path count mismatch");
    Check(path.front() == start && path.back() == goal, "Wrong endpoints");
    double cost = 0;
    for (size_t i = 1; i < path.size(); ++i) {
        double best = std::numeric_limits<double>::infinity();
        for (auto edge : edges) if (edge.From == path[i - 1] && edge.To == path[i]) best = std::min(best, double(edge.Cost));
        Check(std::isfinite(best), "Path contains nonexistent edge"); cost += best;
    }
    Check(std::abs(cost - expected) < 1e-5 * std::max(1.0, expected), "Copied path cost mismatch");
}
}
int main() {
    try {
        std::mt19937 random(20261109);
        for (bool diagonal : {false, true}) {
            std::vector<float> open(24, 1);
            auto emptyMap = Map::Grid(4, 3, 2, diagonal, open);
            auto edges = GridEdges(4, 3, 2, open, diagonal);
            for (int a = 0; a < 24; ++a) for (int b = 0; b < 24; ++b) Compare(emptyMap, edges, a, b);
        }
        for (bool diagonal : {false, true}) for (int depth : {1, 5}) for (int run = 0; run < 60; ++run) {
            constexpr int w = 8, h = 7;
            std::vector<float> costs(w * h * depth);
            for (auto& cost : costs) cost = random() % 5 == 0 ? 0.0f : float(1 + random() % 8);
            costs.front() = costs.back() = 1;
            Compare(Map::Grid(w, h, depth, diagonal, costs), GridEdges(w, h, depth, costs, diagonal), 0, int(costs.size()) - 1);
        }
        for (int run = 0; run < 150; ++run) {
            std::vector<Point> points(30); std::vector<Edge> edges;
            for (auto& p : points) p = {float(random() % 100), float(random() % 100), float(random() % 100)};
            for (int i = 0; i < 150; ++i) edges.push_back({int(random() % 30), int(random() % 30), float(random() % 40)});
            Compare(Map::Graph(points, edges), edges, 0, 29);
        }
        std::vector<float> costs(100, 1);
        auto map = Map::Grid(10, 10, 1, true, costs);
        Search search(map);
        Check(search.GetStatus() == Status::Idle, "Initial status");
        for (int i = 0; i < 1000; ++i) {
            search.Begin(0, i % 100); Check(Finish(search) == Status::Found, "Search reuse failed");
        }
        search.Begin(0, 99, 1); Check(Finish(search) == Status::LimitReached, "Limit not enforced");
        Check(search.GetStatistics().Expanded == 1 && search.CopyPath({}) == 0, "Limit exposes partial path");
        search.Begin(0, 99); search.Step(1); Check(search.Cancel() == Status::Cancelled, "Cancel failed");
        Check(search.Step(10) == Status::Cancelled && search.CopyPath({}) == 0, "Cancelled path exposed");
        search.Begin(5, 5); Check(Finish(search) == Status::Found && search.GetStatistics().Cost == 0, "Same endpoint");
        search.Begin(0, 99); Finish(search);
        int32_t sentinel = -77;
        Check(search.CopyPath({&sentinel, 1}) == 10 && sentinel == -77, "Short buffer overwritten");
        costs[0] = 0; Search blocked(Map::Grid(10, 10, 1, false, costs));
        Check(blocked.Begin(0, 0) == Status::NoPath, "Blocked same-node path accepted");
        Reject([&] { search.Begin(-1, 0); }); Reject([&] { search.Step(0); });
        Reject([&] { search.Begin(0, 1, -1); });
        Reject([&] { Map::Grid(INT32_MAX, INT32_MAX, INT32_MAX, false, {}); });
        Reject([&] { Map::Grid(0, 1, 1, false, {}); });
        costs[0] = .5f; Reject([&] { Map::Grid(10, 10, 1, false, costs); });
        costs[0] = std::numeric_limits<float>::quiet_NaN(); Reject([&] { Map::Grid(10, 10, 1, false, costs); });
        std::vector<float> cube(8, 1); cube[1] = cube[2] = cube[4] = 0;
        Search corner(Map::Grid(2, 2, 2, true, cube)); corner.Begin(0, 7);
        Check(Finish(corner) == Status::NoPath, "3D corner cutting");
        std::vector<std::future<bool>> workers;
        for (int worker = 0; worker < 8; ++worker) workers.push_back(std::async(std::launch::async, [map] {
            Search local(map);
            for (int i = 0; i < 1000; ++i) { local.Begin(0, 99); if (Finish(local) != Status::Found) return false; }
            return true;
        }));
        map.reset();
        for (auto& worker : workers) Check(worker.get(), "Parallel shared map search failed");
        std::cout << "Passed " << g_Checks << " checks; 390 randomized Dijkstra comparisons; 8000 parallel queries.\n";
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
