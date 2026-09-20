using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallStoreService
{
    Task HandleOpenAsync(UserSession session);
    Task HandleCloseAsync(UserSession session);
    Task HandleBuyAsync(UserSession session, Packet packet);
}

public class ShoppingMallStoreService(
    ICharacterStatePersister characterStatePersister,
    IGameDataService gameData,
    IServiceScopeFactory scopeFactory) : IShoppingMallStoreService
{
    private const byte StoreOpen = 1;
    private const byte StoreBuy = 8;
    private const byte StoreBuyItem = 1;

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

    public async Task HandleBuyAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, StoreBuyItem, 0));
            return;
        }

        var sub = packet.ReadByte();
        if (sub != StoreBuyItem)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
            return;
        }

        if (packet.RemainingBytes < 1 + 4 + 1 + 1)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
            return;
        }

        var buyKind = packet.ReadByte();
        var itemId = packet.ReadInt();
        var count = packet.ReadByte();
        var priceType = packet.ReadByte();
        if (buyKind != StoreBuyItem || itemId <= 0 || count <= 0)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
            return;
        }

        var itemData = gameData.GetItem(itemId);
        if (itemData == null)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
            return;
        }

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pusItem = await db.PusItems.AsNoTracking().FirstOrDefaultAsync(x => x.ItemId == itemId);
            if (pusItem == null || pusItem.Price is null)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
                return;
            }

            var totalCost = pusItem.Price.Value * count;
            if ((byte)priceType != pusItem.PriceType || totalCost <= 0 || session.KnightCash < totalCost)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
                return;
            }

            var slot = session.FindSlotForItem(itemId, gameData, (ushort)Math.Min(count, ushort.MaxValue));
            if (slot < 0)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 0));
                return;
            }

            session.KnightCash -= totalCost;

            var inventorySlot = session.Inventory[slot];
            var isNewItem = inventorySlot.IsEmpty;
            if (isNewItem)
            {
                inventorySlot.ItemId = itemId;
                inventorySlot.Durability = itemData.Duration;
                inventorySlot.Count = 0;
                inventorySlot.Flag = 0;
                inventorySlot.ExpiresAt = 0;
            }

            inventorySlot.Count = (ushort)Math.Min(9999, inventorySlot.Count + count);
            inventorySlot.Durability = itemData.Duration;

            await characterStatePersister.SaveAsync(session);
            await session.Client.SendPacket(new ItemCountChangePacketWriter()
                .Add((byte)slot, itemId, inventorySlot.Count, inventorySlot.Durability, isNewItem)
                .Build());
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(StoreBuy, sub, 1));
        }
    }

    public async Task HandleCloseAsync(UserSession session)
    {
        await characterStatePersister.SaveAsync(session);
    }
}
