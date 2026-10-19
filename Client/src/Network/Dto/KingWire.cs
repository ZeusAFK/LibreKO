using System.Collections.Generic;
using System.Text;
using LibreKO.Domain;

namespace LibreKO.Network;

public readonly record struct KingSchedule(KingScheduleKind Kind, int Month, int Day, int Hour, int Minute, int SenatorState);

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
    public const short Success = 1;
    public const short Refused = -1;
    public const short ShoutAskAgain = 2;
    public const byte IntroSaved = 1;
    public const byte TreasuryNoticeShown = 1;

    public static Packet Request(KingSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_KING);
        p.WriteByte((byte)sub);
        return p;
    }

    public static Packet Election(KingElectionSubOpcode sub) => With(Request(KingSubOpcode.Election), (byte)sub);

    public static Packet ElectionPlan(KingPlanSubOpcode sub) => With(Election(KingElectionSubOpcode.Plan), (byte)sub);

    public static Packet PlanBoard(KingPlanBoardSubOpcode sub) => With(ElectionPlan(KingPlanSubOpcode.Read), (byte)sub);

    public static Packet Poll(KingPollSubOpcode sub) => With(Election(KingElectionSubOpcode.Poll), (byte)sub);

    public static Packet Impeachment(KingImpeachmentSubOpcode sub) => With(Request(KingSubOpcode.Impeachment), (byte)sub);

    public static Packet Tax(KingTaxSubOpcode sub) => With(Request(KingSubOpcode.Tax), (byte)sub);

    public static Packet NationIntro(KingIntroSubOpcode sub) => With(Request(KingSubOpcode.NationIntro), (byte)sub);

    public static Packet Named(Packet p, string name)
    {
        p.WriteSByteString(name);
        return p;
    }

    public static Packet Plan(string plan)
    {
        var p = ElectionPlan(KingPlanSubOpcode.Write);
        p.WriteString(plan);
        return p;
    }

    public static Packet Intro(string text)
    {
        var p = NationIntro(KingIntroSubOpcode.Write);
        p.WriteString(text);
        return p;
    }

    public static KingBallot Ballot(bool inFavour) => inFavour ? KingBallot.InFavour : KingBallot.Against;

    public static Packet WithBallot(Packet p, bool inFavour) => With(p, (byte)Ballot(inFavour));

    private static Packet With(Packet p, byte value)
    {
        p.WriteByte(value);
        return p;
    }

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
        return Encoding.UTF8.GetString(p.ReadBytes(length));
    }

    public static KingSchedule ReadSchedule(Packet p)
    {
        var kind = (KingScheduleKind)U8(p);
        if (kind == KingScheduleKind.None) return new KingSchedule(kind, 0, 0, 0, 0, U8(p));
        int month = U8(p), day = U8(p), hour = U8(p), minute = U8(p);
        return new KingSchedule(kind, month, day, hour, minute, 0);
    }

    public static KingCandidates ReadCandidates(Packet p)
    {
        short result = I16(p);
        var list = new List<KingCandidate>();
        if (result == KingWire.Success)
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
        return new KingPlan(result, result == KingWire.Success ? Str16(p) : "");
    }

    public static KingShout ReadShout(Packet p)
    {
        short result = I16(p);
        var lines = new List<KingShoutLine>();
        if (result == KingWire.Success)
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
        if (result == KingWire.Success)
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
        return result == KingWire.Success ? new KingCoins(result, U32(p), U32(p)) : new KingCoins(result, 0, 0);
    }

    public static KingTariff ReadTariff(Packet p)
    {
        short result = I16(p);
        return new KingTariff(result, result == KingWire.Success ? U8(p) : 0);
    }

    public static KingTreasuryNotice ReadTreasuryNotice(Packet p)
    {
        bool shown = U8(p) == KingWire.TreasuryNoticeShown;
        return shown ? new KingTreasuryNotice(true, I32(p), I32(p)) : new KingTreasuryNotice(false, 0, 0);
    }
}

public enum KingSubOpcode : byte
{
    Election = 1,
    Impeachment = 2,
    Tax = 3,
    KingEvent = 4,
    ElectionOfficer = 5,
    NationIntro = 6,
    TreasuryNotice = 7,
}

public enum KingElectionSubOpcode : byte
{
    Schedule = 1,
    Nominate = 2,
    Plan = 3,
    Poll = 4,
    Withdraw = 5,
}

public enum KingPlanSubOpcode : byte
{
    Write = 1,
    Read = 2,
    Board = 5,
}

public enum KingPlanBoardSubOpcode : byte
{
    List = 1,
    Text = 2,
    Shout = 3,
}

public enum KingPollSubOpcode : byte
{
    List = 1,
    Vote = 2,
    KingChange = 4,
}

public enum KingImpeachmentSubOpcode : byte
{
    Propose = 1,
    SenatorVote = 2,
    Senators = 3,
    PublicVote = 4,
    Proposed = 5,
    SenatorBallot = 8,
    PublicBallot = 9,
}

public enum KingBallot : byte
{
    InFavour = 1,
    Against = 2,
}

public enum KingTaxSubOpcode : byte
{
    Open = 1,
    Fund = 2,
    RateRead = 3,
    RateSet = 4,
    Reserve = 5,
    KingItem = 7,
    ChannelOnly = 8,
}

public enum KingIntroSubOpcode : byte
{
    Read = 1,
    Write = 2,
}

public enum KingScheduleKind : byte
{
    None = 0,
    Election = 1,
    Impeachment = 2,
    ImpeachmentVote = 3,
}

public enum KingEventSubOpcode : byte
{
    DropRate = 1,
    Experience = 2,
    Prize = 3,
    Fugitive = 4,
    Weather = 5,
    RoyalOrder = 6,
}

public enum KingWeather : byte
{
    Clear = 1,
    Rain = 2,
    Snow = 3,
}
