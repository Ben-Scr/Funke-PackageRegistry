using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Funke.Networking.Internal;

/// <summary>
/// WebSocket server for browser clients on plain non-blocking sockets, polled from the main thread (no threads, no
/// HttpListener: that needs admin rights on Windows for anything but localhost). Serves ws:// only; for wss:// put a
/// TLS proxy in front (see the package README).
/// </summary>
internal sealed class WebSocketServerTransport : IServerTransport
{
    private const double k_HandshakeTimeout = 5;
    private const double k_PingInterval = 2;
    private const int k_MaxPendingOutput = 8 << 20;

    private sealed class Client
    {
        internal Client(Socket socket, string address, double now)
        {
            Socket = socket;
            Address = address;
            Connected = now;
            LastReceive = now;
            LastPing = now;
        }

        internal readonly Socket Socket;
        internal readonly string Address;
        internal readonly double Connected;
        internal readonly ByteQueue Input = new(4096);
        internal readonly ByteQueue Output = new(4096);
        internal readonly ByteQueue Batch = new(1024);
        internal readonly ByteQueue Fragments = new(256);
        internal int Id;
        internal bool Open;          // handshake done
        internal bool Closing;
        internal double LastReceive;
        internal double LastPing;
        internal int RoundTripMs;
    }

    private readonly NetworkSettings m_Settings;
    private readonly ITransportListener m_Listener;
    private readonly Func<int> m_NextConnectionId;
    private readonly List<Client> m_Clients = new();
    private readonly Dictionary<int, Client> m_ById = new();
    private readonly List<(Client Client, string Reason)> m_Dropped = new();
    private Socket? m_Socket;
    private bool m_Polling;

    internal WebSocketServerTransport(NetworkSettings settings, ITransportListener listener, Func<int> nextConnectionId)
    {
        m_Settings = settings;
        m_Listener = listener;
        m_NextConnectionId = nextConnectionId;
    }

    public NetTransportKind Kind => NetTransportKind.WebSocket;

    public void Start()
    {
        int port = m_Settings.WebSocketPort;
        Socket? socket = null;
        try
        {
            if (Socket.OSSupportsIPv6)
            {
                try
                {
                    socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
                    socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                }
                catch (SocketException e) when (e.SocketErrorCode != SocketError.AddressAlreadyInUse)
                {
                    socket?.Dispose();
                    socket = null;
                }
            }
            if (socket == null)
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            socket.Listen(64);
            socket.Blocking = false;
            m_Socket = socket;
        }
        catch (SocketException e)
        {
            socket?.Dispose();
            throw e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? new NetworkException($"WebSocket port {port} is in use (is another server running?). Choose another NetworkSettings.WebSocketPort, or 0 to turn browser support off.")
                : new NetworkException($"Cannot open WebSocket port {port}: {e.Message}");
        }
    }

    public void Poll(float deltaMilliseconds)
    {
        if (m_Socket == null) return;
        double now = NetClock.Now;
        m_Polling = true;
        Accept(now);
        for (int i = 0; i < m_Clients.Count; i++)
        {
            Client client = m_Clients[i];
            if (client.Closing) continue;
            Receive(client, now);
            if (client.Closing) continue;
            if (!client.Open && now - client.Connected > k_HandshakeTimeout) Drop(client, "WebSocket handshake timed out", notify: false);
            else if (client.Open && now - client.LastReceive > m_Settings.Timeout) Drop(client, TransportReasons.TimedOut, notify: true);
            else if (client.Open && now - client.LastPing > k_PingInterval) Ping(client, now);
        }
        FlushAll();
        m_Polling = false;
        RemoveDropped();
    }

    public void Flush()
    {
        FlushAll();
        if (!m_Polling) RemoveDropped();
    }

    private void FlushAll()
    {
        foreach (Client client in m_Clients)
        {
            if (client.Batch.Count > 0 && !client.Closing)
            {
                WebSocketCodec.WriteFrame(client.Output, WebSocketCodec.OpBinary, client.Batch.Data);
                client.Batch.Clear();
            }
            Flush(client);
        }
    }

    public void Send(int connectionId, ReadOnlySpan<byte> data, NetChannel channel)
    {
        if (!m_ById.TryGetValue(connectionId, out Client? client) || client.Closing) return;
        WebSocketCodec.AppendRecord(client.Batch, data);
        if (client.Batch.Count + client.Output.Count > k_MaxPendingOutput) Drop(client, "Too much data queued for a slow browser connection", notify: true);
    }

    public void Disconnect(int connectionId, string reason)
    {
        if (!m_ById.TryGetValue(connectionId, out Client? client)) return;
        if (client.Batch.Count > 0)
        {
            WebSocketCodec.WriteFrame(client.Output, WebSocketCodec.OpBinary, client.Batch.Data);
            client.Batch.Clear();
        }
        Close(client, WebSocketCodec.CloseNormal, reason);
        Drop(client, reason, notify: false);
        if (!m_Polling) RemoveDropped();   // mid-Poll the client list is being walked
    }

    public int GetRoundTripMs(int connectionId) => m_ById.TryGetValue(connectionId, out Client? client) ? client.RoundTripMs : 0;

    public void Dispose()
    {
        foreach (Client client in m_Clients)
        {
            if (client.Closing) continue;
            Close(client, WebSocketCodec.CloseGoingAway, "Server stopped");
            Release(client);
        }
        m_Clients.Clear();
        m_ById.Clear();
        m_Socket?.Dispose();
        m_Socket = null;
    }

    private void Accept(double now)
    {
        while (true)
        {
            Socket accepted;
            try { accepted = m_Socket!.Accept(); }
            catch (SocketException) { return; }   // WouldBlock: nobody waiting
            accepted.Blocking = false;
            accepted.NoDelay = true;
            // Browsers that never finish the handshake still hold a socket; cap them with the real clients.
            if (m_Clients.Count >= m_Settings.MaxConnections + 8)
            {
                accepted.Dispose();
                continue;
            }
            string address = accepted.RemoteEndPoint is IPEndPoint ep
                ? (ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address).ToString()
                : "?";
            m_Clients.Add(new Client(accepted, address, now));
        }
    }

    private void Receive(Client client, double now)
    {
        try
        {
            while (true)
            {
                ArraySegment<byte> space = client.Input.ReserveSegment(16384);
                int read = client.Socket.Receive(space.Array!, space.Offset, space.Count, SocketFlags.None, out SocketError error);
                if (error == SocketError.WouldBlock) break;
                if (error != SocketError.Success || read == 0)
                {
                    Drop(client, TransportReasons.Closed, notify: client.Open);
                    return;
                }
                client.Input.Commit(read);
                client.LastReceive = now;
                if (client.Input.Count > NetworkSettings.MaxMessageSize + 16384)
                {
                    Close(client, WebSocketCodec.CloseTooBig, "Message too big");
                    Drop(client, "Message too big", notify: client.Open);
                    return;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        if (!client.Open) Handshake(client);
        if (client.Open) ReadFrames(client, now);
    }

    private void Handshake(Client client)
    {
        switch (WebSocketCodec.ParseHandshake(client.Input.Data, out int consumed, out string key))
        {
            case WebSocketCodec.HandshakeResult.Incomplete:
                return;
            case WebSocketCodec.HandshakeResult.Ok:
                client.Input.Consume(consumed);
                client.Output.Append(WebSocketCodec.HandshakeResponse(key));
                client.Open = true;
                client.Id = m_NextConnectionId();
                m_ById[client.Id] = client;
                m_Listener.OnTransportConnected(client.Id, client.Address, NetTransportKind.WebSocket);
                return;
            default:
                client.Output.Append(WebSocketCodec.UpgradeRequiredResponse());
                Flush(client);
                Drop(client, "Not a WebSocket request", notify: false);
                return;
        }
    }

    private void ReadFrames(Client client, double now)
    {
        while (!client.Closing && WebSocketCodec.TryReadHeader(client.Input.Data, out WebSocketCodec.FrameHeader header))
        {
            if (!header.Masked)
            {
                Close(client, WebSocketCodec.CloseProtocolError, "Client frames must be masked");
                Drop(client, "Protocol error", notify: true);
                return;
            }
            if (header.PayloadLength > NetworkSettings.MaxMessageSize)
            {
                Close(client, WebSocketCodec.CloseTooBig, "Message too big");
                Drop(client, "Message too big", notify: true);
                return;
            }
            int total = header.HeaderLength + (int)header.PayloadLength;
            if (client.Input.Count < total) return;

            Span<byte> payload = client.Input.Buffer.AsSpan(client.Input.Start + header.HeaderLength, (int)header.PayloadLength);
            WebSocketCodec.Unmask(payload, header.Mask);
            switch (header.Opcode)
            {
                case WebSocketCodec.OpBinary:
                case WebSocketCodec.OpText:
                case WebSocketCodec.OpContinuation:
                    if (header.Fin && client.Fragments.Count == 0)
                    {
                        Deliver(client, payload);
                    }
                    else
                    {
                        client.Fragments.Append(payload);
                        if (client.Fragments.Count > NetworkSettings.MaxMessageSize)
                        {
                            Close(client, WebSocketCodec.CloseTooBig, "Message too big");
                            Drop(client, "Message too big", notify: true);
                            return;
                        }
                        if (header.Fin)
                        {
                            Deliver(client, client.Fragments.Data);
                            client.Fragments.Clear();
                        }
                    }
                    break;
                case WebSocketCodec.OpPing:
                    WebSocketCodec.WriteFrame(client.Output, WebSocketCodec.OpPong, payload);
                    break;
                case WebSocketCodec.OpPong:
                    if (payload.Length == 8)
                        client.RoundTripMs = (int)((now - BinaryPrimitives.ReadDoubleLittleEndian(payload)) * 1000);
                    break;
                case WebSocketCodec.OpClose:
                    string reason = WebSocketCodec.CloseReason(payload);
                    Close(client, WebSocketCodec.CloseNormal, "");
                    Drop(client, reason, notify: true);
                    return;
                default:
                    Close(client, WebSocketCodec.CloseProtocolError, "Unknown opcode");
                    Drop(client, "Protocol error", notify: true);
                    return;
            }
            client.Input.Consume(total);
        }
    }

    private void Deliver(Client client, ReadOnlySpan<byte> batch)
    {
        try
        {
            int position = 0;
            while (position < batch.Length && !client.Closing)
                m_Listener.OnTransportData(client.Id, WebSocketCodec.NextRecord(batch, ref position));
        }
        catch (NetworkException)
        {
            Close(client, WebSocketCodec.CloseProtocolError, "Malformed data");
            Drop(client, "Protocol error", notify: true);
        }
    }

    private static void Ping(Client client, double now)
    {
        client.LastPing = now;
        Span<byte> stamp = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(stamp, now);
        WebSocketCodec.WriteFrame(client.Output, WebSocketCodec.OpPing, stamp);
    }

    private static void Close(Client client, int code, string reason)
    {
        if (client.Closing || !client.Open) return;
        WebSocketCodec.WriteFrame(client.Output, WebSocketCodec.OpClose, WebSocketCodec.ClosePayload(code, reason));
        Flush(client);
    }

    private static void Flush(Client client)
    {
        while (client.Output.Count > 0)
        {
            int sent;
            try { sent = client.Socket.Send(client.Output.Buffer, client.Output.Start, client.Output.Count, SocketFlags.None, out SocketError error); }
            catch (ObjectDisposedException) { client.Output.Clear(); return; }
            if (sent <= 0) return;   // WouldBlock or broken; the next Receive notices a broken socket
            client.Output.Consume(sent);
        }
    }

    private void Drop(Client client, string reason, bool notify)
    {
        if (client.Closing) return;
        client.Closing = true;
        m_Dropped.Add((client, notify ? reason : ""));
        if (client.Open) m_ById.Remove(client.Id);
    }

    private void RemoveDropped()
    {
        if (m_Dropped.Count == 0) return;
        foreach ((Client client, string reason) in m_Dropped)
        {
            m_Clients.Remove(client);
            Release(client);
            if (reason.Length > 0) m_Listener.OnTransportDisconnected(client.Id, reason);
        }
        m_Dropped.Clear();
    }

    private static void Release(Client client)
    {
        try { client.Socket.Shutdown(SocketShutdown.Both); }
        catch (Exception) { /* already gone */ }
        client.Socket.Dispose();
    }
}
