namespace LibreKO.Game.World;

// An offered item is held here, out of the bag, until the trade completes or is cancelled.
// It carries the whole item identity so it goes back, or across, as the same item.
public class ExchangeItem
{
    public int ItemId { get; set; }
    public short Durability { get; set; }
    public int Count { get; set; }
    public byte Flag { get; set; }
    public long ExpiresAt { get; set; }
    public bool Countable { get; set; }
    public byte SrcPos { get; set; }
    public byte DstPos { get; set; }
    public int UniqueId { get; set; }
}
