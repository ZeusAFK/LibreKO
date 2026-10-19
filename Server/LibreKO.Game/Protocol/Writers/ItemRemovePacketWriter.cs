using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum ItemRemoveResult : byte
{
    Failed = 0,
    Removed = 1,
}

public enum ItemRemoveSource : byte
{
    Inventory = 0,
    Equipment = 1,
    Bag = 2,
    MagicBag = 3,
}

public enum ItemRemoveReason : byte
{
    SystemError = 0,
    NoVipKeyPassword = 20,
    VipKeyLocked = 21,
    WrongVipKeyPassword = 22,
}

public sealed class ItemRemovePacketWriter
{
    public ItemRemoveResult Result { get; set; }
    public ItemRemoveReason Reason { get; set; }

    public static Packet Removed() => new ItemRemovePacketWriter() { Result = ItemRemoveResult.Removed }.Build();
    public static Packet Failed(ItemRemoveReason reason = ItemRemoveReason.SystemError) => new ItemRemovePacketWriter() { Result = ItemRemoveResult.Failed, Reason = reason }.Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte((byte)Result);

        if (Result == ItemRemoveResult.Failed)
            packet.WriteByte((byte)Reason);

        return packet;
    }
}
