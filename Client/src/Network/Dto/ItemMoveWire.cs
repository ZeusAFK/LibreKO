using LibreKO.Domain;

namespace LibreKO.Network;

public static class ItemMoveWire
{
    public static Packet Move(byte direction, int itemId, byte sourcePosition, byte destinationPosition, ushort amount)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_MOVE);
        p.WriteByte(ItemMove.MoveRequest);
        p.WriteByte(direction);
        p.WriteInt(itemId);
        p.WriteByte(sourcePosition);
        p.WriteByte(destinationPosition);
        p.WriteUShort(amount);
        return p;
    }
}
