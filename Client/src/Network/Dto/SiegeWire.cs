using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public readonly record struct SiegeScheduleRow(byte WarType, byte Weekday, byte Hour, byte Minute);

public sealed record SiegeSchedule(short Result, IReadOnlyList<SiegeScheduleRow> Rows);

public readonly record struct SiegeClanRow(string Name, byte Nation, byte Members);

public sealed record SiegeChallengers(short Result, IReadOnlyList<SiegeClanRow> Clans, int SignedUp, int Chosen, uint Fee,
    byte StartDay, byte StartHour, byte StartMinute, byte EndDay);

public sealed record SiegeDefenders(short Result, IReadOnlyList<SiegeClanRow> Clans);

public readonly record struct SiegeOffice(uint Collectable, uint Second);

public readonly record struct SiegeCollected(short Result, int Coins, int Collected);

public readonly record struct SiegeTaxRates(short Result, short Moradon, short Delos, int DungeonFee);

public readonly record struct SiegeRateChanged(byte Sub, short Result, int Value, short Zone);

public static class SiegeWire
{
    public static Packet Request(params byte[] body)
    {
        var p = new Packet(GameOpcodes.GS_SIEGE);
        foreach (byte b in body) p.WriteByte(b);
        return p;
    }

    public static Packet Rate(SiegeRateKind kind, int value)
    {
        var p = Request(SiegeWarfare.CastleOffice, SiegeWarfare.WireSub(kind));
        if (kind == SiegeRateKind.DungeonFee) p.WriteUInt((uint)value);
        else p.WriteUShort((ushort)value);
        return p;
    }

    public static SiegeSchedule ReadSchedule(Packet p)
    {
        short result = KingWire.I16(p);
        var rows = new List<SiegeScheduleRow>();
        if (result == SiegeWarfare.Success)
        {
            int count = KingWire.U8(p);
            for (int i = 0; i < count && p.RemainingBytes > 0; i++)
                rows.Add(new SiegeScheduleRow(KingWire.U8(p), KingWire.U8(p), KingWire.U8(p), KingWire.U8(p)));
        }
        return new SiegeSchedule(result, rows);
    }

    public static SiegeChallengers ReadChallengers(Packet p)
    {
        short result = KingWire.I16(p);
        if (result != SiegeWarfare.Success) return new SiegeChallengers(result, [], 0, 0, 0, 0, 0, 0, 0);
        var clans = ReadClans(p);
        int signedUp = KingWire.U8(p);
        int chosen = KingWire.U8(p);
        uint fee = KingWire.U32(p);
        byte startDay = KingWire.U8(p), startHour = KingWire.U8(p), startMinute = KingWire.U8(p), endDay = KingWire.U8(p);
        return new SiegeChallengers(result, clans, signedUp, chosen, fee, startDay, startHour, startMinute, endDay);
    }

    public static SiegeDefenders ReadDefenders(Packet p)
    {
        short result = KingWire.I16(p);
        return new SiegeDefenders(result, result == SiegeWarfare.Success ? ReadClans(p) : []);
    }

    private static List<SiegeClanRow> ReadClans(Packet p)
    {
        var clans = new List<SiegeClanRow>();
        int count = KingWire.U8(p);
        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            string name = KingWire.Str8(p);
            clans.Add(new SiegeClanRow(name, KingWire.U8(p), KingWire.U8(p)));
        }
        return clans;
    }

    public static SiegeOffice ReadOffice(Packet p) => new(KingWire.U32(p), KingWire.U32(p));

    public static SiegeCollected ReadCollected(Packet p)
    {
        short result = KingWire.I16(p);
        return result == SiegeWarfare.Success
            ? new SiegeCollected(result, KingWire.I32(p), KingWire.I32(p))
            : new SiegeCollected(result, 0, 0);
    }

    public static SiegeTaxRates ReadTaxRates(Packet p)
    {
        short result = KingWire.I16(p);
        return result == SiegeWarfare.Success
            ? new SiegeTaxRates(result, KingWire.I16(p), KingWire.I16(p), KingWire.I32(p))
            : new SiegeTaxRates(result, 0, 0, 0);
    }

    public static SiegeRateChanged ReadRateChanged(Packet p, byte sub)
    {
        short result = KingWire.I16(p);
        if (result != SiegeWarfare.Success) return new SiegeRateChanged(sub, result, 0, 0);
        if (sub == SiegeWarfare.DungeonFee) return new SiegeRateChanged(sub, result, KingWire.I32(p), 0);
        short rate = KingWire.I16(p);
        return new SiegeRateChanged(sub, result, rate, KingWire.I16(p));
    }
}
