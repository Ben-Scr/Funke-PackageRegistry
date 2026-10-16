using System.Diagnostics;

namespace Funke.Networking.Internal;

/// <summary>Seconds since the process started; independent of Time.TimeScale and pauses, precise for days.</summary>
internal static class NetClock
{
    private static readonly Stopwatch s_Watch = Stopwatch.StartNew();

    internal static double Now => s_Watch.Elapsed.TotalSeconds;
}
