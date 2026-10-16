using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Funke.Networking.Internal;

/// <summary>
/// RFC 6455 pieces the server needs, plus the batching both sides use: one WebSocket message carries every
/// game message of a frame as [packed length][bytes] records, which keeps per-message overhead and async sends low.
/// </summary>
internal static class WebSocketCodec
{
    internal const byte OpContinuation = 0x0;
    internal const byte OpText = 0x1;
    internal const byte OpBinary = 0x2;
    internal const byte OpClose = 0x8;
    internal const byte OpPing = 0x9;
    internal const byte OpPong = 0xA;

    internal const int CloseNormal = 1000;
    internal const int CloseGoingAway = 1001;
    internal const int CloseProtocolError = 1002;
    internal const int CloseTooBig = 1009;

    private const string k_AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int k_MaxHandshakeBytes = 8192;

    internal readonly struct FrameHeader
    {
        internal FrameHeader(bool fin, byte opcode, bool masked, long payloadLength, uint mask, int headerLength)
        {
            Fin = fin;
            Opcode = opcode;
            Masked = masked;
            PayloadLength = payloadLength;
            Mask = mask;
            HeaderLength = headerLength;
        }

        internal bool Fin { get; }
        internal byte Opcode { get; }
        internal bool Masked { get; }
        internal long PayloadLength { get; }
        internal uint Mask { get; }
        internal int HeaderLength { get; }
    }

    internal enum HandshakeResult { Incomplete, Ok, NotWebSocket, TooLarge }

    internal static string ComputeAccept(string key)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + k_AcceptGuid));
        return Convert.ToBase64String(hash);
    }

    /// <summary>Parses an HTTP upgrade request; <paramref name="consumed"/> covers the request including the blank line.</summary>
    internal static HandshakeResult ParseHandshake(ReadOnlySpan<byte> data, out int consumed, out string key)
    {
        consumed = 0;
        key = "";
        int end = data.IndexOf("\r\n\r\n"u8);
        if (end < 0) return data.Length > k_MaxHandshakeBytes ? HandshakeResult.TooLarge : HandshakeResult.Incomplete;
        consumed = end + 4;

        string request = Encoding.ASCII.GetString(data[..end]);
        string[] lines = request.Split("\r\n");
        if (lines.Length == 0 || !lines[0].StartsWith("GET ", StringComparison.Ordinal)) return HandshakeResult.NotWebSocket;

        bool upgrade = false;
        bool version = false;
        foreach (string line in lines.AsSpan(1))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
                upgrade = value.Contains("websocket", StringComparison.OrdinalIgnoreCase);
            else if (name.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                key = value;
            else if (name.Equals("Sec-WebSocket-Version", StringComparison.OrdinalIgnoreCase))
                version = value == "13";
        }
        return upgrade && version && key.Length > 0 ? HandshakeResult.Ok : HandshakeResult.NotWebSocket;
    }

    internal static byte[] HandshakeResponse(string key) => Encoding.ASCII.GetBytes(
        "HTTP/1.1 101 Switching Protocols\r\n" +
        "Upgrade: websocket\r\n" +
        "Connection: Upgrade\r\n" +
        $"Sec-WebSocket-Accept: {ComputeAccept(key)}\r\n\r\n");

    internal static byte[] UpgradeRequiredResponse() => Encoding.ASCII.GetBytes(
        "HTTP/1.1 426 Upgrade Required\r\n" +
        "Sec-WebSocket-Version: 13\r\n" +
        "Content-Type: text/plain\r\n" +
        "Connection: close\r\n" +
        "Content-Length: 41\r\n\r\n" +
        "This port is a Funke game WebSocket port.");

    internal static bool TryReadHeader(ReadOnlySpan<byte> data, out FrameHeader header)
    {
        header = default;
        if (data.Length < 2) return false;
        bool fin = (data[0] & 0x80) != 0;
        byte opcode = (byte)(data[0] & 0x0F);
        bool masked = (data[1] & 0x80) != 0;
        long length = data[1] & 0x7F;
        int offset = 2;
        if (length == 126)
        {
            if (data.Length < 4) return false;
            length = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
            offset = 4;
        }
        else if (length == 127)
        {
            if (data.Length < 10) return false;
            length = (long)BinaryPrimitives.ReadUInt64BigEndian(data[2..]);
            offset = 10;
        }
        uint mask = 0;
        if (masked)
        {
            if (data.Length < offset + 4) return false;
            mask = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += 4;
        }
        header = new FrameHeader(fin, opcode, masked, length, mask, offset);
        return true;
    }

    internal static void Unmask(Span<byte> payload, uint mask)
    {
        Span<byte> key = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(key, mask);
        for (int i = 0; i < payload.Length; i++) payload[i] ^= key[i & 3];
    }

    /// <summary>Server-to-client frame (never masked).</summary>
    internal static void WriteFrame(ByteQueue output, byte opcode, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[10];
        header[0] = (byte)(0x80 | opcode);
        int length;
        if (payload.Length < 126)
        {
            header[1] = (byte)payload.Length;
            length = 2;
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            header[1] = 126;
            BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)payload.Length);
            length = 4;
        }
        else
        {
            header[1] = 127;
            BinaryPrimitives.WriteUInt64BigEndian(header[2..], (ulong)payload.Length);
            length = 10;
        }
        output.Append(header[..length]);
        output.Append(payload);
    }

    internal static byte[] ClosePayload(int code, string reason)
    {
        // Control frames hold at most 125 bytes: 2 for the code, the rest for the reason.
        byte[] text = Encoding.UTF8.GetBytes(reason);
        int count = Math.Min(text.Length, 123);
        while (count > 0 && count < text.Length && (text[count] & 0xC0) == 0x80) count--;   // don't cut a UTF-8 sequence
        byte[] payload = new byte[2 + count];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)code);
        Array.Copy(text, 0, payload, 2, count);
        return payload;
    }

    internal static string CloseReason(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > 2)
        {
            try { return Encoding.UTF8.GetString(payload[2..]); }
            catch (Exception) { return TransportReasons.Closed; }
        }
        // Browsers close with 1001 and no text when the tab closes or navigates away.
        if (payload.Length == 2 && BinaryPrimitives.ReadUInt16BigEndian(payload) == CloseGoingAway) return "The browser page was closed";
        return TransportReasons.Closed;
    }

    /// <summary>Appends one game message as a [packed length][bytes] record.</summary>
    internal static void AppendRecord(ByteQueue batch, ReadOnlySpan<byte> message)
    {
        Span<byte> prefix = stackalloc byte[5];
        uint value = (uint)message.Length;
        int count = 0;
        while (value >= 0x80)
        {
            prefix[count++] = (byte)(value | 0x80);
            value >>= 7;
        }
        prefix[count++] = (byte)value;
        batch.Append(prefix[..count]);
        batch.Append(message);
    }

    /// <summary>The record at <paramref name="position"/>; throws <see cref="NetworkException"/> on a malformed batch.</summary>
    internal static ReadOnlySpan<byte> NextRecord(ReadOnlySpan<byte> batch, ref int position)
    {
        uint length = 0;
        for (int shift = 0; ; shift += 7)
        {
            if (position >= batch.Length || shift > 28) throw new NetworkException("Malformed WebSocket batch.");
            byte b = batch[position++];
            length |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
        }
        if (length > (uint)(batch.Length - position)) throw new NetworkException("Malformed WebSocket batch.");
        ReadOnlySpan<byte> record = batch.Slice(position, (int)length);
        position += (int)length;
        return record;
    }
}

/// <summary>A growable byte buffer that is written at the end and consumed from the front.</summary>
internal sealed class ByteQueue
{
    private byte[] m_Buffer;
    private int m_Start;
    private int m_End;

    internal ByteQueue(int capacity = 1024) => m_Buffer = new byte[capacity];

    internal int Count => m_End - m_Start;
    internal byte[] Buffer => m_Buffer;
    internal int Start => m_Start;
    internal ReadOnlySpan<byte> Data => m_Buffer.AsSpan(m_Start, m_End - m_Start);

    internal void Append(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(m_Buffer.AsSpan(m_End));
        m_End += data.Length;
    }

    /// <summary>Space for <paramref name="count"/> bytes at the end; commit what was written with <see cref="Commit"/>.</summary>
    internal Span<byte> Reserve(int count)
    {
        Ensure(count);
        return m_Buffer.AsSpan(m_End, count);
    }

    internal ArraySegment<byte> ReserveSegment(int count)
    {
        Ensure(count);
        return new ArraySegment<byte>(m_Buffer, m_End, count);
    }

    internal void Commit(int count) => m_End += count;

    internal void Consume(int count)
    {
        m_Start += count;
        if (m_Start >= m_End) Clear();
    }

    internal void Clear()
    {
        m_Start = 0;
        m_End = 0;
    }

    private void Ensure(int count)
    {
        if (m_End + count <= m_Buffer.Length) return;
        int used = m_End - m_Start;
        if (m_Start > 0 && used + count <= m_Buffer.Length)
        {
            Array.Copy(m_Buffer, m_Start, m_Buffer, 0, used);
        }
        else
        {
            int size = m_Buffer.Length * 2;
            while (size < used + count) size *= 2;
            byte[] grown = new byte[size];
            Array.Copy(m_Buffer, m_Start, grown, 0, used);
            m_Buffer = grown;
        }
        m_Start = 0;
        m_End = used;
    }
}
