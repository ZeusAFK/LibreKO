using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum ItemRepairResult : byte
{
    Failed = 0,
    Repaired = 1,
}

public sealed class ItemRepairPacketWriter
{
    public ItemRepairResult Result { get; set; }
    public int Money { get; set; }

    public static Packet Repaired(int money) => new ItemRepairPacketWriter() { Result = ItemRepairResult.Repaired, Money = money }.Build();
    public static Packet Completed(ItemRepairResult result, int money) => new ItemRepairPacketWriter() { Result = result, Money = money }.Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_REPAIR);
        packet.WriteByte((byte)Result);
        packet.WriteInt(Money);
        return packet;
    }
}
