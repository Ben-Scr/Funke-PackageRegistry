using System;
using System.Runtime.Loader;
using System.Threading;
using Funke.Coroutines;

namespace Funke.Networking.Internal;

/// <summary>Owns the running session, bridges it to the public events and pumps everything once per frame.</summary>
internal static class NetworkRuntime
{
    private sealed class Handler : ISessionHandler
    {
        public MessageRegistry ServerMessages { get; } = new("NetworkServer");
        public MessageRegistry ClientMessages { get; } = new("NetworkClient");

        public string? Approve(NetConnectRequest request) => NetworkServer.ConnectionApproval?.Invoke(request);
        public void WriteInitialState(NetConnection client) => NetworkObjects.WriteInitialState(Session!, client);

        public void ServerClientConnected(NetConnection client) => Raise(() => NetworkServer.RaiseClientConnected(client), "NetworkServer.ClientConnected");

        public void ServerClientDisconnected(NetConnection client, string reason)
        {
            NetworkObjects.OnClientLeft(Session!, client);
            Raise(() => NetworkServer.RaiseClientDisconnected(client, reason), "NetworkServer.ClientDisconnected");
        }

        public void ClientConnected() => Raise(NetworkClient.RaiseConnected, "NetworkClient.Connected");

        public void ClientDisconnected(string reason)
        {
            NetworkObjects.OnClientSessionEnded();
            Raise(() => NetworkClient.RaiseDisconnected(reason), "NetworkClient.Disconnected");
            End(reason);
        }

        public void ServerObjectPacket(NetConnection from, PacketKind kind, NetReader reader) => NetworkObjects.ServerPacket(Session!, from, kind, reader);
        public void ClientObjectPacket(PacketKind kind, NetReader reader) => NetworkObjects.ClientPacket(Session!, kind, reader);
    }

    private static readonly Handler s_Handler = new();
    private static CancellationTokenSource? s_Lifetime;
    private static bool s_Armed;
    private static int s_LastTickFrame = -1;
    private static bool s_Hooked;

    internal static NetSession? Session { get; private set; }
    internal static MessageRegistry ServerMessages => s_Handler.ServerMessages;
    internal static MessageRegistry ClientMessages => s_Handler.ClientMessages;

    internal static void Start(SessionRole role, NetworkSettings settings, string address, string connectData)
    {
        if (Session is { IsRunning: true })
            throw new NetworkException($"A network session is already running as {Session.Role}; call NetworkManager.Stop() first.");
        Hook();
        var session = new NetSession(role, settings.Clone(), s_Handler);
        if (role == SessionRole.Client)
        {
            session.SetConnectData(connectData);
            Session = session;
            try { session.StartClient(address); }
            catch
            {
                Session = null;
                throw;
            }
        }
        else
        {
            NetworkObjects.ForgetDeadEntities();   // scenes unloaded before hosting must not be reported as despawns
            session.StartServer();
            Session = session;
            string browsers = session.Settings.WebSocketPort > 0 ? $", browsers on WebSocket port {session.Settings.WebSocketPort}" : "";
            NetLog.Info($"{role} started on UDP port {session.UdpPort}{browsers}");
        }
        EnsureDriver();
    }

    internal static void Stop(string reason, bool quitting = false)
    {
        NetSession? session = Session;
        if (session == null) return;
        bool wasServer = session.IsServer;
        session.Stop(reason, quitting);
        if (wasServer) NetworkObjects.OnServerStopped();
        else NetworkObjects.OnClientSessionEnded();
        End(reason);
    }

    private static void End(string reason)
    {
        if (Session == null) return;
        Session = null;
        Raise(() => NetworkManager.RaiseStopped(reason), "NetworkManager.Stopped");
    }

    /// <summary>Play mode ended or the scripts reload: close sockets and forget everything game code registered.</summary>
    internal static void Reset()
    {
        Session?.Stop("The game stopped");
        Session = null;
        NetworkDiscovery.Reset();
        NetworkObjects.Reset();
        s_Handler.ServerMessages.Clear();
        s_Handler.ClientMessages.Clear();
        NetworkManager.ResetState();
        NetworkServer.ResetState();
        NetworkClient.ResetState();
        s_Lifetime?.Cancel();
        s_Lifetime = null;
        s_Armed = false;
        s_LastTickFrame = -1;
    }

    internal static void EnsureDriver()
    {
        // The scheduler drops pending waits on a script reload without running them; re-arm a driver that went quiet.
        if (s_Armed && s_LastTickFrame >= 0 && Time.FrameCount - s_LastTickFrame > 2) s_Armed = false;
        if (s_Armed) return;
        s_Armed = true;
        s_Lifetime ??= new CancellationTokenSource();
        // An explicit token keeps the wait from inheriting the calling script's DestroyToken.
        new WaitForNextFrame(s_Lifetime.Token).GetAwaiter().OnCompleted(Tick);
    }

    private static void Tick()
    {
        s_Armed = false;
        s_LastTickFrame = Time.FrameCount;
        try { Pump(); }
        catch (Exception e) { NetLog.Error($"Network update failed: {e}"); }
        if (Session != null || NetworkDiscovery.IsSearching) EnsureDriver();
    }

    internal static void Pump()
    {
        NetworkDiscovery.Poll();
        NetSession? session = Session;
        if (session == null) return;
        session.Poll();
        if (Session != session || !session.IsRunning) return;
        NetworkObjects.Update(session, NetClock.Now);
        session.Flush();
    }

    internal static void Hook()
    {
        if (s_Hooked) return;
        s_Hooked = true;
        Application.OnPlayModeExited += Reset;
        Application.OnQuit += OnQuit;
        SceneManager.SceneUnloaded += OnSceneUnloaded;
        // This assembly unloads on hot reload; handlers left on Funke-ScriptCore would pin it.
        AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(typeof(NetworkRuntime).Assembly);
        if (context != null && context.IsCollectible) context.Unloading += _ => Unhook();
    }

    private static void Unhook()
    {
        Reset();
        Application.OnPlayModeExited -= Reset;
        Application.OnQuit -= OnQuit;
        SceneManager.SceneUnloaded -= OnSceneUnloaded;
        s_Hooked = false;
    }

    private static void OnQuit() => Stop("The game was closed", quitting: true);

    private static void OnSceneUnloaded(Scene scene) => NetworkObjects.ForgetDeadEntities();

    private static void Raise(Action action, string what)
    {
        try { action(); }
        catch (Exception e) { NetLog.Error($"{what} handler threw: {e}"); }
    }
}
