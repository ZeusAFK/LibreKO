namespace LibreKO.Game.World;

public class MerchantItem
{
    public int ItemId { get; set; }
    public short Durability { get; set; }
    public ushort Count { get; set; }
    public int Price { get; set; }
    public byte OriginalSlot { get; set; }

    public byte Flag { get; set; }
    public long ExpiresAt { get; set; }
    public int UniqueId { get; set; }

    public bool IsEmpty => ItemId == 0;
}
