using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class RegenePacketWriter
{
    public const byte RespawnNormal = 1;
    public const byte RespawnWithTarget = 4;

    public short X { get; init; }
    public short Z { get; init; }
    public byte RespawnType { get; init; } = RespawnNormal;

    public static Packet At(short x, short z) => new RegenePacketWriter() { X = x, Z = z }.Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_REGENE);
        packet.WriteShort(X);
        packet.WriteShort(Z);
        packet.WriteByte(RespawnType);
        return packet;
    }
}
