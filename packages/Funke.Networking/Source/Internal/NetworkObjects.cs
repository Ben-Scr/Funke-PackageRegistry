using System;
using System.Collections.Generic;

namespace Funke.Networking.Internal;

/// <summary>A networked entity. Exists before (and without) its NetworkObject script: script instances only appear after Instantiate.</summary>
internal sealed class NetObjectRecord
{
    internal NetObjectRecord(ulong netId, Entity entity, bool isSceneObject, ulong prefabGuid, int owner)
    {
        NetId = netId;
        Entity = entity;
        IsSceneObject = isSceneObject;
        PrefabGuid = prefabGuid;
        Owner = owner;
    }

    internal ulong NetId { get; }
    internal Entity Entity { get; }
    internal bool IsSceneObject { get; }
    internal ulong PrefabGuid { get; }
    internal int Owner { get; set; }
    internal NetworkObject? Script { get; set; }
}

/// <summary>
/// The object layer: which entities are networked, spawning, ownership, per-object messages and transform sync.
/// Scene objects use their scene GUID as net id; spawned ones count up from <see cref="k_SpawnedBit"/>.
/// </summary>
internal static class NetworkObjects
{
    internal enum Target { Server, Clients, Owner }

    private const ulong k_SpawnedBit = 1UL << 63;
    private const int k_TransformPacketBudget = 900;
    private const double k_KeepAliveInterval = 1;
    private const byte k_FlagPosition = 1, k_FlagRotation = 2, k_FlagScale = 4, k_Flag3D = 8, k_FlagTeleport = 16;

    private static readonly Dictionary<ulong, NetObjectRecord> s_ByNetId = new();
    private static readonly Dictionary<ulong, NetObjectRecord> s_ByEntity = new();
    private static readonly Dictionary<ulong, NetworkTransformBase> s_Transforms = new();
    private static readonly HashSet<ulong> s_GoneSceneObjects = new();
    private static readonly HashSet<NetworkTransformBase> s_TeleportNext = new();
    private static readonly Dictionary<int, ClockSync> s_Clocks = new();
    private static readonly List<NetObjectRecord> s_Scratch = new();
    private static readonly NetWriter s_Writer = new(1024);
    private static readonly NetWriter s_Relay = new(1024);
    private static ulong s_NextSpawnId = 1;
    private static double s_NextTick;

    internal static void Reset()
    {
        s_ByNetId.Clear();
        s_ByEntity.Clear();
        s_Transforms.Clear();
        s_GoneSceneObjects.Clear();
        s_TeleportNext.Clear();
        s_Clocks.Clear();
        s_NextSpawnId = 1;
        s_NextTick = 0;
    }

    // ── Scripts ─────────────────────────────────────────────

    internal static void Attach(NetworkObject script)
    {
        NetworkRuntime.Hook();   // scene unloads must be seen even before a session starts
        Entity entity = script.Entity;
        if (s_ByEntity.TryGetValue(entity.ID, out NetObjectRecord? record))
        {
            record.Script = script;
            script.Record = record;
            return;
        }
        if (!entity.IsSceneEntity) return;   // a plain runtime copy: networked only when spawned through the server

        ulong netId = entity.SceneGUID;
        if (netId == 0) return;
        if (s_ByNetId.TryGetValue(netId, out NetObjectRecord? clash))
        {
            NetLog.Warn($"{entity.Name} and {clash.Entity.Name} share scene GUID {netId}; is the scene loaded twice? Only the first one is networked.");
            return;
        }
        record = new NetObjectRecord(netId, entity, isSceneObject: true, prefabGuid: 0, owner: NetworkManager.ServerClientId) { Script = script };
        script.Record = record;
        Add(record);
        if (s_GoneSceneObjects.Contains(netId) && NetworkRuntime.Session is { IsServer: false })
            Entity.Destroy(entity);   // the server destroyed it before this client loaded the scene
    }

    internal static void Detach(NetworkObject script)
    {
        // The record stays: the entity may outlive the script, and destruction is noticed by the liveness check.
        if (script.Record != null && script.Record.Script == script) script.Record.Script = null;
        script.Record = null;
    }

    internal static void AttachTransform(NetworkTransformBase transform) => s_Transforms[transform.Entity.ID] = transform;

    internal static void DetachTransform(NetworkTransformBase transform)
    {
        if (s_Transforms.TryGetValue(transform.Entity.ID, out NetworkTransformBase? current) && current == transform)
            s_Transforms.Remove(transform.Entity.ID);
        s_TeleportNext.Remove(transform);
    }

    internal static NetworkObject? FindScript(Entity entity)
        => entity != null && s_ByEntity.TryGetValue(entity.ID, out NetObjectRecord? record) ? record.Script : null;

    internal static NetObjectRecord? FindRecord(Entity entity)
        => entity != null && s_ByEntity.TryGetValue(entity.ID, out NetObjectRecord? record) ? record : null;

    // ── Spawning (server) ───────────────────────────────────

    internal static Entity Spawn(Entity prefab, Vector3? position, Vector3? rotation, int owner)
    {
        NetSession session = RequireServer("spawn objects");
        if (prefab == null || !prefab.IsPrefabInstance || prefab.PrefabGUID == 0)
            throw new NetworkException("NetworkServer.Spawn needs a prefab (a prefab field of a script), not a scene entity: every client instantiates it from its own copy of the prefab.");
        if (owner != NetworkManager.ServerClientId && session.GetClient(owner) == null)
            throw new NetworkException($"Cannot give the spawned object to client {owner}: no such client is connected.");

        // From the asset, as every client does: a prefab instance in the scene may have local overrides.
        Entity entity = Entity.Instantiate(Entity.FromPrefabGUID(prefab.PrefabGUID));
        if (!entity) throw new NetworkException($"Prefab {prefab.PrefabGUID} could not be instantiated.");
        SetTransform(entity, position, rotation);

        var record = new NetObjectRecord(k_SpawnedBit | s_NextSpawnId++, entity, isSceneObject: false, prefab.PrefabGUID, owner);
        Add(record);
        session.SendToClients(WriteSpawn(session, record).Written, NetChannel.Reliable, includeLocal: false);
        return entity;
    }

    internal static void Despawn(Entity entity)
    {
        NetSession session = RequireServer("despawn objects");
        if (!s_ByEntity.TryGetValue(entity.ID, out NetObjectRecord? record))
        {
            if (entity) Entity.Destroy(entity);
            return;
        }
        Remove(record);
        BroadcastGone(session, record);
        if (entity) Entity.Destroy(entity);
    }

    internal static void SetOwner(NetworkObject script, int owner)
    {
        NetSession session = RequireServer("change owners");
        NetObjectRecord record = script.Record ?? throw new NetworkException($"{script.Entity.Name} is not networked.");
        if (owner != NetworkManager.ServerClientId && session.GetClient(owner) == null)
            throw new NetworkException($"Client {owner} is not connected.");
        if (record.Owner == owner) return;
        record.Owner = owner;
        if (s_Transforms.TryGetValue(record.Entity.ID, out NetworkTransformBase? transform)) transform.HasSent = false;
        NetWriter writer = session.BeginPacket(PacketKind.Owner);
        writer.WritePacked(record.NetId);
        writer.WritePacked(owner);
        session.SendToClients(writer.Written, NetChannel.Reliable, includeLocal: false);
        script.RaiseOwnerChanged();
    }

    // ── Session hooks ───────────────────────────────────────

    internal static void WriteInitialState(NetSession session, NetConnection client)
    {
        foreach (NetObjectRecord record in s_ByNetId.Values)
        {
            if (!record.Entity) continue;
            if (!record.IsSceneObject)
            {
                session.SendRaw(client, WriteSpawn(session, record).Written, NetChannel.Reliable);
            }
            else if (record.Owner != NetworkManager.ServerClientId)
            {
                NetWriter writer = session.BeginPacket(PacketKind.Owner);
                writer.WritePacked(record.NetId);
                writer.WritePacked(record.Owner);
                session.SendRaw(client, writer.Written, NetChannel.Reliable);
            }
        }
        foreach (ulong netId in s_GoneSceneObjects)
        {
            NetWriter writer = session.BeginPacket(PacketKind.SceneObjectGone);
            writer.WritePacked(netId);
            session.SendRaw(client, writer.Written, NetChannel.Reliable);
        }
    }

    internal static void OnClientLeft(NetSession session, NetConnection client)
    {
        s_Clocks.Remove(client.Id);
        s_Scratch.Clear();
        foreach (NetObjectRecord record in s_ByNetId.Values)
            if (record.Owner == client.Id) s_Scratch.Add(record);
        foreach (NetObjectRecord record in s_Scratch)
        {
            bool destroy = record.Script?.DestroyWithOwner ?? !record.IsSceneObject;
            if (destroy && !record.IsSceneObject)
            {
                Despawn(record.Entity);
                continue;
            }
            record.Owner = NetworkManager.ServerClientId;
            NetWriter writer = session.BeginPacket(PacketKind.Owner);
            writer.WritePacked(record.NetId);
            writer.WritePacked(record.Owner);
            session.SendToClients(writer.Written, NetChannel.Reliable, includeLocal: false);
            record.Script?.RaiseOwnerChanged();
        }
        s_Scratch.Clear();
    }

    /// <summary>The client lost the server: what the server spawned is gone, scene objects go back to the server.</summary>
    internal static void OnClientSessionEnded()
    {
        s_Scratch.Clear();
        s_Scratch.AddRange(s_ByNetId.Values);
        foreach (NetObjectRecord record in s_Scratch)
        {
            if (record.IsSceneObject)
            {
                if (record.Owner != NetworkManager.ServerClientId)
                {
                    record.Owner = NetworkManager.ServerClientId;
                    record.Script?.RaiseOwnerChanged();
                }
                continue;
            }
            Remove(record);
            if (record.Entity) Entity.Destroy(record.Entity);
        }
        s_Scratch.Clear();
        s_GoneSceneObjects.Clear();
        s_Clocks.Clear();
        foreach (NetworkTransformBase transform in s_Transforms.Values) transform.Snapshots.Clear();
    }

    internal static void OnServerStopped()
    {
        s_Clocks.Clear();
        s_GoneSceneObjects.Clear();
        foreach (NetworkTransformBase transform in s_Transforms.Values) transform.Snapshots.Clear();
    }

    /// <summary>Entities of an unloaded scene die with it; that is not a despawn every client must hear about.</summary>
    internal static void ForgetDeadEntities()
    {
        s_Scratch.Clear();
        foreach (NetObjectRecord record in s_ByNetId.Values)
            if (!record.Entity) s_Scratch.Add(record);
        foreach (NetObjectRecord record in s_Scratch) Remove(record);
        s_Scratch.Clear();
        s_GoneSceneObjects.Clear();
    }

    internal static void ServerPacket(NetSession session, NetConnection from, PacketKind kind, NetReader reader)
    {
        switch (kind)
        {
            case PacketKind.Transforms:
                ReadTransforms(session, reader, from);
                break;
            case PacketKind.ObjectMessage:
                ulong netId = reader.ReadPackedULong();
                uint id = reader.ReadUInt();
                if (s_ByNetId.TryGetValue(netId, out NetObjectRecord? record) && record.Script != null)
                    record.Script.DispatchServer(id, from, reader);
                break;
        }
    }

    internal static void ClientPacket(NetSession session, PacketKind kind, NetReader reader)
    {
        switch (kind)
        {
            case PacketKind.Spawn:
                ReadSpawn(reader);
                break;
            case PacketKind.Despawn:
            {
                ulong netId = reader.ReadPackedULong();
                if (s_ByNetId.TryGetValue(netId, out NetObjectRecord? record))
                {
                    Remove(record);
                    if (record.Entity) Entity.Destroy(record.Entity);
                }
                break;
            }
            case PacketKind.SceneObjectGone:
            {
                ulong netId = reader.ReadPackedULong();
                s_GoneSceneObjects.Add(netId);
                if (s_ByNetId.TryGetValue(netId, out NetObjectRecord? record))
                {
                    Remove(record);
                    if (record.Entity) Entity.Destroy(record.Entity);
                }
                break;
            }
            case PacketKind.Owner:
            {
                ulong netId = reader.ReadPackedULong();
                int owner = reader.ReadPackedInt();
                if (s_ByNetId.TryGetValue(netId, out NetObjectRecord? record) && record.Owner != owner)
                {
                    record.Owner = owner;
                    if (s_Transforms.TryGetValue(record.Entity.ID, out NetworkTransformBase? transform)) transform.HasSent = false;
                    record.Script?.RaiseOwnerChanged();
                }
                break;
            }
            case PacketKind.Transforms:
                ReadTransforms(session, reader, null);
                break;
            case PacketKind.ObjectMessage:
            {
                ulong netId = reader.ReadPackedULong();
                uint id = reader.ReadUInt();
                if (s_ByNetId.TryGetValue(netId, out NetObjectRecord? record) && record.Script != null)
                    record.Script.DispatchClient(id, session.ServerConnection!, reader);
                break;
            }
        }
    }

    // ── Per frame ───────────────────────────────────────────

    internal static void Update(NetSession session, double now)
    {
        if (now >= s_NextTick)
        {
            double interval = 1.0 / Math.Max(1, session.ServerTickRate);
            s_NextTick = now - s_NextTick > interval ? now + interval : s_NextTick + interval;   // no burst after a hitch
            if (session.IsServer) DetectDestroyed(session);
            SendTransforms(session, now);
        }
        ApplyTransforms(session, now);
    }

    internal static bool HasAuthority(NetworkTransformBase transform)
    {
        NetSession? session = NetworkRuntime.Session;
        if (session == null || !session.IsRunning) return true;   // offline everything is local
        NetObjectRecord? record = FindRecord(transform.Entity);
        return record != null && IsAuthority(session, transform, record);
    }

    internal static void Teleport(NetworkTransformBase transform) => s_TeleportNext.Add(transform);

    private static bool IsAuthority(NetSession session, NetworkTransformBase transform, NetObjectRecord record)
    {
        // The host's own client is the server (id 0), so a client-owned object always belongs to a remote process.
        if (transform.Authority == NetAuthority.Owner && record.Owner != NetworkManager.ServerClientId)
            return !session.IsServer && session.LocalClientId == record.Owner;
        return session.IsServer;
    }

    private static void DetectDestroyed(NetSession session)
    {
        s_Scratch.Clear();
        foreach (NetObjectRecord record in s_ByNetId.Values)
            if (!record.Entity) s_Scratch.Add(record);
        foreach (NetObjectRecord record in s_Scratch)
        {
            Remove(record);
            BroadcastGone(session, record);
        }
        s_Scratch.Clear();
    }

    private static void SendTransforms(NetSession session, double now)
    {
        if (s_Transforms.Count == 0) return;
        if (session.IsClient && !session.IsServer && !session.ClientReady) return;
        int source = session.IsServer ? NetworkManager.ServerClientId : session.LocalClientId;
        BeginTransforms(session, s_Writer, source, now);
        int headerLength = s_Writer.Length;
        foreach (NetworkTransformBase transform in s_Transforms.Values)
        {
            if (!s_ByEntity.TryGetValue(transform.Entity.ID, out NetObjectRecord? record) || !IsAuthority(session, transform, record)) continue;
            TransformState state = transform.Read();
            bool teleport = s_TeleportNext.Remove(transform);
            if (!teleport && transform.HasSent && now - transform.LastSentTime < k_KeepAliveInterval && !Changed(transform.LastSent, state)) continue;
            transform.LastSent = state;
            transform.LastSentTime = now;
            transform.HasSent = true;
            WriteRecord(s_Writer, record.NetId, transform, state, teleport);
            if (s_Writer.Length >= k_TransformPacketBudget)
            {
                SendTransformPacket(session, s_Writer.Written);
                BeginTransforms(session, s_Writer, source, now);
            }
        }
        if (s_Writer.Length > headerLength) SendTransformPacket(session, s_Writer.Written);
    }

    private static void SendTransformPacket(NetSession session, ReadOnlySpan<byte> packet)
    {
        if (session.IsServer) session.SendToClients(packet, NetChannel.Unreliable, includeLocal: false);
        else session.SendRaw(session.ServerConnection!, packet, NetChannel.Unreliable);
    }

    private static void ApplyTransforms(NetSession session, double now)
    {
        foreach (NetworkTransformBase transform in s_Transforms.Values)
        {
            if (transform.Snapshots.Count == 0) continue;
            if (!s_ByEntity.TryGetValue(transform.Entity.ID, out NetObjectRecord? record) || IsAuthority(session, transform, record)) continue;
            if (!s_Clocks.TryGetValue(transform.Snapshots.Source, out ClockSync? clock)) continue;
            double renderTime = clock.RemoteNow(now) - transform.InterpolationDelay;
            if (transform.Snapshots.Sample(renderTime, transform.TeleportDistance, out TransformState state)) transform.Apply(state);
        }
    }

    private static void ReadTransforms(NetSession session, NetReader reader, NetConnection? from)
    {
        int source = reader.ReadPackedInt();
        double sendTime = reader.ReadDouble();
        if (from != null) source = from.Id;   // a client speaks only for itself
        double now = NetClock.Now;
        if (!s_Clocks.TryGetValue(source, out ClockSync? clock)) s_Clocks[source] = clock = new ClockSync();
        clock.Observe(now, sendTime);
        double interval = 1.0 / Math.Max(1, session.ServerTickRate);

        bool relay = from != null && session.IsServer;
        if (relay) BeginTransforms(session, s_Relay, source, sendTime);
        int relayHeader = s_Relay.Length;

        while (reader.Remaining > 0)
        {
            ulong netId = reader.ReadPackedULong();
            byte flags = reader.ReadByte();
            TransformState state = ReadState(reader, flags);
            if (!s_ByNetId.TryGetValue(netId, out NetObjectRecord? record)) continue;
            if (!s_Transforms.TryGetValue(record.Entity.ID, out NetworkTransformBase? transform)) continue;
            if (from != null && (transform.Authority != NetAuthority.Owner || record.Owner != from.Id)) continue;   // not theirs to move
            if (IsAuthority(session, transform, record)) continue;
            if ((flags & k_FlagTeleport) != 0) transform.Snapshots.Clear();
            transform.Snapshots.Add(source, sendTime, state, interval);
            if (relay)
            {
                WriteRecord(s_Relay, netId, transform, state, (flags & k_FlagTeleport) != 0);
                if (s_Relay.Length >= k_TransformPacketBudget)
                {
                    session.SendToClients(s_Relay.Written, NetChannel.Unreliable, exceptClientId: source, includeLocal: false);
                    BeginTransforms(session, s_Relay, source, sendTime);
                }
            }
        }
        if (relay && s_Relay.Length > relayHeader)
            session.SendToClients(s_Relay.Written, NetChannel.Unreliable, exceptClientId: source, includeLocal: false);
    }

    private static void BeginTransforms(NetSession session, NetWriter writer, int source, double time)
    {
        writer.Reset();
        writer.Write((byte)PacketKind.Transforms);
        writer.WritePacked(source);
        writer.Write(time);
    }

    private static void WriteRecord(NetWriter writer, ulong netId, NetworkTransformBase transform, in TransformState state, bool teleport)
    {
        byte flags = 0;
        if (transform.SyncPosition) flags |= k_FlagPosition;
        if (transform.SyncRotation) flags |= k_FlagRotation;
        if (transform.SyncScale) flags |= k_FlagScale;
        if (transform.Is3D) flags |= k_Flag3D;
        if (teleport) flags |= k_FlagTeleport;
        writer.WritePacked(netId);
        writer.Write(flags);
        bool is3D = transform.Is3D;
        if (transform.SyncPosition)
        {
            if (is3D) writer.Write(state.Position);
            else writer.Write(new Vector2(state.Position.X, state.Position.Y));
        }
        if (transform.SyncRotation)
        {
            if (is3D) writer.Write(state.Rotation);
            else writer.Write(state.Rotation.Z);
        }
        if (transform.SyncScale)
        {
            if (is3D) writer.Write(state.Scale);
            else writer.Write(new Vector2(state.Scale.X, state.Scale.Y));
        }
    }

    private static TransformState ReadState(NetReader reader, byte flags)
    {
        bool is3D = (flags & k_Flag3D) != 0;
        var state = new TransformState { Scale = new Vector3(1, 1, 1) };
        if ((flags & k_FlagPosition) != 0)
            state.Position = is3D ? reader.ReadVector3() : ToVector3(reader.ReadVector2(), 0);
        if ((flags & k_FlagRotation) != 0)
            state.Rotation = is3D ? reader.ReadVector3() : new Vector3(0, 0, reader.ReadFloat());
        if ((flags & k_FlagScale) != 0)
            state.Scale = is3D ? reader.ReadVector3() : ToVector3(reader.ReadVector2(), 1);
        return state;
    }

    private static bool Changed(in TransformState a, in TransformState b)
    {
        const float positionEpsilon = 0.0005f, rotationEpsilon = 0.01f, scaleEpsilon = 0.0005f;
        return Vector3.Distance(a.Position, b.Position) > positionEpsilon
            || Math.Abs(TransformState.DeltaAngle(a.Rotation.X, b.Rotation.X)) > rotationEpsilon
            || Math.Abs(TransformState.DeltaAngle(a.Rotation.Y, b.Rotation.Y)) > rotationEpsilon
            || Math.Abs(TransformState.DeltaAngle(a.Rotation.Z, b.Rotation.Z)) > rotationEpsilon
            || Vector3.Distance(a.Scale, b.Scale) > scaleEpsilon;
    }

    // ── Object messages ─────────────────────────────────────

    internal static void SendObjectMessage<T>(NetworkObject script, in T message, NetChannel channel, Target target) where T : INetMessage
    {
        NetSession session = NetworkRuntime.Session is { IsRunning: true } running ? running : throw new NetworkException("No network session is running.");
        NetObjectRecord record = script.Record ?? throw new NetworkException($"{script.Entity.Name} is not networked (spawn it with NetworkServer.Spawn or place it in the scene).");
        NetWriter writer = session.BeginPacket(PacketKind.ObjectMessage);
        writer.WritePacked(record.NetId);
        writer.Write(MessageId<T>.Value);
        message.Serialize(writer);
        switch (target)
        {
            case Target.Server:
                if (!session.IsClient) throw new NetworkException("SendToServer is for clients; the server can call the handler directly.");
                session.SendRaw(session.ServerConnection!, writer.Written, channel);
                break;
            case Target.Clients:
                if (!session.IsServer) throw new NetworkException("Only the server can send to clients.");
                session.SendToClients(writer.Written, channel);
                break;
            case Target.Owner:
                if (!session.IsServer) throw new NetworkException("Only the server can send to an owner.");
                NetConnection? owner = session.GetClient(record.Owner);
                if (owner != null) session.SendRaw(owner, writer.Written, channel);
                break;
        }
    }

    // ── Helpers ─────────────────────────────────────────────

    private static NetSession RequireServer(string action)
    {
        NetSession? session = NetworkRuntime.Session;
        if (session == null || !session.IsRunning || !session.IsServer)
            throw new NetworkException($"Only a running server or host can {action}.");
        return session;
    }

    private static void Add(NetObjectRecord record)
    {
        s_ByNetId[record.NetId] = record;
        s_ByEntity[record.Entity.ID] = record;
        if (record.Script == null)
        {
            // The script may have woken before the record existed.
            NetworkObject? script = record.Entity.GetScript(nameof(NetworkObject)) as NetworkObject;
            if (script != null)
            {
                record.Script = script;
                script.Record = record;
            }
        }
    }

    private static void Remove(NetObjectRecord record)
    {
        s_ByNetId.Remove(record.NetId);
        s_ByEntity.Remove(record.Entity.ID);
        if (record.Script != null) record.Script.Record = null;
    }

    private static void BroadcastGone(NetSession session, NetObjectRecord record)
    {
        NetWriter writer;
        if (record.IsSceneObject)
        {
            s_GoneSceneObjects.Add(record.NetId);
            writer = session.BeginPacket(PacketKind.SceneObjectGone);
        }
        else
        {
            writer = session.BeginPacket(PacketKind.Despawn);
        }
        writer.WritePacked(record.NetId);
        session.SendToClients(writer.Written, NetChannel.Reliable, includeLocal: false);
    }

    private static NetWriter WriteSpawn(NetSession session, NetObjectRecord record)
    {
        NetWriter writer = session.BeginPacket(PacketKind.Spawn);
        writer.WritePacked(record.NetId);
        writer.Write(record.PrefabGuid);
        writer.WritePacked(record.Owner);
        Transform3D? transform3D = record.Entity.GetComponent<Transform3D>();
        if (transform3D != null)
        {
            writer.Write(true);
            writer.Write(transform3D.Position);
            writer.Write(transform3D.Rotation);
        }
        else
        {
            Transform2D? transform2D = record.Entity.Transform;
            writer.Write(false);
            writer.Write(transform2D?.Position ?? default);
            writer.Write(transform2D?.Rotation ?? 0f);
        }
        return writer;
    }

    private static void ReadSpawn(NetReader reader)
    {
        ulong netId = reader.ReadPackedULong();
        ulong prefabGuid = reader.ReadULong();
        int owner = reader.ReadPackedInt();
        bool is3D = reader.ReadBool();
        Vector3 position;
        Vector3 rotation;
        if (is3D)
        {
            position = reader.ReadVector3();
            rotation = reader.ReadVector3();
        }
        else
        {
            position = ToVector3(reader.ReadVector2(), 0);
            rotation = new Vector3(0, 0, reader.ReadFloat());
        }
        if (s_ByNetId.ContainsKey(netId)) return;

        Entity prefab = Entity.FromPrefabGUID(prefabGuid);
        Entity entity = prefab ? Entity.Instantiate(prefab) : Entity.Invalid;
        if (!entity)
        {
            NetLog.Warn($"The server spawned prefab {prefabGuid}, but this game has no such prefab (different build?).");
            return;
        }
        SetTransform(entity, position, rotation);
        Add(new NetObjectRecord(netId, entity, isSceneObject: false, prefabGuid, owner));
    }

    private static void SetTransform(Entity entity, Vector3? position, Vector3? rotation)
    {
        Transform3D? transform3D = entity.GetComponent<Transform3D>();
        if (transform3D != null)
        {
            if (position.HasValue) transform3D.Position = position.Value;
            if (rotation.HasValue) transform3D.Rotation = rotation.Value;
            return;
        }
        Transform2D? transform2D = entity.Transform;
        if (transform2D == null) return;
        if (position.HasValue) transform2D.Position = new Vector2(position.Value.X, position.Value.Y);
        if (rotation.HasValue) transform2D.Rotation = rotation.Value.Z;
    }

    private static Vector3 ToVector3(Vector2 value, float z) => new(value.X, value.Y, z);
}
