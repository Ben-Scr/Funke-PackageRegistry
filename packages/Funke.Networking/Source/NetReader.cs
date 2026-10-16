using System;
using System.Buffers.Binary;
using System.Text;

namespace Funke.Networking;

/// <summary>
/// Reads what <see cref="NetWriter"/> wrote, in the same order. Reading past the end throws <see cref="NetworkException"/>;
/// the message is then dropped, so a malformed packet from a peer never takes the game down.
/// </summary>
public sealed class NetReader
{
    // Strings and byte arrays from a peer are capped so a forged length can't allocate gigabytes.
    private const int k_MaxStringBytes = 1 << 20;

    private byte[] m_Buffer = Array.Empty<byte>();
    private int m_Start;
    private int m_Position;
    private int m_End;

    public NetReader() { }

    public NetReader(byte[] buffer) => Reset(buffer, 0, buffer.Length);

    public int Position => m_Position - m_Start;
    public int Length => m_End - m_Start;
    public int Remaining => m_End - m_Position;

    internal void Reset(byte[] buffer, int offset, int length)
    {
        m_Buffer = buffer;
        m_Start = offset;
        m_Position = offset;
        m_End = offset + length;
    }

    public bool ReadBool() => ReadByte() != 0;

    public byte ReadByte()
    {
        Need(1);
        return m_Buffer[m_Position++];
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();
    public short ReadShort() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadUShort() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadInt() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long ReadLong() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public ulong ReadULong() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public float ReadFloat() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public uint ReadPackedUInt()
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = ReadByte();
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new NetworkException("Malformed packed integer.");
    }

    public int ReadPackedInt()
    {
        uint raw = ReadPackedUInt();
        return (int)(raw >> 1) ^ -(int)(raw & 1);
    }

    public ulong ReadPackedULong()
    {
        ulong value = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            byte b = ReadByte();
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new NetworkException("Malformed packed integer.");
    }

    public string ReadString()
    {
        int count = (int)ReadPackedUInt();
        if (count > k_MaxStringBytes) throw new NetworkException($"String of {count} bytes exceeds the {k_MaxStringBytes} byte limit.");
        return Encoding.UTF8.GetString(Take(count));
    }

    public byte[] ReadBytes()
    {
        int count = (int)ReadPackedUInt();
        if (count > k_MaxStringBytes) throw new NetworkException($"Byte array of {count} bytes exceeds the {k_MaxStringBytes} byte limit.");
        return Take(count).ToArray();
    }

    /// <summary>The next <paramref name="count"/> bytes without copying; valid until the handler returns.</summary>
    public ReadOnlySpan<byte> ReadRaw(int count) => Take(count);

    /// <summary>Everything not read yet; valid until the handler returns.</summary>
    public ReadOnlySpan<byte> ReadRemaining() => Take(Remaining);

    public Vector2 ReadVector2() => new(ReadFloat(), ReadFloat());
    public Vector3 ReadVector3() => new(ReadFloat(), ReadFloat(), ReadFloat());
    public Vector4 ReadVector4() => new(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
    public Vector2Int ReadVector2Int() => new(ReadPackedInt(), ReadPackedInt());
    public Vector3Int ReadVector3Int() => new(ReadPackedInt(), ReadPackedInt(), ReadPackedInt());
    public Color ReadColor() => new(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());

    public T Read<T>() where T : INetMessage, new()
    {
        T message = new();
        message.Deserialize(this);
        return message;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        Need(count);
        ReadOnlySpan<byte> span = m_Buffer.AsSpan(m_Position, count);
        m_Position += count;
        return span;
    }

    private void Need(int count)
    {
        if (count < 0 || m_Position + count > m_End)
            throw new NetworkException($"Message too short: wanted {count} more byte(s) at offset {Position} of {Length}.");
    }
}
