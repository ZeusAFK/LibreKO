using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class KingPacketWriter
{
    public const short Accepted = 1;

    public readonly record struct PollCandidate(string Name, string ClanName);

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

    public static Packet FlagWithAmount(byte sub, byte subType, byte result, int amount)
    {
        var packet = Sub(sub, subType);
        packet.WriteByte(result);
        packet.WriteInt(amount);
        return packet;
    }

    public static Packet NationIntro(byte sub, byte result, string kingName, int treasury, byte tariff)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(sub);
        packet.WriteByte(result);
        packet.WriteSByteString(kingName);
        packet.WriteInt(treasury);
        packet.WriteByte(tariff);
        return packet;
    }

    public static Packet KingNpc(byte sub, string kingName)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(sub);
        packet.WriteSByteString(kingName);
        return packet;
    }

    public static Packet ResultWithName(byte sub, byte subType, short result, string name)
    {
        var packet = Sub(sub, subType);
        packet.WriteShort(result);
        packet.WriteString(name);
        return packet;
    }

    public static Packet ImpeachmentState(
        byte sub, byte subType, short result, short impeachmentType, short extra)
    {
        var packet = Sub(sub, subType);
        packet.WriteShort(result);
        packet.WriteShort(impeachmentType);
        packet.WriteShort(extra);
        return packet;
    }

    public static Packet Flags(byte sub, byte subType, byte first, byte second)
    {
        var packet = Sub(sub, subType);
        packet.WriteByte(first);
        packet.WriteByte(second);
        return packet;
    }

    public static Packet Acknowledge(byte sub, byte subType) => Sub(sub, subType);

    public static Packet TaxRate(byte sub, byte subType, string name, int amount, byte rate)
    {
        var packet = Sub(sub, subType);
        packet.WriteSByteString(name);
        packet.WriteInt(amount);
        packet.WriteByte(rate);
        return packet;
    }

    public static Packet Treasury(byte sub, string name, byte first, byte second, int amount)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(sub);
        packet.WriteSByteString(name);
        packet.WriteByte(first);
        packet.WriteByte(second);
        packet.WriteInt(amount);
        return packet;
    }

    public static Packet Notice(byte sub, string message)
        => sub == NoticePacketWriter.LoginNotice
            ? NoticePacketWriter.Login([(string.Empty, message)])
            : NoticePacketWriter.Screen(message);

    public static Packet Election(byte electionOpcode, byte election) => Sub(election, electionOpcode);

    public static Packet ElectionSchedule(
        byte electionOpcode, byte election, byte month, byte day, byte hour, byte minute)
    {
        var packet = Sub(election, electionOpcode);
        packet.WriteByte(1);
        packet.WriteByte(month);
        packet.WriteByte(day);
        packet.WriteByte(hour);
        packet.WriteByte(minute);
        return packet;
    }

    public static Packet NoticeBoardResult(
        byte boardOpcode, byte election, byte noticeBoard, short result)
    {
        var packet = NoticeBoard(boardOpcode, election, noticeBoard);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet NoticeBoardEntry(
        byte boardOpcode, byte election, byte noticeBoard, byte readSubOpcode)
    {
        var packet = NoticeBoard(boardOpcode, election, noticeBoard);
        packet.WriteByte(readSubOpcode);
        return packet;
    }

    public static Packet PollEntry(byte election, byte poll, byte pollOpcode)
    {
        var packet = Sub(election, poll);
        packet.WriteByte(pollOpcode);
        return packet;
    }

    public static Packet PollResult(byte election, byte poll, byte pollOpcode, short result)
    {
        var packet = PollEntry(election, poll, pollOpcode);
        packet.WriteShort(result);
        return packet;
    }

    public static Packet PollCandidates(
        byte election, byte poll, byte pollOpcode, IReadOnlyCollection<PollCandidate> candidates)
    {
        var packet = PollResult(election, poll, pollOpcode, Accepted);
        packet.WriteByte((byte)candidates.Count);
        foreach (var candidate in candidates)
        {
            packet.WriteSByteString(candidate.Name);
            packet.WriteSByteString(candidate.ClanName);
        }

        return packet;
    }

    public static Packet CandidateList(
        byte boardOpcode, byte election, byte noticeBoard, byte readSubOpcode,
        IReadOnlyCollection<string> names)
    {
        var packet = NoticeBoardEntry(boardOpcode, election, noticeBoard, readSubOpcode);
        packet.WriteByte((byte)names.Count);
        foreach (var name in names)
            packet.WriteSByteString(name);
        return packet;
    }

    public static Packet CandidateNotice(
        byte boardOpcode, byte election, byte noticeBoard, byte readSubOpcode,
        short noticeLength, byte[] notice)
    {
        var packet = NoticeBoard(boardOpcode, election, noticeBoard);
        packet.WriteByte(readSubOpcode);
        packet.WriteShort(noticeLength);
        packet.WriteBytes(notice);
        return packet;
    }

    public static Packet NoticeBoard(byte boardOpcode, byte election, byte noticeBoard)
    {
        var packet = Sub(election, noticeBoard);
        packet.WriteByte(boardOpcode);
        return packet;
    }

    private static Packet Sub(byte sub, byte subType)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(sub);
        packet.WriteByte(subType);
        return packet;
    }
}
