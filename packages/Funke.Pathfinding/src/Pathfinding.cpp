#include "Pathfinding.hpp"
#include <algorithm>
#include <cmath>
#include <limits>
#include <stdexcept>

namespace Funke::Pathfinding {
namespace {
constexpr int32_t k_MaxNodes = 8 * 1024 * 1024;
double Distance(Point a, Point b) { return std::hypot(double(a.X) - b.X, double(a.Y) - b.Y, double(a.Z) - b.Z); }
void Require(bool condition, const char* message) { if (!condition) throw std::invalid_argument(message); }
}
std::shared_ptr<const Map> Map::Grid(int32_t width, int32_t height, int32_t depth,
    bool diagonals, std::span<const float> costs) {
    Require(width > 0 && height > 0 && depth > 0, "Grid dimensions must be positive");
    const int64_t plane = int64_t(width) * height;
    Require(plane <= k_MaxNodes && depth <= k_MaxNodes / plane, "Grid exceeds 8388608 cells");
    const auto count = int32_t(plane * depth);
    Require(costs.size() == size_t(count), "Grid cost count must equal cell count");
    for (float cost : costs) Require(std::isfinite(cost) && (cost == 0 || cost >= 1), "Cost must be zero (blocked) or finite and >= 1");
    auto map = std::shared_ptr<Map>(new Map);
    map->m_Count = count;
    map->m_Width = width; map->m_Height = height; map->m_Depth = depth;
    map->m_Diagonals = diagonals;
    map->m_OpenUniform = std::all_of(costs.begin(), costs.end(), [](float cost) { return cost == 1; });
    map->m_Costs.assign(costs.begin(), costs.end());
    return map;
}
std::shared_ptr<const Map> Map::Graph(std::span<const Point> points, std::span<const Edge> edges) {
    Require(!points.empty() && points.size() <= k_MaxNodes, "Graph must have 1..8388608 nodes");
    Require(edges.size() <= size_t(std::numeric_limits<int32_t>::max()), "Too many edges");
    for (auto p : points) Require(std::isfinite(p.X) && std::isfinite(p.Y) && std::isfinite(p.Z), "Positions must be finite");
    auto map = std::shared_ptr<Map>(new Map);
    map->m_Count = int32_t(points.size());
    map->m_Points.assign(points.begin(), points.end());
    map->m_Offsets.resize(points.size() + 1);
    map->m_Arcs.resize(edges.size());
    for (auto edge : edges) {
        Require(edge.From >= 0 && edge.From < map->m_Count && edge.To >= 0 && edge.To < map->m_Count, "Edge endpoint is out of range");
        Require(std::isfinite(edge.Cost) && edge.Cost >= 0, "Edge cost must be finite and nonnegative");
        ++map->m_Offsets[size_t(edge.From) + 1];
        const double distance = Distance(points[edge.From], points[edge.To]);
        if (distance > 0) map->m_HeuristicScale = std::min(map->m_HeuristicScale, edge.Cost / distance);
    }
    // Discount the bound slightly so float positions and costs cannot round it upward.
    map->m_HeuristicScale *= 1.0 - 1e-12;
    for (size_t i = 1; i < map->m_Offsets.size(); ++i) map->m_Offsets[i] += map->m_Offsets[i - 1];
    auto cursor = map->m_Offsets;
    for (auto edge : edges) map->m_Arcs[cursor[edge.From]++] = { edge.To, edge.Cost };
    return map;
}
bool Map::Walkable(int32_t node) const { return node >= 0 && node < m_Count && (m_Width == 0 || m_Costs[node] != 0); }
Point Map::Position(int32_t node) const {
    Require(node >= 0 && node < m_Count, "Node is out of range");
    if (m_Width == 0) return m_Points[node];
    return { float(node % m_Width), float(node / m_Width % m_Height), float(node / (m_Width * m_Height)) };
}
double Map::Heuristic(int32_t from, int32_t to) const {
    if (m_Width == 0) return Distance(m_Points[from], m_Points[to]) * m_HeuristicScale;
    const auto a = Position(from), b = Position(to);
    double distances[] = { std::abs(double(a.X) - b.X), std::abs(double(a.Y) - b.Y), std::abs(double(a.Z) - b.Z) };
    if (!m_Diagonals) return distances[0] + distances[1] + distances[2];
    std::sort(std::begin(distances), std::end(distances));
    return distances[2] + (std::sqrt(2.0) - 1) * distances[1] + (std::sqrt(3.0) - std::sqrt(2.0)) * distances[0];
}
Search::Search(std::shared_ptr<const Map> map) : m_Map(std::move(map)) {
    Require(bool(m_Map), "Map is null");
    m_Records.resize(m_Map->Count()); m_Heap.reserve(m_Map->Count());
}
bool Search::Less(int32_t a, int32_t b) const {
    const auto& x = m_Records[a]; const auto& y = m_Records[b];
    if (x.F != y.F) return x.F < y.F;
    if (x.G != y.G) return x.G > y.G;
    return a < b;
}
void Search::Up(int32_t index) {
    while (index > 0) {
        const auto parent = (index - 1) / 2;
        if (!Less(m_Heap[index], m_Heap[parent])) break;
        std::swap(m_Heap[index], m_Heap[parent]);
        m_Records[m_Heap[index]].Heap = index; m_Records[m_Heap[parent]].Heap = parent;
        index = parent;
    }
}
void Search::Push(int32_t node) {
    auto& record = m_Records[node]; record.Heap = int32_t(m_Heap.size());
    m_Heap.push_back(node); Up(record.Heap);
    m_Stats.OpenPeak = std::max(m_Stats.OpenPeak, int32_t(m_Heap.size()));
}
int32_t Search::Pop() {
    const int32_t result = m_Heap.front(), last = m_Heap.back();
    m_Heap.pop_back(); m_Records[result].Heap = -1;
    if (m_Heap.empty()) return result;
    m_Heap.front() = last; m_Records[last].Heap = 0;
    int32_t index = 0;
    while (size_t(index) * 2 + 1 < m_Heap.size()) {
        int32_t child = index * 2 + 1;
        if (size_t(child + 1) < m_Heap.size() && Less(m_Heap[child + 1], m_Heap[child])) ++child;
        if (!Less(m_Heap[child], m_Heap[index])) break;
        std::swap(m_Heap[child], m_Heap[index]);
        m_Records[m_Heap[child]].Heap = child; m_Records[m_Heap[index]].Heap = index;
        index = child;
    }
    return result;
}
Status Search::Begin(int32_t start, int32_t goal, int32_t expansionLimit) {
    Require(start >= 0 && start < m_Map->Count() && goal >= 0 && goal < m_Map->Count(), "Endpoint out of range");
    Require(expansionLimit >= 0, "Expansion limit must be nonnegative");
    m_Heap.clear(); m_Stats = {}; m_Start = start; m_Goal = goal; m_Limit = expansionLimit;
    if (++m_Stamp == 0) { for (auto& record : m_Records) record.Stamp = 0; m_Stamp = 1; }
    if (!m_Map->Walkable(start) || !m_Map->Walkable(goal)) return m_Status = Status::NoPath;
    m_Records[start] = { 0, m_Map->Heuristic(start, goal), m_Stamp, -1, -1 };
    m_Stats.Visited = 1; Push(start);
    return m_Status = Status::Searching;
}
void Search::Relax(int32_t from, int32_t to, double cost) {
    auto& target = m_Records[to]; const double g = m_Records[from].G + cost;
    if (target.Stamp == m_Stamp && !(g < target.G)) return;
    if (target.Stamp != m_Stamp) { target.Heap = -1; ++m_Stats.Visited; }
    target.G = g; target.F = g + m_Map->Heuristic(to, m_Goal);
    target.Parent = from; target.Stamp = m_Stamp;
    if (target.Heap < 0) Push(to); else Up(target.Heap);
}
void Search::ExpandGrid(int32_t from) {
    const int32_t w = m_Map->m_Width, h = m_Map->m_Height;
    const int32_t x = from % w, y = from / w % h, z = from / (w * h);
    if (m_Map->m_OpenUniform) {
        const int gx = m_Goal % w, gy = m_Goal / w % h, gz = m_Goal / (w * h);
        int dx = (gx > x) - (gx < x), dy = (gy > y) - (gy < y), dz = (gz > z) - (gz < z);
        if (!m_Map->m_Diagonals) { if (dx != 0) { dy = 0; dz = 0; } else if (dy != 0) dz = 0; }
        // With no obstacles or weights, this monotone route is provably optimal and needs no alternative frontier.
        Relax(from, from + dx + w * (dy + h * dz), std::sqrt(double((dx != 0) + (dy != 0) + (dz != 0))));
        return;
    }
    for (int dz = -1; dz <= 1; ++dz) for (int dy = -1; dy <= 1; ++dy) for (int dx = -1; dx <= 1; ++dx) {
        const int axes = (dx != 0) + (dy != 0) + (dz != 0);
        if (axes == 0 || (!m_Map->m_Diagonals && axes != 1)) continue;
        const int nx = x + dx, ny = y + dy, nz = z + dz;
        if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= m_Map->m_Depth) continue;
        const int32_t to = nx + w * (ny + h * nz);
        if (!m_Map->Walkable(to)) continue;
        bool clear = true;
        // Every proper subset must be clear: diagonals cannot squeeze past blocked faces or edges.
        const int mask = (dx != 0 ? 1 : 0) | (dy != 0 ? 2 : 0) | (dz != 0 ? 4 : 0);
        for (int subset = (mask - 1) & mask; subset != 0; subset = (subset - 1) & mask) {
            const auto intermediate = from + ((subset & 1) ? dx : 0) + ((subset & 2) ? dy * w : 0) + ((subset & 4) ? dz * w * h : 0);
            if (!m_Map->Walkable(intermediate)) { clear = false; break; }
        }
        if (clear) Relax(from, to, std::sqrt(double(axes)) * m_Map->m_Costs[to]);
    }
}
Status Search::Step(int32_t expansions) {
    Require(expansions > 0, "Step budget must be positive");
    if (m_Status != Status::Searching) return m_Status;
    for (int32_t i = 0; i < expansions; ++i) {
        if (m_Heap.empty()) return m_Status = Status::NoPath;
        const auto from = m_Heap.front();
        if (from == m_Goal) {
            m_Stats.Cost = m_Records[from].G;
            for (auto node = from; node != -1; node = m_Records[node].Parent) ++m_Stats.PathLength;
            return m_Status = Status::Found;
        }
        if (m_Limit != 0 && m_Stats.Expanded >= m_Limit) return m_Status = Status::LimitReached;
        Pop(); ++m_Stats.Expanded;
        if (m_Map->m_Width != 0) ExpandGrid(from);
        else for (uint32_t arc = m_Map->m_Offsets[from]; arc < m_Map->m_Offsets[from + 1]; ++arc)
            Relax(from, m_Map->m_Arcs[arc].To, m_Map->m_Arcs[arc].Cost);
    }
    return m_Status;
}
Status Search::Cancel() {
    if (m_Status == Status::Searching) { m_Status = Status::Cancelled; m_Heap.clear(); }
    return m_Status;
}
int32_t Search::CopyPath(std::span<int32_t> destination) const {
    if (m_Status != Status::Found) return 0;
    if (destination.size() < size_t(m_Stats.PathLength)) return m_Stats.PathLength;
    int32_t index = m_Stats.PathLength;
    for (auto node = m_Goal; node != -1; node = m_Records[node].Parent) destination[--index] = node;
    return m_Stats.PathLength;
}
}
