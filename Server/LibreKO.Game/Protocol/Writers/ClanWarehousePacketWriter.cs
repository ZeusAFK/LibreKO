using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ClanWarehousePacketWriter
{
    public static Packet Result(WarehouseSubOpcode sub, ClanWarehouseResult result)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)result);
        return packet;
    }

    public static Packet Contents(WarehouseSubOpcode sub, ClanWarehouseResult result, int gold, IReadOnlyList<ItemSlot> slots)
    {
        var packet = Result(sub, result);
        packet.WriteInt(gold);

        foreach (var slot in slots)
        {
            packet.WriteInt(slot.ItemId);
            packet.WriteShort(slot.Durability);
            packet.WriteUShort(slot.Count);
            packet.WriteByte(slot.Flag);
            packet.WriteULong(0);
        }

        return packet;
    }

    private static Packet Sub(WarehouseSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
