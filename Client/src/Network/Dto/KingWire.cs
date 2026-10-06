using System.Collections.Generic;
using System.Text;
using LibreKO.Domain;

namespace LibreKO.Network;

public readonly record struct KingSchedule(byte Kind, int Month, int Day, int Hour, int Minute, int SenatorState);

public readonly record struct KingCandidate(int Number, string Name, string Clan);

public sealed record KingCandidates(short Result, IReadOnlyList<KingCandidate> Candidates);

public readonly record struct KingPlan(short Result, string Text);

public readonly record struct KingShoutLine(int Number, string Name, string Plan);

public sealed record KingShout(short Result, IReadOnlyList<KingShoutLine> Lines);

public readonly record struct KingChange(uint NewKingId, uint OldKingId);

public sealed record KingSenators(short Result, IReadOnlyList<string> Names);

public readonly record struct KingTreasury(short View, long Tribute, long Treasury);

public readonly record struct KingCoins(short Result, long Coins, long Amount);

public readonly record struct KingTariff(short Result, int Tariff);

public readonly record struct KingTreasuryNotice(bool Shown, int Used, int Left);

public static class KingWire
{
    public static Packet Request(params byte[] body)
    {
        var p = new Packet(GameOpcodes.GS_KING);
        foreach (byte b in body) p.WriteByte(b);
        return p;
    }

    public static Packet Named(string name, params byte[] body)
    {
        var p = Request(body);
        p.WriteSByteString(name);
        return p;
    }

    public static Packet Plan(string plan)
    {
        var p = Request(KingElection.Election, KingElection.Plan, KingElection.PlanWrite);
        p.WriteString(plan);
        return p;
    }

    public static Packet Intro(string text)
    {
        var p = Request(KingElection.NationIntro, KingElection.IntroWrite);
        p.WriteString(text);
        return p;
    }

    public static byte Ballot(bool inFavour) => inFavour ? KingElection.InFavour : KingElection.Against;

    public static byte U8(Packet p) => p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;

    public static short I16(Packet p) => p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;

    public static uint U32(Packet p) => p.RemainingBytes >= 4 ? p.ReadUInt() : 0;

    public static int I32(Packet p) => p.RemainingBytes >= 4 ? p.ReadInt() : 0;

    public static string Str8(Packet p) => Bytes(p, U8(p));

    public static string Str16(Packet p) => Bytes(p, p.RemainingBytes >= 2 ? p.ReadUShort() : 0);

    private static string Bytes(Packet p, int length)
    {
        if (length <= 0) return "";
        if (p.RemainingBytes < length)
        {
            p.ReadBytes(p.RemainingBytes);
            return "";
        }
        return Encoding.ASCII.GetString(p.ReadBytes(length));
    }

    public static KingSchedule ReadSchedule(Packet p)
    {
        byte kind = U8(p);
        if (kind == KingElection.ScheduleNone) return new KingSchedule(kind, 0, 0, 0, 0, U8(p));
        int month = U8(p), day = U8(p), hour = U8(p), minute = U8(p);
        return new KingSchedule(kind, month, day, hour, minute, 0);
    }

    public static KingCandidates ReadCandidates(Packet p)
    {
        short result = I16(p);
        var list = new List<KingCandidate>();
        if (result == KingElection.Success)
        {
            int count = U8(p);
            for (int i = 0; i < count && p.RemainingBytes > 0; i++)
            {
                int number = U8(p);
                string name = Str8(p);
                list.Add(new KingCandidate(number, name, Str8(p)));
            }
        }
        return new KingCandidates(result, list);
    }

    public static KingPlan ReadPlan(Packet p)
    {
        short result = I16(p);
        return new KingPlan(result, result == KingElection.Success ? Str16(p) : "");
    }

    public static KingShout ReadShout(Packet p)
    {
        short result = I16(p);
        var lines = new List<KingShoutLine>();
        if (result == KingElection.Success)
        {
            int count = U8(p);
            for (int i = 0; i < count && p.RemainingBytes > 0; i++)
            {
                int number = U8(p);
                string name = Str8(p);
                lines.Add(new KingShoutLine(number, name, Str16(p)));
            }
        }
        return new KingShout(result, lines);
    }

    public static KingChange ReadKingChange(Packet p) => new(U32(p), U32(p));

    public static KingSenators ReadSenators(Packet p)
    {
        short result = I16(p);
        var names = new List<string>();
        if (result == KingElection.Success)
        {
            int count = U8(p);
            for (int i = 0; i < count && p.RemainingBytes > 0; i++) names.Add(Str8(p));
        }
        return new KingSenators(result, names);
    }

    public static KingTreasury ReadTreasury(Packet p)
    {
        short view = I16(p);
        return view switch
        {
            NationTreasury.KingView => new KingTreasury(view, U32(p), U32(p)),
            NationTreasury.CitizenView => new KingTreasury(view, 0, U32(p)),
            _ => new KingTreasury(view, 0, 0),
        };
    }

    public static KingCoins ReadCoins(Packet p)
    {
        short result = I16(p);
        return result == KingElection.Success ? new KingCoins(result, U32(p), U32(p)) : new KingCoins(result, 0, 0);
    }

    public static KingTariff ReadTariff(Packet p)
    {
        short result = I16(p);
        return new KingTariff(result, result == KingElection.Success ? U8(p) : 0);
    }

    public static KingTreasuryNotice ReadTreasuryNotice(Packet p)
    {
        bool shown = U8(p) == KingElection.TreasuryNoticeShown;
        return shown ? new KingTreasuryNotice(true, I32(p), I32(p)) : new KingTreasuryNotice(false, 0, 0);
    }
}
