using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ItemDropPacketWriter
{
    public int SourceId { get; set; }
    public int BundleId { get; set; }
    public bool HasItems { get; set; }

    public static Packet Dropped(int sourceId, int bundleId, bool hasItems) => new ItemDropPacketWriter() { SourceId = sourceId, BundleId = bundleId, HasItems = hasItems }.Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_DROP);
        packet.WriteInt(SourceId);
        packet.WriteInt(BundleId);
        packet.WriteByte(HasItems ? (byte)1 : (byte)0);
        return packet;
    }
}
