using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Funke.Networking.Internal;

internal static class Udp
{
    internal static DeliveryMethod ToDelivery(NetChannel channel) => channel switch
    {
        NetChannel.ReliableUnordered => DeliveryMethod.ReliableUnordered,
        NetChannel.Unreliable => DeliveryMethod.Unreliable,
        NetChannel.Sequenced => DeliveryMethod.Sequenced,
        _ => DeliveryMethod.ReliableOrdered,
    };

    /// <summary>LiteNetLib in manual mode: no threads, the socket is read in Poll. Same behaviour on every platform.</summary>
    internal static NetManager CreateManager(INetEventListener listener, NetworkSettings settings)
    {
        var manager = new NetManager(listener)
        {
            AutoRecycle = true,
            DisconnectTimeout = Math.Max(1000, (int)(settings.Timeout * 1000)),
            UnconnectedMessagesEnabled = true,
        };
        if (settings.SimulatedLatencyMax > 0)
        {
            manager.SimulateLatency = true;
            manager.SimulationMinLatency = Math.Max(0, settings.SimulatedLatencyMin / 2);
            manager.SimulationMaxLatency = Math.Max(manager.SimulationMinLatency, settings.SimulatedLatencyMax / 2);
        }
        if (settings.SimulatedPacketLoss > 0)
        {
            manager.SimulatePacketLoss = true;
            manager.SimulationPacketLossChance = Math.Clamp(settings.SimulatedPacketLoss, 0, 100);
        }
        return manager;
    }

    internal static void Send(NetPeer peer, ReadOnlySpan<byte> data, NetChannel channel)
    {
        DeliveryMethod method = ToDelivery(channel);
        if ((channel == NetChannel.Unreliable || channel == NetChannel.Sequenced) && data.Length > peer.GetMaxSinglePacketSize(method))
            throw new NetworkException($"An {channel} message can be at most {NetworkSettings.MaxUnreliableSize} bytes ({data.Length} given). Send big data on NetChannel.Reliable.");
        peer.Send(data, method);
    }

    internal static string DescribeDisconnect(DisconnectInfo info, string address)
    {
        if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
        {
            try { return Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytesSpan()); }
            catch (Exception) { /* not our reason text */ }
        }
        return info.Reason switch
        {
            DisconnectReason.Timeout => TransportReasons.TimedOut,
            DisconnectReason.ConnectionFailed => $"{address} does not answer (no server on that address and port, or a firewall blocks UDP)",
            DisconnectReason.HostUnreachable => $"{address} is unreachable",
            DisconnectReason.NetworkUnreachable => "No network connection",
            DisconnectReason.ConnectionRejected => "The server rejected the connection (different game)",
            DisconnectReason.UnknownHost => $"Unknown host {address}",
            DisconnectReason.InvalidProtocol => "Incompatible network library version",
            _ => TransportReasons.Closed,
        };
    }

    internal static byte[] ReasonBytes(string reason) => Encoding.UTF8.GetBytes(reason.Length > 200 ? reason[..200] : reason);
}

internal sealed class UdpServerTransport : IServerTransport, INetEventListener
{
    private readonly NetworkSettings m_Settings;
    private readonly ITransportListener m_Listener;
    private readonly Func<int> m_NextConnectionId;
    private readonly Func<IPEndPoint, ReadOnlySpan<byte>, byte[]?> m_AnswerUnconnected;
    private readonly Dictionary<int, NetPeer> m_Peers = new();
    private readonly Dictionary<int, int> m_IdsByPeer = new();
    private NetManager? m_Manager;

    internal UdpServerTransport(NetworkSettings settings, ITransportListener listener, Func<int> nextConnectionId,
                                Func<IPEndPoint, ReadOnlySpan<byte>, byte[]?> answerUnconnected)
    {
        m_Settings = settings;
        m_Listener = listener;
        m_NextConnectionId = nextConnectionId;
        m_AnswerUnconnected = answerUnconnected;
    }

    public NetTransportKind Kind => NetTransportKind.Udp;
    internal int LocalPort => m_Manager?.LocalPort ?? 0;

    public void Start()
    {
        m_Manager = Udp.CreateManager(this, m_Settings);
        m_Manager.BroadcastReceiveEnabled = m_Settings.Discoverable;
        if (!m_Manager.StartInManualMode(m_Settings.Port))
        {
            m_Manager = null;
            throw new NetworkException($"UDP port {m_Settings.Port} is in use (is another server running?). Choose another NetworkSettings.Port.");
        }
    }

    public void Poll(float deltaMilliseconds)
    {
        if (m_Manager == null) return;
        m_Manager.PollEvents();
        m_Manager?.ManualUpdate(deltaMilliseconds);
    }

    public void Flush() => m_Manager?.ManualUpdate(0);

    public void Send(int connectionId, ReadOnlySpan<byte> data, NetChannel channel)
    {
        if (m_Peers.TryGetValue(connectionId, out NetPeer? peer)) Udp.Send(peer, data, channel);
    }

    public void Disconnect(int connectionId, string reason)
    {
        if (!m_Peers.Remove(connectionId, out NetPeer? peer)) return;
        m_IdsByPeer.Remove(peer.Id);
        m_Manager?.DisconnectPeer(peer, Udp.ReasonBytes(reason));
    }

    public int GetRoundTripMs(int connectionId) => m_Peers.TryGetValue(connectionId, out NetPeer? peer) ? peer.RoundTripTime : 0;

    public void Dispose()
    {
        NetManager? manager = m_Manager;
        m_Manager = null;
        if (manager == null) return;
        foreach (NetPeer peer in m_Peers.Values) manager.DisconnectPeer(peer, Udp.ReasonBytes("Server stopped"));
        manager.ManualUpdate(0);   // flush the goodbyes before the socket closes
        manager.Stop(false);
        m_Peers.Clear();
        m_IdsByPeer.Clear();
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey(m_Settings.GameId);

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        int id = m_NextConnectionId();
        m_Peers[id] = peer;
        m_IdsByPeer[peer.Id] = id;
        m_Listener.OnTransportConnected(id, peer.Address.ToString(), NetTransportKind.Udp);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (!m_IdsByPeer.Remove(peer.Id, out int id)) return;   // we closed it ourselves
        m_Peers.Remove(id);
        m_Listener.OnTransportDisconnected(id, Udp.DescribeDisconnect(info, peer.ToString()));
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (m_IdsByPeer.TryGetValue(peer.Id, out int id)) m_Listener.OnTransportData(id, reader.GetRemainingBytesSpan());
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint from, NetPacketReader reader, UnconnectedMessageType type)
    {
        byte[]? answer = m_AnswerUnconnected(from, reader.GetRemainingBytesSpan());
        if (answer != null) m_Manager?.SendUnconnectedMessage(answer, from);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}

internal sealed class UdpClientTransport : IClientTransport, INetEventListener
{
    private readonly NetworkSettings m_Settings;
    private readonly ITransportListener m_Listener;
    private readonly string m_Host;
    private readonly int m_Port;
    private NetManager? m_Manager;
    private NetPeer? m_Peer;
    private Task<IPAddress[]>? m_Resolve;
    private string? m_FailReason;
    private bool m_Closed;

    internal UdpClientTransport(NetworkSettings settings, ITransportListener listener, string host, int port)
    {
        m_Settings = settings;
        m_Listener = listener;
        m_Host = host;
        m_Port = port;
    }

    public NetTransportKind Kind => NetTransportKind.Udp;
    public int RoundTripMs => m_Peer?.RoundTripTime ?? 0;
    private string Address => m_Host.Contains(':') ? $"[{m_Host}]:{m_Port}" : $"{m_Host}:{m_Port}";

    public void Connect()
    {
        m_Manager = Udp.CreateManager(this, m_Settings);
        if (!m_Manager.StartInManualMode(0))
            throw new NetworkException("Could not open a UDP socket.");
        if (IPAddress.TryParse(m_Host, out IPAddress? address))
            m_Peer = m_Manager.Connect(new IPEndPoint(address, m_Port), m_Settings.GameId);
        else
            m_Resolve = Dns.GetHostAddressesAsync(m_Host);   // a blocking lookup would freeze the frame
    }

    public void Poll(float deltaMilliseconds)
    {
        if (m_Manager == null) return;
        if (m_Resolve != null && m_Resolve.IsCompleted)
        {
            Task<IPAddress[]> resolve = m_Resolve;
            m_Resolve = null;
            IPAddress? address = resolve.IsCompletedSuccessfully ? PickAddress(resolve.Result) : null;
            if (address == null) Fail($"Unknown host {m_Host}");
            else m_Peer = m_Manager.Connect(new IPEndPoint(address, m_Port), m_Settings.GameId);
        }
        if (m_FailReason == null)
        {
            m_Manager.PollEvents();
            m_Manager.ManualUpdate(deltaMilliseconds);
        }
        // Stopping the manager from inside its own event callback would pull the socket out from under PollEvents.
        if (m_FailReason != null)
        {
            string reason = m_FailReason;
            m_FailReason = null;
            Dispose();
            m_Listener.OnTransportDisconnected(0, reason);
        }
    }

    public void Flush()
    {
        if (m_FailReason == null) m_Manager?.ManualUpdate(0);
    }

    public void Send(ReadOnlySpan<byte> data, NetChannel channel)
    {
        if (m_Peer != null && m_Peer.ConnectionState == ConnectionState.Connected) Udp.Send(m_Peer, data, channel);
    }

    public void Disconnect(string reason, bool blocking)
    {
        m_Closed = true;
        if (m_Peer != null) m_Manager?.DisconnectPeer(m_Peer, Udp.ReasonBytes(reason));
        Dispose();   // LiteNetLib sends the disconnect packet right away; nothing to wait for
    }

    public void Dispose()
    {
        NetManager? manager = m_Manager;
        m_Manager = null;
        if (manager == null) return;
        manager.ManualUpdate(0);
        manager.Stop(false);
    }

    private static IPAddress? PickAddress(IPAddress[] addresses)
    {
        foreach (IPAddress address in addresses)
            if (address.AddressFamily == AddressFamily.InterNetwork) return address;
        return addresses.Length > 0 ? addresses[0] : null;
    }

    private void Fail(string reason)
    {
        if (m_Closed) return;
        m_Closed = true;
        m_FailReason = reason;
    }

    void INetEventListener.OnPeerConnected(NetPeer peer) => m_Listener.OnTransportConnected(0, Address, NetTransportKind.Udp);

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => Fail(Udp.DescribeDisconnect(info, Address));

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (!m_Closed) m_Listener.OnTransportData(0, reader.GetRemainingBytesSpan());
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request) => request.Reject();
    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint from, NetPacketReader reader, UnconnectedMessageType type) { }
    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}

/// <summary>Sends LAN discovery broadcasts and collects the answers.</summary>
internal sealed class UdpDiscoveryClient : INetEventListener, IDisposable
{
    private readonly NetManager m_Manager;
    private readonly Action<IPEndPoint, ReadOnlySpan<byte>> m_OnAnswer;

    internal UdpDiscoveryClient(Action<IPEndPoint, ReadOnlySpan<byte>> onAnswer)
    {
        m_OnAnswer = onAnswer;
        m_Manager = new NetManager(this) { UnconnectedMessagesEnabled = true, AutoRecycle = true };
        if (!m_Manager.StartInManualMode(0)) throw new NetworkException("Could not open a UDP socket for LAN discovery.");
    }

    internal void Broadcast(byte[] request, int port)
    {
        m_Manager.SendBroadcast(request, port);
        m_Manager.SendUnconnectedMessage(request, new IPEndPoint(IPAddress.Loopback, port));   // a server on this machine
    }

    internal void Poll(float deltaMilliseconds)
    {
        m_Manager.PollEvents();
        m_Manager.ManualUpdate(deltaMilliseconds);
    }

    public void Dispose() => m_Manager.Stop(false);

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint from, NetPacketReader reader, UnconnectedMessageType type)
        => m_OnAnswer(from, reader.GetRemainingBytesSpan());

    void INetEventListener.OnPeerConnected(NetPeer peer) { }
    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info) { }
    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method) { }
    void INetEventListener.OnConnectionRequest(ConnectionRequest request) => request.Reject();
    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
