using System.Net.Sockets;
using System.Numerics;

namespace LibreKO.Common.Infrastructure.Network;

public class BotClient : IClient
{
    public Guid Id { get; } = Guid.NewGuid();
    public Socket Socket => throw new NotSupportedException("Bot clients do not have an underlying network socket.");
    public uint PacketSequenceId { get; set; }
    public uint SendSequenceId { get; set; }
    public int AccountId { get; set; }
    public int CharacterId { get; set; }
    public bool ExpectedClose { get; set; }
    public bool IsCryptoEnabled => false;
    public bool IsConnected => true;

    public void EnableCrypto(BigInteger publicKey) { }
    public void EnableLoginCrypto(byte[] seedBytes) { }

    public Task SendPacket(Packet packet, CancellationToken ct = default) => Task.CompletedTask;

    public Task<Packet> ReceivePacket(CancellationToken ct = default) =>
        Task.FromCanceled<Packet>(new CancellationToken(true));

    public void Disconnect() { }

    public PacketCipher? GetPacketCipher() => null;
}
