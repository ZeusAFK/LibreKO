namespace LibreKO.Game.World;

public class ActiveOverTimeEffect
{
    public int MagicId { get; set; }
    public int CasterId { get; set; }
    public short TickAmount { get; set; }
    public byte TickIntervalSeconds { get; set; } = 2;
    public byte TickCount { get; set; }
    public byte TickLimit { get; set; }
    public long NextTickTicks { get; set; }

    public byte PartyStatusCode { get; set; }
}
