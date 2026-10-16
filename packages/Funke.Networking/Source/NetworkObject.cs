using System;
using Funke.Networking.Internal;

namespace Funke.Networking;

/// <summary>
/// Makes an entity known on every peer. On an entity placed in a scene it links the copies all peers load (the
/// scene must be the same everywhere); prefabs spawned with <see cref="NetworkServer.Spawn(Entity, int)"/> are
/// networked even without it, and get it as their API: owner, per-object messages.
/// </summary>
public class NetworkObject : EntityScript
{
    [ToolTip("Despawn this object when its owner leaves; off hands it back to the server.")]
    public bool DestroyWithOwner = true;

    private MessageRegistry? m_ServerHandlers;
    private MessageRegistry? m_ClientHandlers;

    internal NetObjectRecord? Record { get; set; }

    /// <summary>The same on every peer; 0 when the object isn't networked.</summary>
    public ulong NetId => Record?.NetId ?? 0;
    /// <summary>Client id of the owner; <see cref="NetworkManager.ServerClientId"/> for server-owned objects.</summary>
    public int OwnerId => Record?.Owner ?? NetworkManager.ServerClientId;
    public bool IsOwner => Record != null && NetworkManager.IsRunning && Record.Owner == NetworkManager.LocalClientId;
    /// <summary>Server-owned objects are the server's; client-owned ones the owner's.</summary>
    public bool IsSpawned => Record != null && NetworkManager.IsRunning;
    public bool IsSceneObject => Record?.IsSceneObject ?? false;

    /// <summary>Raised on every peer when <see cref="OwnerId"/> changes.</summary>
    public event Action<NetworkObject>? OwnerChanged;

    /// <summary>The NetworkObject of <paramref name="entity"/>, or null.</summary>
    public static NetworkObject? Of(Entity entity) => NetworkObjects.FindScript(entity);

    public override void OnAwake() => NetworkObjects.Attach(this);

    public override void OnDestroy()
    {
        NetworkObjects.Detach(this);
        m_ServerHandlers?.Clear();
        m_ClientHandlers?.Clear();
        OwnerChanged = null;
    }

    /// <summary>Server: hands the object to <paramref name="clientId"/> (or back to the server).</summary>
    public void SetOwner(int clientId) => NetworkObjects.SetOwner(this, clientId);

    /// <summary>Messages a client sends to this object with <see cref="SendToServer{T}"/>; <c>from</c> tells who.</summary>
    public void RegisterServerHandler<T>(Action<NetConnection, T> handler) where T : INetMessage, new()
        => (m_ServerHandlers ??= new MessageRegistry("NetworkObject.Server")).Register(handler);

    /// <summary>Messages the server sends to this object with <see cref="SendToClients{T}"/> or <see cref="SendToOwner{T}"/>.</summary>
    public void RegisterClientHandler<T>(Action<T> handler) where T : INetMessage, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        (m_ClientHandlers ??= new MessageRegistry("NetworkObject.Client")).Register<T>((_, message) => handler(message));
    }

    public void SendToServer<T>(in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
        => NetworkObjects.SendObjectMessage(this, message, channel, NetworkObjects.Target.Server);

    public void SendToClients<T>(in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
        => NetworkObjects.SendObjectMessage(this, message, channel, NetworkObjects.Target.Clients);

    public void SendToOwner<T>(in T message, NetChannel channel = NetChannel.Reliable) where T : INetMessage
        => NetworkObjects.SendObjectMessage(this, message, channel, NetworkObjects.Target.Owner);

    internal void DispatchServer(uint id, NetConnection from, NetReader reader) => Dispatch(m_ServerHandlers, id, from, reader);
    internal void DispatchClient(uint id, NetConnection from, NetReader reader) => Dispatch(m_ClientHandlers, id, from, reader);

    internal void RaiseOwnerChanged()
    {
        try { OwnerChanged?.Invoke(this); }
        catch (Exception e) { NetLog.Error($"OwnerChanged handler threw: {e}"); }
    }

    private void Dispatch(MessageRegistry? registry, uint id, NetConnection from, NetReader reader)
    {
        if (registry != null) registry.Dispatch(id, from, reader);
        else NetLog.Warn($"{Entity.Name} got an object message but registered no handler for it.");
    }
}
