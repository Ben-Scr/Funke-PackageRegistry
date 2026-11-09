using System;
using Funke.Pathfinding;

namespace Funke.Pathfinding3D.Examples;

// Attach to an unparented entity with Transform3D and no dynamic Rigidbody3D.
public sealed class NavigationExample : EntityScript
{
    public bool Flying = false;
    public float Speed = 3;
    private NavigationMap3D? m_Map;
    private NavigationSearch3D? m_Search;
    private Transform3D? m_Transform;
    private readonly PathFollower3D m_Follower = new();
    private readonly int[] m_NodeBuffer = new int[4096];
    private readonly Vector3[] m_PointBuffer = new Vector3[4096];

    public override void OnStart()
    {
        m_Transform = GetComponent<Transform3D>();
        if (m_Transform == null || Entity.Parent != null) throw new InvalidOperationException("Example requires an unparented Transform3D.");
        var obstacles = new ObstacleBoxes3D(new[] {
            new NavigationBox(new Vector3(7, 0, 0), new Vector3(8, 3, 11))
        });
        if (Flying)
            m_Map = FlightVolume.Bake(Vector3.Zero, 1, 16, 8, 16, obstacles.CellCost, agentRadius: .2f);
        else
        {
            var heights = new float[16 * 16];
            var costs = new float[heights.Length];
            for (int z = 0; z < 16; ++z) for (int x = 0; x < 16; ++x)
            {
                var feet = new Vector3(x + .5f, 0, z + .5f);
                costs[x + 16 * z] = obstacles.CanTraverse(feet, feet, .25f, 1.8f) ? 1 : 0;
            }
            m_Map = GroundSurface.FromHeights(Vector3.Zero, 1, 16, 16, heights, costs,
                canTraverse: (a, b) => obstacles.CanTraverse(a, b, .25f, 1.8f));
        }
        var start = new Vector3(2.5f, Flying ? 2.5f : 0, 2.5f);
        var goal = new Vector3(13.5f, Flying ? 5.5f : 0, 13.5f);
        m_Transform.Position = start;
        m_Search = m_Map.CreateSearch();
        m_Search.Begin(start, goal, expansionLimit: 20000);
    }
    public override void OnUpdate()
    {
        if (m_Search == null || m_Transform == null) return;
        if (m_Search.Status == SearchStatus.Searching && m_Search.Step(128) == SearchStatus.Found)
        {
            int count = m_Search.CopyPath(m_NodeBuffer, m_PointBuffer);
            if (count > m_PointBuffer.Length) throw new InvalidOperationException("Increase example path buffers.");
            m_Follower.SetPath(m_PointBuffer.AsSpan(0, count));
        }
        m_Transform.Position = m_Follower.NextPosition(m_Transform.Position, Speed, Time.DeltaTime);
    }
    public override void OnDestroy()
    {
        m_Search?.Dispose(); m_Map?.Dispose();
        m_Search = null; m_Map = null; m_Follower.Clear();
    }
}
