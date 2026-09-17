using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class PartyBbsPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public const byte ModeNormal = 0;
    public const byte ModeRestricted = 2;

    public const byte EntrySeeker = 2;
    public const byte EntryWantedParty = 3;

    public readonly record struct BoardEntry(
        string Name,
        short Class,
        byte Level,
        byte Type,
        string Message,
        short ZoneId,
        byte MemberCount,
        byte Nation);

    public static Packet Result(byte mode, byte sub, byte result)
    {
        var packet = Sub(mode, sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet Page(
        byte mode, byte sub, short pageIndex, short totalPages, IReadOnlyList<BoardEntry> entries)
    {
        var packet = Result(mode, sub, Succeeded);
        packet.WriteShort(pageIndex);
        packet.WriteShort((short)entries.Count);
        packet.WriteShort(totalPages);

        foreach (var entry in entries)
        {
            packet.WriteString(entry.Name);
            packet.WriteInt(entry.Class);
            packet.WriteByte(entry.Level);
            packet.WriteByte(entry.Type);
            packet.WriteByte(0);
            packet.WriteSByteString(entry.Message);
            packet.WriteShort(entry.ZoneId);
            packet.WriteByte(entry.MemberCount);
            packet.WriteByte(entry.Nation);
            packet.WriteByte(0);
        }

        return packet;
    }

    private static Packet Sub(byte mode, byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY_BBS);
        packet.WriteByte(mode);
        packet.WriteByte(sub);
        return packet;
    }
}
