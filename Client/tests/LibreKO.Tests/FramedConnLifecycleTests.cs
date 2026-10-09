using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class FramedConnLifecycleTests
{
    private static readonly byte[] FrameHeader = { 0xAA, 0x55 };
    private static readonly byte[] FrameTail = { 0x55, 0xAA };
    private const int FrameLengthBytes = sizeof(ushort);
    private const byte StaleOpcode = 0x41;
    private const byte FreshOpcode = 0x42;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class PausedConnection : FramedConn
    {
        public readonly TaskCompletionSource StaleDecoded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource StaleReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();

        protected override Packet? BuildIncoming(byte[] body)
        {
            if (body[0] == StaleOpcode)
            {
                StaleDecoded.TrySetResult();
                if (!Release.Wait(Patience)) throw new TimeoutException();
                StaleReleased.TrySetResult();
            }
            return base.BuildIncoming(body);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition()) await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
    }

    private static async Task Send(TcpClient peer, byte opcode)
    {
        var frame = new byte[FrameHeader.Length + FrameLengthBytes + sizeof(byte) + FrameTail.Length];
        FrameHeader.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(FrameHeader.Length), sizeof(byte));
        frame[FrameHeader.Length + FrameLengthBytes] = opcode;
        FrameTail.CopyTo(frame, frame.Length - FrameTail.Length);
        await peer.GetStream().WriteAsync(frame);
    }

    private static int PortOf(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    [Fact]
    public async Task CloseDiscardsDecodedPacketsWaitingForTheMainThread()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var connection = new FramedConn();
        try
        {
            connection.Connect(IPAddress.Loopback.ToString(), PortOf(listener));
            using var peer = await listener.AcceptTcpClientAsync();
            await WaitUntil(() => connection.Connected);
            await Send(peer, FreshOpcode);
            await WaitUntil(() => connection.Incoming.Count == 1);

            connection.Close();

            Assert.False(connection.Connected);
            Assert.Empty(connection.Incoming);
        }
        finally { connection.Close(); }
    }

    [Fact]
    public async Task AStaleReceiverCannotEnqueueIntoOrDisconnectAReplacementConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var connection = new PausedConnection();
        try
        {
            connection.Connect(IPAddress.Loopback.ToString(), PortOf(listener));
            using var stalePeer = await listener.AcceptTcpClientAsync();
            await WaitUntil(() => connection.Connected);
            await Send(stalePeer, StaleOpcode);
            await connection.StaleDecoded.Task.WaitAsync(Patience);

            connection.Connect(IPAddress.Loopback.ToString(), PortOf(listener));
            using var freshPeer = await listener.AcceptTcpClientAsync();
            await WaitUntil(() => connection.Connected);
            connection.Release.Set();
            await connection.StaleReleased.Task.WaitAsync(Patience);
            stalePeer.Close();
            await Send(freshPeer, FreshOpcode);
            await WaitUntil(() => !connection.Incoming.IsEmpty);

            Assert.True(connection.Incoming.TryDequeue(out var packet));
            Assert.Equal(FreshOpcode, packet!.GetOpcode());
            Assert.Empty(connection.Incoming);
            Assert.True(connection.Connected);
            Assert.Null(connection.LastError);
        }
        finally
        {
            connection.Release.Set();
            connection.Close();
        }
    }
}
