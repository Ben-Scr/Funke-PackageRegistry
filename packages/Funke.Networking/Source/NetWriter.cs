using System;
using System.Buffers.Binary;
using System.Text;

namespace Funke.Networking;

/// <summary>Little-endian binary writer for network messages. Integers can be written packed (<see cref="WritePacked(uint)"/>).</summary>
public sealed class NetWriter
{
    private byte[] m_Buffer;
    private int m_Length;

    public NetWriter(int capacity = 256) => m_Buffer = new byte[Math.Max(16, capacity)];

    public int Length => m_Length;
    public ReadOnlySpan<byte> Written => m_Buffer.AsSpan(0, m_Length);

    public void Reset() => m_Length = 0;

    public void Write(bool value) => Write(value ? (byte)1 : (byte)0);

    public void Write(byte value)
    {
        Ensure(1);
        m_Buffer[m_Length++] = value;
    }

    public void Write(sbyte value) => Write((byte)value);

    public void Write(short value) => BinaryPrimitives.WriteInt16LittleEndian(Take(2), value);
    public void Write(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), value);
    public void Write(int value) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), value);
    public void Write(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);
    public void Write(long value) => BinaryPrimitives.WriteInt64LittleEndian(Take(8), value);
    public void Write(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), value);
    public void Write(float value) => BinaryPrimitives.WriteSingleLittleEndian(Take(4), value);
    public void Write(double value) => BinaryPrimitives.WriteDoubleLittleEndian(Take(8), value);

    /// <summary>1 byte for 0..127, up to 5 bytes for large values.</summary>
    public void WritePacked(uint value)
    {
        Ensure(5);
        while (value >= 0x80)
        {
            m_Buffer[m_Length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        m_Buffer[m_Length++] = (byte)value;
    }

    /// <summary>Zig-zag packed: small negative numbers stay small too.</summary>
    public void WritePacked(int value) => WritePacked((uint)((value << 1) ^ (value >> 31)));

    public void WritePacked(ulong value)
    {
        Ensure(10);
        while (value >= 0x80)
        {
            m_Buffer[m_Length++] = (byte)(value | 0x80);
            value >>= 7;
        }
        m_Buffer[m_Length++] = (byte)value;
    }

    /// <summary>UTF-8 with a packed length; null is written as an empty string.</summary>
    public void Write(string? value)
    {
        value ??= "";
        int max = Encoding.UTF8.GetMaxByteCount(value.Length);
        Ensure(max + 5);
        int count = Encoding.UTF8.GetByteCount(value);
        WritePacked((uint)count);
        m_Length += Encoding.UTF8.GetBytes(value, m_Buffer.AsSpan(m_Length));
    }

    /// <summary>Length-prefixed bytes; read back with <see cref="NetReader.ReadBytes"/>.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WritePacked((uint)value.Length);
        WriteRaw(value);
    }

    /// <summary>Bytes without a length prefix.</summary>
    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        value.CopyTo(Take(value.Length));
    }

    public void Write(Vector2 value)
    {
        Write(value.X);
        Write(value.Y);
    }

    public void Write(Vector3 value)
    {
        Write(value.X);
        Write(value.Y);
        Write(value.Z);
    }

    public void Write(Vector4 value)
    {
        Write(value.X);
        Write(value.Y);
        Write(value.Z);
        Write(value.W);
    }

    public void Write(Vector2Int value)
    {
        WritePacked(value.X);
        WritePacked(value.Y);
    }

    public void Write(Vector3Int value)
    {
        WritePacked(value.X);
        WritePacked(value.Y);
        WritePacked(value.Z);
    }

    public void Write(Color value)
    {
        Write(value.R);
        Write(value.G);
        Write(value.B);
        Write(value.A);
    }

    public void Write<T>(in T message) where T : INetMessage => message.Serialize(this);

    internal void Patch(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(m_Buffer.AsSpan(offset, 2), value);

    private Span<byte> Take(int count)
    {
        Ensure(count);
        Span<byte> span = m_Buffer.AsSpan(m_Length, count);
        m_Length += count;
        return span;
    }

    private void Ensure(int count)
    {
        if (m_Length + count <= m_Buffer.Length) return;
        int size = m_Buffer.Length * 2;
        while (size < m_Length + count) size *= 2;
        Array.Resize(ref m_Buffer, size);
    }
}
