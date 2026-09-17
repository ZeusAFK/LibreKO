using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ZoneChangePacketWriter
{

    public const ushort NoBattleZone = ushort.MaxValue;

    public static Packet Loading(short zoneId, short x, short z, short y)
    {
        var packet = Sub(ZoneChangeSubOpcode.Loading);
        packet.WriteShort(zoneId);
        packet.WriteShort(x);
        packet.WriteShort(z);
        packet.WriteShort(y);
        return packet;
    }

    public static Packet Ready() => Sub(ZoneChangeSubOpcode.Ready);

    public static Packet Teleport(short zoneId, ushort x, ushort z, ushort y, byte nation)
    {
        var packet = Sub(ZoneChangeSubOpcode.Teleport);
        packet.WriteShort(zoneId);
        packet.WriteShort(0);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        packet.WriteUShort(y);
        packet.WriteByte(nation);
        packet.WriteUShort(NoBattleZone);
        return packet;
    }

    public static Packet ZoneAbility(
        byte sub, bool canTrade, byte zoneType, bool canTalk, ushort tariff)
    {
        var packet = new Packet(GameOpcodes.GS_ZONEABILITY);
        packet.WriteByte(sub);
        packet.WriteByte(canTrade ? (byte)1 : (byte)0);
        packet.WriteByte(zoneType);
        packet.WriteByte(canTalk ? (byte)1 : (byte)0);
        packet.WriteUShort(tariff);
        return packet;
    }

    private static Packet Sub(ZoneChangeSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_ZONE_CHANGE);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
