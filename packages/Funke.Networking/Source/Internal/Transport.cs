using System;

namespace Funke.Networking.Internal;

/// <summary>Receives a transport's events. Every call happens inside the transport's Poll, on the main thread.</summary>
internal interface ITransportListener
{
    void OnTransportConnected(int connectionId, string address, NetTransportKind kind);
    void OnTransportData(int connectionId, ReadOnlySpan<byte> data);
    void OnTransportDisconnected(int connectionId, string reason);
}

/// <summary>Accepts connections of one kind (UDP or WebSocket). Connection ids come from the session and are unique across transports.</summary>
internal interface IServerTransport : IDisposable
{
    NetTransportKind Kind { get; }
    void Start();
    void Poll(float deltaMilliseconds);
    /// <summary>Puts everything queued since the last Poll on the wire.</summary>
    void Flush();
    void Send(int connectionId, ReadOnlySpan<byte> data, NetChannel channel);
    void Disconnect(int connectionId, string reason);
    int GetRoundTripMs(int connectionId);
}

/// <summary>One outgoing connection. Reports the server as connection id 0.</summary>
internal interface IClientTransport : IDisposable
{
    NetTransportKind Kind { get; }
    void Connect();
    void Poll(float deltaMilliseconds);
    void Flush();
    void Send(ReadOnlySpan<byte> data, NetChannel channel);
    /// <summary><paramref name="blocking"/>: the process is about to exit, wait briefly until the goodbye is out.</summary>
    void Disconnect(string reason, bool blocking);
    int RoundTripMs { get; }
}

internal static class TransportReasons
{
    internal const string Closed = "Connection closed";
    internal const string TimedOut = "Connection timed out";
}
