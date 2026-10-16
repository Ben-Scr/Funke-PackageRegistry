using System;
using System.Text;
using Funke.Networking.Internal;
using Xunit;

namespace Funke.Networking.Tests;

public class SerializationTests
{
    [Fact]
    public void WriterAndReaderRoundTripEveryType()
    {
        var writer = new NetWriter(4);
        writer.Write(true);
        writer.Write((byte)200);
        writer.Write((short)-1234);
        writer.Write(123456789);
        writer.Write(ulong.MaxValue);
        writer.Write(3.25f);
        writer.Write(-2.5);
        writer.WritePacked(300u);
        writer.WritePacked(-70);
        writer.WritePacked(1UL << 63);
        writer.Write("Grüße 🙂");
        writer.Write((string?)null);
        writer.WriteBytes(new byte[] { 1, 2, 3 });
        writer.Write(new Vector2(1, -2));
        writer.Write(new Vector3(1, 2, 3));
        writer.Write(new Vector2Int(-5, 7));
        writer.Write(new Color(0.1f, 0.2f, 0.3f, 0.4f));

        var reader = new NetReader(writer.Written.ToArray());
        Assert.True(reader.ReadBool());
        Assert.Equal(200, reader.ReadByte());
        Assert.Equal(-1234, reader.ReadShort());
        Assert.Equal(123456789, reader.ReadInt());
        Assert.Equal(ulong.MaxValue, reader.ReadULong());
        Assert.Equal(3.25f, reader.ReadFloat());
        Assert.Equal(-2.5, reader.ReadDouble());
        Assert.Equal(300u, reader.ReadPackedUInt());
        Assert.Equal(-70, reader.ReadPackedInt());
        Assert.Equal(1UL << 63, reader.ReadPackedULong());
        Assert.Equal("Grüße 🙂", reader.ReadString());
        Assert.Equal("", reader.ReadString());
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadBytes());
        Assert.Equal(new Vector2(1, -2), reader.ReadVector2());
        Assert.Equal(new Vector3(1, 2, 3), reader.ReadVector3());
        Assert.Equal(new Vector2Int(-5, 7), reader.ReadVector2Int());
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), reader.ReadColor());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadingPastTheEndThrowsNetworkException()
    {
        var reader = new NetReader(new byte[] { 1, 2 });
        Assert.Throws<NetworkException>(() => reader.ReadInt());
    }

    [Fact]
    public void ForgedStringLengthIsRejected()
    {
        var writer = new NetWriter();
        writer.WritePacked(uint.MaxValue);
        var reader = new NetReader(writer.Written.ToArray());
        Assert.Throws<NetworkException>(() => reader.ReadString());
    }

    [Fact]
    public void MessageIdsAreStableAcrossRuns()
    {
        // Peers compute ids independently, so the hash must never change.
        Assert.Equal(MessageIds.Of(typeof(ChatMessage)), MessageId<ChatMessage>.Value);
        Assert.Equal(Fnv(typeof(ChatMessage).FullName!), MessageId<ChatMessage>.Value);
    }

    private static uint Fnv(string text)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return hash;
    }
}

public class WebSocketCodecTests
{
    [Fact]
    public void AcceptKeyMatchesRfc6455Example()
        => Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", WebSocketCodec.ComputeAccept("dGhlIHNhbXBsZSBub25jZQ=="));

    [Fact]
    public void ParsesABrowserHandshake()
    {
        byte[] request = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\nHost: localhost:7778\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\nOrigin: https://example.com\r\n\r\nextra");
        Assert.Equal(WebSocketCodec.HandshakeResult.Ok, WebSocketCodec.ParseHandshake(request, out int consumed, out string key));
        Assert.Equal("dGhlIHNhbXBsZSBub25jZQ==", key);
        Assert.Equal(request.Length - 5, consumed);
    }

    [Fact]
    public void PlainHttpIsNotAWebSocket()
    {
        byte[] request = Encoding.ASCII.GetBytes("GET /index.html HTTP/1.1\r\nHost: x\r\n\r\n");
        Assert.Equal(WebSocketCodec.HandshakeResult.NotWebSocket, WebSocketCodec.ParseHandshake(request, out _, out _));
        Assert.Equal(WebSocketCodec.HandshakeResult.Incomplete, WebSocketCodec.ParseHandshake(request.AsSpan(0, 10), out _, out _));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(300)]
    [InlineData(70000)]
    public void FrameHeaderRoundTrips(int length)
    {
        var output = new ByteQueue();
        WebSocketCodec.WriteFrame(output, WebSocketCodec.OpBinary, new byte[length]);
        Assert.True(WebSocketCodec.TryReadHeader(output.Data, out WebSocketCodec.FrameHeader header));
        Assert.True(header.Fin);
        Assert.Equal(WebSocketCodec.OpBinary, header.Opcode);
        Assert.False(header.Masked);
        Assert.Equal(length, header.PayloadLength);
        Assert.Equal(output.Count, header.HeaderLength + length);
    }

    [Fact]
    public void BatchRecordsRoundTrip()
    {
        var batch = new ByteQueue();
        WebSocketCodec.AppendRecord(batch, new byte[] { 1 });
        WebSocketCodec.AppendRecord(batch, new byte[200]);
        WebSocketCodec.AppendRecord(batch, Array.Empty<byte>());
        byte[] data = batch.Data.ToArray();
        int position = 0;
        Assert.Equal(1, WebSocketCodec.NextRecord(data, ref position).Length);
        Assert.Equal(200, WebSocketCodec.NextRecord(data, ref position).Length);
        Assert.Equal(0, WebSocketCodec.NextRecord(data, ref position).Length);
        Assert.Equal(data.Length, position);
        position = 2;   // the 200 byte record, cut short
        Assert.Throws<NetworkException>(() => WebSocketCodec.NextRecord(data.AsSpan(0, 10), ref position));
    }

    [Fact]
    public void ABrowserClosingItsTabGetsAReadableReason()
    {
        Assert.Equal("The browser page was closed", WebSocketCodec.CloseReason(new byte[] { 0x03, 0xE9 }));   // 1001
        Assert.Equal("Connection closed", WebSocketCodec.CloseReason(new byte[] { 0x03, 0xE8 }));             // 1000
        Assert.Equal("Bye", WebSocketCodec.CloseReason(WebSocketCodec.ClosePayload(1001, "Bye")));
    }

    [Fact]
    public void CloseReasonIsCutToTheControlFrameLimitWithoutBreakingUtf8()
    {
        byte[] payload = WebSocketCodec.ClosePayload(1000, new string('ä', 100));
        Assert.True(payload.Length <= 125);
        Assert.Equal(new string('ä', (payload.Length - 2) / 2), WebSocketCodec.CloseReason(payload));
    }
}

public class InterpolationTests
{
    private static TransformState At(float x) => new() { Position = new Vector3(x, 0, 0), Scale = new Vector3(1, 1, 1) };

    [Fact]
    public void BlendsBetweenTheTwoStatesAroundRenderTime()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(0, 1.0, At(0), 0.1);
        buffer.Add(0, 1.1, At(10), 0.1);
        Assert.True(buffer.Sample(1.05, 0, out TransformState state));
        Assert.Equal(5f, state.Position.X, 3);
    }

    [Fact]
    public void HoldsTheNewestStateWhenStarvedAndDropsReorderedOnes()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(0, 1.0, At(0), 0.1);
        buffer.Add(0, 1.1, At(10), 0.1);
        Assert.False(buffer.Add(0, 1.05, At(99), 0.1));
        Assert.True(buffer.Sample(5, 0, out TransformState state));
        Assert.Equal(10f, state.Position.X);
    }

    [Fact]
    public void ResumingAfterAPauseDoesNotStretchTheFirstBlend()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(0, 1.0, At(0), 0.1);
        buffer.Add(0, 5.0, At(10), 0.1);   // stood still for 4 s, then moved
        Assert.True(buffer.Sample(4.95, 0, out TransformState state));
        Assert.Equal(5f, state.Position.X, 3);
    }

    [Fact]
    public void TeleportsAreNotBlended()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(0, 1.0, At(0), 0.1);
        buffer.Add(0, 1.1, At(100), 0.1);
        Assert.True(buffer.Sample(1.05, 50, out TransformState state));
        Assert.Equal(0f, state.Position.X);
    }

    [Fact]
    public void AnglesBlendTheShortWayRound()
    {
        var a = new TransformState { Rotation = new Vector3(0, 0, 350) };
        var b = new TransformState { Rotation = new Vector3(0, 0, 10) };
        float z = TransformState.Lerp(a, b, 0.5f).Rotation.Z;
        Assert.True(Math.Abs(TransformState.DeltaAngle(z, 0)) < 0.01f, $"got {z}");
    }

    [Fact]
    public void ClockSyncTracksTheFastestPacketAndRisesSlowly()
    {
        var clock = new ClockSync();
        clock.Observe(localNow: 10.05, remoteTime: 100);    // 50 ms + offset
        clock.Observe(localNow: 10.18, remoteTime: 100.1);  // a late one: 80 ms
        Assert.InRange(clock.RemoteNow(10.2), 100.14, 100.151);
        clock.Observe(localNow: 10.22, remoteTime: 100.2);  // faster than ever: 20 ms
        Assert.Equal(100.23, clock.RemoteNow(10.25), 6);
    }
}
