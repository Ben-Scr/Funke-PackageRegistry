#pragma once
#include <cstdint>
#include <memory>
#include <span>
#include <vector>

namespace Funke::Pathfinding {
struct Point { float X, Y, Z; };
struct Edge { int32_t From, To; float Cost; };
enum class Status : int32_t { Idle, Searching, Found, NoPath, LimitReached, Cancelled };
struct Statistics {
    int32_t Expanded = 0, Visited = 0, OpenPeak = 0, PathLength = 0;
    double Cost = 0;
};
class Map {
public:
    static std::shared_ptr<const Map> Grid(int32_t width, int32_t height, int32_t depth,
        bool diagonals, std::span<const float> costs);
    static std::shared_ptr<const Map> Graph(std::span<const Point> points, std::span<const Edge> edges);
    int32_t Count() const { return m_Count; }
    bool Walkable(int32_t node) const;
    Point Position(int32_t node) const;
private:
    friend class Search;
    double Heuristic(int32_t from, int32_t to) const;
    struct Arc { int32_t To; float Cost; };
    int32_t m_Count = 0, m_Width = 0, m_Height = 0, m_Depth = 0;
    bool m_Diagonals = false, m_OpenUniform = false;
    double m_HeuristicScale = 1;
    std::vector<float> m_Costs;
    std::vector<Point> m_Points;
    std::vector<uint32_t> m_Offsets;
    std::vector<Arc> m_Arcs;
};
// Separate searches may share a map; each search requires exclusive access.
class Search {
public:
    explicit Search(std::shared_ptr<const Map> map);
    Status Begin(int32_t start, int32_t goal, int32_t expansionLimit = 0);
    Status Step(int32_t expansions);
    Status Cancel();
    Status GetStatus() const { return m_Status; }
    Statistics GetStatistics() const { return m_Stats; }
    int32_t CopyPath(std::span<int32_t> destination) const;
private:
    struct Record {
        double G = 0, F = 0;
        uint32_t Stamp = 0;
        int32_t Parent = -1, Heap = -1;
    };
    bool Less(int32_t a, int32_t b) const;
    void Up(int32_t index);
    void Push(int32_t node);
    int32_t Pop();
    void Relax(int32_t from, int32_t to, double cost);
    void ExpandGrid(int32_t from);
    std::shared_ptr<const Map> m_Map;
    std::vector<Record> m_Records;
    std::vector<int32_t> m_Heap;
    uint32_t m_Stamp = 0;
    int32_t m_Start = -1, m_Goal = -1, m_Limit = 0;
    Status m_Status = Status::Idle;
    Statistics m_Stats;
};
}
