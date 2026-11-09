using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Funke.Pathfinding;

internal static unsafe class NativeBindings
{
    private const string Library = "Pkg.Funke.Pathfinding.Native";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_ApiVersion")]
    internal static extern int ApiVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_LastError")]
    private static extern IntPtr LastError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_Grid")]
    internal static extern IntPtr Grid(int w, int h, int d, int diagonal, float* costs, int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_Graph")]
    internal static extern IntPtr Graph(Vector3* points, int count, PathEdge* edges, int edgeCount);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_FreeMap")]
    internal static extern void FreeMap(IntPtr map);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_CreateSearch")]
    internal static extern IntPtr CreateSearch(MapHandle map);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_FreeSearch")]
    internal static extern void FreeSearch(IntPtr search);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_Begin")]
    internal static extern int Begin(SearchHandle search, int start, int goal, int limit);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_Step")]
    internal static extern int Step(SearchHandle search, int budget);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_Cancel")]
    internal static extern int Cancel(SearchHandle search);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_Statistics")]
    internal static extern int Statistics(SearchHandle search, out SearchStatistics result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Funke_Pathfinding_CopyPath")]
    internal static extern int CopyPath(SearchHandle search, int* buffer, int capacity);
    internal static Exception Error() => new InvalidOperationException(Marshal.PtrToStringUTF8(LastError()) ?? "Native pathfinding failed.");
    internal static int Check(int result) { if (result < 0) throw Error(); return result; }
    internal static IntPtr Check(IntPtr result) { if (result == IntPtr.Zero) throw Error(); return result; }
    internal static void VerifyVersion()
    {
        if (ApiVersion() != 1) throw new InvalidOperationException("Pathfinding native/managed API versions differ.");
    }
}
internal sealed class MapHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal MapHandle(IntPtr value) : base(true) => SetHandle(value);
    protected override bool ReleaseHandle() { NativeBindings.FreeMap(handle); return true; }
}
internal sealed class SearchHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SearchHandle(IntPtr value) : base(true) => SetHandle(value);
    protected override bool ReleaseHandle() { NativeBindings.FreeSearch(handle); return true; }
}
