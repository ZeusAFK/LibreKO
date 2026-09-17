namespace LibreKO.Game.World;

public class ExchangeItem
{
    public int ItemId { get; set; }
    public short Durability { get; set; }
    public int Count { get; set; }
    public byte SrcPos { get; set; }
    public byte DstPos { get; set; }
}
