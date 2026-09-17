using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IMerchantPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task HandleSessionEndedAsync(UserSession session);
}

public class MerchantPacketCoordinator(
    SessionManager sessionManager,
    IMerchantLifecycleService merchantLifecycleService,
    IMerchantListingService merchantListingService,
    IMerchantBuyingService merchantBuyingService,
    ILogger<MerchantPacketCoordinator> logger) : IMerchantPacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var sub = (MerchantSubOpcode)packet.ReadByte();
        logger.LogDebug("Merchant {Sub} from {Name}: {Payload}",
            sub, session.Name, Convert.ToHexString(packet.GetData()));

        switch (sub)
        {
            case MerchantSubOpcode.Open:
                await merchantLifecycleService.OpenAsync(session);
                break;

            case MerchantSubOpcode.Close:
                await merchantLifecycleService.CloseAsync(session, session.Trade.IsMerchanting ? MerchantInOut.StallClosed : null);
                break;

            case MerchantSubOpcode.ItemAdd:
                await merchantListingService.AddItemAsync(session, packet);
                break;

            case MerchantSubOpcode.ItemCancel:
                await merchantListingService.CancelItemAsync(session, packet);
                break;

            case MerchantSubOpcode.ItemList:
                await merchantListingService.ListItemsAsync(session, packet);
                break;

            case MerchantSubOpcode.ItemBuy:
                await merchantListingService.BuyItemAsync(session, packet);
                break;

            case MerchantSubOpcode.Insert:
                await merchantLifecycleService.InsertAsync(session, packet);
                break;

            case MerchantSubOpcode.TradeCancel:
                await merchantLifecycleService.CancelAsync(session);
                break;

            case MerchantSubOpcode.BuyOpen:
                await merchantBuyingService.OpenAsync(session);
                break;

            case MerchantSubOpcode.BuyInsert:
                await merchantBuyingService.InsertAsync(session, packet);
                break;

            case MerchantSubOpcode.BuyList:
                await merchantBuyingService.ListAsync(session, packet);
                break;

            case MerchantSubOpcode.BuyBuy:
                await merchantBuyingService.BuyAsync(session, packet);
                break;

            case MerchantSubOpcode.BuyClose:
                await merchantBuyingService.CloseAsync(session, broadcast: true);
                break;

            case MerchantSubOpcode.SellingStallRequest:
            case MerchantSubOpcode.SellingStallOpen:
            case MerchantSubOpcode.BuyingStallRequest:
            case MerchantSubOpcode.BuyingStallOpen:
                await merchantLifecycleService.RequestStallAsync(session, packet, sub);
                break;

            case MerchantSubOpcode.StallList:
                await merchantLifecycleService.SendStallListAsync(session, packet.ReadInt());
                break;
        }
    }

    public async Task HandleSessionEndedAsync(UserSession session)
    {
        merchantLifecycleService.ReleaseViewedStall(session);
        await merchantBuyingService.CloseAsync(session, broadcast: true);
        await merchantLifecycleService.CloseAsync(session, session.Trade.IsMerchanting ? MerchantInOut.SessionEnded : null);
    }
}
