using System;

namespace Funke.Networking;

/// <summary>How a message travels. Browser connections (WebSocket) deliver every channel reliably and in order.</summary>
public enum NetChannel : byte
{
    /// <summary>Arrives exactly once, in send order. Lobby, chat, spawns, game events.</summary>
    Reliable = 0,
    /// <summary>Arrives exactly once, in any order.</summary>
    ReliableUnordered = 1,
    /// <summary>May be lost or arrive out of order. At most <see cref="NetworkSettings.MaxUnreliableSize"/> bytes.</summary>
    Unreliable = 2,
    /// <summary>May be lost; older messages arriving after newer ones are dropped. Positions, inputs.</summary>
    Sequenced = 3,
}

public enum NetTransportKind : byte
{
    /// <summary>The host's own client inside the server process.</summary>
    Local = 0,
    Udp = 1,
    WebSocket = 2,
}

/// <summary>A message type. Write and read the fields in the same order.</summary>
public interface INetMessage
{
    void Serialize(NetWriter writer);
    void Deserialize(NetReader reader);
}

public sealed class NetworkException : Exception
{
    public NetworkException(string message) : base(message) { }
    public NetworkException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>What a joining client sent; <see cref="NetworkServer.ConnectionApproval"/> decides whether it may play.</summary>
public sealed class NetConnectRequest
{
    internal NetConnectRequest(string address, NetTransportKind transport, string connectData)
    {
        Address = address;
        Transport = transport;
        ConnectData = connectData;
    }

    public string Address { get; }
    public NetTransportKind Transport { get; }
    /// <summary>The string the client passed to <see cref="NetworkManager.StartClient"/> (player name, password, ...).</summary>
    public string ConnectData { get; }
}

/// <summary>A LAN game answering <see cref="NetworkDiscovery"/>.</summary>
public readonly struct DiscoveredServer
{
    internal DiscoveredServer(string address, int port, int webSocketPort, string name, int players, int maxPlayers,
                              string info, bool compatible)
    {
        Address = address;
        Port = port;
        WebSocketPort = webSocketPort;
        Name = name;
        Players = players;
        MaxPlayers = maxPlayers;
        Info = info;
        IsCompatible = compatible;
    }

    public string Address { get; }
    public int Port { get; }
    /// <summary>0 when the server does not accept browsers.</summary>
    public int WebSocketPort { get; }
    public string Name { get; }
    public int Players { get; }
    public int MaxPlayers { get; }
    /// <summary><see cref="NetworkSettings.DiscoveryInfo"/> of the server (map, mode, ...).</summary>
    public string Info { get; }
    /// <summary>False when the server runs another <see cref="NetworkSettings.ProtocolVersion"/>; joining would be rejected.</summary>
    public bool IsCompatible { get; }

    internal DiscoveredServer WithAddress(string address) => new(address, Port, WebSocketPort, Name, Players, MaxPlayers, Info, IsCompatible);

    /// <summary>"address:port", ready for <see cref="NetworkManager.StartClient"/>.</summary>
    public string EndPoint => Address.Contains(':') ? $"[{Address}]:{Port}" : $"{Address}:{Port}";

    public override string ToString() => $"{Name} ({EndPoint}, {Players}/{MaxPlayers})";
}
