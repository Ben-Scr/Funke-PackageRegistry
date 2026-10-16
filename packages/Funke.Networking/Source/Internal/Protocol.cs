using System;
using System.Text;

namespace Funke.Networking.Internal;

/// <summary>First byte of every packet.</summary>
internal enum PacketKind : byte
{
    Hello = 1,       // client -> server: game id, protocol version, connect data
    Welcome = 2,     // server -> client: client id, tick rate
    Ready = 3,       // server -> client: initial objects sent
    Ping = 4,        // client -> server (WebSocket only; UDP has its own)
    Pong = 5,
    User = 10,       // type hash + payload

    // Handled by the object layer.
    Spawn = 20,
    Despawn = 21,
    Owner = 22,
    SceneObjectGone = 23,
    Transforms = 24,
    ObjectMessage = 25,
}

internal static class MessageIds
{
    /// <summary>FNV-1a of the full type name: the same on every peer without a registration handshake.</summary>
    internal static uint Of(Type type)
    {
        string name = type.FullName ?? type.Name;
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }
}

internal static class MessageId<T>
{
    internal static readonly uint Value = MessageIds.Of(typeof(T));
}
