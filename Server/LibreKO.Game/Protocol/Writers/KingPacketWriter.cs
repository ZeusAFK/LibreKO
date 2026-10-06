using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class KingPacketWriter
{
    public const short Accepted = 1;

    public readonly record struct PollCandidate(byte Number, string Name, string ClanName);

    public static Packet Result(byte sub, byte subType, short result)
    {
        var packet = Sub(sub, subType);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet Flag(byte sub, byte subType, byte result)
    {
        var packet = Sub(sub, subType);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet FlagWithValue(byte sub, byte subType, byte result, byte value)
    {
        var packet = Sub(sub, subType);
        packet.WriteByte(result);
        packet.WriteByte(value);
        return packet;
    }

    public static Packet Notice(byte sub, string message)
        => sub == NoticePacketWriter.LoginNotice
            ? NoticePacketWriter.Login([(string.Empty, message)])
            : NoticePacketWriter.Screen(message);

    public static Packet ElectionOfficer(string kingName)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(KingPacketConstants.ElectionOfficer);
        packet.WriteSByteString(kingName);
        return packet;
    }

    public static Packet KingTreasury(uint kingsFund, uint nationalTreasury)
    {
        var packet = Result(KingPacketConstants.Tax, KingPacketConstants.TaxTreasury, KingPacketConstants.TreasuryKing);
        packet.WriteUInt(kingsFund);
        packet.WriteUInt(nationalTreasury);
        return packet;
    }

    public static Packet CitizenTreasury(uint nationalTreasury)
    {
        var packet = Result(KingPacketConstants.Tax, KingPacketConstants.TaxTreasury, KingPacketConstants.TreasuryCitizen);
        packet.WriteUInt(nationalTreasury);
        return packet;
    }

    public static Packet ElectionSchedule(byte kind, byte month, byte day, byte hour, byte minute)
    {
        var packet = Sub(KingPacketConstants.Election, KingPacketConstants.ElectionSchedule);
        packet.WriteByte(kind);
        packet.WriteByte(month);
        packet.WriteByte(day);
        packet.WriteByte(hour);
        packet.WriteByte(minute);
        return packet;
    }

    public static Packet NoElectionSchedule(byte impeachmentState)
    {
        var packet = Sub(KingPacketConstants.Election, KingPacketConstants.ElectionSchedule);
        packet.WriteByte(KingPacketConstants.ScheduleNone);
        packet.WriteByte(impeachmentState);
        return packet;
    }

    public static Packet PlanWriteResult(short result)
    {
        var packet = Sub(KingPacketConstants.Election, KingPacketConstants.ElectionNoticeBoard);
        packet.WriteByte(KingPacketConstants.CandidacyBoardWrite);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet Plan(byte[] plan)
    {
        var packet = PlanRead(Accepted);
        packet.WriteUShort((ushort)plan.Length);
        packet.WriteBytes(plan);
        return packet;
    }

    public static Packet PlanRefused(short result) => PlanRead(result);

    private static Packet PlanRead(short result)
    {
        var packet = Sub(KingPacketConstants.Election, KingPacketConstants.ElectionNoticeBoard);
        packet.WriteByte(KingPacketConstants.CandidacyBoardRead);
        packet.WriteByte(KingPacketConstants.BoardReadPlan);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet PollCandidates(IReadOnlyCollection<PollCandidate> candidates)
    {
        var packet = PollResult(KingPacketConstants.PollCandidateList, Accepted);
        packet.WriteByte((byte)candidates.Count);
        foreach (var candidate in candidates)
        {
            packet.WriteByte(candidate.Number);
            packet.WriteSByteString(candidate.Name);
            packet.WriteSByteString(candidate.ClanName);
        }

        return packet;
    }

    public static Packet PollResult(byte pollOpcode, short result)
    {
        var packet = Sub(KingPacketConstants.Election, KingPacketConstants.ElectionPoll);
        packet.WriteByte(pollOpcode);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet ImpeachmentSupporters(IReadOnlyCollection<string> senators)
    {
        var packet = Result(KingPacketConstants.Impeachment, KingPacketConstants.ImpeachmentList, Accepted);
        packet.WriteByte((byte)senators.Count);
        foreach (var senator in senators)
            packet.WriteSByteString(senator);
        return packet;
    }

    public static Packet KingsFundCollected(uint newCoins, uint collected)
    {
        var packet = Result(KingPacketConstants.Tax, KingPacketConstants.TaxCollect, Accepted);
        packet.WriteUInt(newCoins);
        packet.WriteUInt(collected);
        return packet;
    }

    public static Packet Tariff(byte taxOpcode, byte tariff)
    {
        var packet = Result(KingPacketConstants.Tax, taxOpcode, Accepted);
        packet.WriteByte(tariff);
        return packet;
    }

    public static Packet NationIntro(string intro)
    {
        var packet = Sub(KingPacketConstants.NationIntro, KingPacketConstants.NationIntroRead);
        packet.WriteString(intro);
        return packet;
    }

    public static Packet NationIntroWritten(byte result) =>
        Flag(KingPacketConstants.NationIntro, KingPacketConstants.NationIntroWrite, result);

    private static Packet Sub(byte sub, byte subType)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(sub);
        packet.WriteByte(subType);
        return packet;
    }
}
