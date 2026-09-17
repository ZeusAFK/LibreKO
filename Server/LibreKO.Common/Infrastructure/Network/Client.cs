using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Channels;

namespace LibreKO.Common.Infrastructure.Network;

public interface IClient
{
    Guid Id { get; }
    Socket Socket { get; }
    uint PacketSequenceId { get; set; }
    uint SendSequenceId { get; set; }
    int AccountId { get; set; }
    int CharacterId { get; set; }
    bool ExpectedClose { get; set; }
    bool IsCryptoEnabled { get; }
    bool IsConnected { get; }

    void EnableCrypto(BigInteger publicKey);
    void EnableLoginCrypto(byte[] seedBytes);
    Task SendPacket(Packet packet, CancellationToken ct = default);
    Task<Packet> ReceivePacket(CancellationToken ct = default);
    void Disconnect();
    PacketCipher? GetPacketCipher();
}

public enum ServerType { Login, Game }

public class Client(Socket socket, ServerType serverType, ILogger<Client> logger) : IClient, IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public Socket Socket { get; } = socket;
    public uint PacketSequenceId { get; set; }
    public uint SendSequenceId { get; set; }
    public int AccountId { get; set; } = 0;
    public int CharacterId { get; set; } = 0;
    public bool ExpectedClose { get; set; }
    public bool IsCryptoEnabled { get; private set; }
    public PacketCipher? GetPacketCipher() => packetCipher;

    public bool IsConnected
    {
        get
        {
            if (Volatile.Read(ref closeRequested) != 0 || Volatile.Read(ref disposed) != 0)
                return false;

            try
            {
                return Socket.Connected;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    private const int SendTimeoutMs = 5000;
    private const int SendQueueCapacity = 4096;
    private const int MaxSendBatch = 256;
    private const int ReadBufferSize = 16 * 1024;
    // Accumulation window for the send loop: wait this long for more packets to queue so
    // a trickle becomes one write instead of several small send() syscalls. Trades a few
    // ms latency (fine for a ~10Hz game) for fewer syscalls + async transitions.
    private const int SendBatchWindowMs = 10;

    private PacketCipher? packetCipher;
    private byte[]? loginSeedBytes;
    private readonly NetworkStream stream = new(socket);
    // Read-ahead buffer so the 4 ReadExactlyAsync calls per packet (header/len/body/tail)
    // coalesce into ~1 recv() per 16KB instead of ~4 recv() per packet. Reads only;
    // the send loop writes through `stream` directly. Lazy-init on the receive loop.
    private BufferedStream? readBuffer;
    private readonly Channel<Packet> sendQueue = Channel.CreateBounded<Packet>(
        new BoundedChannelOptions(SendQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private int writerStarted;
    private int closeRequested;
    private int disposed;

    public void EnableCrypto(BigInteger publicKey)
    {
        packetCipher ??= new PacketCipher(publicKey);
        IsCryptoEnabled = true;
    }

    public void EnableLoginCrypto(byte[] seedBytes)
    {
        loginSeedBytes = seedBytes.ToArray();
        IsCryptoEnabled = true;
    }

    public Task SendPacket(Packet packet, CancellationToken ct = default)
    {
        if (Volatile.Read(ref closeRequested) != 0 || Volatile.Read(ref disposed) != 0)
            return Task.CompletedTask;

        EnsureSendLoopStarted();

        if (!sendQueue.Writer.TryWrite(packet))
        {
            // The outbound queue is full: this client is draining slower than
            // the world is producing updates for it (a lagging or dead socket).
            // Drop it instead of blocking the shared broadcast path that feeds
            // every other nearby player.
            logger.LogWarning("Send queue overflow ({Capacity} packets); disconnecting client {ClientId}", SendQueueCapacity, Id);
            Disconnect();
        }

        return Task.CompletedTask;
    }

    private void EnsureSendLoopStarted()
    {
        if (Interlocked.CompareExchange(ref writerStarted, 1, 0) == 0)
            _ = Task.Run(SendLoopAsync);
    }

    private async Task SendLoopAsync()
    {
        var reader = sendQueue.Reader;
        // Reused across cycles; grows to the largest batch then stays put.
        var batch = new ArrayBufferWriter<byte>(16 * 1024);
        try
        {
            while (await reader.WaitToReadAsync())
            {
                if (Volatile.Read(ref closeRequested) != 0 || Volatile.Read(ref disposed) != 0)
                    return;

                // Brief accumulation window (game stream only): let more packets queue so
                // a trickle groups into one write. Skipped when a full batch is already
                // waiting (bursts get no added latency) and for the low-volume login stream.
                if (SendBatchWindowMs > 0
                    && serverType == ServerType.Game
                    && (!reader.CanCount || reader.Count < MaxSendBatch))
                {
                    await Task.Delay(SendBatchWindowMs);
                    if (Volatile.Read(ref closeRequested) != 0 || Volatile.Read(ref disposed) != 0)
                        return;
                }

                // Drain everything queued and frame it into one buffer, so a burst of
                // broadcast packets becomes a single socket write instead of one send()
                // syscall per packet (the dominant cost at scale).
                batch.Clear();
                var n = 0;
                while (n < MaxSendBatch && reader.TryRead(out var packet))
                {
                    AppendFramedPacket(batch, packet);
                    n++;
                }

                if (batch.WrittenCount > 0 && !await FlushBatchAsync(batch))
                    return;
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            Disconnect();
        }
    }

    private void AppendFramedPacket(ArrayBufferWriter<byte> batch, Packet packet)
    {
        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("Sending packet 0x{Opcode:X2} ({OpcodeName}): {Packet}",
                packet.GetOpcode(), ResolveOpcodeName(packet.GetOpcode(), outbound: true, packet.GetLength()), Convert.ToHexString(packet.GetBytes()));

        if (packetCipher != null)
            SendSequenceId++;

        var packetToSend = packet;
        if (serverType == ServerType.Login
            && loginSeedBytes != null
            && packet.GetOpcode() != (byte)LoginOpcodes.LS_CRYPTION)
        {
            var protectedPayload = LoginSeedCipher.Protect(packet.GetBytes(), loginSeedBytes);
            packetToSend = new Packet(protectedPayload[0]);
            if (protectedPayload.Length > 1)
                packetToSend.WriteBytes(protectedPayload[1..]);
        }

        batch.Write(PacketProvider.WrapPacket(packetToSend, packetCipher, SendSequenceId));
    }

    private async Task<bool> FlushBatchAsync(ArrayBufferWriter<byte> batch)
    {
        if (Volatile.Read(ref closeRequested) != 0 || Volatile.Read(ref disposed) != 0)
            return false;

        using var sendCts = new CancellationTokenSource(SendTimeoutMs);
        try
        {
            await stream.WriteAsync(batch.WrittenMemory, sendCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Send timed out after {TimeoutMs}ms; disconnecting client {ClientId}", SendTimeoutMs, Id);
            Disconnect();
            return false;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Dropping send for disconnected client {ClientId}: {Reason}", Id, ex.Message);
            Disconnect();
            return false;
        }
    }

    public async Task<Packet> ReceivePacket(CancellationToken ct = default)
    {
        var packet = await PacketProvider.ReadFromStream(readBuffer ??= new BufferedStream(stream, ReadBufferSize), ct);

        if (serverType == ServerType.Login && loginSeedBytes != null)
        {
            var rawPacket = packet.GetBytes();
            if (LoginSeedCipher.LooksLikeProtectedPacket(rawPacket))
            {
                packet = PacketProvider.UnwrapLoginSeedPacket(packet, loginSeedBytes);
            }
            else
            {
                var previousSequenceId = PacketSequenceId;
                (packet, PacketSequenceId) = PacketProvider.UnwrapPacket(packet, IsCryptoEnabled, packetCipher);

                if (packetCipher != null && PacketSequenceId != 0 && PacketSequenceId != previousSequenceId + 1)
                    throw new InvalidDataException($"Invalid crypto sequence: expected {previousSequenceId + 1}, got {PacketSequenceId}");

                if (rawPacket.Length > 1 && rawPacket[0] == LoginSeedCipher.SelectorByte)
                    throw new InvalidDataException("Malformed seed-protected login packet.");
            }
        }
        else
        {
            var previousSequenceId = PacketSequenceId;
            (packet, PacketSequenceId) = PacketProvider.UnwrapPacket(packet, IsCryptoEnabled, packetCipher);

            if (packetCipher != null && PacketSequenceId != 0 && PacketSequenceId != previousSequenceId + 1)
                throw new InvalidDataException($"Invalid crypto sequence: expected {previousSequenceId + 1}, got {PacketSequenceId}");
        }

        if (logger.IsEnabled(LogLevel.Trace))
            logger.LogTrace("Received packet 0x{Opcode:X2} ({OpcodeName}): {Packet}",
                packet.GetOpcode(), ResolveOpcodeName(packet.GetOpcode(), outbound: false, packet.GetLength()), Convert.ToHexString(packet.GetBytes()));

        return packet;
    }

    public void Disconnect()
    {
        if (Interlocked.Exchange(ref closeRequested, 1) != 0)
            return;

        sendQueue.Writer.TryComplete();
        CloseSocket();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        Interlocked.Exchange(ref closeRequested, 1);
        sendQueue.Writer.TryComplete();
        CloseSocket();
        readBuffer?.Dispose();
        stream.Dispose();
        GC.SuppressFinalize(this);
    }

    private string ResolveOpcodeName(byte opcode, bool outbound, int bodyLength)
    {
        if (serverType == ServerType.Login)
            return Enum.IsDefined(typeof(LoginOpcodes), (ushort)opcode)
                ? ((LoginOpcodes)opcode).ToString()
                : "Unknown";

        // Opcode 0x02 is GS_CREATE_CHARACTER inbound (pre-game) but the same
        // byte is reused as the heartbeat probe outbound (server-pushed every
        // ~14s, 16 bytes of cryptographically-random padding). Disambiguate
        // by direction + payload size so the log is readable.
        if (outbound && opcode == (byte)GameOpcodes.GS_CREATE_CHARACTER && bodyLength == 16)
            return "GS_HEARTBEAT";

        return Enum.IsDefined(typeof(GameOpcodes), opcode)
            ? ((GameOpcodes)opcode).ToString()
            : "Unknown";
    }

    private void CloseSocket()
    {
        try
        {
            if (Socket.Connected)
                Socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            Socket.Close();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
