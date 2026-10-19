namespace LibreKO.Network;

public readonly record struct VipVaultContents(bool PinSet, bool Open, uint ExpiresAt, ItemSlot[] Slots)
{
    public const int Storages = 4;
    public const byte OwnStorage = 0;
    private const int PinFlagBytes = 1;
    private const int StorageRecordBytes = 5;
    private const int ItemRecordBytes = 17;
    private const int ItemTailBytes = 8;

    public static VipVaultContents? Read(Packet p, int slots)
    {
        if (p.RemainingBytes < PinFlagBytes + Storages * StorageRecordBytes) return null;
        bool pinSet = p.ReadByte() != 0;
        bool open = false;
        uint expiresAt = 0;
        for (int storage = 0; storage < Storages; storage++)
        {
            bool active = p.ReadByte() != 0;
            uint at = p.ReadUInt();
            if (storage != OwnStorage) continue;
            open = active;
            expiresAt = at;
        }
        var items = new ItemSlot[slots];
        for (int i = 0; i < slots && p.RemainingBytes >= ItemRecordBytes; i++)
        {
            int itemId = p.ReadInt();
            short durability = (short)p.ReadUShort();
            short count = (short)p.ReadUShort();
            byte flag = p.ReadByte();
            for (int skip = 0; skip < ItemTailBytes; skip++) p.ReadByte();
            items[i] = itemId == 0 ? default : new ItemSlot { ItemId = itemId, Durability = durability, Count = count, Flag = flag };
        }
        return new VipVaultContents(pinSet, open, expiresAt, items);
    }
}

public readonly record struct VipVaultKeyReply(VipWarehouseResult Result, byte Storage, uint ExpiresAt)
{
    private const int StorageAndTimeBytes = 5;

    public static VipVaultKeyReply? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var result = (VipWarehouseResult)p.ReadByte();
        if (result != VipWarehouseResult.Succeeded) return new VipVaultKeyReply(result, 0, 0);
        return p.RemainingBytes >= StorageAndTimeBytes ? new VipVaultKeyReply(result, p.ReadByte(), p.ReadUInt()) : null;
    }
}
