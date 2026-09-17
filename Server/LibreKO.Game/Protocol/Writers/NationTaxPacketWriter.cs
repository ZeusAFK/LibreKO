using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class NationTaxPacketWriter
{
    public static Packet Status(byte sub, byte sellPercent, byte zonePercent, int treasury)
    {
        var packet = new Packet(GameOpcodes.GS_NATION_TAX);
        packet.WriteByte(sub);
        packet.WriteByte(sellPercent);
        packet.WriteByte(zonePercent);
        packet.WriteInt(treasury);
        return packet;
    }
}
