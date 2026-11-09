using System;

namespace Funke.Pathfinding;

/// <summary>Reuse per worker or active request. Calls are serialized; independent searches run in parallel.</summary>
public sealed unsafe class PathSearch : IDisposable
{
    private readonly SearchHandle m_Handle;
    private readonly object m_Gate = new();
    private SearchStatus m_Status;
    internal PathSearch(SearchHandle handle) => m_Handle = handle;
    public SearchStatus Status { get { lock (m_Gate) return m_Status; } }
    public SearchStatistics Statistics
    {
        get { lock (m_Gate) { NativeBindings.Check(NativeBindings.Statistics(m_Handle, out var result)); return result; } }
    }
    public SearchStatus Begin(int start, int goal, int expansionLimit = 0)
    {
        lock (m_Gate) return m_Status = (SearchStatus)NativeBindings.Check(NativeBindings.Begin(m_Handle, start, goal, expansionLimit));
    }
    /// <summary>Limits node expansions, not elapsed time; one high-degree graph node may have many edges.</summary>
    public SearchStatus Step(int expansionBudget = 256)
    {
        lock (m_Gate) return m_Status = (SearchStatus)NativeBindings.Check(NativeBindings.Step(m_Handle, expansionBudget));
    }
    public SearchStatus Cancel()
    {
        lock (m_Gate) return m_Status = (SearchStatus)NativeBindings.Check(NativeBindings.Cancel(m_Handle));
    }
    /// <summary>Returns required length; an undersized buffer is left untouched. Only Found has a path.</summary>
    public int CopyPath(Span<int> destination)
    {
        lock (m_Gate) fixed (int* p = destination)
            return NativeBindings.Check(NativeBindings.CopyPath(m_Handle, p, destination.Length));
    }
    public void Dispose() { lock (m_Gate) m_Handle.Dispose(); }
}
