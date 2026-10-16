using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Funke.Networking.Internal;

/// <summary>
/// ClientWebSocket: the browser's own WebSocket on Web builds, a managed one elsewhere (desktop tests use it too).
/// Its async continuations may run on other threads, so results reach the main thread through queues read in Poll.
/// </summary>
internal sealed class WebSocketClientTransport : IClientTransport
{
    private readonly NetworkSettings m_Settings;
    private readonly ITransportListener m_Listener;
    private readonly Uri m_Uri;
    private readonly ConcurrentQueue<byte[]> m_Received = new();
    private readonly CancellationTokenSource m_Cancel = new();
    private readonly object m_SendLock = new();
    private ByteQueue m_Batch = new(1024);
    private ByteQueue m_Sending = new(1024);
    private ClientWebSocket? m_Socket;
    private Task? m_Connect;
    private Task? m_Send;
    private Task? m_Receive;
    private double m_ConnectStarted;
    private volatile string? m_CloseReason;
    private bool m_Connected;
    private bool m_Done;

    internal WebSocketClientTransport(NetworkSettings settings, ITransportListener listener, Uri uri)
    {
        m_Settings = settings;
        m_Listener = listener;
        m_Uri = uri;
    }

    public NetTransportKind Kind => NetTransportKind.WebSocket;
    public int RoundTripMs { get; internal set; }

    public void Connect()
    {
        m_Socket = new ClientWebSocket();
        m_ConnectStarted = NetClock.Now;
        m_Connect = m_Socket.ConnectAsync(m_Uri, m_Cancel.Token);
    }

    public void Poll(float deltaMilliseconds)
    {
        if (m_Done || m_Socket == null) return;
        if (!m_Connected)
        {
            if (m_Connect == null) return;
            if (!m_Connect.IsCompleted)
            {
                if (NetClock.Now - m_ConnectStarted > m_Settings.ConnectTimeout)
                    Finish($"{m_Uri} does not answer (no server, or a firewall blocks port {m_Uri.Port})");
                return;
            }
            if (!m_Connect.IsCompletedSuccessfully)
            {
                Finish($"Cannot connect to {m_Uri}: {Describe(m_Connect.Exception)}");
                return;
            }
            m_Connected = true;
            m_Receive = ReceiveLoop(m_Socket);
            m_Listener.OnTransportConnected(0, m_Uri.ToString(), NetTransportKind.WebSocket);
        }

        while (!m_Done && m_Received.TryDequeue(out byte[]? batch))
        {
            try
            {
                int position = 0;
                while (position < batch.Length && !m_Done)
                    m_Listener.OnTransportData(0, WebSocketCodec.NextRecord(batch, ref position));
            }
            catch (NetworkException)
            {
                Finish("The server sent malformed data");
            }
        }
        if (m_Done) return;
        if (m_CloseReason != null && m_Received.IsEmpty)
        {
            Finish(m_CloseReason);
            return;
        }
        StartSend();
    }

    public void Flush()
    {
        if (!m_Done && m_Connected) StartSend();
    }

    public void Send(ReadOnlySpan<byte> data, NetChannel channel)
    {
        if (m_Done) return;
        lock (m_SendLock) WebSocketCodec.AppendRecord(m_Batch, data);
    }

    public void Disconnect(string reason, bool blocking)
    {
        if (m_Done) return;
        m_Done = true;
        ClientWebSocket? socket = m_Socket;
        if (socket == null || !m_Connected)
        {
            Dispose();
            return;
        }
        Task close = CloseAsync(socket, reason);
        // On quit the process would end before the close frame leaves. Browsers close the socket themselves (and can't block).
        if (blocking && !OperatingSystem.IsBrowser()) close.Wait(TimeSpan.FromMilliseconds(500));
    }

    public void Dispose()
    {
        m_Done = true;
        m_Cancel.Cancel();
        m_Socket?.Dispose();
        m_Socket = null;
    }

    private void StartSend()
    {
        if (m_Send != null && !m_Send.IsCompleted) return;
        if (m_Send is { IsFaulted: true })
        {
            Finish($"Connection lost: {Describe(m_Send.Exception)}");
            return;
        }
        lock (m_SendLock)
        {
            if (m_Batch.Count == 0) return;
            (m_Batch, m_Sending) = (m_Sending, m_Batch);
            m_Batch.Clear();
        }
        // m_Sending stays untouched until this send completes: the next swap waits for IsCompleted.
        var segment = new ArraySegment<byte>(m_Sending.Buffer, m_Sending.Start, m_Sending.Count);
        m_Send = m_Socket!.SendAsync(segment, WebSocketMessageType.Binary, true, m_Cancel.Token);
    }

    private async Task ReceiveLoop(ClientWebSocket socket)
    {
        var message = new ByteQueue(4096);
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                ArraySegment<byte> space = message.ReserveSegment(16384);
                WebSocketReceiveResult result = await socket.ReceiveAsync(space, m_Cancel.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    string? description = socket.CloseStatusDescription;
                    m_CloseReason = string.IsNullOrEmpty(description) ? "The server closed the connection" : description;
                    return;
                }
                message.Commit(result.Count);
                if (message.Count > NetworkSettings.MaxMessageSize)
                {
                    m_CloseReason = "The server sent a message that is too big";
                    return;
                }
                if (!result.EndOfMessage) continue;
                m_Received.Enqueue(message.Data.ToArray());
                message.Clear();
            }
            m_CloseReason ??= "The server closed the connection";
        }
        catch (Exception e)
        {
            m_CloseReason ??= m_Cancel.IsCancellationRequested ? TransportReasons.Closed : $"Connection lost: {Describe(e)}";
        }
    }

    private async Task CloseAsync(ClientWebSocket socket, string reason)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            string text = reason.Length > 100 ? reason[..100] : reason;
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, text, timeout.Token).ConfigureAwait(false);
            // Aborting before the server answers can reset the connection and lose the close frame (and the reason).
            if (m_Receive != null) await Task.WhenAny(m_Receive, Task.Delay(1000)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection is going away either way.
        }
        finally
        {
            m_Cancel.Cancel();
            socket.Dispose();
        }
    }

    private void Finish(string reason)
    {
        if (m_Done) return;
        Dispose();
        m_Listener.OnTransportDisconnected(0, reason);
    }

    private static string Describe(Exception? e)
    {
        while (e is AggregateException { InnerException: not null } aggregate) e = aggregate.InnerException;
        while (e is WebSocketException { InnerException: not null } inner) e = inner.InnerException;
        return e?.Message ?? "unknown error";
    }
}
