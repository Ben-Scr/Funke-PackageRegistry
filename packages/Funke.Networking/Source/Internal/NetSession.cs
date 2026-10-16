using System;
using System.Collections.Generic;

namespace Funke.Networking.Internal;

internal enum SessionRole { Server, Host, Client }

/// <summary>What a session reports upwards. The runtime implements it; tests use a fake.</summary>
internal interface ISessionHandler
{
    MessageRegistry ServerMessages { get; }
    MessageRegistry ClientMessages { get; }
    string? Approve(NetConnectRequest request);
    void WriteInitialState(NetConnection client);
    void ServerClientConnected(NetConnection client);
    void ServerClientDisconnected(NetConnection client, string reason);
    void ClientConnected();
    void ClientDisconnected(string reason);
    void ServerObjectPacket(NetConnection from, PacketKind kind, NetReader reader);
    void ClientObjectPacket(PacketKind kind, NetReader reader);
}

/// <summary>
/// One server, host or client run: transports, handshake, connections and message routing. Knows nothing about
/// entities; the object layer plugs in through <see cref="ISessionHandler"/>. Everything happens in <see cref="Poll"/>.
/// </summary>
internal sealed class NetSession : ITransportListener
{
    private const double k_PingInterval = 1;

    private readonly ISessionHandler m_Handler;
    private readonly Dictionary<int, NetConnection> m_Connections = new();
    private readonly List<NetConnection> m_Clients = new();
    private readonly List<NetConnection> m_Expired = new();
    private readonly Queue<(bool ToServer, byte[] Data)> m_Loopback = new();
    private readonly NetWriter m_Writer = new(512);
    private readonly NetReader m_Reader = new();
    private byte[] m_Scratch = new byte[2048];
    private UdpServerTransport? m_Udp;
    private WebSocketServerTransport? m_WebSocket;
    private IClientTransport? m_ClientTransport;
    private int m_NextClientId = 1;
    private double m_LastPoll;
    private double m_LastPing;
    private double m_LastServerData;
    private double m_HandshakeStarted;
    private bool m_HostStartPending;
    private string m_ServerAddress = "";
    private readonly ulong m_InstanceId = (ulong)System.Random.Shared.NextInt64();

    internal NetSession(SessionRole role, NetworkSettings settings, ISessionHandler handler)
    {
        Role = role;
        Settings = settings;
        m_Handler = handler;
        m_LastPoll = NetClock.Now;
    }

    internal SessionRole Role { get; }
    internal NetworkSettings Settings { get; }
    internal bool IsServer => Role != SessionRole.Client;
    internal bool IsClient => Role != SessionRole.Server;
    internal bool IsHost => Role == SessionRole.Host;
    internal bool IsRunning { get; private set; }
    /// <summary>0 on a server or host; assigned by the server on a client; -1 before that.</summary>
    internal int LocalClientId { get; private set; } = -1;
    internal bool ClientReady { get; private set; }
    internal int ServerTickRate { get; private set; }
    /// <summary>Client side: the server. Host: a local stand-in.</summary>
    internal NetConnection? ServerConnection { get; private set; }
    /// <summary>Host: the server's view of its own client.</summary>
    internal NetConnection? LocalClient { get; private set; }
    /// <summary>Server side: connected clients, the host's own first.</summary>
    internal IReadOnlyList<NetConnection> Clients => m_Clients;
    internal int UdpPort => m_Udp?.LocalPort ?? 0;

    internal void StartServer()
    {
        try
        {
            m_Udp = new UdpServerTransport(Settings, this, () => m_NextClientId++, AnswerDiscovery);
            m_Udp.Start();
            if (Settings.WebSocketPort > 0)
            {
                m_WebSocket = new WebSocketServerTransport(Settings, this, () => m_NextClientId++);
                m_WebSocket.Start();
            }
        }
        catch
        {
            m_Udp?.Dispose();
            m_WebSocket?.Dispose();
            m_Udp = null;
            m_WebSocket = null;
            throw;
        }
        IsRunning = true;
        LocalClientId = 0;
        ServerTickRate = Settings.TickRate;
        if (IsHost)
        {
            LocalClient = new NetConnection(this, NetworkManager.ServerClientId, "local", NetTransportKind.Local, null) { IsConnected = true };
            ServerConnection = new NetConnection(this, 0, "local", NetTransportKind.Local, null) { IsConnected = true, IsServerSide = true };
            m_Clients.Add(LocalClient);
            m_HostStartPending = true;   // events wait for the first Poll so handlers added right after Start see them
        }
    }

    internal void StartClient(string address)
    {
        bool browser = OperatingSystem.IsBrowser();
        IClientTransport transport;
        if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) throw new NetworkException($"Not a valid address: {address}");
            transport = new WebSocketClientTransport(Settings, this, uri);
            m_ServerAddress = uri.ToString();
        }
        else
        {
            ParseHostPort(address, browser ? Settings.WebSocketPort : Settings.Port, out string host, out int port);
            if (browser)
            {
                if (port <= 0) throw new NetworkException("Browsers join over WebSocket: set NetworkSettings.WebSocketPort or pass a ws:// address.");
                string hostPart = host.Contains(':') ? $"[{host}]" : host;
                var uri = new Uri($"ws://{hostPart}:{port}/");
                transport = new WebSocketClientTransport(Settings, this, uri);
                m_ServerAddress = uri.ToString();
            }
            else
            {
                transport = new UdpClientTransport(Settings, this, host, port);
                m_ServerAddress = host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
            }
        }
        ServerConnection = new NetConnection(this, 0, m_ServerAddress, transport.Kind, null) { IsServerSide = true };
        m_ClientTransport = transport;
        IsRunning = true;
        transport.Connect();
    }

    internal void Poll()
    {
        if (!IsRunning) return;
        double now = NetClock.Now;
        float elapsed = (float)Math.Min(1000, (now - m_LastPoll) * 1000);
        m_LastPoll = now;

        if (m_HostStartPending)
        {
            m_HostStartPending = false;
            m_Handler.ServerClientConnected(LocalClient!);
            if (!IsRunning) return;
            ClientReady = true;
            m_Handler.ClientConnected();
            if (!IsRunning) return;
        }

        m_Udp?.Poll(elapsed);
        if (IsRunning) m_WebSocket?.Poll(elapsed);
        if (IsRunning) m_ClientTransport?.Poll(elapsed);
        if (IsRunning) DeliverLoopback();
        if (!IsRunning) return;

        if (IsServer) ExpireHandshakes(now);
        else UpdateClient(now);
    }

    internal void Flush()
    {
        if (!IsRunning) return;
        m_Udp?.Flush();
        m_WebSocket?.Flush();
        m_ClientTransport?.Flush();
    }

    /// <summary>Ends the run without raising disconnect events; the other side learns <paramref name="reason"/>.</summary>
    internal void Stop(string reason, bool quitting = false)
    {
        if (!IsRunning) return;
        IsRunning = false;
        foreach (NetConnection connection in m_Connections.Values)
        {
            connection.ServerTransport?.Disconnect(connection.Id, reason);
            connection.IsConnected = false;
            connection.Closed = true;
        }
        m_Connections.Clear();
        m_Udp?.Dispose();
        m_WebSocket?.Dispose();
        m_ClientTransport?.Disconnect(reason, quitting);
        m_Udp = null;
        m_WebSocket = null;
        m_ClientTransport = null;
        m_Loopback.Clear();
        foreach (NetConnection client in m_Clients) client.IsConnected = false;
        m_Clients.Clear();
        if (ServerConnection != null) ServerConnection.IsConnected = false;
        ClientReady = false;
    }

    // ── Sending ─────────────────────────────────────────────

    internal NetWriter BeginPacket(PacketKind kind)
    {
        m_Writer.Reset();
        m_Writer.Write((byte)kind);
        return m_Writer;
    }

    internal NetWriter BeginUser<T>(in T message) where T : INetMessage
    {
        NetWriter writer = BeginPacket(PacketKind.User);
        writer.Write(MessageId<T>.Value);
        message.Serialize(writer);
        return writer;
    }

    internal void SendUser<T>(NetConnection to, in T message, NetChannel channel) where T : INetMessage
        => SendRaw(to, BeginUser(message).Written, channel);

    /// <summary>To a client (server side) or to the server (when <paramref name="to"/> is <see cref="ServerConnection"/>).</summary>
    internal void SendRaw(NetConnection to, ReadOnlySpan<byte> data, NetChannel channel)
    {
        if (!IsRunning || to.Closed) return;
        CheckSize(data.Length, channel);
        if (to.IsServerSide)
        {
            if (IsHost) m_Loopback.Enqueue((true, data.ToArray()));
            else m_ClientTransport?.Send(data, channel);
        }
        else if (to.IsLocal)
        {
            m_Loopback.Enqueue((false, data.ToArray()));
        }
        else
        {
            to.ServerTransport?.Send(to.Id, data, channel);
        }
    }

    internal void SendToClients(ReadOnlySpan<byte> data, NetChannel channel, int exceptClientId = -1, bool includeLocal = true)
    {
        CheckSize(data.Length, channel);
        foreach (NetConnection client in m_Clients)
        {
            if (client.Id == exceptClientId || (client.IsLocal && !includeLocal)) continue;
            SendRaw(client, data, channel);
        }
    }

    internal void Disconnect(NetConnection connection, string reason)
    {
        if (connection.IsServerSide)
        {
            Stop(reason);
            return;
        }
        if (connection.IsLocal || connection.Closed) return;   // the host's own client leaves with Stop
        connection.Closed = true;
        connection.ServerTransport?.Disconnect(connection.Id, reason);
        m_Connections.Remove(connection.Id);
        Drop(connection, reason);
    }

    internal NetConnection? GetClient(int clientId)
    {
        if (LocalClient != null && clientId == LocalClient.Id) return LocalClient;
        return m_Connections.TryGetValue(clientId, out NetConnection? connection) && connection.IsConnected ? connection : null;
    }

    internal int GetRoundTripMs(NetConnection connection)
    {
        if (connection.IsLocal) return 0;
        if (connection.IsServerSide) return m_ClientTransport?.RoundTripMs ?? 0;
        return connection.ServerTransport?.GetRoundTripMs(connection.Id) ?? 0;
    }

    // ── Transport events ────────────────────────────────────

    void ITransportListener.OnTransportConnected(int connectionId, string address, NetTransportKind kind)
    {
        if (IsServer)
        {
            IServerTransport transport = kind == NetTransportKind.WebSocket ? m_WebSocket! : m_Udp!;
            m_Connections[connectionId] = new NetConnection(this, connectionId, address, kind, transport);
            return;
        }
        m_HandshakeStarted = NetClock.Now;
        m_LastServerData = m_HandshakeStarted;
        NetWriter writer = BeginPacket(PacketKind.Hello);
        writer.Write(Settings.GameId);
        writer.WritePacked(Settings.ProtocolVersion);
        writer.Write(m_ConnectData);
        m_ClientTransport!.Send(writer.Written, NetChannel.Reliable);
    }

    void ITransportListener.OnTransportData(int connectionId, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        if (IsServer)
        {
            if (m_Connections.TryGetValue(connectionId, out NetConnection? connection)) HandleServerPacket(connection, data);
        }
        else
        {
            m_LastServerData = NetClock.Now;
            HandleClientPacket(data);
        }
    }

    void ITransportListener.OnTransportDisconnected(int connectionId, string reason)
    {
        if (IsServer)
        {
            if (!m_Connections.Remove(connectionId, out NetConnection? connection)) return;
            connection.Closed = true;
            Drop(connection, reason);
            return;
        }
        EndClient(reason);
    }

    // ── Server side ─────────────────────────────────────────

    private void HandleServerPacket(NetConnection from, ReadOnlySpan<byte> data)
    {
        NetReader reader = Load(data);
        try
        {
            var kind = (PacketKind)reader.ReadByte();
            if (!from.IsConnected)
            {
                if (kind == PacketKind.Hello) HandleHello(from, reader);
                return;
            }
            switch (kind)
            {
                case PacketKind.User:
                    m_Handler.ServerMessages.Dispatch(reader.ReadUInt(), from, reader);
                    break;
                case PacketKind.Ping:
                    double stamp = reader.ReadDouble();
                    NetWriter writer = BeginPacket(PacketKind.Pong);
                    writer.Write(stamp);
                    SendRaw(from, writer.Written, NetChannel.Unreliable);
                    break;
                case >= PacketKind.Spawn:
                    m_Handler.ServerObjectPacket(from, kind, reader);
                    break;
            }
        }
        catch (NetworkException e)
        {
            NetLog.Warn($"Dropped a malformed packet from {from}: {e.Message}");
        }
    }

    private void HandleHello(NetConnection from, NetReader reader)
    {
        string gameId = reader.ReadString();
        int version = reader.ReadPackedInt();
        string connectData = reader.ReadString();

        string? reason = null;
        if (gameId != Settings.GameId)
            reason = $"This server runs a different game ('{Settings.GameId}', you have '{gameId}').";
        else if (version != Settings.ProtocolVersion)
            reason = $"Version mismatch: the server runs protocol {Settings.ProtocolVersion}, this game {version}. Both need the same game version.";
        else if (CountRemoteClients() >= Settings.MaxConnections)
            reason = $"The server is full ({Settings.MaxConnections}/{Settings.MaxConnections}).";
        else
        {
            try { reason = m_Handler.Approve(new NetConnectRequest(from.Address, from.Transport, connectData)); }
            catch (Exception e)
            {
                NetLog.Error($"ConnectionApproval threw: {e}");
                reason = "The server could not check the connection.";
            }
        }
        if (reason != null)
        {
            NetLog.Info($"Rejected {from.Address} ({from.Transport}): {reason}");
            from.Closed = true;
            m_Connections.Remove(from.Id);
            from.ServerTransport?.Disconnect(from.Id, reason);
            return;
        }

        from.ConnectData = connectData;
        from.IsConnected = true;
        m_Clients.Add(from);
        NetWriter writer = BeginPacket(PacketKind.Welcome);
        writer.WritePacked(from.Id);
        writer.WritePacked(Settings.TickRate);
        SendRaw(from, writer.Written, NetChannel.Reliable);
        m_Handler.WriteInitialState(from);
        if (!IsRunning || from.Closed) return;
        SendRaw(from, BeginPacket(PacketKind.Ready).Written, NetChannel.Reliable);
        NetLog.Info($"{from} joined");
        m_Handler.ServerClientConnected(from);
    }

    private void Drop(NetConnection connection, string reason)
    {
        if (!connection.IsConnected) return;
        connection.IsConnected = false;
        m_Clients.Remove(connection);
        NetLog.Info($"{connection} left: {reason}");
        m_Handler.ServerClientDisconnected(connection, reason);
    }

    private void ExpireHandshakes(double now)
    {
        foreach (NetConnection connection in m_Connections.Values)
            if (connection.IsPending && now - connection.Started > Settings.ConnectTimeout) m_Expired.Add(connection);
        foreach (NetConnection connection in m_Expired)
        {
            connection.Closed = true;
            m_Connections.Remove(connection.Id);
            connection.ServerTransport?.Disconnect(connection.Id, "Handshake timed out");
        }
        m_Expired.Clear();
    }

    private int CountRemoteClients()
    {
        int count = 0;
        foreach (NetConnection client in m_Clients)
            if (!client.IsLocal) count++;
        return count;
    }

    private byte[]? AnswerDiscovery(System.Net.IPEndPoint from, ReadOnlySpan<byte> request)
    {
        if (!Settings.Discoverable || !IsRunning) return null;
        return Discovery.Answer(request, Settings, m_InstanceId, CountRemoteClients(), UdpPort, m_WebSocket != null ? Settings.WebSocketPort : 0);
    }

    // ── Client side ─────────────────────────────────────────

    private string m_ConnectData = "";

    internal void SetConnectData(string connectData) => m_ConnectData = connectData ?? "";

    private void HandleClientPacket(ReadOnlySpan<byte> data)
    {
        NetReader reader = Load(data);
        try
        {
            var kind = (PacketKind)reader.ReadByte();
            switch (kind)
            {
                case PacketKind.Welcome:
                    LocalClientId = reader.ReadPackedInt();
                    ServerTickRate = Math.Max(1, reader.ReadPackedInt());
                    break;
                case PacketKind.Ready:
                    if (ClientReady) break;
                    ClientReady = true;
                    ServerConnection!.IsConnected = true;
                    NetLog.Info($"Connected to {m_ServerAddress} as client {LocalClientId}");
                    m_Handler.ClientConnected();
                    break;
                case PacketKind.Pong:
                    if (m_ClientTransport is WebSocketClientTransport ws)
                        ws.RoundTripMs = (int)((NetClock.Now - reader.ReadDouble()) * 1000);
                    break;
                case PacketKind.User:
                    m_Handler.ClientMessages.Dispatch(reader.ReadUInt(), ServerConnection!, reader);
                    break;
                case >= PacketKind.Spawn:
                    m_Handler.ClientObjectPacket(kind, reader);
                    break;
            }
        }
        catch (NetworkException e)
        {
            NetLog.Warn($"Dropped a malformed packet from the server: {e.Message}");
        }
    }

    private void UpdateClient(double now)
    {
        if (m_ClientTransport == null || IsHost) return;
        if (!ClientReady)
        {
            if (m_HandshakeStarted > 0 && now - m_HandshakeStarted > Settings.ConnectTimeout)
                EndClient($"{m_ServerAddress} accepted the connection but did not answer the handshake");
            return;
        }
        if (m_ClientTransport.Kind != NetTransportKind.WebSocket) return;   // UDP pings and times out by itself
        if (now - m_LastServerData > Settings.Timeout)
        {
            EndClient(TransportReasons.TimedOut);
            return;
        }
        if (now - m_LastPing >= k_PingInterval)
        {
            m_LastPing = now;
            NetWriter writer = BeginPacket(PacketKind.Ping);
            writer.Write(now);
            m_ClientTransport.Send(writer.Written, NetChannel.Unreliable);
        }
    }

    private void EndClient(string reason)
    {
        if (!IsRunning) return;
        IsRunning = false;
        m_ClientTransport?.Dispose();
        m_ClientTransport = null;
        if (ServerConnection != null)
        {
            ServerConnection.IsConnected = false;
            ServerConnection.Closed = true;
        }
        ClientReady = false;
        NetLog.Info($"Disconnected from {m_ServerAddress}: {reason}");
        m_Handler.ClientDisconnected(reason);
    }

    // ── Shared ──────────────────────────────────────────────

    private void DeliverLoopback()
    {
        // Messages queued by these handlers wait for the next frame, like remote ones.
        int count = m_Loopback.Count;
        for (int i = 0; i < count && IsRunning; i++)
        {
            (bool toServer, byte[] data) = m_Loopback.Dequeue();
            if (toServer) HandleServerPacket(LocalClient!, data);
            else HandleClientPacket(data);
        }
    }

    private NetReader Load(ReadOnlySpan<byte> data)
    {
        // Transports hand out spans into their own buffers; the reader needs an array.
        if (m_Scratch.Length < data.Length) m_Scratch = new byte[Math.Max(data.Length, m_Scratch.Length * 2)];
        data.CopyTo(m_Scratch);
        m_Reader.Reset(m_Scratch, 0, data.Length);
        return m_Reader;
    }

    private static void CheckSize(int length, NetChannel channel)
    {
        if ((channel == NetChannel.Unreliable || channel == NetChannel.Sequenced) && length > NetworkSettings.MaxUnreliableSize)
            throw new NetworkException($"An {channel} message can be at most {NetworkSettings.MaxUnreliableSize} bytes ({length} given). Send big data on NetChannel.Reliable.");
        if (length > NetworkSettings.MaxMessageSize)
            throw new NetworkException($"Messages can be at most {NetworkSettings.MaxMessageSize} bytes ({length} given).");
    }

    internal static void ParseHostPort(string address, int defaultPort, out string host, out int port)
    {
        address = address.Trim();
        port = defaultPort;
        if (address.Length == 0) throw new NetworkException("No server address given.");
        if (address.StartsWith('['))
        {
            int close = address.IndexOf(']');
            if (close < 0) throw new NetworkException($"Not a valid address: {address}");
            host = address[1..close];
            if (close + 1 < address.Length)
            {
                if (address[close + 1] != ':' || !int.TryParse(address[(close + 2)..], out port)) throw new NetworkException($"Not a valid address: {address}");
            }
            return;
        }
        int colon = address.LastIndexOf(':');
        // More than one colon without brackets: a bare IPv6 address.
        if (colon > 0 && address.IndexOf(':') == colon)
        {
            host = address[..colon];
            if (!int.TryParse(address[(colon + 1)..], out port) || port <= 0 || port > 65535) throw new NetworkException($"Not a valid port in {address}");
            return;
        }
        host = address;
    }
}
