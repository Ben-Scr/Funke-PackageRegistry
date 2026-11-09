using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Funke;
using Funke.Pathfinding;
using Funke.Pathfinding3D;

internal static class Program
{
    private static int s_Checks;
    private static void Check(bool value, string message) { ++s_Checks; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Check(rejected, "Expected " + typeof(T).Name);
    }
    private static SearchStatus Finish(PathSearch query) { while (query.Status == SearchStatus.Searching) query.Step(3); return query.Status; }
    private static SearchStatus Finish(NavigationSearch3D query) { while (query.Status == SearchStatus.Searching) query.Step(3); return query.Status; }
    private static int Main(string[] args)
    {
        try
        {
            IntPtr library = NativeLibrary.Load(args[0]);
            NativeLibrary.SetDllImportResolver(typeof(PathMap).Assembly, (name, _, _) => name == "Pkg.Funke.Pathfinding.Native" ? library : IntPtr.Zero);
            Check(Marshal.SizeOf<Vector3>() == 12 && Marshal.SizeOf<PathEdge>() == 12 && Marshal.SizeOf<SearchStatistics>() == 24, "ABI layout");
            float[] costs = new float[100]; Array.Fill(costs, 1);
            using var map = PathMap.Grid2D(10, 10, costs);
            using var query = map.CreateSearch();
            query.Begin(0, 99); Check(Finish(query) == SearchStatus.Found, "P/Invoke grid search");
            int[] nodes = new int[100];
            int count = query.CopyPath(nodes);
            Check(count == 10 && nodes[0] == 0 && nodes[count - 1] == 99, "Copied native path");
            Check(Math.Abs(query.Statistics.Cost - 9 * Math.Sqrt(2)) < 1e-9, "Native statistics");
            Span<int> tiny = stackalloc int[1]; tiny[0] = -7;
            Check(query.CopyPath(tiny) == 10 && tiny[0] == -7, "Short path buffer");
            query.Begin(0, 99, 1); Check(Finish(query) == SearchStatus.LimitReached, "Limit status");
            query.Begin(0, 99); Check(query.Cancel() == SearchStatus.Cancelled, "Cancellation");
            Reject<InvalidOperationException>(() => query.Begin(-1, 99));
            Reject<InvalidOperationException>(() => query.Step(0));
            Reject<InvalidOperationException>(() => PathMap.Grid(3, 4, 5, costs));
            costs[0] = float.NaN; Reject<InvalidOperationException>(() => PathMap.Grid2D(10, 10, costs)); costs[0] = 1;
            map.Dispose(); query.Begin(0, 99); Check(Finish(query) == SearchStatus.Found, "Query retains disposed map");
            Reject<ObjectDisposedException>(() => map.CreateSearch());
            query.Dispose(); Reject<ObjectDisposedException>(() => query.Step());

            using var graph = PathMap.Graph(new[] { Vector3.Zero, new Vector3(100, 0, 0), new Vector3(1, 0, 0) },
                new[] { new PathEdge(0, 1, 0), new PathEdge(1, 2, 1), new PathEdge(0, 2, 10) });
            using var graphQuery = graph.CreateSearch(); graphQuery.Begin(0, 2);
            Check(Finish(graphQuery) == SearchStatus.Found && graphQuery.Statistics.Cost == 1, "Zero-cost shortcut heuristic");

            using var flight = FlightVolume.FromCosts(Vector3.Zero, 2, 5, 4, 5, costs);
            Check(flight.TryGetNode(new Vector3(1, 1, 1), out int start) && start == 0, "World mapping");
            Check(!flight.TryGetNode(new Vector3(-.1f, 1, 1), out _), "Negative coordinate mapping");
            using var flying = flight.CreateSearch(); flying.Begin(0, 99);
            Check(Finish(flying) == SearchStatus.Found, "3D path");
            Vector3[] points = new Vector3[100]; count = flying.CopyPath(nodes, points);
            Check(points[0] == new Vector3(1, 1, 1) && points[count - 1] == new Vector3(9, 7, 9), "World path endpoints");
            using var inflated = FlightVolume.FromCosts(Vector3.Zero, 1, 5, 4, 5, costs, .1f);
            Check(!inflated.IsWalkable(0) && inflated.IsWalkable(1 + 5 * (1 + 4)), "Volume boundary inflation");
            CheckClearance();
            CheckBoxes();

            float[] heights = new float[25]; float[] groundCosts = new float[25]; Array.Fill(groundCosts, 1);
            for (int z = 0; z < 5; ++z) heights[2 + z * 5] = 3;
            using var cliff = GroundSurface.FromHeights(Vector3.Zero, 1, 5, 5, heights, groundCosts);
            using var walking = cliff.CreateSearch(); walking.Begin(0, 4);
            Check(Finish(walking) == SearchStatus.NoPath, "Cliff crossing");
            heights[2 + 2 * 5] = 0;
            using var ground = GroundSurface.FromHeights(Vector3.Zero, 1, 5, 5, heights, groundCosts);
            using var groundQuery = ground.CreateSearch(); groundQuery.Begin(0, 4);
            Check(Finish(groundQuery) == SearchStatus.Found, "Ground detour");
            count = groundQuery.CopyPath(nodes, points);
            Check(count > 5, "Ground route ignored cliff");
            Check(!ground.TryGetNode(new Vector3(.5f, 100, .5f), out _), "Wrong floor accepted");
            Reject<ArgumentException>(() => groundQuery.Begin(new Vector3(.5f, 100, .5f), Vector3.Zero));

            int rays = 0, sweeps = 0;
            bool Ray(Vector3 origin, Vector3 direction, float distance, out GroundHit hit)
            {
                ++rays; hit = new GroundHit(new Vector3(origin.X, 0, origin.Z), Vector3.Up);
                return direction.Y < 0;
            }
            using var baked = PhysicsGroundBaker.Bake(Vector3.Zero, 1, 5, 5, 10, -10, 1.8f,
                (a, b) => { ++sweeps; return !(a.X < 2 && b.X > 2); }, raycast: Ray);
            Check(rays == 50 && sweeps > 25, "Bake geometry integration");
            using var bakeQuery = baked.CreateSearch(); bakeQuery.Begin(0, 4);
            Check(Finish(bakeQuery) == SearchStatus.NoPath, "Swept clearance ignored");
            bakeQuery.Begin(4, 0); Check(Finish(bakeQuery) == SearchStatus.Found, "Directional clearance ignored");
            Check(rays == 50, "Search called physics");

            var follower = new PathFollower3D();
            follower.SetPath(new[] {Vector3.Zero, new Vector3(1, 0, 0), new Vector3(1, 0, 1)});
            Check(follower.NextPosition(Vector3.Zero, 100, 1) == new Vector3(1, 0, 0), "Follower skipped turn");
            Check(follower.NextPosition(new Vector3(1, 0, 0), 100, 1) == new Vector3(1, 0, 1), "Follower next segment");
            follower.NextPosition(new Vector3(1, 0, 1), 1, 1); Check(follower.IsFinished, "Follower arrival");
            Reject<ArgumentException>(() => follower.SetPath(new[] {new Vector3(float.NaN)}));
            Parallel.For(0, 8, _ => {
                using var local = flight.CreateSearch();
                for (int i = 0; i < 100; ++i) { local.Begin(0, 99); if (Finish(local) != SearchStatus.Found) throw new Exception("Parallel interop"); }
            });
            using var reuse = flight.CreateSearch(); reuse.Begin(0, 99); Finish(reuse); reuse.CopyPath(nodes, points);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; ++i) { reuse.Begin(0, 99); Finish(reuse); reuse.CopyPath(nodes, points); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "Managed query allocations: " + allocated);
            Console.WriteLine($"Passed {s_Checks} managed/integration checks; 800 parallel native queries; reuse allocated {allocated} managed bytes.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void CheckClearance()
    {
        var random = new System.Random(911);
        foreach (bool ground in new[] { false, true })
        for (int run = 0; run < 20; ++run)
        {
            int w = 7, h = ground ? 1 : 5, d = 6, r = 1 + run % 2;
            float[] costs = new float[w * h * d];
            for (int i = 0; i < costs.Length; ++i) costs[i] = random.Next(9) == 0 ? 0 : 1;
            using var map = ground ? GroundSurface.FromHeights(Vector3.Zero, 1, w, d, new float[costs.Length], costs, agentRadius: r)
                : FlightVolume.FromCosts(Vector3.Zero, 1, w, h, d, costs, r);
            for (int z = 0; z < d; ++z) for (int y = 0; y < h; ++y) for (int x = 0; x < w; ++x)
            {
                bool clear = true;
                for (int dz = -r; dz <= r; ++dz) for (int dy = ground ? 0 : -r; dy <= (ground ? 0 : r); ++dy)
                for (int dx = -r; dx <= r; ++dx)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d || costs[nx + w * (ny + h * nz)] == 0) clear = false;
                }
                Check(map.IsWalkable(x + w * (y + h * z)) == clear, "Clearance differs from brute force");
            }
        }
    }
    private static void CheckBoxes()
    {
        var random = new System.Random(37);
        var boxes = new NavigationBox[100];
        for (int i = 0; i < boxes.Length; ++i)
        {
            var min = new Vector3(random.Next(30), random.Next(30), random.Next(30));
            boxes[i] = new NavigationBox(min, min + new Vector3(1 + random.Next(4)));
        }
        var field = new ObstacleBoxes3D(boxes);
        for (int i = 0; i < 1000; ++i)
        {
            var min = new Vector3(random.Next(30), random.Next(30), random.Next(30));
            var volume = new NavigationBox(min, min + new Vector3(2));
            bool free = true;
            foreach (var box in boxes)
                if (box.Min.X <= volume.Max.X && box.Max.X >= volume.Min.X && box.Min.Y <= volume.Max.Y
                    && box.Max.Y >= volume.Min.Y && box.Min.Z <= volume.Max.Z && box.Max.Z >= volume.Min.Z) free = false;
            Check(field.IsFree(volume) == free, "BVH differs from brute force");
        }
        var wall = new ObstacleBoxes3D(new[] {new NavigationBox(new Vector3(2, 0, -10), new Vector3(3, 3, 10))});
        Check(!wall.CanTraverse(Vector3.Zero, new Vector3(5, 0, 0), .2f, 1.8f), "Swept body crosses wall");
        Check(wall.CanTraverse(new Vector3(0, 4, 0), new Vector3(5, 4, 0), .2f, 1.8f), "Clear body rejected");
        Check(wall.CellCost(new Vector3(2.5f, 1, 0), .5f) == 0, "Flight obstacle missed");
        var empty = new ObstacleBoxes3D(Array.Empty<NavigationBox>());
        Check(empty.CellCost(Vector3.Zero, 1) == 1, "Empty obstacle field");
        Reject<ArgumentException>(() => new NavigationBox(Vector3.One, Vector3.Zero));
    }
}
