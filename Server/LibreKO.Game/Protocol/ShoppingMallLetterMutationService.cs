using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IShoppingMallLetterMutationService
{
    Task HandleSendAsync(UserSession session, Packet packet);
    Task HandleDeleteAsync(UserSession session, Packet packet);
    Task HandleGetItemAsync(UserSession session, Packet packet);
}

public class ShoppingMallLetterMutationService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ICharacterStatePersister statePersister,
    ILogger<ShoppingMallLetterMutationService> logger) : IShoppingMallLetterMutationService
{
    private const byte Malformed = 0;
    private const byte Succeeded = 1;
    private const byte Rejected = unchecked((byte)-1);
    private const byte GetItemNoLetter = unchecked((byte)-2);
    private const byte DeleteTooMany = unchecked((byte)-3);
    private const byte SendToSelf = unchecked((byte)-6);
    private const byte SendItemNotMailable = unchecked((byte)-32);
    private const byte LetterUnclaimed = 1;
    private const byte LetterClaimed = 2;

    public async Task HandleSendAsync(UserSession session, Packet packet)
    {
        if (session.IsBusy)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        if (packet.RemainingBytes < 3)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Malformed));
            return;
        }

        var recipientName = packet.ReadSByteString();
        var subject = packet.ReadSByteString();
        if (packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Malformed));
            return;
        }

        var letterType = packet.ReadByte();
        if (letterType is 0 or > ShoppingMallPacketWriter.LetterTypeWithItem)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        var itemId = 0;
        var cost = ShoppingMallLetterProtocol.LetterSendCost;
        byte sourcePosition = 0;
        var coins = 0;

        if (letterType == ShoppingMallPacketWriter.LetterTypeWithItem)
        {
            if (packet.RemainingBytes < 9)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Malformed));
                return;
            }

            itemId = packet.ReadInt();
            sourcePosition = packet.ReadByte();
            _ = packet.ReadInt(); // Coins are part of the wire format but disabled for this protocol branch.
            cost = ShoppingMallLetterProtocol.LetterSendItemCost;
        }

        if (packet.RemainingBytes < 1)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Malformed));
            return;
        }

        var message = packet.ReadString();
        if (string.IsNullOrEmpty(recipientName)
            || string.IsNullOrEmpty(subject)
            || string.IsNullOrEmpty(message)
            || subject.EnumerateRunes().Count() > ShoppingMallLetterProtocol.MaxLetterSubject
            || message.EnumerateRunes().Count() > ShoppingMallLetterProtocol.MaxLetterMessage)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        if (string.Equals(recipientName, session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, SendToSelf));
            return;
        }

        if (session.Money < cost)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var recipient = await db.Characters
            .Where(character => character.Name == recipientName && character.DeletionTime == null)
            .Select(character => new { character.Sealed })
            .FirstOrDefaultAsync();
        // A sealed character cannot log in, so a letter to it could hold the very ring that unseals it.
        if (recipient == null || recipient.Sealed)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        var sourceIndex = -1;
        ItemData? itemData = null;
        if (letterType == ShoppingMallPacketWriter.LetterTypeWithItem)
        {
            sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
            if (sourcePosition >= InventoryConstants.HaveMax || sourceIndex >= session.Inventory.Length)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
                return;
            }

            itemData = gameDataService.GetItem(itemId);
            if (itemData == null
                || itemData.Race == 7
                || itemData.IsUntradeable
                || itemId >= InventoryConstants.ItemGold)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, SendItemNotMailable));
                return;
            }
        }

        (bool Taken, ExchangeItem? Item, byte Refusal) taken = (false, null, Rejected);
        var outcome = await statePersister.CommitAsync(session, unitOfWork =>
        {
            taken = session.WithLock(s => TakeLetterCost(s, sourceIndex, itemId, itemData, cost));
            if (!taken.Taken)
                return Task.FromResult(false);

            var enclosed = taken.Item;
            unitOfWork.MailBoxes.Add(new MailBox
            {
                SendDate = DateTime.UtcNow,
                Status = LetterUnclaimed,
                SenderId = session.Name,
                RecipientId = recipientName,
                Subject = subject,
                Message = message,
                Type = letterType,
                ItemId = itemId,
                Count = (short)(enclosed?.Count ?? 0),
                Durability = enclosed?.Durability ?? 0,
                Flag = enclosed?.Flag ?? 0,
                ExpiresAt = enclosed?.ExpiresAt ?? 0,
                SerialNumber = enclosed?.UniqueId ?? 0,
                Coins = coins,
                Deleted = false
            });
            return Task.FromResult(true);
        }, () => session.WithLock(s => GiveBackLetterCost(s, cost, taken.Item)));
        if (outcome != StateCommit.Saved)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend,
                outcome == StateCommit.Unchanged ? taken.Refusal : Rejected));
            return;
        }

        await userNotificationService.SendGoldLossAsync(session, cost);
        if (taken.Item != null)
            await userNotificationService.SendStackChangeAsync(session, (byte)sourceIndex, 0, 0, 0);

        logger.LogInformation("{Name} sent mail to {Recipient} (type={LetterType})", session.Name, recipientName, letterType);

        await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Succeeded));

        var online = sessionManager.GetByName(recipientName);
        if (online != null)
            await userNotificationService.SendUnreadNotificationAsync(online);
    }

    // Checked and taken in one locked step, so nothing can change or expire the item in between.
    private static (bool Taken, ExchangeItem? Item, byte Refusal) TakeLetterCost(
        UserSession session, int sourceIndex, int itemId, ItemData? itemData, int cost)
    {
        if (session.IsBusy || session.Money < cost)
            return (false, null, Rejected);

        ExchangeItem? item = null;
        if (sourceIndex >= 0)
        {
            var slot = session.Inventory[sourceIndex];
            if (slot.ItemId != itemId || slot.IsEmpty || slot.Count == 0)
                return (false, null, Rejected);
            if (!slot.IsTradable)
                return (false, null, SendItemNotMailable);

            item = new ExchangeItem
            {
                ItemId = slot.ItemId, Count = slot.Count, Durability = slot.Durability, Flag = slot.Flag,
                ExpiresAt = slot.ExpiresAt, UniqueId = slot.UniqueId, Countable = itemData!.Countable != 0, SrcPos = (byte)sourceIndex,
            };
            slot.Clear();
        }

        session.Money -= cost;
        return (true, item, Succeeded);
    }

    // The bag or purse may have filled while the save ran, so what no longer fits goes to the warehouse.
    private void GiveBackLetterCost(UserSession session, int cost, ExchangeItem? item)
    {
        if (session.ReturnHeldGold(cost).Lost > 0)
            logger.LogError("{Name}'s unsent letter fee found no room to come back", session.Name);
        if (item != null && !session.ReturnEscrowedItem(item))
            logger.LogError("{Name}'s unsent letter item {ItemId} x{Count} found no room to come back", session.Name, item.ItemId, item.Count);
    }

    public async Task HandleDeleteAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var count = packet.ReadByte();
        if (count > ShoppingMallLetterProtocol.MaxDeleteCount)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterDelete, DeleteTooMany));
            return;
        }

        if (packet.RemainingBytes < count * 4)
            return;

        var letterIds = new int[count];
        for (var index = 0; index < count; index++)
            letterIds[index] = packet.ReadInt();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A letter whose item or coins were never taken would take them with it.
        var letters = await db.MailBoxes
            .Where(mail => letterIds.Contains(mail.LetterId) && mail.RecipientId == session.Name && !mail.Deleted
                && !(mail.Type == ShoppingMallPacketWriter.LetterTypeWithItem && mail.Status == LetterUnclaimed))
            .ToListAsync();

        foreach (var letter in letters)
            letter.Deleted = true;

        await db.SaveChangesAsync();

        await session.Client.SendPacket(ShoppingMallPacketWriter.DeletedLetters(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterDelete,
            letters.Select(letter => letter.LetterId).ToList()));
    }

    public async Task HandleGetItemAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 4)
            return;

        var letterId = packet.ReadInt();
        if (session.IsBusy)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, Rejected));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var letter = await db.MailBoxes
            .OrderBy(mail => mail.LetterId)
            .FirstOrDefaultAsync(mail => mail.LetterId == letterId
                && mail.RecipientId == session.Name
                && mail.Status == LetterUnclaimed
                && mail.Type == ShoppingMallPacketWriter.LetterTypeWithItem
                && !mail.Deleted);

        if (letter == null)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, GetItemNoLetter));
            return;
        }

        var slot = -1;
        ExchangeItem? enclosed = null;
        if (letter.ItemId > 0)
        {
            var itemData = gameDataService.GetItem(letter.ItemId);
            if (itemData != null)
            {
                enclosed = new ExchangeItem
                {
                    ItemId = letter.ItemId, Count = letter.Count, Durability = letter.Durability, Flag = letter.Flag,
                    ExpiresAt = letter.ExpiresAt, UniqueId = (int)letter.SerialNumber, Countable = itemData.Countable != 0,
                };
                slot = ItemPlacement.FindForDelivery(
                    session.Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, enclosed);
            }

            if (itemData == null || slot < 0 || !CanReceiveItem(session, itemData, letter.Count))
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, Rejected));
                return;
            }
        }

        // All the coins fit under the cap or none are handed out.
        var coins = letter.Coins;
        if (coins > 0 && !session.WithLock(s =>
            {
                if ((long)s.Money + coins > ExchangePacketConstants.CoinMax)
                    return false;
                s.Money += coins;
                return true;
            }))
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, Rejected));
            return;
        }

        if (enclosed != null)
        {
            var slotEntry = session.Inventory[slot];
            var isNewItem = slotEntry.IsEmpty;
            var durability = slotEntry.Durability;
            ItemPlacement.Put(slotEntry, enclosed);
            if (!isNewItem)
                slotEntry.Durability = (short)(durability + letter.Durability);

            await userNotificationService.SendStackChangeAsync(
                session,
                (byte)slot,
                letter.ItemId,
                slotEntry.Count,
                slotEntry.Durability,
                isNewItem);
        }

        if (coins > 0)
            await userNotificationService.SendGoldGainAsync(session, coins);

        letter.Status = LetterClaimed;
        await db.SaveChangesAsync();

        await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, Succeeded));
    }

    private static bool CanReceiveItem(UserSession session, ItemData itemData, short count)
    {
        if (count <= 0)
            return false;

        var totalWeight = itemData.Weight * count;
        return session.Stats.ItemWeight + totalWeight <= session.Stats.MaxWeight;
    }
}
