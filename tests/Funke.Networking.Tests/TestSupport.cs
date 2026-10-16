using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Funke.Networking;
using Funke.Networking.Internal;

namespace Funke.Networking.Tests;

internal struct ChatMessage : INetMessage
{
    public string Text;
    public int Number;

    public void Serialize(NetWriter writer)
    {
        writer.Write(Text);
        writer.WritePacked(Number);
    }

    public void Deserialize(NetReader reader)
    {
        Text = reader.ReadString();
        Number = reader.ReadPackedInt();
    }
}

internal struct BlobMessage : INetMessage
{
    public byte[] Data;

    public void Serialize(NetWriter writer) => writer.WriteBytes(Data);
    public void Deserialize(NetReader reader) => Data = reader.ReadBytes();
}

/// <summary>Records what a session reports; nothing here touches the engine.</summary>
internal sealed class FakeHandler : ISessionHandler
{
    public MessageRegistry ServerMessages { get; } = new("NetworkServer");
    public MessageRegistry ClientMessages { get; } = new("NetworkClient");
    public Func<NetConnectRequest, string?>? Approval;
    public readonly List<NetConnection> Joined = new();
    public readonly List<(NetConnection Client, string Reason)> Left = new();
    public bool Connected;
    public string? DisconnectReason;

    public string? Approve(NetConnectRequest request) => Approval?.Invoke(request);
    public void WriteInitialState(NetConnection client) { }
    public void ServerClientConnected(NetConnection client) => Joined.Add(client);
    public void ServerClientDisconnected(NetConnection client, string reason) => Left.Add((client, reason));
    public void ClientConnected() => Connected = true;
    public void ClientDisconnected(string reason) => DisconnectReason = reason;
    public void ServerObjectPacket(NetConnection from, PacketKind kind, NetReader reader) { }
    public void ClientObjectPacket(PacketKind kind, NetReader reader) { }
}

internal static class Net
{
    static Net()
    {
        // Funke.Log needs the native engine.
        NetLog.InfoSink = static _ => { };
        NetLog.WarnSink = static message => Warnings.Add(message);
        NetLog.ErrorSink = static message => Errors.Add(message);
    }

    internal static readonly List<string> Warnings = new();
    internal static readonly List<string> Errors = new();

    internal static void Init() { }

    internal static NetworkSettings Settings(int webSocketPort = 0) => new()
    {
        GameId = "NetTests",
        Port = 0,
        WebSocketPort = webSocketPort,
        ConnectTimeout = 3,
        Timeout = 3,
    };

    internal static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Polls every session until <paramref name="done"/> or the timeout.</summary>
    internal static bool PumpUntil(Func<bool> done, params NetSession[] sessions) => PumpUntil(done, 5, sessions);

    internal static bool PumpUntil(Func<bool> done, double seconds, params NetSession[] sessions)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            foreach (NetSession session in sessions)
            {
                session.Poll();
                session.Flush();
            }
            if (done()) return true;
            Thread.Sleep(2);
        }
        return done();
    }

    internal static void Pump(double seconds, params NetSession[] sessions) => PumpUntil(() => false, seconds, sessions);
}
