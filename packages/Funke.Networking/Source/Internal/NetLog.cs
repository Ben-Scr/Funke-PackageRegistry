using System;

namespace Funke.Networking.Internal;

/// <summary>Engine log by default; tests swap the sinks because Funke.Log needs the native engine.</summary>
internal static class NetLog
{
    internal static Action<string> InfoSink = static message => Log.Info(message);
    internal static Action<string> WarnSink = static message => Log.Warn(message);
    internal static Action<string> ErrorSink = static message => Log.Error(message);

    internal static void Info(string message) => InfoSink("[Net] " + message);
    internal static void Warn(string message) => WarnSink("[Net] " + message);
    internal static void Error(string message) => ErrorSink("[Net] " + message);
}
