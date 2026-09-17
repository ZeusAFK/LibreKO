using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ItemCountChangePacketWriter
{
    public const byte KindUpdate = 1;
    public const byte FlagNewItem = 100;
    public const byte FlagCountChanged = 0;

    public const int EntryBytes = 17;

    public readonly record struct Entry(
        byte Kind,
        byte Position,
        int ItemId,
        int Quantity,
        byte Flag,
        short Durability,
        int Serial);

    private readonly List<Entry> _entries = [];

    public static byte NormalizePosition(byte pos) =>
        pos >= InventoryConstants.InventoryStart
            ? (byte)(pos - InventoryConstants.InventoryStart)
            : pos;

    public ItemCountChangePacketWriter Add(
        byte position, int itemId, int quantity, short durability, bool isNewItem = false, int serial = 0)
    {
        _entries.Add(new Entry(
            KindUpdate,
            NormalizePosition(position),
            itemId,
            quantity,
            isNewItem ? FlagNewItem : FlagCountChanged,
            durability,
            serial));

        return this;
    }

    public Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_COUNT_CHANGE);
        packet.WriteShort((short)_entries.Count);

        foreach (var entry in _entries)
        {
            packet.WriteByte(entry.Kind);
            packet.WriteByte(entry.Position);
            packet.WriteInt(entry.ItemId);
            packet.WriteInt(entry.Quantity);
            packet.WriteByte(entry.Flag);
            packet.WriteShort(entry.Durability);
            packet.WriteInt(entry.Serial);
        }

        return packet;
    }
}
