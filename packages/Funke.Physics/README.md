# Funke.Physics

Lightweight 2D physics for scenes with many simple bodies: bullets, particles with collision, crowds, pickups, falling
blocks. Bodies have a box or circle collider, no rotation, and are resolved by a uniform-grid broadphase and a simple
impulse solver. It runs next to Box2D in the same scene; use `Rigidbody2D` (Box2D) when you need rotation, joints,
polygons or continuous collision.

Until engine 2026.1 these components were built into the engine. They now live in this package together with the
Funke-Physics library (formerly Index-Physics), so projects that don't need them don't ship them.

## Install

Package Manager → **Funke.Physics** → Install, or `funke package add Funke.Physics`. The package enables the
`Funke.2D` module. Scenes saved with the old built-in components load unchanged: the serialized names
(`FastBody2D`, `FastBoxCollider2D`, `FastCircleCollider2D`) and fields are the same.

## Components

| Component | Fields |
|---|---|
| **Fast Body 2D** | Type (Static, Dynamic, Kinematic), Mass, Use Gravity, Boundary Check |
| **Fast Box Collider 2D** | Half Extents (multiplied by Transform 2D scale) |
| **Fast Circle Collider 2D** | Radius (multiplied by the larger of \|scale.x\| and \|scale.y\|) |

A body uses one collider. With both collider components on an entity the one added first collides; the other takes
over when the first is removed. Colliders without a Fast Body 2D are not simulated and never hit by queries.

The world steps together with Box2D at the physics fixed step and while 2D physics is enabled. Gravity is
(0, -9.81); Boundary Check keeps a body inside ±1000 units. Collision events reach scripts through the usual
`OnCollisionEnter2D` / `OnCollisionStay2D` / `OnCollisionExit2D` callbacks.

## Scripting

```csharp
using Funke;
using Funke.Physics;

public class Shooter : EntityScript
{
    public override void OnUpdate()
    {
        FastBody2D body = Entity.GetComponent<FastBody2D>()!;
        body.Velocity = new Vector2(4f, body.Velocity.Y);

        Vector2 position = Transform!.Position;
        RaycastHit2D hit = FastPhysics2D.Raycast(position, Vector2.Right, 10f);
        if (hit.Hit)
            Log.Info($"hit {hit.Entity} at {hit.Point}");

        foreach (Entity e in FastPhysics2D.OverlapCircleAll(position, 2f))
            if (e != Entity) e.Destroy();
    }
}
```

`FastPhysics2D` mirrors `Physics2D`: `Raycast`, `OverlapCircle`, `OverlapBox` (size is the full width/height),
`OverlapPolygon` (convex, up to `MaxPolygonVertices` x,y pairs relative to the origin) and `ContainsPoint`, each with
`*Check` and `*All` variants. Queries run on the main thread only.

## Performance

Measured with the library benchmark (`Library/bench`, Release, one thread), old library vs. this version:

| Scenario | Before | Now |
|---|---|---|
| Pile of 1 000 boxes, per step | 2.13 ms | 0.12 ms |
| Pile of 4 000 boxes, per step | 13.9 ms | 0.58 ms |
| 20 000 sparse bodies, per step | 5.46 ms | 0.74 ms |
| Raycast among 20 000 bodies | 274 µs | 0.62 µs |
| Overlap circle among 20 000 bodies | 257 µs | 0.22 µs |
| Create and destroy 20 000 bodies | 128 ms | 0.55 ms |

Contacts and query results are identical to the old library.

## Library

`Library/` is the engine-independent C++20 library (`FunkePhys` namespace) with its own CMake build, tests and
benchmark:

```bash
cmake -S Library -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
ctest --test-dir build -C Release
```

## License

MIT, see [LICENSE.txt](LICENSE.txt).
