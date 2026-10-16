using System;
using Funke.Networking.Internal;

namespace Funke.Networking;

/// <summary>The client side of a client or host: the connection to the server and messages from it.</summary>
public static class NetworkClient
{
    public static bool IsActive => NetworkManager.IsClient;
    /// <summary>True once the server accepted this client and sent the objects that already exist.</summary>
    public static bool IsConnected => NetworkRuntime.Session is { IsRunning: true, IsClient: true, ClientReady: true };
    public static int LocalClientId => NetworkManager.LocalClientId;
    /// <summary>The server; null before <see cref="NetworkManager.StartClient"/>.</summary>
    public static NetConnection? Connection => NetworkRuntime.Session is { IsClient: true } session ? session.ServerConnection : null;
    public static int RoundTripMs => Connection?.RoundTripMs ?? 0;

    /// <summary>Joined the server; existing objects are spawned already.</summary>
    public static event Action? Connected;
    /// <summary>The connection failed or ended without <see cref="NetworkManager.Stop"/>: rejected, server gone, timeout. Carries the reason.</summary>
    public static event Action<string>? Disconnected;

    /// <summary>Handles messages the server sends with <see cref="NetworkServer.Send{T}(NetConnection, in T, NetChannel)"/> or <see cref="NetworkServer.SendToAll{T}"/>.</summary>
    public static void RegisterHandler<T>(Action<T> handler) where T : INetMessage, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        NetworkRuntime.ClientMessages.Register<T>((_, message) => handler(message));
    }

    public static void UnregisterHandler<T>() where T : INetMessage => NetworkRuntime.ClientMessages.Unregister<T>();

    /// <summary>To the server's handler for <typeparamref name="T"/>.</summary>
    public static void Send<T>(in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
    {
        NetSession session = NetworkRuntime.Session is { IsRunning: true, IsClient: true } running
            ? running
            : throw new NetworkException("No client is running (NetworkManager.StartClient or StartHost).");
        session.SendUser(session.ServerConnection!, message, channel);
    }

    internal static void RaiseConnected() => Connected?.Invoke();
    internal static void RaiseDisconnected(string reason) => Disconnected?.Invoke(reason);

    internal static void ResetState()
    {
        Connected = null;
        Disconnected = null;
    }
}
