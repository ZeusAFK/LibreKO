using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class RentalPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const short NpcAcknowledged = 1;

    public readonly record struct CatalogEntry(int ItemId, int Days, int Cost);

    public static Packet Catalog(byte sub, IReadOnlyCollection<CatalogEntry> entries)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)entries.Count);
        foreach (var entry in entries)
        {
            packet.WriteInt(entry.ItemId);
            packet.WriteInt(entry.Days);
            packet.WriteInt(entry.Cost);
        }
        return packet;
    }

    public static Packet RentResult(byte sub, byte result, int itemId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(itemId);
        return packet;
    }

    public static Packet NpcOpened(byte sub)
    {
        var packet = Sub(sub);
        packet.WriteShort(NpcAcknowledged);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_RENTAL);
        packet.WriteByte(sub);
        return packet;
    }
}
