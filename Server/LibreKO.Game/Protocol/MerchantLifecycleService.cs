using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMerchantLifecycleService
{
    Task OpenAsync(UserSession session);
    Task CloseAsync(UserSession session, MerchantInOut? merchantInOutType);
    Task InsertAsync(UserSession session, Packet packet);
    Task CancelAsync(UserSession session);
    Task RequestStallAsync(UserSession session, Packet packet, MerchantSubOpcode sub);
    void ReleaseViewedStall(UserSession session);
    Task SendStallListAsync(UserSession session, int merchantId);
    MerchantPacketWriter.StallOwner StallOwnerOf(UserSession session);
}

public class MerchantLifecycleService(
    SessionManager sessionManager,
    IMerchantBotService merchantBotService,
    ISlaveMerchantService slaveMerchants,
    IMerchantBuyingService merchantBuyingService,
    ILogger<MerchantLifecycleService> logger) : IMerchantLifecycleService
{
    private const short OpenAccepted = 1;
    private const short OpenWhileDead = -2;
    private const short OpenWhileTrading = -3;
    private const short OpenWhileMerchanting = -4;
    private const short MinimumMerchantLevel = 30;

    public async Task OpenAsync(UserSession session)
    {
        short openResult;
        if (session.Hp <= 0)
        {
            openResult = OpenWhileDead;
        }
        else if (session.Trade.IsTrading || session.IsGathering)
        {
            openResult = OpenWhileTrading;
        }
        else if (session.Trade.IsMerchanting)
        {
            openResult = OpenWhileMerchanting;
        }
        else if (session.Level < MinimumMerchantLevel)
        {
            openResult = MinimumMerchantLevel;
        }
        else
        {
            openResult = OpenAccepted;
            session.Trade.MerchantTargetUserId = -1;
            session.Trade.IsSellingMerchantPreparing = true;
            for (var i = 0; i < session.Trade.MerchantItems.Length; i++)
                session.Trade.MerchantItems[i] = new MerchantItem();
        }

        await session.Client.SendPacket(MerchantPacketWriter.OpenResult(
            MerchantSubOpcode.Open, openResult));

        if (session.Trade.IsMerchanting)
            await CloseAsync(session, MerchantInOut.StallClosed);
    }

    public async Task CloseAsync(UserSession session, MerchantInOut? merchantInOutType)
    {
        // A slave merchant's stall is closed only by the slave merchant service.
        if (session is SlaveMerchantBotSession)
            return;

        // A buying stall closes through its own path, which hands back the gold it holds.
        if (session.Trade.IsBuyingMerchant || session.Trade.IsBuyingMerchantPreparing || session.Trade.BuyMerchantGold > 0)
            await merchantBuyingService.CloseAsync(session, broadcast: merchantInOutType.HasValue);

        if (!session.Trade.IsMerchanting && !session.Trade.IsSellingMerchantPreparing && !HasMerchantItems(session))
            return;

        for (var i = 0; i < session.Trade.MerchantItems.Length; i++)
            session.Trade.MerchantItems[i] = new MerchantItem();

        session.Trade.MerchantState = MerchantMode.None;
        session.Trade.IsSellingMerchantPreparing = false;
        session.Trade.MerchantTargetUserId = -1;
        session.Trade.MerchantAdvert = string.Empty;
        sessionManager.ForgetStall(session);

        if (!merchantInOutType.HasValue)
            return;

        var close = MerchantPacketWriter.StallClosed(session.CharacterId);
        await sessionManager.Regions.SendToRegion(session, close, excludeSender: merchantInOutType.Value == MerchantInOut.SessionEnded);
    }

    public async Task InsertAsync(UserSession session, Packet packet)
    {
        var advertMessage = packet.ReadString();
        var toSlave = packet.RemainingBytes > 0 && packet.ReadByte() != 0;
        var staged = session.Trade.MerchantItems.Count(item => item is { IsEmpty: false });
        var refusal =
            session.Hp <= 0 || session.Trade.IsTrading || session.IsGathering ? "dead, trading or gathering"
            : !session.Trade.IsSellingMerchantPreparing || session.Trade.IsMerchanting ? "no stall is being set up"
            : advertMessage.Length > MerchantPacketConstants.MaxAdvertLength ? $"advert is {advertMessage.Length} characters"
            : staged == 0 ? "no items staged"
            : null;

        if (refusal != null)
        {
            logger.LogDebug("Merchant insert refused for {Name}: {Reason}", session.Name, refusal);
            await session.Client.SendPacket(MerchantPacketWriter.InsertRefused());
            return;
        }

        logger.LogDebug(
            "Merchant insert accepted for {Name}: advert \"{Advert}\", {Staged} staged, character {CharacterId}",
            session.Name, advertMessage, staged, session.CharacterId);

        if (toSlave)
        {
            await slaveMerchants.DeployAsync(session, advertMessage);
            await session.Client.SendPacket(MerchantPacketWriter.StallClosed(session.CharacterId));
            await CloseAsync(session, null);
            return;
        }

        if (session.IsGM)
        {
            await merchantBotService.CloneFromGmAsync(session, advertMessage);
            await session.Client.SendPacket(MerchantPacketWriter.StallClosed(session.CharacterId));
            await CloseAsync(session, null);
            return;
        }

        session.Trade.MerchantState = MerchantMode.Selling;
        session.Trade.IsSellingMerchantPreparing = true;
        session.Trade.MerchantAdvert = advertMessage;

        var broadcast = MerchantPacketWriter.StallInserted(
            MerchantPacketWriter.Succeeded, advertMessage, session.CharacterId,
            StallFlags(session), DisplayItemIds(session));

        logger.LogDebug("Merchant insert REPLY to {Name}: {Payload}",
            session.Name, Convert.ToHexString(broadcast.GetData()));

        await sessionManager.Regions.SendToRegion(session, broadcast, excludeSender: false);
    }

    public Task CancelAsync(UserSession session)
    {
        ReleaseViewedStall(session);
        return session.Client.SendPacket(
            MerchantPacketWriter.Result(MerchantSubOpcode.TradeCancel, MerchantPacketWriter.Succeeded));
    }

    public async Task RequestStallAsync(UserSession session, Packet packet, MerchantSubOpcode sub)
    {
        var merchantId = packet.ReadInt();
        var merchant = sessionManager.GetByCharacterId(merchantId);
        var generation = merchant?.Trade.StallGeneration ?? 0;
        bool buying = sub is MerchantSubOpcode.BuyingStallRequest or MerchantSubOpcode.BuyingStallOpen;

        var refusal =
            merchant == null ? "no such merchant"
            : MerchantPacketConstants.IsOwnStall(merchant, session) ? "that is your own stall"
            : !merchant.Trade.IsMerchanting ? "the stall is closed"
            : merchant.Trade.IsBuyingMerchant != buying ? "wrong stall kind for that request"
            : session.Trade.IsMerchanting ? "you are running a stall"
            : session.Hp <= 0 ? "you are dead"
            : !ExchangePacketConstants.IsWithinTradeRange(session, merchant) ? "out of range"
            : IsStallBusy(merchant, session) ? "someone else is looking at it"
            : null;

        bool windowStep = sub is MerchantSubOpcode.SellingStallRequest or MerchantSubOpcode.BuyingStallRequest;

        if (refusal != null)
        {
            logger.LogDebug("Stall open refused for {Name} on {MerchantId}: {Reason}",
                session.Name, merchantId, refusal);
            await session.Client.SendPacket(windowStep
                ? MerchantPacketWriter.StallWindowOpen(
                    sub, MerchantPacketWriter.StallOpenRefused, merchantId, string.Empty)
                : MerchantPacketWriter.StallContentsAllowed(sub, allowed: false));
            return;
        }

        ReleaseViewedStall(session);
        merchant!.Trade.MerchantViewerId = session.CharacterId;
        session.Trade.LookAt(merchant.CharacterId, generation);

        await session.Client.SendPacket(windowStep
            ? MerchantPacketWriter.StallWindowOpen(
                sub, MerchantPacketWriter.Succeeded, merchantId, ShopNameOf(merchant))
            : MerchantPacketWriter.StallContentsAllowed(sub, allowed: true));
    }

    private static string ShopNameOf(UserSession merchant) =>
        merchant.Trade.MerchantAdvert.Length > 0 ? merchant.Trade.MerchantAdvert : merchant.Name;

    private bool IsStallBusy(UserSession merchant, UserSession viewer)
    {
        var current = merchant.Trade.MerchantViewerId;
        if (current < 0 || current == viewer.CharacterId) return false;

        var holder = sessionManager.GetByCharacterId(current);
        if (holder != null && holder.Trade.MerchantTargetUserId == merchant.CharacterId) return true;

        merchant.Trade.MerchantViewerId = -1;
        return false;
    }

    public void ReleaseViewedStall(UserSession session)
    {
        var viewed = sessionManager.GetByCharacterId(session.Trade.MerchantTargetUserId);
        if (viewed != null && viewed.Trade.MerchantViewerId == session.CharacterId)
            viewed.Trade.MerchantViewerId = -1;
        session.Trade.MerchantTargetUserId = -1;
    }

    public async Task SendStallListAsync(UserSession session, int merchantId)
    {
        var merchant = sessionManager.GetByCharacterId(merchantId);
        if (merchant == null || !merchant.Trade.IsMerchanting)
            return;

        await session.Client.SendPacket(
            MerchantPacketWriter.StallList(StallOwnerOf(merchant), DisplayItemIds(merchant)));
    }

    public MerchantPacketWriter.StallOwner StallOwnerOf(UserSession session) =>
        new(session.CharacterId,
            session.Trade.IsBuyingMerchant,
            session.Trade.IsBuyingMerchant ? (byte)0 : StallFlags(session));

    public static byte StallFlags(UserSession session) =>
        session.Trade.PremiumMerchant ? MerchantPacketConstants.PremiumStallFlag : (byte)0;

    public static List<int> DisplayItemIds(UserSession session)
    {
        if (!session.Trade.IsBuyingMerchant)
            return session.Trade.MerchantItems
                .Take(ShowcaseSlots(session))
                .Select(item => item == null || item.IsEmpty ? 0 : item.ItemId)
                .ToList();

        return session.Trade.BuyMerchantItems
            .Where(item => item != null && !item.IsEmpty)
            .Select(item => item.ItemId)
            .ToList();
    }

    public static int ShowcaseSlots(UserSession session) =>
        session.Trade.PremiumMerchant
            ? MerchantPacketConstants.StallDisplaySlotsPremium
            : MerchantPacketConstants.StallDisplaySlots;

    private static bool HasMerchantItems(UserSession session)
    {
        return session.Trade.MerchantItems.Any(entry => entry != null && !entry.IsEmpty);
    }
}
