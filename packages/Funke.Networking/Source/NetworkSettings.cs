namespace Funke.Networking;

/// <summary>Set before <see cref="NetworkManager.StartHost"/>, <see cref="NetworkManager.StartServer"/> or <see cref="NetworkManager.StartClient"/>.</summary>
public sealed class NetworkSettings
{
    /// <summary>Clients and servers only talk when this matches: keeps other games' traffic out.</summary>
    public string GameId = "FunkeGame";

    /// <summary>Bump when the message format changes; older clients are rejected with a clear reason.</summary>
    public int ProtocolVersion = 1;

    /// <summary>UDP port of the server (desktop, Android, iOS clients) and of LAN discovery.</summary>
    public int Port = 7777;

    /// <summary>WebSocket port for browser clients; 0 turns browser support off.</summary>
    public int WebSocketPort = 7778;

    /// <summary>Remote clients the server accepts (the host's own client does not count).</summary>
    public int MaxConnections = 16;

    /// <summary>How often per second networked transforms are sent.</summary>
    public int TickRate = 30;

    /// <summary>A client gives up connecting after this many seconds.</summary>
    public float ConnectTimeout = 5f;

    /// <summary>A connection that sends nothing for this long is dropped.</summary>
    public float Timeout = 10f;

    /// <summary>The server answers LAN discovery searches.</summary>
    public bool Discoverable = true;

    /// <summary>Server name shown in LAN discovery.</summary>
    public string ServerName = "Funke Game";

    /// <summary>Free text shown in LAN discovery (map, mode, ...).</summary>
    public string DiscoveryInfo = "";

    /// <summary>Simulated round-trip delay range in milliseconds on UDP connections (0 = off). For testing bad networks.</summary>
    public int SimulatedLatencyMin;
    public int SimulatedLatencyMax;

    /// <summary>Percentage of UDP packets dropped on purpose (0..100). For testing bad networks.</summary>
    public int SimulatedPacketLoss;

    /// <summary>Largest message on <see cref="NetChannel.Unreliable"/> and <see cref="NetChannel.Sequenced"/> (one UDP packet).</summary>
    public const int MaxUnreliableSize = 1000;

    /// <summary>Largest message on the reliable channels.</summary>
    public const int MaxMessageSize = 1 << 20;

    internal NetworkSettings Clone() => (NetworkSettings)MemberwiseClone();
}
