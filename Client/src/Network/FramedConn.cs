using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LibreKO.Network;

public class FramedConn
{
    private static readonly byte[] Header = { 0xAA, 0x55 };
    private static readonly byte[] Tail = { 0x55, 0xAA };

    private TcpClient? tcp;
    private NetworkStream? stream;
    private CancellationTokenSource? cts;
    private readonly object sendLock = new();
    private readonly object receiveLock = new();
    private int attempt;

    private const int ConnectTimeoutMs = 5000;
    private const int WriteTimeoutMs = 8000;

    public readonly ConcurrentQueue<Packet> Incoming = new();
    public volatile bool Connected;
    public volatile bool ConnectFailed;
    public volatile string? LastError;

    public void Connect(string host, int port)
    {
        Close();
        ResetProtocolState();
        ConnectFailed = false;
        LastError = null;
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        int attempt = Volatile.Read(ref this.attempt);
        _ = Task.Run(() =>
        {
            var tcp = new TcpClient { NoDelay = true, SendTimeout = WriteTimeoutMs };
            try
            {
                if (!tcp.ConnectAsync(host, port).Wait(ConnectTimeoutMs, ct))
                {
                    try { tcp.Close(); } catch { }
                    lock (receiveLock)
                    {
                        if (!IsCurrent(attempt)) return;
                        LastError = $"connection to {host}:{port} timed out";
                        ConnectFailed = true;
                    }
                    return;
                }
                NetworkStream stream;
                lock (receiveLock)
                {
                    if (!IsCurrent(attempt))
                    {
                        try { tcp.Close(); } catch { }
                        return;
                    }
                    this.tcp = tcp;
                    this.stream = stream = tcp.GetStream();
                    Connected = true;
                }
                ReceiveLoop(stream, ct, attempt);
            }
            catch (Exception e)
            {
                try { tcp.Close(); } catch { }
                var cause = e.InnerException ?? e;
                lock (receiveLock)
                {
                    if (!IsCurrent(attempt)) return;
                    Godot.GD.Print($"[net] connection to {host}:{port} failed: {cause.Message}");
                    LastError = ConnectErrorText(cause);
                    ConnectFailed = true;
                    Connected = false;
                }
            }
        }, ct);
    }

    private static string ConnectErrorText(Exception cause) => cause is SocketException se
        ? se.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => "The server refused the connection. It may be offline.",
            SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "The server address could not be found.",
            SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.NetworkDown => "The server cannot be reached from this network.",
            SocketError.TimedOut => "The connection timed out.",
            _ => cause.Message,
        }
        : cause.Message;

    private bool IsCurrent(int attempt) => Volatile.Read(ref this.attempt) == attempt;

    public void Send(Packet packet)
    {
        int attempt = Volatile.Read(ref this.attempt);
        var s = stream;
        if (s == null || !Connected) return;
        var body = TransformOutgoing(packet.GetBytes());
        var frame = new byte[2 + 2 + body.Length + 2];
        frame[0] = Header[0]; frame[1] = Header[1];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)body.Length);
        body.CopyTo(frame.AsSpan(4));
        frame[^2] = Tail[0]; frame[^1] = Tail[1];
        try
        {
            lock (sendLock) { s.Write(frame, 0, frame.Length); s.Flush(); }
        }
        catch (Exception e)
        {
            lock (receiveLock)
            {
                if (!IsCurrent(attempt)) return;
                LastError = e.Message;
                Connected = false;
            }
        }
    }

    private void ReceiveLoop(NetworkStream s, CancellationToken ct, int attempt)
    {
        var two = new byte[2];
        try
        {
            while (!ct.IsCancellationRequested && IsCurrent(attempt) && Connected)
            {
                s.ReadExactly(two, 0, 2);
                if (two[0] != Header[0] || two[1] != Header[1])
                    throw new InvalidOperationException($"bad header {two[0]:X2}{two[1]:X2}");

                s.ReadExactly(two, 0, 2);
                int len = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(two);
                var body = new byte[len];
                s.ReadExactly(body, 0, len);

                s.ReadExactly(two, 0, 2);
                if (two[0] != Tail[0] || two[1] != Tail[1])
                    throw new InvalidOperationException($"bad tail {two[0]:X2}{two[1]:X2}");

                if (body.Length == 0) continue;

                var packet = BuildIncoming(body);
                if (packet == null) continue;
                lock (receiveLock)
                {
                    if (!IsCurrent(attempt)) return;
                    Incoming.Enqueue(packet);
                }
            }
        }
        catch (Exception e)
        {
            lock (receiveLock)
                if (!ct.IsCancellationRequested && IsCurrent(attempt)) LastError = e.Message;
        }
        finally
        {
            lock (receiveLock)
                if (IsCurrent(attempt)) Connected = false;
        }
    }

    protected virtual byte[] TransformOutgoing(byte[] body) => body;

    protected virtual void ResetProtocolState() { }

    protected virtual Packet? BuildIncoming(byte[] body) => BuildPacket(body);

    protected static Packet BuildPacket(byte[] data)
    {
        var p = new Packet(data[0]);
        if (data.Length > 1)
            p.WriteBytes(data[1..]);
        p.ResetOffset();
        return p;
    }

    public void Close()
    {
        CancellationTokenSource? cts;
        NetworkStream? stream;
        TcpClient? tcp;
        lock (receiveLock)
        {
            Interlocked.Increment(ref attempt);
            Connected = false;
            Incoming.Clear();
            cts = this.cts;
            stream = this.stream;
            tcp = this.tcp;
            this.stream = null;
            this.tcp = null;
        }
        try { cts?.Cancel(); } catch { }
        try { stream?.Dispose(); } catch { }
        try { tcp?.Close(); } catch { }
    }
}
