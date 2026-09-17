using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BundleOpenPacketWriter
{
    public const int WireSlots = 12;
    public const int EntryBytes = 6;

    public readonly record struct Entry(int ItemId, ushort Count);

    private readonly List<Entry> _entries = [];

    public int BundleId { get; set; }

    public BundleOpenPacketWriter Add(int itemId, ushort count)
    {
        if (_entries.Count < WireSlots)
            _entries.Add(new Entry(itemId, count));

        return this;
    }

    public Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        packet.WriteInt(BundleId);

        if (_entries.Count == 0)
        {
            packet.WriteByte(0);
            return packet;
        }

        packet.WriteByte(1);
        for (var index = 0; index < WireSlots; index++)
        {
            var entry = index < _entries.Count ? _entries[index] : default;
            packet.WriteInt(entry.ItemId);
            packet.WriteUShort(entry.Count);
        }

        return packet;
    }
}
