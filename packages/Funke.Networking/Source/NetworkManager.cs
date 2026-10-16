using System;
using Funke.Networking.Internal;

namespace Funke.Networking;

/// <summary>
/// Starts and stops the network session. One process is a host (server plus its own player), a dedicated server or
/// a client. Desktop, Android and iOS clients connect over UDP, browsers over WebSocket; a server takes both.
/// </summary>
public static class NetworkManager
{
    /// <summary>The server's client id: owner of server objects, and the host's own player.</summary>
    public const int ServerClientId = 0;

    /// <summary>Used by the next Start call; changing it does not affect a running session.</summary>
    public static NetworkSettings Settings { get; set; } = new();

    public static bool IsRunning => NetworkRuntime.Session is { IsRunning: true };
    public static bool IsServer => NetworkRuntime.Session is { IsRunning: true, IsServer: true };
    public static bool IsClient => NetworkRuntime.Session is { IsRunning: true, IsClient: true };
    public static bool IsHost => NetworkRuntime.Session is { IsRunning: true, IsHost: true };
    /// <summary>This process's client id: <see cref="ServerClientId"/> on a server or host, assigned by the server on a client, -1 offline.</summary>
    public static int LocalClientId => NetworkRuntime.Session is { IsRunning: true } session ? session.LocalClientId : -1;

    /// <summary>The session ended: <see cref="Stop"/>, the game closing, or a client losing its server. Carries the reason.</summary>
    public static event Action<string>? Stopped;

    /// <summary>Server with its own player: <see cref="NetworkServer.ClientConnected"/> and <see cref="NetworkClient.Connected"/> fire for it next frame.</summary>
    public static void StartHost(NetworkSettings? settings = null) => NetworkRuntime.Start(SessionRole.Host, settings ?? Settings, "", "");

    /// <summary>Dedicated server without a player of its own.</summary>
    public static void StartServer(NetworkSettings? settings = null) => NetworkRuntime.Start(SessionRole.Server, settings ?? Settings, "", "");

    /// <summary>
    /// Joins a server: "192.168.0.5", "host:7777", "[::1]:7777", or "ws://host:7778" / "wss://example.com/game".
    /// Browsers always use WebSocket; there "host" means <see cref="NetworkSettings.WebSocketPort"/> and "host:port" that port.
    /// <paramref name="connectData"/> reaches the server's <see cref="NetworkServer.ConnectionApproval"/> (player name, password, ...).
    /// The result arrives as <see cref="NetworkClient.Connected"/> or <see cref="NetworkClient.Disconnected"/>.
    /// </summary>
    public static void StartClient(string address, string connectData = "", NetworkSettings? settings = null)
        => NetworkRuntime.Start(SessionRole.Client, settings ?? Settings, address, connectData);

    /// <summary>Ends the session; the other side is told <paramref name="reason"/>.</summary>
    public static void Stop(string reason = "Stopped") => NetworkRuntime.Stop(reason);

    internal static void RaiseStopped(string reason) => Stopped?.Invoke(reason);

    internal static void ResetState()
    {
        Settings = new NetworkSettings();
        Stopped = null;
    }
}
