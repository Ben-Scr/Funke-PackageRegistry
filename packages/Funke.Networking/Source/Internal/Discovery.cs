using System;
using System.Net;

namespace Funke.Networking.Internal;

/// <summary>LAN discovery packets: a broadcast request on the game port, answered by every server of the same game.</summary>
internal static class Discovery
{
    private static ReadOnlySpan<byte> RequestMagic => "FNKQ"u8;
    private static ReadOnlySpan<byte> AnswerMagic => "FNKA"u8;
    private const byte k_Version = 1;

    internal static byte[] Request(string gameId)
    {
        var writer = new NetWriter(64);
        writer.WriteRaw(RequestMagic);
        writer.Write(k_Version);
        writer.Write(gameId);
        return writer.Written.ToArray();
    }

    internal static byte[]? Answer(ReadOnlySpan<byte> request, NetworkSettings settings, ulong instanceId, int players, int udpPort, int webSocketPort)
    {
        if (request.Length < 5 || !request[..4].SequenceEqual(RequestMagic) || request[4] != k_Version) return null;
        try
        {
            var reader = new NetReader(request[5..].ToArray());
            if (reader.ReadString() != settings.GameId) return null;
        }
        catch (NetworkException)
        {
            return null;
        }
        var writer = new NetWriter(128);
        writer.WriteRaw(AnswerMagic);
        writer.Write(k_Version);
        writer.Write(settings.GameId);
        writer.Write(instanceId);
        writer.WritePacked(settings.ProtocolVersion);
        writer.Write(settings.ServerName);
        writer.WritePacked(players);
        writer.WritePacked(settings.MaxConnections);
        writer.WritePacked(udpPort);
        writer.WritePacked(webSocketPort);
        writer.Write(settings.DiscoveryInfo);
        return writer.Written.ToArray();
    }

    internal static bool TryReadAnswer(IPEndPoint from, ReadOnlySpan<byte> data, string gameId, int protocolVersion, out ulong instanceId, out DiscoveredServer server)
    {
        server = default;
        instanceId = 0;
        if (data.Length < 5 || !data[..4].SequenceEqual(AnswerMagic) || data[4] != k_Version) return false;
        try
        {
            var reader = new NetReader(data[5..].ToArray());
            if (reader.ReadString() != gameId) return false;
            instanceId = reader.ReadULong();
            int version = reader.ReadPackedInt();
            string name = reader.ReadString();
            int players = reader.ReadPackedInt();
            int max = reader.ReadPackedInt();
            int port = reader.ReadPackedInt();
            int webSocketPort = reader.ReadPackedInt();
            string info = reader.ReadString();
            IPAddress address = from.Address.IsIPv4MappedToIPv6 ? from.Address.MapToIPv4() : from.Address;
            server = new DiscoveredServer(address.ToString(), port, webSocketPort, name, players, max, info, version == protocolVersion);
            return true;
        }
        catch (NetworkException)
        {
            return false;
        }
    }
}
