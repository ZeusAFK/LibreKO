using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class SiegePacketWriter
{
    public const byte NoMasterClan = 0;
    public const short Accepted = 1;

    public readonly record struct ClanBanner(ushort ClanId, ushort MarkVersion, byte Flag, byte Grade);

    public readonly record struct WarSchedule(byte WarType, byte Weekday, byte Hour, byte Minute);

    public readonly record struct ClanRow(string Name, byte Nation, byte Members);

    public readonly record struct RegistrationPeriod(byte StartWeekday, byte StartHour, byte StartMinute, byte EndWeekday);

    public static Packet Result(byte sub, byte subType)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        return packet;
    }

    public static Packet Result(byte sub, byte subType, short result)
    {
        var packet = Result(sub, subType);
        packet.WriteShort(result);
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

    public static Packet WarfareNpc() => Result(SiegePacketConstants.WarfareNpc, SiegePacketConstants.WarfareOpen);

    public static Packet Schedule(IReadOnlyCollection<WarSchedule> wars)
    {
        var packet = Result(SiegePacketConstants.WarfareNpc, SiegePacketConstants.WarfareSchedule, Accepted);
        packet.WriteByte((byte)wars.Count);
        foreach (var war in wars)
        {
            packet.WriteByte(war.WarType);
            packet.WriteByte(war.Weekday);
            packet.WriteByte(war.Hour);
            packet.WriteByte(war.Minute);
        }

        return packet;
    }

    public static Packet Challengers(
        IReadOnlyCollection<ClanRow> clans, byte chosen, uint signUpFee, RegistrationPeriod period)
    {
        var packet = Result(SiegePacketConstants.WarfareNpc, SiegePacketConstants.WarfareChallengers, Accepted);
        WriteClans(packet, clans);
        packet.WriteByte((byte)clans.Count);
        packet.WriteByte(chosen);
        packet.WriteUInt(signUpFee);
        packet.WriteByte(period.StartWeekday);
        packet.WriteByte(period.StartHour);
        packet.WriteByte(period.StartMinute);
        packet.WriteByte(period.EndWeekday);
        return packet;
    }

    public static Packet DefendingUnion(IReadOnlyCollection<ClanRow> clans)
    {
        var packet = Result(SiegePacketConstants.WarfareNpc, SiegePacketConstants.WarfareDefendingUnion, Accepted);
        WriteClans(packet, clans);
        return packet;
    }

    public static Packet CastleManager(uint collectable, uint moradonTax)
    {
        var packet = Result(SiegePacketConstants.CastleManager, SiegePacketConstants.ManagerOpen);
        packet.WriteUInt(collectable);
        packet.WriteUInt(moradonTax);
        return packet;
    }

    public static Packet TaxCollected(int newCoins, int collected)
    {
        var packet = Result(SiegePacketConstants.CastleManager, SiegePacketConstants.ManagerCollect, Accepted);
        packet.WriteInt(newCoins);
        packet.WriteInt(collected);
        return packet;
    }

    public static Packet Tariffs(short moradonTariff, short delosTariff, int dungeonFee)
    {
        var packet = Result(SiegePacketConstants.CastleManager, SiegePacketConstants.ManagerTariffs, Accepted);
        packet.WriteShort(moradonTariff);
        packet.WriteShort(delosTariff);
        packet.WriteInt(dungeonFee);
        return packet;
    }

    public static Packet TariffChanged(byte subType, short tariff, short zoneId)
    {
        var packet = Result(SiegePacketConstants.CastleManager, subType, Accepted);
        packet.WriteShort(tariff);
        packet.WriteShort(zoneId);
        return packet;
    }

    public static Packet DungeonFeeChanged(int fee)
    {
        var packet = Result(SiegePacketConstants.CastleManager, SiegePacketConstants.ManagerDungeonFee, Accepted);
        packet.WriteInt(fee);
        return packet;
    }

    public static Packet RankList(byte sub, byte subType, byte count)
    {
        var packet = Sub(sub);
        packet.WriteByte(subType);
        packet.WriteByte(count);
        return packet;
    }

    private static void WriteClans(Packet packet, IReadOnlyCollection<ClanRow> clans)
    {
        packet.WriteByte((byte)clans.Count);
        foreach (var clan in clans)
        {
            packet.WriteSByteString(clan.Name);
            packet.WriteByte(clan.Nation);
            packet.WriteByte(clan.Members);
        }
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_SIEGE);
        packet.WriteByte(sub);
        return packet;
    }
}
