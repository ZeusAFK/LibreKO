using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BundleOpenPacketWriter
{
    public const int WireSlots = 12;
    public const int EntryBytes = 6;

    public const byte ResultEmpty = 0;
    public const byte ResultOpened = 1;
    public const byte ResultGone = 2;
    public const byte ResultNotYours = 4;

    public static Packet Refused(int bundleId, byte result)
    {
        var packet = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        packet.WriteInt(bundleId);
        packet.WriteByte(result);
        return packet;
    }

    public readonly record struct Entry(int ItemId, ushort Count);

    private readonly List<Entry> entries = [];

    public int BundleId { get; set; }

    public BundleOpenPacketWriter Add(int itemId, ushort count)
    {
        if (entries.Count < WireSlots)
            entries.Add(new Entry(itemId, count));

        return this;
    }

    public Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        packet.WriteInt(BundleId);

        if (entries.Count == 0)
        {
            packet.WriteByte(ResultEmpty);
            return packet;
        }

        packet.WriteByte(ResultOpened);
        for (var index = 0; index < WireSlots; index++)
        {
            var entry = index < entries.Count ? entries[index] : default;
            packet.WriteInt(entry.ItemId);
            packet.WriteUShort(entry.Count);
        }

        return packet;
    }
}
