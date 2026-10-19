namespace LibreKO.Network;

public readonly record struct StallShowcaseReply(int Owner, byte Flags, int[]? ItemIds, int Slot)
{
    public const int NoSlot = -1;
    private const int HeadBytes = 6;

    public static StallShowcaseReply? Read(Packet p)
    {
        if (p.RemainingBytes < HeadBytes) return null;
        int owner = p.ReadInt();
        p.ReadByte();
        byte flags = p.ReadByte();
        if ((flags & MerchantWire.PremiumFlagMask) != 0)
        {
            var ids = new int[MerchantWire.StallDisplaySlotsPremium];
            for (int i = 0; i < ids.Length && p.RemainingBytes >= sizeof(int); i++) ids[i] = p.ReadInt();
            return new StallShowcaseReply(owner, flags, ids, NoSlot);
        }
        return p.RemainingBytes >= 1 ? new StallShowcaseReply(owner, flags, null, p.ReadByte()) : null;
    }
}
