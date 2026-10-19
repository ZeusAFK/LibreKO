using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public static class VoiceDatagram
{
    public const byte Hello = 1;
    public const byte Frame = 2;
    public const int ClientFrameHeader = 3;
    public const int ServerFrameHeader = 7;
    public const int MaxDatagram = 1400;
}

public interface IVoiceRelay
{
    bool Enabled { get; }
    ushort UdpPort { get; }
    Task ForwardAsync(VoiceMember speaker, ushort seq, ReadOnlyMemory<byte> opus);
}

public sealed class VoiceRelayService(
    VoiceRoster roster,
    SessionManager sessions,
    IOptions<GameServerSettings> settings,
    ILogger<VoiceRelayService> logger,
    TimeProvider? clock = null) : BackgroundService, IVoiceRelay
{
    private const int WindowsUdpConnReset = -1744830452;

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private Socket? socket;

    public bool Enabled => settings.Value.ChatRooms.Voice.Enabled;
    public ushort UdpPort { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var voice = settings.Value.ChatRooms.Voice;
        if (!voice.Enabled) return;
        try
        {
            socket = Bind(voice);
        }
        catch (Exception ex) when (ex is SocketException or FormatException)
        {
            logger.LogError(ex, "Voice relay could not bind UDP {Host}:{Port}; voice falls back to the game connection", voice.BindHost, voice.UdpPort);
            return;
        }
        UdpPort = (ushort)voice.UdpPort;
        logger.LogInformation("Voice relay listening on UDP {Host}:{Port}", voice.BindHost, voice.UdpPort);

        var buffer = new byte[VoiceDatagram.MaxDatagram];
        var plain = new byte[VoiceDatagram.MaxDatagram];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                logger.LogDebug(ex, "Voice relay receive failed");
                continue;
            }
            try
            {
                await HandleDatagramAsync(buffer.AsMemory(0, received.ReceivedBytes), (IPEndPoint)received.RemoteEndPoint, plain);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Voice datagram dropped");
            }
        }
        socket.Dispose();
    }

    private static Socket Bind(VoiceSettings voice)
    {
        var address = voice.BindHost is "*" or "" ? IPAddress.Any : IPAddress.Parse(voice.BindHost);
        var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (OperatingSystem.IsWindows())
            socket.IOControl(WindowsUdpConnReset, [0, 0, 0, 0], null);
        socket.Bind(new IPEndPoint(address, voice.UdpPort));
        return socket;
    }

    private async Task HandleDatagramAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remote, byte[] plain)
    {
        var member = roster.BySession(VoiceSeal.SessionOf(datagram.Span));
        if (member == null || !member.Seal.TryOpen(VoiceSeal.ClientToServer, datagram.Span, plain, out int length) || length < 1)
            return;
        member.Endpoint = remote;
        switch (plain[0])
        {
            case VoiceDatagram.Hello:
                await SendSealedAsync(member, [VoiceDatagram.Hello]);
                break;
            case VoiceDatagram.Frame when length > VoiceDatagram.ClientFrameHeader:
                ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(1));
                var opus = plain.AsMemory(VoiceDatagram.ClientFrameHeader, length - VoiceDatagram.ClientFrameHeader);
                if (roster.AllowFrame(member, opus.Length, clock.GetUtcNow().ToUnixTimeMilliseconds()))
                    await ForwardAsync(member, seq, opus);
                break;
        }
    }

    public async Task ForwardAsync(VoiceMember speaker, ushort seq, ReadOnlyMemory<byte> opus)
    {
        var listeners = roster.ListenersOf(speaker);
        if (listeners.Count == 0) return;
        var frame = new byte[VoiceDatagram.ServerFrameHeader + opus.Length];
        frame[0] = VoiceDatagram.Frame;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(1), speaker.CharacterId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), seq);
        opus.Span.CopyTo(frame.AsSpan(VoiceDatagram.ServerFrameHeader));
        Packet? tcpFrame = null;
        foreach (var listener in listeners)
        {
            if (listener.Tcp)
            {
                tcpFrame ??= RoomMediaPacketWriter.Frame(speaker.CharacterId, seq, opus.Span);
                var session = sessions.GetByCharacterId(listener.CharacterId);
                if (session == null) continue;
                try { await session.Client.SendPacket(tcpFrame); }
                catch (Exception ex) { logger.LogDebug(ex, "Voice frame over TCP failed for {Id}", listener.CharacterId); }
                continue;
            }
            if (listener.Endpoint != null)
                await SendSealedAsync(listener, frame);
        }
    }

    private async Task SendSealedAsync(VoiceMember member, byte[] plain)
    {
        if (socket == null || member.Endpoint == null) return;
        var datagram = new byte[plain.Length + VoiceSeal.Overhead];
        int length;
        try
        {
            length = member.Seal.Seal(VoiceSeal.ServerToClient, plain, datagram);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        try
        {
            await socket.SendToAsync(datagram.AsMemory(0, length), SocketFlags.None, member.Endpoint);
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "Voice send to {Endpoint} failed", member.Endpoint);
        }
    }
}
