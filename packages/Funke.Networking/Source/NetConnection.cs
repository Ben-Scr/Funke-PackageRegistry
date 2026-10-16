using Funke.Networking.Internal;

namespace Funke.Networking;

/// <summary>
/// On the server: one client (the host's own client is <see cref="NetworkManager.ServerClientId"/> with <see cref="IsLocal"/>).
/// On a client: the server.
/// </summary>
public sealed class NetConnection
{
    private readonly NetSession m_Session;

    internal NetConnection(NetSession session, int id, string address, NetTransportKind transport, IServerTransport? serverTransport)
    {
        m_Session = session;
        Id = id;
        Address = address;
        Transport = transport;
        ServerTransport = serverTransport;
        Started = NetClock.Now;
    }

    /// <summary>The client id (on a client, the server's connection has id 0).</summary>
    public int Id { get; }
    public string Address { get; }
    public NetTransportKind Transport { get; }
    public bool IsLocal => Transport == NetTransportKind.Local;
    /// <summary>True once the handshake is done; false again after it closed.</summary>
    public bool IsConnected { get; internal set; }
    /// <summary>What the client passed to <see cref="NetworkManager.StartClient"/> (server side only).</summary>
    public string ConnectData { get; internal set; } = "";
    public int RoundTripMs => m_Session.GetRoundTripMs(this);
    /// <summary>Free slot for the game (player object, team, ...).</summary>
    public object? UserData { get; set; }

    internal IServerTransport? ServerTransport { get; }
    /// <summary>This object stands for the server (client side).</summary>
    internal bool IsServerSide { get; init; }
    internal double Started { get; }
    internal bool IsPending => !IsConnected && !Closed;
    internal bool Closed { get; set; }

    /// <summary>Sends to this connection: from the server to a client, or from a client to the server.</summary>
    public void Send<T>(in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
        => m_Session.SendUser(this, message, channel);

    /// <summary>Closes the connection; the other side gets <paramref name="reason"/>. On a client this is <see cref="NetworkManager.Stop"/>.</summary>
    public void Disconnect(string reason = "Disconnected")
    {
        if (IsServerSide) NetworkManager.Stop(reason);
        else m_Session.Disconnect(this, reason);
    }

    public override string ToString()
    {
        if (IsServerSide) return IsLocal ? "Server (local)" : $"Server ({Address}, {Transport})";
        return IsLocal ? $"Client {Id} (local)" : $"Client {Id} ({Address}, {Transport})";
    }
}
