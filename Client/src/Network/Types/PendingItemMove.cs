namespace LibreKO.Network;

public readonly struct PendingItemMove
{
    public readonly ItemMoveDirection Dir;
    public readonly byte Src, Dst;
    public readonly ushort Amount;

    public PendingItemMove(ItemMoveDirection dir, byte src, byte dst, ushort amount)
    {
        Dir = dir;
        Src = src;
        Dst = dst;
        Amount = amount;
    }
}
