namespace LibreKO.Game.World;

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
