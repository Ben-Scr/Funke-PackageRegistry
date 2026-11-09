# Pathfinding packages — validation, 9 November 2026

Scope: the source releases of `Funke.Pathfinding` 0.1.0 and `Funke.Pathfinding3D` 0.1.0. The existing engine and unrelated package changes were preserved. Publishing updates installation documentation and archive checksums; the tested implementation is unchanged.

## Reproduce

On a Windows engine development machine with the existing Release ScriptCore available:

```powershell
./scripts/test-pathfinding.ps1 -EngineRoot ../Index
```

The script builds only these native/managed packages and their tests. It preserves build caches, uses a dedicated fixture, checks the ordinary Funke package manifests/dependency build and static export scanner, then runs the integration suite against the generated package DLLs. Default output is `dist/pathfinding-validation`; another output root must be new or carry the script's ownership marker. No engine rebuild, branch, commit, upload or registry edit is performed.

For native tests alone, use `packages/Funke.Pathfinding/Tests/CMakeLists.txt`. The same suite was also run under WSL Ubuntu with GCC 15.2, `-fsanitize=address,undefined -fno-omit-frame-pointer`, and no sanitizer findings. These are native-library tests, not a complete Linux engine acceptance run.

## Checks

* 390 deterministic randomized weighted-grid/directed-graph comparisons against an independent Dijkstra implementation; full copied paths are checked for real edges and matching cost.
* Exhaustive endpoint pairs in a small entirely open 3D grid, in both axis and diagonal modes, validate the open-grid optimization.
* Corner obstruction, disconnected/blocked endpoints, start=goal, invalid input, buffer bounds, cancellation, expansion limit, restart reuse, zero-cost graph shortcuts and map ownership.
* 8,000 native queries across eight concurrent searches sharing one map.
* Managed tests call the actual native DLL, check interop layouts, disposed-map/query behavior, world mapping, cliffs, directional traversal, mock ground-ray preparation and absence of physics callbacks during search.
* Clearance inflation is compared with a brute-force volume check; the obstacle BVH is compared with a linear AABB reference on 1,000 queries.
* 800 parallel managed-to-native queries; 100 warmed repeated searches and output copies allocate zero managed bytes on the measured thread.
* All 12 native entry points referenced by C# are found by the engine's static-package scanner. The native package emits a `standalone_c` ABI sidecar through the ordinary package build.

Assertion counts are not code coverage. The direct-source managed suite and package-assembly suite exercise the same checks and must not be counted as independent test cases.

## Performance

Windows 11 build 26200, AMD Ryzen 9 7900X (24 logical processors), MSVC 19.44.35228, x64 Release. Native CPU-only search; no rendering or physics. Fixed seed 20261109, 20 warmups followed by 200 measured endpoint pairs per workload, three complete repetitions. Grid creation and search-scratch initialization are excluded. Each measurement includes Begin and Step calls until completion, with a 512-expansion call budget; output-path copying is excluded. All 200 requests found paths in every measured workload. Raw samples retain slow runs.

Initial retained measurement ranges (three repetitions):

| Workload | Cells | Mean per query | P95 per query |
|---|---:|---:|---:|
| Open 2D, unit cost | 65,536 | 0.0033–0.0063 ms | 0.0058–0.0142 ms |
| 2D, about 10% blocked | 65,536 | 0.410–0.432 ms | 1.294–1.338 ms |
| 3D, about 10% blocked | 131,072 | 0.191–0.198 ms | 0.654–0.783 ms |

The open-map optimization is used only after construction proves every cell has cost 1. It advances a mathematically optimal monotone path within the same expansion budget. Other maps retain ordinary A*. It is not a claim that arbitrary obstacle/weighted workloads run at open-map speed.

Ordinary native allocation instrumentation counted zero allocations during all measured searches. Search scratch is approximately 36 bytes/node/search on this build; 100 simultaneous million-node search contexts would therefore be inappropriate. Reuse a bounded search pool. A node budget is not a time budget, and one high-degree graph node can take longer than a grid node.

The initial samples are retained under the engine checkout's `artifacts/pathfinding/{benchmark-1,benchmark-2,benchmark-3,raw-1,raw-2,raw-3}.csv`. The reproducible final runner stores a separate set under its output root. These synthetic workloads are not a game-frame-rate, many-agent crowd, navmesh, mobile-device or worst-case latency guarantee.

The last full validation run was slower: 2D-obstacle means 0.722–0.896 ms (P95 2.024–2.734 ms; maximum 18.757 ms), and 3D-obstacle means 0.278–0.324 ms (P95 0.964–1.413 ms; maximum 4.436 ms). Its raw samples are retained in `artifacts/pathfinding/final`; no slow observations were removed. The differing runs show workstation timing variation and do not justify a fixed latency guarantee or regression threshold.

## Acceptance boundaries

A supplemental Windows runtime probe loaded the native package through PackageHost, sampled an actual Jolt floor after 30 updates, and found both paths: `PATHFINDING_RUNTIME_OK ground=23 flight=13`. The process exited normally. Its project, script and screenshot are retained in the engine checkout under `artifacts/pathfinding/PathfindingRuntime` and `artifacts/pathfinding/final/runtime-probe.*`. The source fixture uses real Physics3D rays; its clearance callback is unrestricted because the ground fixture contains only an unobstructed supporting floor. The flight case uses an explicit obstacle-box snapshot.

This is a successful functional probe, not a clean full runtime qualification: the pre-existing Runtime EXE reports a header fingerprint different from its Engine DLL. The first attempt additionally encountered missing Physics3D activation and a different copied ScriptCore MVID. The package now explicitly requires both `Funke.3D` and `Funke.Physics3D`; the private fixture was recompiled against the Runtime's own ScriptCore copy. No compatibility guard was disabled and no existing engine binary was replaced. The remaining native header mismatch is retained in the final log. A fully synchronized engine/runtime build is required before release qualification.

No full Editor workflow, exported-game, macOS, Android, iOS or browser runtime acceptance is claimed. Ground scenes must be baked after physics synchronization and checked against the intended character controller. Unit geometry tests also retain their injected-ray coverage independently of this runtime probe.

Ground navigation is a single-height XZ surface with cardinal links, step/slope limits and explicit body-clearance validation. Flight navigation is a volume grid. General navmesh baking, stacked-surface extraction, crowd avoidance and automatic moving-obstacle tracking are outside this first version. Source ZIPs include the manifest and documentation and are distributed as GitHub Release assets through the public registry. The editor builds them after installation; no prebuilt engine binaries are included.
