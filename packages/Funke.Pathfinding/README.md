# Funke.Pathfinding

Native C++20 A* with a C# API for weighted 2D/3D grids and directed waypoint graphs. Version 0.1.0 ships as source through the Funke PackageRegistry. The native layer is `native_standalone`: it does not link the engine, renderer, physics or managed runtime. `Funke.Pathfinding3D` adds world coordinates, ground/flight map preparation and waypoint movement.

## Install

In the editor's **Package Manager**, refresh the registry, search for **Funke.Pathfinding** and install it. For an offline installation, use **+ → Install package from disk**, selecting this folder (extract a ZIP first). Or copy this folder into `YourProject/Packages/Funke.Pathfinding`, add `Funke.Pathfinding` to the existing `project.json` package list, and reopen the project. The editor generates/builds the native and C# package layers. Native compilation needs the platform's C++20 toolchain; C# uses .NET 10. No optional engine module is needed for the base package.

For offline installation, install this package before installing `Funke.Pathfinding3D`. Registry installation includes this dependency automatically in editors with dependency resolution support.

## C# quick start

```csharp
using System;
using Funke.Pathfinding;

float[] costs = new float[64 * 64];
Array.Fill(costs, 1);
costs[10 + 64 * 10] = 0;
using var map = PathMap.Grid2D(64, 64, costs);
using var search = map.CreateSearch();
search.Begin(0, 4095, expansionLimit: 20000);

// In a game, call Step once per update and retain the search between frames.
while (search.Status == SearchStatus.Searching)
    search.Step(expansionBudget: 128);

if (search.Status == SearchStatus.Found)
{
    int[] path = new int[search.Statistics.PathLength];
    search.CopyPath(path);
    // Reuse this buffer for subsequent requests.
}
```

`Begin` restarts the same search and reuses its storage. `Cancel` stops a pending request. `NoPath`, `LimitReached` and `Cancelled` are distinct outcomes and never return a partial path. A zero total expansion limit means unlimited; a Step budget must be positive. A start equal to the goal succeeds only when that cell is walkable. Invalid input throws without replacing a previously active search.

`CopyPath` returns the required number of node IDs. If the buffer is too small it writes nothing; only `Found` has a path. Endpoints are included. Statistics contain expanded/visited node counts, maximum open set, path length and total cost. Inspect Status before interpreting Cost.

## Maps and correctness

* Grid storage is `x + width * (y + height * z)`. Use depth 1 for XY grids. Zero cost blocks a cell; any other cost must be finite and at least 1.
* Axis mode has 4 neighbors in 2D and 6 in 3D. Diagonal mode has 8/26. Every intervening face/edge cell must be free; paths cannot cut blocked corners.
* Moving into a cell costs its multiplier times 1, sqrt(2) or sqrt(3). The start's cost is not charged. Grid cost is in cell units; scale by cell size for world distance on uniform-cost maps.
* `PathMap.Graph(positions, edges)` copies a directed graph into compact adjacency arrays. Add reverse edges explicitly for bidirectional travel. Nonnegative finite edge costs, duplicate edges and zero-cost teleport links are valid.
* Graph heuristics use Euclidean distance scaled by the minimum edge-cost/distance ratio. Cheap/zero-cost shortcuts cannot make the heuristic overestimate; a zero scale becomes Dijkstra. Grid heuristics use Manhattan or generalized octile distance. Results are shortest paths in the supplied discrete map, subject to floating-point arithmetic, not continuous-world optimal paths.
* Entirely open, unit-cost grids use a proven optimal monotone route while still honoring incremental budgets. Weighted/obstructed maps use the general A* heap.
* Maps are immutable snapshots. Build a replacement when terrain changes. Old searches deliberately finish on their old snapshot; the game must cancel/discard them if that snapshot is no longer acceptable. Input spans are copied during creation.

The maximum map size is 8,388,608 nodes. Map creation validates dimensions, costs, positions and edge indices. Explicit grids store approximately 4 bytes per node in native memory; search scratch is approximately **36 bytes per node per search** on the tested 64-bit build. Graphs additionally store positions, offsets and edges. Initialization is O(nodes + edges), including search scratch initialization. A grid search does not allocate an explicit adjacency graph.

## Many agents and threads

Share one map and reuse a small bounded pool of searches. Do not allocate one large search per entity when only a few requests run at once. Each search owns an indexed binary heap and stamped records: ordinary restarts do not clear all nodes, and expansion does not allocate. Epoch wraparound performs one full stamp reset after about four billion starts.

Different searches can run concurrently against one map, including on the engine job system or a caller-owned worker pool. Give each active request exclusive ownership of its search. Managed calls serialize access to one search, but they do not make interleaved Begin/Step calls from different requests meaningful. Cancellation takes effect between Step calls. No native worker pool is spawned by this package.

Budget expansions across all active requests, not independently per agent without a global cap. A budget limits node expansions, **not milliseconds**: expanding a high-degree waypoint node may inspect many edges. `CopyPath` costs O(path length). Map preparation, scratch creation and final output-buffer growth should happen outside latency-sensitive updates.

Dispose searches/maps in the owning scene/script's teardown, including before native package unloading. SafeHandle guards managed/native lifetimes; existing searches retain a shared native map even after its managed map wrapper is disposed. There is no global scene cache or static subscription that survives Play mode.

## Native C++ and portability

`include/Pathfinding.hpp` exposes `Map` and `Search` for native callers compiling/linking `src/Pathfinding.cpp` into their own target. The package DLL's cross-language ABI is the prefixed C interface in `src/PInvoke.cpp`; no STL types cross that boundary. The C# wrapper checks API version 1; future ABI changes must update both sides. Exceptions are caught before returning through P/Invoke.

All 12 C# entry points carry `FUNKE_PACKAGE_API` in compiled source. The Funke static-package scanner recognizes them and the initialization export. This is build-contract coverage, not proof of Android, iOS or Web execution. Threadless Web can use incremental Step calls on its main thread.

## Validation

Native tests compare randomized weighted grids and directed graphs with an independent Dijkstra implementation, exhaustively check a small open 3D grid, validate corner rules, reuse, buffers, limits, invalid input, shared lifetimes and parallel searches.

```sh
cmake -S Tests -B /path/to/pathfinding-build -DCMAKE_BUILD_TYPE=Release
cmake --build /path/to/pathfinding-build --config Release
ctest --test-dir /path/to/pathfinding-build -C Release --output-on-failure
```

On Windows use `-G "Visual Studio 17 2022" -A x64` when selecting the matching Funke toolchain. `PathfindingBenchmark [raw.csv]` runs deterministic workloads with 20 warmups and 200 measured requests per workload; stdout is a summary CSV. It checks ordinary native allocation calls during search, independently of map construction. Performance depends on topology, query distance and hardware.

The registry's `tests/Funke.Pathfinding.Tests` console suite compiles both packages against the actual ScriptCore and loads the real native DLL. `tests/Funke.Pathfinding.PackageTests` runs the same tests against assemblies built by Funke's generated package workspace. See `../../docs/Pathfinding-Validation.md` in the registry checkout for measured results and commands.
