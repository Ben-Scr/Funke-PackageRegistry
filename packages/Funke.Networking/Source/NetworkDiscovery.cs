using System;
using System.Collections.Generic;
using Funke.Networking.Internal;

namespace Funke.Networking;

/// <summary>
/// Finds servers of this game in the local network (UDP broadcast on <see cref="NetworkSettings.Port"/>). Not
/// available in browsers; on iOS broadcasts need Apple's multicast entitlement, joining by address works without it.
/// </summary>
public static class NetworkDiscovery
{
    private const double k_BroadcastInterval = 1;

    private static readonly List<DiscoveredServer> s_Servers = new();
    private static readonly List<ulong> s_InstanceIds = new();
    private static UdpDiscoveryClient? s_Client;
    private static NetworkSettings? s_Settings;
    private static double s_NextBroadcast;
    private static double s_LastPoll;

    public static bool IsSupported => !OperatingSystem.IsBrowser();
    public static bool IsSearching => s_Client != null;
    /// <summary>Servers that answered since <see cref="StartSearch"/>, in the order they answered.</summary>
    public static IReadOnlyList<DiscoveredServer> Servers => s_Servers;

    /// <summary>A new server answered, or a known one sent new details (players, info).</summary>
    public static event Action<DiscoveredServer>? ServerFound;

    /// <summary>Asks the network every second until <see cref="StopSearch"/>. Uses <see cref="NetworkManager.Settings"/> unless given others.</summary>
    public static void StartSearch(NetworkSettings? settings = null)
    {
        if (!IsSupported) throw new NetworkException("LAN discovery needs UDP, which browsers don't have. Join by address instead.");
        StopSearch();
        s_Settings = (settings ?? NetworkManager.Settings).Clone();
        s_Servers.Clear();
        s_InstanceIds.Clear();
        s_Client = new UdpDiscoveryClient(OnAnswer);
        s_NextBroadcast = 0;
        s_LastPoll = NetClock.Now;
        NetworkRuntime.EnsureDriver();
    }

    public static void StopSearch()
    {
        s_Client?.Dispose();
        s_Client = null;
    }

    internal static void Poll()
    {
        if (s_Client == null || s_Settings == null) return;
        double now = NetClock.Now;
        if (now >= s_NextBroadcast)
        {
            s_NextBroadcast = now + k_BroadcastInterval;
            s_Client.Broadcast(Discovery.Request(s_Settings.GameId), s_Settings.Port);
        }
        s_Client.Poll((float)((now - s_LastPoll) * 1000));
        s_LastPoll = now;
    }

    internal static void Reset()
    {
        StopSearch();
        s_Servers.Clear();
        s_InstanceIds.Clear();
        ServerFound = null;
    }

    private static void OnAnswer(System.Net.IPEndPoint from, ReadOnlySpan<byte> data)
    {
        if (s_Settings == null || !Discovery.TryReadAnswer(from, data, s_Settings.GameId, s_Settings.ProtocolVersion, out ulong instanceId, out DiscoveredServer server)) return;
        // One server answers on every interface (LAN, loopback, IPv6 link-local); list it once, by its most useful address.
        int index = s_InstanceIds.IndexOf(instanceId);
        if (index >= 0)
        {
            DiscoveredServer known = s_Servers[index];
            if (Rank(known.Address) > Rank(server.Address)) server = server.WithAddress(known.Address);
            if (Equals(known, server)) return;
            s_Servers[index] = server;
        }
        else
        {
            s_InstanceIds.Add(instanceId);
            s_Servers.Add(server);
        }
        try { ServerFound?.Invoke(server); }
        catch (Exception e) { NetLog.Error($"ServerFound handler threw: {e}"); }
    }

    private static int Rank(string address)
    {
        if (!System.Net.IPAddress.TryParse(address, out System.Net.IPAddress? ip)) return 0;
        bool loopback = System.Net.IPAddress.IsLoopback(ip);
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return loopback ? 2 : 4;
        return loopback || ip.IsIPv6LinkLocal ? 1 : 3;
    }
}
