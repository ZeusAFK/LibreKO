using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public class TradeState
{
    public const int MerchantSlots = 12;

    // Exchange (player-to-player trade)
    public int ExchangeUser { get; set; } = -1;
    public bool ExchangeOk { get; set; }
    public bool AskedForExchange { get; set; }
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
    public MerchantItem[] MerchantItems { get; } = new MerchantItem[MerchantSlots];
    public MerchantItem[] BuyMerchantItems { get; } = new MerchantItem[MerchantSlots];

    // PvP Challenge (duel)
    public int ChallengeUser { get; set; } = -1;
    public bool IsRequestingChallenge { get; set; }
    public bool IsChallengeRequested { get; set; }
}
