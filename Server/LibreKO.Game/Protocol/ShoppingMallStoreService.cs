using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallStoreService
{
    Task HandleOpenAsync(UserSession session);
    Task HandleCloseAsync(UserSession session);
}

public class ShoppingMallStoreService(ICharacterStatePersister characterStatePersister) : IShoppingMallStoreService
{
    private const byte StoreOpen = 1;

    public async Task HandleOpenAsync(UserSession session)
    {
        short errorCode = 1;
        short freeSlot = -1;

        if (session.Hp <= 0)
        {
            errorCode = -2;
        }
        else if (session.Trade.IsTrading)
        {
            errorCode = -3;
        }
        else if (session.Trade.IsMerchanting)
        {
            errorCode = -4;
        }
        else if (session.ZoneId is >= 40 and <= 45)
        {
            errorCode = -5;
        }
        else
        {
            for (var i = InventoryConstants.SlotMax; i < InventoryConstants.InventoryTotal; i++)
            {
                if (session.Inventory[i].IsEmpty)
                {
                    freeSlot = (short)i;
                    break;
                }
            }

            if (freeSlot < 0)
                errorCode = -8;
        }

        await session.Client.SendPacket(
            ShoppingMallPacketWriter.StoreOpened(StoreOpen, errorCode, freeSlot));
    }

    public async Task HandleCloseAsync(UserSession session)
    {
        await characterStatePersister.SaveAsync(session);
    }
}
