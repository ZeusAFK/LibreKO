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

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly object _sendLock = new();

    private const int ConnectTimeoutMs = 5000;

    public readonly ConcurrentQueue<Packet> Incoming = new();
    public volatile bool Connected;
    public volatile bool ConnectFailed;
    public volatile string? LastError;

    public void Connect(string host, int port)
    {
        Close();
        while (Incoming.TryDequeue(out _)) { }
        ResetProtocolState();
        ConnectFailed = false;
        LastError = null;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() =>
        {
            var tcp = new TcpClient();
            try
            {
                if (!tcp.ConnectAsync(host, port).Wait(ConnectTimeoutMs, ct))
                {
                    LastError = $"connection to {host}:{port} timed out";
                    ConnectFailed = true;
                    try { tcp.Close(); } catch { }
                    return;
                }
                _tcp = tcp;
                _stream = tcp.GetStream();
                Connected = true;
                ReceiveLoop(ct);
            }
            catch (Exception e)
            {
                LastError = (e.InnerException ?? e).Message;
                ConnectFailed = true;
                Connected = false;
                try { tcp.Close(); } catch { }
            }
        }, ct);
    }

    public void Send(Packet packet)
    {
        var s = _stream;
        if (s == null || !Connected) return;
        var body = TransformOutgoing(packet.GetBytes());
        var frame = new byte[2 + 2 + body.Length + 2];
        frame[0] = Header[0]; frame[1] = Header[1];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)body.Length);
        body.CopyTo(frame.AsSpan(4));
        frame[^2] = Tail[0]; frame[^1] = Tail[1];
        try
        {
            lock (_sendLock) { s.Write(frame, 0, frame.Length); s.Flush(); }
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Connected = false;
        }
    }

    private void ReceiveLoop(CancellationToken ct)
    {
        var two = new byte[2];
        try
        {
            var s = _stream!;
            while (!ct.IsCancellationRequested && Connected)
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
                if (packet != null) Incoming.Enqueue(packet);
            }
        }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested)
                LastError = e.Message;
        }
        finally
        {
            Connected = false;
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
        Connected = false;
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
        _stream = null;
        _tcp = null;
    }
}
