using System;
using System.Collections.Generic;
using System.Net;
using Funke.Networking.Internal;
using Xunit;

namespace Funke.Networking.Tests;

/// <summary>Real sockets on loopback: a server session and client sessions polled in turn, like two games would.</summary>
public class SessionTests : IDisposable
{
    private readonly List<NetSession> m_Sessions = new();

    public SessionTests() => Net.Init();

    public void Dispose()
    {
        foreach (NetSession session in m_Sessions) session.Stop("test over");
    }

    private (NetSession Session, FakeHandler Handler) Server(NetworkSettings settings, SessionRole role = SessionRole.Server)
    {
        var handler = new FakeHandler();
        var session = new NetSession(role, settings, handler);
        session.StartServer();
        m_Sessions.Add(session);
        return (session, handler);
    }

    private (NetSession Session, FakeHandler Handler) Client(NetworkSettings settings, string address, string connectData = "")
    {
        var handler = new FakeHandler();
        var session = new NetSession(SessionRole.Client, settings, handler);
        session.SetConnectData(connectData);
        session.StartClient(address);
        m_Sessions.Add(session);
        return (session, handler);
    }

    private static string UdpAddress(NetSession server) => $"127.0.0.1:{server.UdpPort}";

    [Theory]
    [InlineData(NetTransportKind.Udp)]
    [InlineData(NetTransportKind.WebSocket)]
    public void ClientJoinsAndMessagesFlowBothWays(NetTransportKind transport)
    {
        int wsPort = transport == NetTransportKind.WebSocket ? Net.FreeTcpPort() : 0;
        var (server, serverHandler) = Server(Net.Settings(wsPort));
        string address = transport == NetTransportKind.WebSocket ? $"ws://127.0.0.1:{wsPort}/" : UdpAddress(server);
        var (client, clientHandler) = Client(Net.Settings(wsPort), address, "Nova");

        var atServer = new List<(int From, ChatMessage Message)>();
        var atClient = new List<ChatMessage>();
        serverHandler.ServerMessages.Register<ChatMessage>((from, message) =>
        {
            atServer.Add((from.Id, message));
            server.SendToClients(server.BeginUser(new ChatMessage { Text = "echo " + message.Text, Number = message.Number + 1 }).Written, NetChannel.Reliable);
        });
        clientHandler.ClientMessages.Register<ChatMessage>((_, message) => atClient.Add(message));

        Assert.True(Net.PumpUntil(() => clientHandler.Connected, server, client), clientHandler.DisconnectReason ?? "no connect");
        Assert.Single(serverHandler.Joined);
        NetConnection joined = serverHandler.Joined[0];
        Assert.Equal(transport, joined.Transport);
        Assert.Equal("Nova", joined.ConnectData);
        Assert.Equal(joined.Id, client.LocalClientId);

        client.SendUser(client.ServerConnection!, new ChatMessage { Text = "hi", Number = 41 }, NetChannel.Reliable);
        Assert.True(Net.PumpUntil(() => atClient.Count == 1, server, client));
        Assert.Equal(("hi", 41, joined.Id), (atServer[0].Message.Text, atServer[0].Message.Number, atServer[0].From));
        Assert.Equal("echo hi", atClient[0].Text);
        Assert.Equal(42, atClient[0].Number);
    }

    [Theory]
    [InlineData(NetTransportKind.Udp)]
    [InlineData(NetTransportKind.WebSocket)]
    public void BigReliableMessagesArriveWhole(NetTransportKind transport)
    {
        int wsPort = transport == NetTransportKind.WebSocket ? Net.FreeTcpPort() : 0;
        var (server, serverHandler) = Server(Net.Settings(wsPort));
        string address = transport == NetTransportKind.WebSocket ? $"ws://127.0.0.1:{wsPort}/" : UdpAddress(server);
        var (client, clientHandler) = Client(Net.Settings(wsPort), address);
        byte[]? received = null;
        serverHandler.ServerMessages.Register<BlobMessage>((_, message) => received = message.Data);
        Assert.True(Net.PumpUntil(() => clientHandler.Connected, server, client));

        var data = new byte[200_000];
        new System.Random(5).NextBytes(data);
        client.SendUser(client.ServerConnection!, new BlobMessage { Data = data }, NetChannel.Reliable);
        Assert.True(Net.PumpUntil(() => received != null, server, client));
        Assert.Equal(data, received);
    }

    [Theory]
    [InlineData(NetTransportKind.Udp)]
    [InlineData(NetTransportKind.WebSocket)]
    public void OlderProtocolVersionIsRejectedWithAReadableReason(NetTransportKind transport)
    {
        int wsPort = transport == NetTransportKind.WebSocket ? Net.FreeTcpPort() : 0;
        NetworkSettings serverSettings = Net.Settings(wsPort);
        serverSettings.ProtocolVersion = 3;
        var (server, serverHandler) = Server(serverSettings);
        NetworkSettings clientSettings = Net.Settings(wsPort);
        clientSettings.ProtocolVersion = 2;
        string address = transport == NetTransportKind.WebSocket ? $"ws://127.0.0.1:{wsPort}/" : UdpAddress(server);
        var (client, clientHandler) = Client(clientSettings, address);

        Assert.True(Net.PumpUntil(() => clientHandler.DisconnectReason != null, server, client));
        Assert.Contains("protocol 3", clientHandler.DisconnectReason);
        Assert.Contains("this game 2", clientHandler.DisconnectReason);
        Assert.False(clientHandler.Connected);
        Assert.Empty(serverHandler.Joined);
        Assert.False(client.IsRunning);
    }

    [Fact]
    public void ApprovalCanTurnClientsAwayAndTheServerFills()
    {
        NetworkSettings settings = Net.Settings();
        settings.MaxConnections = 1;
        var (server, serverHandler) = Server(settings);
        serverHandler.Approval = request => request.ConnectData == "banned" ? "You are banned." : null;

        var (banned, bannedHandler) = Client(Net.Settings(), UdpAddress(server), "banned");
        Assert.True(Net.PumpUntil(() => bannedHandler.DisconnectReason != null, server, banned));
        Assert.Equal("You are banned.", bannedHandler.DisconnectReason);

        var (first, firstHandler) = Client(Net.Settings(), UdpAddress(server), "a");
        Assert.True(Net.PumpUntil(() => firstHandler.Connected, server, first));
        var (second, secondHandler) = Client(Net.Settings(), UdpAddress(server), "b");
        Assert.True(Net.PumpUntil(() => secondHandler.DisconnectReason != null, server, first, second));
        Assert.Contains("full", secondHandler.DisconnectReason);
        Assert.True(first.ClientReady);
    }

    [Theory]
    [InlineData(NetTransportKind.Udp)]
    [InlineData(NetTransportKind.WebSocket)]
    public void ClientsLearnWhyTheServerWentAway(NetTransportKind transport)
    {
        int wsPort = transport == NetTransportKind.WebSocket ? Net.FreeTcpPort() : 0;
        var (server, serverHandler) = Server(Net.Settings(wsPort));
        string address = transport == NetTransportKind.WebSocket ? $"ws://127.0.0.1:{wsPort}/" : UdpAddress(server);
        var (client, clientHandler) = Client(Net.Settings(wsPort), address);
        Assert.True(Net.PumpUntil(() => clientHandler.Connected, server, client));

        server.Stop("Round over");
        Assert.True(Net.PumpUntil(() => clientHandler.DisconnectReason != null, client));
        Assert.Equal("Round over", clientHandler.DisconnectReason);
    }

    [Theory]
    [InlineData(NetTransportKind.Udp)]
    [InlineData(NetTransportKind.WebSocket)]
    public void KickedAndLeavingClientsAreReported(NetTransportKind transport)
    {
        int wsPort = transport == NetTransportKind.WebSocket ? Net.FreeTcpPort() : 0;
        var (server, serverHandler) = Server(Net.Settings(wsPort));
        string address = transport == NetTransportKind.WebSocket ? $"ws://127.0.0.1:{wsPort}/" : UdpAddress(server);
        var (kicked, kickedHandler) = Client(Net.Settings(wsPort), address);
        var (leaver, leaverHandler) = Client(Net.Settings(wsPort), address);
        Assert.True(Net.PumpUntil(() => kickedHandler.Connected && leaverHandler.Connected, server, kicked, leaver));

        server.Disconnect(server.GetClient(kicked.LocalClientId)!, "Cheating");
        Assert.True(Net.PumpUntil(() => kickedHandler.DisconnectReason != null, server, kicked, leaver));
        Assert.Equal("Cheating", kickedHandler.DisconnectReason);

        leaver.Stop("Bye");
        Assert.True(Net.PumpUntil(() => serverHandler.Left.Count == 2, server));
        Assert.Equal("Bye", serverHandler.Left[1].Reason);
        Assert.Empty(server.Clients);
    }

    [Fact]
    public void UnreachableServerFailsWithAClearReason()
    {
        var handler = new FakeHandler();
        NetworkSettings settings = Net.Settings();
        settings.ConnectTimeout = 1;
        var client = new NetSession(SessionRole.Client, settings, handler);
        m_Sessions.Add(client);
        client.StartClient("127.0.0.1:1");
        Assert.True(Net.PumpUntil(() => handler.DisconnectReason != null, 6, client));
        Assert.Contains("does not answer", handler.DisconnectReason);
    }

    [Fact]
    public void HostTalksToItselfThroughTheLoopback()
    {
        var (host, handler) = Server(Net.Settings(), SessionRole.Host);
        var atServer = new List<int>();
        var atClient = new List<string>();
        handler.ServerMessages.Register<ChatMessage>((from, message) =>
        {
            atServer.Add(from.Id);
            host.SendToClients(host.BeginUser(new ChatMessage { Text = "back" }).Written, NetChannel.Reliable);
        });
        handler.ClientMessages.Register<ChatMessage>((_, message) => atClient.Add(message.Text));

        Assert.True(Net.PumpUntil(() => handler.Connected, host));
        Assert.Equal(NetworkManager.ServerClientId, Assert.Single(handler.Joined).Id);
        Assert.True(handler.Joined[0].IsLocal);

        host.SendUser(host.ServerConnection!, new ChatMessage { Text = "me" }, NetChannel.Reliable);
        Assert.True(Net.PumpUntil(() => atClient.Count == 1, host));
        Assert.Equal(new[] { NetworkManager.ServerClientId }, atServer);
        Assert.Equal("back", atClient[0]);
    }

    [Fact]
    public void UnreliableMessagesAreLimitedToOnePacket()
    {
        var (host, _) = Server(Net.Settings(), SessionRole.Host);
        var big = new BlobMessage { Data = new byte[NetworkSettings.MaxUnreliableSize + 1] };
        var e = Assert.Throws<NetworkException>(() => host.SendUser(host.ServerConnection!, big, NetChannel.Unreliable));
        Assert.Contains("Reliable", e.Message);
    }

    [Fact]
    public void MalformedMessagesAreDroppedNotFatal()
    {
        var (server, serverHandler) = Server(Net.Settings());
        var (client, clientHandler) = Client(Net.Settings(), UdpAddress(server));
        int received = 0;
        serverHandler.ServerMessages.Register<ChatMessage>((_, _) => received++);
        Assert.True(Net.PumpUntil(() => clientHandler.Connected, server, client));

        // A ChatMessage id with a truncated body.
        NetWriter writer = client.BeginPacket(PacketKind.User);
        writer.Write(MessageId<ChatMessage>.Value);
        writer.WritePacked(50u);
        client.SendRaw(client.ServerConnection!, writer.Written, NetChannel.Reliable);
        client.SendUser(client.ServerConnection!, new ChatMessage { Text = "ok" }, NetChannel.Reliable);

        Assert.True(Net.PumpUntil(() => received == 1, server, client));
        Assert.Contains(Net.Warnings, warning => warning.Contains("malformed"));
        Assert.True(client.ClientReady);
    }

    [Fact]
    public void LanDiscoveryFindsTheServer()
    {
        NetworkSettings settings = Net.Settings();
        settings.ServerName = "Test Lobby";
        settings.DiscoveryInfo = "map=forest";
        var (server, _) = Server(settings);
        var found = new List<DiscoveredServer>();
        using var discovery = new UdpDiscoveryClient((from, data) =>
        {
            if (Discovery.TryReadAnswer(from, data, "NetTests", 1, out _, out DiscoveredServer answer)) found.Add(answer);
        });
        discovery.Broadcast(Discovery.Request("NetTests"), server.UdpPort);
        DateTime end = DateTime.UtcNow.AddSeconds(3);
        while (found.Count == 0 && DateTime.UtcNow < end)
        {
            server.Poll();
            server.Flush();
            discovery.Poll(2);
            System.Threading.Thread.Sleep(2);
        }
        Assert.Contains(found, s => s.Port == server.UdpPort);
        DiscoveredServer hit = found.Find(s => s.Port == server.UdpPort);
        Assert.Equal("Test Lobby", hit.Name);
        Assert.Equal("map=forest", hit.Info);
        Assert.True(hit.IsCompatible);
    }

    [Fact]
    public void DiscoveryIgnoresOtherGames()
    {
        NetworkSettings settings = Net.Settings();
        Assert.Null(Discovery.Answer(Discovery.Request("SomethingElse"), settings, 1, 0, 7777, 0));
        Assert.NotNull(Discovery.Answer(Discovery.Request("NetTests"), settings, 1, 0, 7777, 0));
        Assert.Null(Discovery.Answer(new byte[] { 1, 2, 3 }, settings, 1, 0, 7777, 0));
    }

    [Theory]
    [InlineData("192.168.0.5", 7777, "192.168.0.5", 7777)]
    [InlineData("host:9000", 7777, "host", 9000)]
    [InlineData("[::1]:9000", 7777, "::1", 9000)]
    [InlineData("::1", 7777, "::1", 7777)]
    public void AddressesParse(string address, int defaultPort, string host, int port)
    {
        NetSession.ParseHostPort(address, defaultPort, out string parsedHost, out int parsedPort);
        Assert.Equal((host, port), (parsedHost, parsedPort));
    }

    [Fact]
    public void UdpPortInUseGivesAClearError()
    {
        var (server, _) = Server(Net.Settings());
        NetworkSettings clash = Net.Settings();
        clash.Port = server.UdpPort;
        var session = new NetSession(SessionRole.Server, clash, new FakeHandler());
        var e = Assert.Throws<NetworkException>(session.StartServer);
        Assert.Contains("in use", e.Message);
    }
}
