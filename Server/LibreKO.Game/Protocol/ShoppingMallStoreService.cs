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
    private const byte StoreCatalog = 3;
    private const byte StoreCategories = 4;
    private const byte StoreBalance = 5;
    private const byte KnightCashPriceType = 0;
    private const byte UsdPriceType = 1;
    private const int BuyRequestSize = 7;

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

        if (errorCode != 1)
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var categories = await db.PusCategories.AsNoTracking()
            .Where(x => x.Status != 0)
            .OrderBy(x => x.Id)
            .Select(x => new ShoppingMallPacketWriter.Category(x.CategoryId, x.CategoryName, x.Description))
            .ToListAsync();
        var catalog = await db.PusItems.AsNoTracking()
            .Where(x => x.Price != null && x.Price > 0)
            .OrderBy(x => x.Id)
            .Select(x => new ShoppingMallPacketWriter.CatalogEntry(
                x.Id, x.ItemId, x.ItemName ?? x.ItemTitle ?? string.Empty, x.ItemDesc,
                x.Category, x.Price!.Value, x.PriceType))
            .ToListAsync();

        await session.Client.SendPacket(ShoppingMallPacketWriter.Catalog(StoreOpen, StoreCatalog, catalog));
        await session.Client.SendPacket(ShoppingMallPacketWriter.Categories(StoreOpen, StoreCategories, categories));
        await session.Client.SendPacket(ShoppingMallPacketWriter.Balance(
            StoreOpen, StoreBalance, session.KnightCash, session.UsdBalance));
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

        if (packet.RemainingBytes < BuyRequestSize)
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
            if (pusItem.PriceType != priceType
                || priceType is not (KnightCashPriceType or UsdPriceType)
                || totalCost <= 0
                || (priceType == KnightCashPriceType
                    ? session.KnightCash < totalCost
                    : session.UsdBalance < totalCost))
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

            if (priceType == KnightCashPriceType)
                session.KnightCash -= totalCost;
            else
                session.UsdBalance -= totalCost;

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

            inventorySlot.Count = (ushort)Math.Min(InventoryConstants.MaxStackCount, inventorySlot.Count + count);

            await characterStatePersister.SaveAsync(session);
            await session.Client.SendPacket(new ItemCountChangePacketWriter()
                .Add((byte)slot, itemId, inventorySlot.Count, inventorySlot.Durability, isNewItem)
                .Build());
            await session.Client.SendPacket(ShoppingMallPacketWriter.PurchaseResult(
                StoreBuy, sub, ShoppingMallPacketWriter.Succeeded, session.KnightCash, session.UsdBalance));
        }
    }

    public async Task HandleCloseAsync(UserSession session)
    {
        await characterStatePersister.SaveAsync(session);
    }
}
