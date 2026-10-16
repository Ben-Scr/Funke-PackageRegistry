using System;
using System.Collections.Generic;
using Funke.Networking.Internal;

namespace Funke.Networking;

/// <summary>The server side of a host or dedicated server: clients, messages from them, spawning.</summary>
public static class NetworkServer
{
    public static bool IsActive => NetworkManager.IsServer;

    /// <summary>Connected clients; on a host its own client (id 0, <see cref="NetConnection.IsLocal"/>) comes first.</summary>
    public static IReadOnlyList<NetConnection> Clients => NetworkRuntime.Session is { IsRunning: true, IsServer: true } session ? session.Clients : Array.Empty<NetConnection>();

    /// <summary>Decides about each joining client: return null to accept, or the reason it is turned away.</summary>
    public static Func<NetConnectRequest, string?>? ConnectionApproval { get; set; }

    /// <summary>A client finished joining (on a host also its own client, one frame after start).</summary>
    public static event Action<NetConnection>? ClientConnected;
    /// <summary>A client left, timed out or was kicked. Its objects are already despawned.</summary>
    public static event Action<NetConnection, string>? ClientDisconnected;

    public static NetConnection? GetClient(int clientId) => NetworkRuntime.Session is { IsRunning: true, IsServer: true } session ? session.GetClient(clientId) : null;

    /// <summary>Handles messages clients send with <see cref="NetworkClient.Send{T}"/>. One handler per type; registering again replaces it.</summary>
    public static void RegisterHandler<T>(Action<NetConnection, T> handler) where T : INetMessage, new() => NetworkRuntime.ServerMessages.Register(handler);

    public static void UnregisterHandler<T>() where T : INetMessage => NetworkRuntime.ServerMessages.Unregister<T>();

    public static void Send<T>(NetConnection to, in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
        => Require().SendUser(to, message, channel);

    public static void Send<T>(int clientId, in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
    {
        NetSession session = Require();
        NetConnection to = session.GetClient(clientId) ?? throw new NetworkException($"Client {clientId} is not connected.");
        session.SendUser(to, message, channel);
    }

    /// <summary>To every client (the host's own included), except <paramref name="exceptClientId"/> if given.</summary>
    public static void SendToAll<T>(in T message, NetChannel channel = NetChannel.Reliable, int exceptClientId = -1) where T : INetMessage
    {
        NetSession session = Require();
        session.SendToClients(session.BeginUser(message).Written, channel, exceptClientId);
    }

    public static void Kick(NetConnection client, string reason = "Kicked by the server") => client.Disconnect(reason);

    /// <summary>Instantiates <paramref name="prefab"/> here and on every client (also on clients that join later).</summary>
    public static Entity Spawn(Entity prefab, int ownerId = NetworkManager.ServerClientId)
        => NetworkObjects.Spawn(prefab, null, null, ownerId);

    /// <summary>2D: position and rotation in degrees. 3D entities get the position, rotation stays the prefab's.</summary>
    public static Entity Spawn(Entity prefab, Vector3 position, float rotation = 0f, int ownerId = NetworkManager.ServerClientId)
        => NetworkObjects.Spawn(prefab, position, new Vector3(0, 0, rotation), ownerId);

    /// <summary>3D: position and Euler rotation in degrees.</summary>
    public static Entity Spawn(Entity prefab, Vector3 position, Vector3 rotation, int ownerId = NetworkManager.ServerClientId)
        => NetworkObjects.Spawn(prefab, position, rotation, ownerId);

    /// <summary>Destroys a networked entity here and on every client. Destroying it any other way works too, one tick later.</summary>
    public static void Despawn(Entity entity) => NetworkObjects.Despawn(entity);

    internal static void RaiseClientConnected(NetConnection client) => ClientConnected?.Invoke(client);
    internal static void RaiseClientDisconnected(NetConnection client, string reason) => ClientDisconnected?.Invoke(client, reason);

    internal static void ResetState()
    {
        ConnectionApproval = null;
        ClientConnected = null;
        ClientDisconnected = null;
    }

    private static NetSession Require()
        => NetworkRuntime.Session is { IsRunning: true, IsServer: true } session ? session : throw new NetworkException("No server is running (NetworkManager.StartHost or StartServer).");
}
