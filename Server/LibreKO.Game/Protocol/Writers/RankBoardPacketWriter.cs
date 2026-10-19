using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public readonly record struct RankBoardEntry(
    ushort Slot, string Name, short ClanId, short MarkVersion, string ClanName, byte Flag, byte Grade)
{
    public const short NoClan = -1;
}

public static class RankBoardPacketWriter
{
    public const ushort KingSlot = 1000;
    public const ushort FirstPlayerSlot = 1101;
    public const ushort FirstClanSlot = 1201;
    public const int Rows = 10;
    private const ushort Unused = 0;

    public static Packet Board(IReadOnlyList<RankBoardEntry> entries)
    {
        var packet = BattleEventPacketWriter.Sub(BattleEventSubOpcode.RankBoard);
        packet.WriteByte((byte)entries.Count);
        foreach (var entry in entries)
        {
            packet.WriteUShort(entry.Slot);
            packet.WriteString(entry.Name);
            packet.WriteUShort(Unused);
            packet.WriteShort(entry.ClanId);
            packet.WriteShort(entry.MarkVersion);
            packet.WriteString(entry.ClanName);
            packet.WriteByte(entry.Flag);
            packet.WriteByte(entry.Grade);
        }
        return packet;
    }
}
