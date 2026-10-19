namespace LibreKO.Network;

public static class ItemMoveWire
{
    public static Packet Move(ItemMoveDirection direction, int itemId, byte sourcePosition, byte destinationPosition, ushort amount)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_MOVE);
        p.WriteByte((byte)ItemMoveSubOpcode.Move);
        p.WriteByte((byte)direction);
        p.WriteInt(itemId);
        p.WriteByte(sourcePosition);
        p.WriteByte(destinationPosition);
        if (amount != ItemMove.WholeStack) p.WriteUShort(amount);
        return p;
    }
}
