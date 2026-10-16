using System;
using System.Collections.Generic;

namespace Funke.Networking.Internal;

/// <summary>Message handlers of one side (server or client), keyed by <see cref="MessageIds"/>.</summary>
internal sealed class MessageRegistry
{
    private readonly struct Entry
    {
        internal Entry(Type type, Action<NetConnection, NetReader> invoke)
        {
            Type = type;
            Invoke = invoke;
        }

        internal Type Type { get; }
        internal Action<NetConnection, NetReader> Invoke { get; }
    }

    private readonly Dictionary<uint, Entry> m_Handlers = new();
    private readonly HashSet<uint> m_WarnedUnknown = new();
    private readonly string m_Side;

    internal MessageRegistry(string side) => m_Side = side;

    internal void Register<T>(Action<NetConnection, T> handler) where T : INetMessage, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        uint id = MessageId<T>.Value;
        if (m_Handlers.TryGetValue(id, out Entry existing) && existing.Type != typeof(T))
            throw new NetworkException($"{typeof(T).FullName} and {existing.Type.FullName} map to the same message id; rename one of them.");
        m_Handlers[id] = new Entry(typeof(T), (from, reader) =>
        {
            T message = new();
            message.Deserialize(reader);
            handler(from, message);
        });
    }

    internal void Unregister<T>() where T : INetMessage => m_Handlers.Remove(MessageId<T>.Value);

    internal void Clear()
    {
        m_Handlers.Clear();
        m_WarnedUnknown.Clear();
    }

    internal void Dispatch(uint id, NetConnection from, NetReader reader)
    {
        if (!m_Handlers.TryGetValue(id, out Entry entry))
        {
            if (m_WarnedUnknown.Add(id))
                NetLog.Warn($"No {m_Side} handler for message 0x{id:X8} from {from}. Register one with {m_Side}.RegisterHandler<T>() (the type's full name must match on both sides).");
            return;
        }
        try
        {
            entry.Invoke(from, reader);
        }
        catch (NetworkException e)
        {
            NetLog.Warn($"Dropped malformed {entry.Type.Name} from {from}: {e.Message}");
        }
        catch (Exception e)
        {
            NetLog.Error($"{m_Side} handler for {entry.Type.Name} threw: {e}");
        }
    }
}
