using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class SiegePacketWriter
{
    public const byte NoMasterClan = 0;

    public readonly record struct ClanBanner(ushort ClanId, ushort MarkVersion, byte Flag, byte Grade);

    public readonly record struct WarSchedule(byte Day, byte Hour, byte Minute);

    public static Packet Result(byte sub, byte subType)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        return packet;
    }

    public static Packet CastleFlag(byte sub, ClanBanner? banner)
    {
        var packet = Sub(sub);
        packet.WriteByte(0);

        if (banner is { } clan)
        {
            packet.WriteUShort(clan.ClanId);
            packet.WriteUShort(clan.MarkVersion);
            packet.WriteByte(clan.Flag);
            packet.WriteByte(clan.Grade);
        }
        else
        {
            packet.WriteUShort(0);
            packet.WriteUShort(0);
            packet.WriteByte(NoMasterClan);
            packet.WriteByte(NoMasterClan);
        }

        return packet;
    }

    public static Packet CastleSchedule(
        byte sub, byte subType, ushort castleIndex, byte siegeType, WarSchedule schedule)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteUShort(castleIndex);
        packet.WriteUShort(siegeType);
        packet.WriteByte(schedule.Day);
        packet.WriteByte(schedule.Hour);
        packet.WriteByte(schedule.Minute);
        return packet;
    }

    public static Packet CastleApplicants(
        byte sub, byte subType, ushort castleIndex, string clanName, byte nation, ushort members,
        WarSchedule schedule)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteUShort(castleIndex);
        packet.WriteByte(1);
        packet.WriteString(clanName);
        packet.WriteByte(nation);
        packet.WriteUShort(members);
        packet.WriteByte(schedule.Day);
        packet.WriteByte(schedule.Hour);
        packet.WriteByte(schedule.Minute);
        return packet;
    }

    public static Packet CastleOwner(
        byte sub, byte subType, ushort castleIndex, byte siegeType, string clanName, byte nation,
        ushort members)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteUShort(castleIndex);
        packet.WriteByte(siegeType);
        packet.WriteString(clanName);
        packet.WriteByte(nation);
        packet.WriteUShort(members);
        return packet;
    }

    public static Packet Tariffs(
        byte sub, byte subType, ushort castleIndex, ushort moradonTariff, ushort delosTariff,
        int dungeonCharge)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteUShort(castleIndex);
        packet.WriteUShort(moradonTariff);
        packet.WriteUShort(delosTariff);
        packet.WriteInt(dungeonCharge);
        return packet;
    }

    public static Packet TariffChanged(byte sub, byte subType, ushort tariff, byte zoneId)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteUShort(1);
        packet.WriteUShort(tariff);
        packet.WriteByte(zoneId);
        return packet;
    }

    public static Packet RankList(byte sub, byte subType, byte count)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteByte(count);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_SIEGE);
        packet.WriteByte(sub);
        return packet;
    }
}
