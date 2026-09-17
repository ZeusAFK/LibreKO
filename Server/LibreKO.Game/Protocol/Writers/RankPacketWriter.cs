using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class RankPacketWriter
{
    public const int ChaosDungeonTrailer = 1;
    public const ushort NoPremiumBonus = 0;
    public const byte NoSymbolRank = 0xFF;

    public readonly record struct RankEntry(
        string Name,
        byte Nation,
        ushort ClanId,
        ushort MarkVersion,
        string ClanName,
        int DailyLoyalty);

    public static Packet PkZone(
        byte rankType,
        IReadOnlyList<RankEntry> karus,
        IReadOnlyList<RankEntry> elmorad,
        ushort myRank,
        int myDailyLoyalty)
    {
        var packet = Sub(rankType);
        WriteNation(packet, karus);
        WriteNation(packet, elmorad);
        packet.WriteUShort(myRank);
        packet.WriteInt(myDailyLoyalty);
        packet.WriteUShort(NoPremiumBonus);
        return packet;
    }

    public static Packet BorderDefenseWar(byte rankType)
    {
        var packet = Sub(rankType);
        packet.WriteUShort(0);
        packet.WriteUShort(0);
        packet.WriteLong(0);
        packet.WriteLong(0);
        return packet;
    }

    public static Packet ChaosDungeon(byte rankType)
    {
        var packet = Sub(rankType);
        packet.WriteByte(0);
        packet.WriteInt(0);
        packet.WriteInt(0);
        packet.WriteInt(ChaosDungeonTrailer);
        return packet;
    }

    public static Packet Unsupported(byte rankType)
    {
        var packet = Sub(rankType);
        packet.WriteUShort(0);
        return packet;
    }

    private static void WriteNation(Packet packet, IReadOnlyList<RankEntry> entries)
    {
        packet.WriteUShort((ushort)entries.Count);
        foreach (var entry in entries)
        {
            packet.WriteSByteString(entry.Name);
            packet.WriteByte(entry.Nation);
            packet.WriteUShort(entry.ClanId);
            packet.WriteUShort(entry.MarkVersion);
            packet.WriteSByteString(entry.ClanName);
            packet.WriteInt(entry.DailyLoyalty);
            packet.WriteUShort(NoPremiumBonus);
            packet.WriteByte(NoSymbolRank);
        }
    }

    private static Packet Sub(byte rankType)
    {
        var packet = new Packet(GameOpcodes.GS_RANK);
        packet.WriteByte(rankType);
        return packet;
    }
}
