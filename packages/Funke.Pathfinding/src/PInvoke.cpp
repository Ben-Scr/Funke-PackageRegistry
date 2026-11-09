#include "Pathfinding.hpp"
#include "PathfindingApi.hpp"
#include <cstdio>
#include <exception>
#include <stdexcept>

namespace {
using namespace Funke::Pathfinding;
using MapHandle = std::shared_ptr<const Map>;
static_assert(sizeof(Point) == 12 && sizeof(Edge) == 12 && sizeof(Statistics) == 24);
thread_local char g_Error[512]{};
template<class F> auto Guard(F&& fn, decltype(fn()) failure) noexcept {
    g_Error[0] = 0;
    try { return fn(); }
    catch (const std::exception& e) { std::snprintf(g_Error, sizeof(g_Error), "%s", e.what()); }
    catch (...) { std::snprintf(g_Error, sizeof(g_Error), "Unknown native pathfinding error"); }
    return failure;
}
template<class T> std::span<const T> Input(const T* data, int32_t count) {
    if (count < 0 || (!data && count != 0)) throw std::invalid_argument("Invalid input buffer");
    return {data, size_t(count)};
}
Search& Query(void* handle) {
    if (!handle) throw std::invalid_argument("Search handle is null");
    return *static_cast<Search*>(handle);
}
}
extern "C" FUNKE_PACKAGE_API int32_t Funke_Pathfinding_ApiVersion() { return 1; }
extern "C" FUNKE_PACKAGE_API const char* Funke_Pathfinding_LastError() { return g_Error; }
extern "C" FUNKE_PACKAGE_API void* Funke_Pathfinding_Grid(int32_t w, int32_t h, int32_t d,
    int32_t diagonal, const float* costs, int32_t count) {
    return Guard([&]() -> void* { return new MapHandle(Map::Grid(w, h, d, diagonal != 0, Input(costs, count))); }, static_cast<void*>(nullptr));
}
extern "C" FUNKE_PACKAGE_API void* Funke_Pathfinding_Graph(const Point* points, int32_t count,
    const Edge* edges, int32_t edgeCount) {
    return Guard([&]() -> void* { return new MapHandle(Map::Graph(Input(points, count), Input(edges, edgeCount))); }, static_cast<void*>(nullptr));
}
extern "C" FUNKE_PACKAGE_API void Funke_Pathfinding_FreeMap(void* map) { delete static_cast<MapHandle*>(map); }
extern "C" FUNKE_PACKAGE_API void* Funke_Pathfinding_CreateSearch(void* map) {
    return Guard([&]() -> void* {
        if (!map) throw std::invalid_argument("Map handle is null");
        return new Search(*static_cast<MapHandle*>(map));
    }, static_cast<void*>(nullptr));
}
extern "C" FUNKE_PACKAGE_API void Funke_Pathfinding_FreeSearch(void* search) { delete static_cast<Search*>(search); }
extern "C" FUNKE_PACKAGE_API int32_t Funke_Pathfinding_Begin(void* search, int32_t start, int32_t goal, int32_t limit) {
    return Guard([&] { return int32_t(Query(search).Begin(start, goal, limit)); }, int32_t(-1));
}
extern "C" FUNKE_PACKAGE_API int32_t Funke_Pathfinding_Step(void* search, int32_t budget) {
    return Guard([&] { return int32_t(Query(search).Step(budget)); }, int32_t(-1));
}
extern "C" FUNKE_PACKAGE_API int32_t Funke_Pathfinding_Cancel(void* search) {
    return Guard([&] { return int32_t(Query(search).Cancel()); }, int32_t(-1));
}
extern "C" FUNKE_PACKAGE_API int32_t Funke_Pathfinding_Statistics(void* search, Statistics* result) {
    return Guard([&] {
        if (!result) throw std::invalid_argument("Statistics buffer is null");
        *result = Query(search).GetStatistics(); return 0;
    }, int32_t(-1));
}
extern "C" FUNKE_PACKAGE_API int32_t Funke_Pathfinding_CopyPath(void* search, int32_t* result, int32_t capacity) {
    return Guard([&] {
        if (capacity < 0 || (!result && capacity != 0)) throw std::invalid_argument("Invalid path buffer");
        return Query(search).CopyPath({result, size_t(capacity)});
    }, int32_t(-1));
}
