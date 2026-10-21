using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public class TradeState
{
    public const int MerchantSlots = 12;

    // Exchange (player-to-player trade)
    public int ExchangeUser { get; set; } = -1;
    public bool ExchangeOk { get; set; }
    public bool AskedForExchange { get; set; }
    public bool ExchangeAccepted { get; set; }
    public List<ExchangeItem> ExchangeItemList { get; } = [];
    public bool IsTrading => ExchangeUser != -1;

    // Merchant (personal shop)
    public MerchantMode MerchantState { get; set; } = MerchantMode.None;
    public bool IsMerchanting => MerchantState != MerchantMode.None;
    public bool IsSellingMerchant => MerchantState == MerchantMode.Selling;
    public bool IsBuyingMerchant => MerchantState == MerchantMode.Buying;
    public bool IsSellingMerchantPreparing { get; set; }
    public bool IsBuyingMerchantPreparing { get; set; }
    public bool IsMerchantPreparing => IsSellingMerchantPreparing || IsBuyingMerchantPreparing;
    public bool PremiumMerchant { get; set; }
    public string MerchantAdvert { get; set; } = string.Empty;
    public int MerchantTargetUserId { get; set; } = -1;
    public int MerchantViewerId { get; set; } = -1;

    private static int stallGenerations;
    private int stallGeneration = Interlocked.Increment(ref stallGenerations);

    // A look at a stall holds only for the stall that was open when the look began, and no two stalls ever share a count.
    public int StallGeneration => Volatile.Read(ref stallGeneration);
    public void EndStall() => Volatile.Write(ref stallGeneration, Interlocked.Increment(ref stallGenerations));
    public int ViewedStallGeneration { get; private set; }

    public void LookAt(int merchantId, int generation) => (MerchantTargetUserId, ViewedStallGeneration) = (merchantId, generation);
    public MerchantItem[] MerchantItems { get; } = new MerchantItem[MerchantSlots];
    public MerchantItem[] BuyMerchantItems { get; } = new MerchantItem[MerchantSlots];

    public int BuyMerchantGold { get; set; }

    // PvP Challenge (duel)
    public int ChallengeUser { get; set; } = -1;
    public bool IsRequestingChallenge { get; set; }
    public bool IsChallengeRequested { get; set; }
}
