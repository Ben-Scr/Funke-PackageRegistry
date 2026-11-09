# Funke.Pathfinding3D

Ground and flying navigation built on the native `Funke.Pathfinding` A* package. The hot search loop is C++; this managed integration layer prepares maps, converts coordinates, samples Funke physics and supplies waypoint motion. It requires the `Funke.3D` and `Funke.Physics3D` engine modules (use an engine build with the split physics-module support). Version 0.1.0 ships as source through the Funke PackageRegistry.

In the editor's **Package Manager**, refresh the registry, search for **Funke.Pathfinding3D** and install it. The registry declares `Funke.Pathfinding@^0.1.0` as a dependency. For offline installation, install `Funke.Pathfinding` first, then this folder through **+ → Install package from disk**. Both folders can alternatively be copied into a project's `Packages/` directory, with `Funke.Pathfinding3D` added to the existing package list. Keep existing entries. The native dependency builds with the engine's normal package tooling.

## Flying and voxel navigation

```csharp
using Funke;
using Funke.Pathfinding;
using Funke.Pathfinding3D;

var obstacles = new ObstacleBoxes3D(new[] {
    new NavigationBox(new Vector3(7, 0, 0), new Vector3(8, 3, 11))
});
using var volume = FlightVolume.Bake(Vector3.Zero, 1, 16, 8, 16,
    obstacles.CellCost, agentRadius: .2f);
using var search = volume.CreateSearch();
search.Begin(new Vector3(2.5f, 2.5f, 2.5f), new Vector3(13.5f, 5.5f, 13.5f));
```

`FlightVolume.FromCosts` accepts an existing X-fastest 3D array: `x + width * (y + height * z)`. Use a voxel world's occupancy/cost snapshot directly. `Bake` calls a sampler with each cell's center and half-extent. A positive cell must represent an entirely unobstructed volume, not just an empty center point. Zero is blocked; other multipliers are >= 1.

Agent radius conservatively inflates obstacles and map boundaries by `ceil(radius/cellSize)` cells in all three axes. Three sliding-window passes make inflation linear in the cell count rather than cubic in the radius. This may reject narrow but physically passable gaps; use finer cells if needed. Movement is between cell centers with 6 or 26 neighbors and strict blocked-corner prevention.

## Ground navigation

`GroundSurface.FromHeights` consumes one world Y height and one cost per XZ cell, indexed `x + width * z`. It builds a native directed waypoint graph. Origin controls the XZ footprint; supplied heights are absolute world coordinates. Edges connect cardinal neighbors and must satisfy **both** maximum step and maximum slope. This intentionally conservative rule may reject stairs that a specialized character controller can climb.

```csharp
float[] heights = new float[16 * 16];
float[] costs = new float[heights.Length];
System.Array.Fill(costs, 1);
using var ground = GroundSurface.FromHeights(Vector3.Zero, 1, 16, 16,
    heights, costs, maxStep: .4f, maxSlopeDegrees: 45,
    canTraverse: (a, b) => obstacles.CanTraverse(a, b, .25f, 1.8f));
```

Costs must also mark occupied standing cells as blocked; the link callback alone does not classify standing cells. `Examples/NavigationExample.cs` demonstrates both checks. A ground radius adds conservative XZ erosion around blocked cells and the footprint boundary. Edge clearance still has to account for body width/height and obstacles between samples. There is no implicit interpolation over missing ground.

For scene geometry use `PhysicsGroundBaker.Bake`: it casts down using Funke's actual `Physics3D.Raycast`, rejects steep surface normals, checks headroom and invokes a required swept-volume clearance callback for standing poses and links. Run it on the scene thread in Play mode **after the physics world has synchronized**. It queries the active scene, not every additive scene automatically.

```csharp
using var ground = PhysicsGroundBaker.Bake(
    minimum: Vector3.Zero, cellSize: 1, width: 32, depth: 32,
    probeTop: 20, probeBottom: -10, agentHeight: 1.8f,
    clearance: (a, b) => obstacles.CanTraverse(a, b, .25f, 1.8f));
```

The existing Physics3D API supplies closest-hit rays, not a capsule sweep. The required callback prevents treating rays as complete body-clearance evidence. `ObstacleBoxes3D` supplies a ready-to-use conservative swept upright box test against an immutable BVH of world AABBs. Supply bounds containing each obstacle's full geometry and ceilings, but exclude supporting walkable floors from this obstacle set. It does not automatically discover scene colliders; the game owns this snapshot. Rotated/complex obstacles represented by enclosing AABBs can block extra space. Terrain support between grid samples remains the game's sampling contract.

The baker has one surface per XZ column and sees the highest hit in its probe range. For bridges, stacked floors or caves, use separate ground maps, an explicitly connected `PathMap.Graph`, or an appropriate voxel representation. This package is **not a triangle-navmesh baker** and provides no crowd avoidance or moving-platform logic.

## Incremental searches and movement

Keep the map/search alive across frames; call `Step(128)` while Status is Searching. `TryGetNode` performs constant-time cell lookup. Ground lookup rejects points beyond its explicit vertical tolerance; it never silently selects another floor or the nearest walkable cell.

```csharp
int[] scratch = new int[4096];
Vector3[] points = new Vector3[4096];
// After search.Status == SearchStatus.Found:
int count = search.CopyPath(scratch, points);
if (count > points.Length)
    throw new System.InvalidOperationException("Grow both buffers and copy again.");
```

Both buffers must fit; otherwise neither is written. Endpoints are cell centers/ground samples, not the original arbitrary world positions. Validate the short approach from the actual agent position to its start node and the final approach to the exact destination with the character controller.

`PathFollower3D.SetPath` copies waypoints into a reusable array. `NextPosition` returns one segment's next target and does not consume multiple turns in one frame. Apply it through your collision-aware controller. The follower never moves an entity itself, performs no physics and does not guarantee that a map is still current. The example's direct Transform3D movement is for an unparented entity without a dynamic rigidbody; a parented Transform3D.Position is local, not world space.

On obstacle changes replace the map and cancel or discard requests on the old snapshot. On scene teardown dispose searches and maps. Queue many agents through a small pool of searches instead of allocating map-sized scratch per agent. Ground sampling/BVH construction are preprocessing operations; frame-budgeted A* does not make synchronous map baking frame-budgeted.

## Example and verification

Copy `Examples/NavigationExample.cs` to the game's scripts and attach it to an unparented entity with Transform3D. Set Flying to choose the mode. The example defines an obstacle snapshot; author matching visual/collider geometry in the scene. It builds one map, searches incrementally and releases native resources in OnDestroy.

Native and C# tests cover optimal paths, world-coordinate mapping, ground cliffs, directed clearance, inflation against brute force, BVH queries against brute force, no physics calls during searches, lifetimes and parallel calls. Ground-ray tests inject deterministic geometry; they verify adapter logic but do not substitute for a live Jolt scene/device acceptance run. See `../../docs/Pathfinding-Validation.md` in the registry checkout.
