using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

// What handing a trade's escrowed offers back did: the gold that went into the purse or, past
// the coin cap, into the warehouse, and whatever found no room anywhere.
public sealed class ExchangeReturn
{
    public int GoldToPurse { get; set; }
    public int GoldToWarehouse { get; set; }
    public long GoldLost { get; set; }
    public List<ExchangeItem> Unplaced { get; } = [];

    public void LogLosses(ILogger logger, string owner)
    {
        foreach (var item in Unplaced)
            logger.LogError("Exchange item {ItemId} x{Count} of {Name} found no room to return", item.ItemId, item.Count, owner);
        if (GoldLost > 0)
            logger.LogError("Exchange gold {Gold} of {Name} fit in neither the purse nor the warehouse", GoldLost, owner);
    }
}
