using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallStoreService
{
    Task HandleOpenAsync(UserSession session);
    Task HandleCloseAsync(UserSession session);
    Task HandlePurchaseAsync(UserSession session, Packet packet);
    Task SendBalanceAsync(UserSession session);
}

public class ShoppingMallStoreService(
    ICharacterStatePersister characterStatePersister,
    IGameDataService gameData,
    IMailService mailService,
    IServiceScopeFactory scopeFactory,
    ILogger<ShoppingMallStoreService> logger) : IShoppingMallStoreService
{
    public const byte StorePurchase = 8;
    public const byte PurchaseCart = 1;
    public const byte PurchaseCheckRecipient = 2;
    public const int LineCountMax = 999;

    private const byte StoreOpen = 1;
    private const byte StoreCatalog = 3;
    private const byte StoreCategories = 4;
    private const byte StoreBalance = 5;
    private const int CartLineSize = sizeof(int) + sizeof(ushort) + sizeof(int);
    private const string PurchaseSubject = "Power-Up Store purchase";
    private const string PurchaseBody = "Your Power-Up Store items are attached to this mail.";

    private readonly record struct CartLine(PusItemData Entry, int Count, int UnitPrice);

    private readonly record struct Recipient(int CharacterId, string Name, byte Level, short Class, short KnightsId);

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
            .Select(x => new ShoppingMallPacketWriter.Category(x.Id, x.Name))
            .ToListAsync();
        var items = await db.PusItems.AsNoTracking()
            .Where(x => x.Price > 0)
            .OrderBy(x => x.Id)
            .ToListAsync();
        var discounts = await db.PusDiscounts.AsNoTracking().ToListAsync();
        var now = DateTime.UtcNow;
        var catalog = items
            .Where(x => gameData.GetItem(x.ItemId) != null)
            .Select(x =>
            {
                var price = PowerUpStoreCatalog.PriceOf(x, discounts, now);
                return new ShoppingMallPacketWriter.CatalogEntry(
                    x.Id, x.ItemId, x.Category, x.Price, x.Featured,
                    price.Discounted ? price.Price : 0, price.Discounted ? price.DiscountEndsAt : null);
            })
            .ToList();

        await session.Client.SendPacket(ShoppingMallPacketWriter.Catalog(StoreOpen, StoreCatalog, catalog));
        await session.Client.SendPacket(ShoppingMallPacketWriter.Categories(StoreOpen, StoreCategories, categories));
        await SendBalanceAsync(session);
    }

    public Task SendBalanceAsync(UserSession session) =>
        session.Client.SendPacket(BalancePacket(session.KnightCash));

    public static Packet BalancePacket(int knightCash) =>
        ShoppingMallPacketWriter.Balance(StoreOpen, StoreBalance, knightCash);

    public async Task HandlePurchaseAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        switch (packet.ReadByte())
        {
            case PurchaseCart:
                var result = await BuyCartAsync(session, packet);
                await session.Client.SendPacket(ShoppingMallPacketWriter.PurchaseResult(
                    StorePurchase, PurchaseCart, (byte)result, session.KnightCash));
                break;
            case PurchaseCheckRecipient:
                await CheckRecipientAsync(session, packet);
                break;
        }
    }

    private async Task<PowerUpStoreResult> BuyCartAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return PowerUpStoreResult.Failed;

        var recipientName = packet.ReadSByteString().Trim();
        if (packet.RemainingBytes < 1)
            return PowerUpStoreResult.Failed;

        int lineCount = packet.ReadByte();
        if (lineCount == 0 || packet.RemainingBytes < lineCount * CartLineSize)
            return PowerUpStoreResult.Failed;

        var requested = new List<(int CatalogId, int Count, int UnitPrice)>(lineCount);
        for (var i = 0; i < lineCount; i++)
            requested.Add((packet.ReadInt(), packet.ReadUShort(), packet.ReadInt()));

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var recipientId = session.CharacterId;
        var gift = recipientName.Length > 0;
        if (gift)
        {
            var (lookup, recipient) = await FindRecipientAsync(db, session, recipientName);
            if (lookup != PowerUpStoreResult.Succeeded)
                return lookup;
            recipientId = recipient.CharacterId;
        }

        var catalogIds = requested.Select(line => line.CatalogId).Distinct().ToList();
        var entries = await db.PusItems.AsNoTracking()
            .Where(x => catalogIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);
        var discounts = await db.PusDiscounts.AsNoTracking()
            .Where(x => catalogIds.Contains(x.PusItemId))
            .ToListAsync();
        var now = DateTime.UtcNow;

        var lines = new List<CartLine>(lineCount);
        long total = 0;
        foreach (var (catalogId, count, seenPrice) in requested)
        {
            if (count is < 1 or > LineCountMax
                || !entries.TryGetValue(catalogId, out var entry)
                || entry.Price <= 0
                || gameData.GetItem(entry.ItemId) == null)
                return PowerUpStoreResult.Unavailable;

            var unitPrice = PowerUpStoreCatalog.PriceOf(entry, discounts, now).Price;
            if (unitPrice != seenPrice)
                return PowerUpStoreResult.PriceChanged;

            lines.Add(new CartLine(entry, count, unitPrice));
            total += (long)unitPrice * count;
        }

        if (total > session.KnightCash)
            return PowerUpStoreResult.NotEnoughCash;

        var attachments = lines
            .Select(line => new MailAttachmentDraft(
                MailAttachmentKind.Item, line.Entry.ItemId, line.Count, gameData.GetItem(line.Entry.ItemId)!.Duration))
            .ToList();

        session.KnightCash -= (int)total;
        try
        {
            if (gift)
                await mailService.SendSystemMailAsync(recipientId, $"A gift from {session.Name}",
                    $"{session.Name} sent you a gift from the Power-Up Store. It is attached to this mail.", attachments, MailKind.Store);
            else
                await mailService.SendSystemMailAsync(recipientId, PurchaseSubject, PurchaseBody, attachments, MailKind.Store);
        }
        catch (Exception ex)
        {
            session.KnightCash += (int)total;
            logger.LogError(ex, "Power-Up Store mail for {Buyer} failed; {Total} cash refunded", session.Name, total);
            return PowerUpStoreResult.Failed;
        }

        await characterStatePersister.SaveAsync(session);
        return PowerUpStoreResult.Succeeded;
    }

    private async Task CheckRecipientAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var name = packet.ReadSByteString().Trim();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (result, recipient) = await FindRecipientAsync(db, session, name);
        await session.Client.SendPacket(result == PowerUpStoreResult.Succeeded
            ? ShoppingMallPacketWriter.Recipient(StorePurchase, PurchaseCheckRecipient, recipient.Name, recipient.Level, recipient.Class)
            : ShoppingMallPacketWriter.Result(StorePurchase, PurchaseCheckRecipient, (byte)result));
    }

    private static async Task<(PowerUpStoreResult Result, Recipient Recipient)> FindRecipientAsync(
        AppDbContext db, UserSession session, string name)
    {
        if (name.Length == 0)
            return (PowerUpStoreResult.RecipientNotFound, default);

        var found = await db.Characters.AsNoTracking()
            .Where(c => c.Name == name)
            .Select(c => new Recipient(c.Id, c.Name, c.Level, c.Class, c.KnightsId))
            .FirstOrDefaultAsync();
        if (found.Name == null)
            return (PowerUpStoreResult.RecipientNotFound, default);
        if (found.CharacterId == session.CharacterId)
            return (PowerUpStoreResult.RecipientIsSelf, default);
        var clanmate = session.KnightsId > 0 && found.KnightsId == session.KnightsId;
        if (!clanmate && !await db.Friendships.AnyAsync(f => f.CharacterId == session.CharacterId && f.FriendCharacterId == found.CharacterId))
            return (PowerUpStoreResult.RecipientNotAllowed, default);
        return (PowerUpStoreResult.Succeeded, found);
    }

    public async Task HandleCloseAsync(UserSession session)
    {
        await characterStatePersister.SaveAsync(session);
    }
}
