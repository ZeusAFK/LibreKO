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
    ILogger<ShoppingMallLetterMutationService> logger) : IShoppingMallLetterMutationService
{
    private const byte Malformed = 0;
    private const byte Succeeded = 1;
    private const byte Rejected = unchecked((byte)-1);
    private const byte GetItemNoLetter = unchecked((byte)-2);
    private const byte DeleteTooMany = unchecked((byte)-3);
    private const byte SendToSelf = unchecked((byte)-6);
    private const byte SendItemNotMailable = unchecked((byte)-32);

    public async Task HandleSendAsync(UserSession session, Packet packet)
    {

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
        if (letterType is 0 or > 2)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        var itemId = 0;
        short itemDurability = 0;
        short itemCount = 0;
        var cost = ShoppingMallLetterProtocol.LetterSendCost;
        ItemSlot? itemSlot = null;
        byte sourcePosition = 0;
        var coins = 0;

        var requestedCount = 0;

        if (letterType == 2)
        {
            if (packet.RemainingBytes < 9)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Malformed));
                return;
            }

            itemId = packet.ReadInt();
            sourcePosition = packet.ReadByte();
            requestedCount = packet.ReadInt(); // Can specify partial stack count
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
            || subject.Length > ShoppingMallLetterProtocol.MaxLetterSubject
            || message.Length > ShoppingMallLetterProtocol.MaxLetterMessage)
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

        var recipientExists = await db.Characters.AnyAsync(character => character.Name == recipientName);
        if (!recipientExists)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
            return;
        }

        if (letterType == 2)
        {
            var sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
            if (sourcePosition >= InventoryConstants.HaveMax || sourceIndex >= session.Inventory.Length)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
                return;
            }

            itemSlot = session.Inventory[sourceIndex];
            if (itemSlot.ItemId != itemId || itemSlot.IsEmpty)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Rejected));
                return;
            }

            var itemData = gameDataService.GetItem(itemId);
            if (itemData == null
                || itemData.Race == 7
                || itemId >= InventoryConstants.ItemGold
                || !itemSlot.IsTradable)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, SendItemNotMailable));
                return;
            }

            itemDurability = itemSlot.Durability;
            if (requestedCount > 0 && requestedCount < itemSlot.Count)
            {
                itemCount = (short)requestedCount;
                itemSlot.Count -= (ushort)requestedCount;
            }
            else
            {
                itemCount = (short)itemSlot.Count;
            }
        }

        session.Money -= cost;
        await userNotificationService.SendGoldLossAsync(session, cost);

        if (itemSlot != null)
        {
            if (requestedCount > 0 && itemSlot.Count > 0)
            {
                await userNotificationService.SendStackChangeAsync(session, sourcePosition, itemSlot.ItemId, itemSlot.Count, itemSlot.Durability);
            }
            else
            {
                itemSlot.Clear();
                await userNotificationService.SendStackChangeAsync(session, sourcePosition, 0, 0, 0);
            }
        }

        db.MailBoxes.Add(new MailBox
        {
            SendDate = DateTime.UtcNow,
            Status = 1,
            SenderId = session.Name,
            RecipientId = recipientName,
            Subject = subject,
            Message = message,
            Type = letterType,
            ItemId = itemId,
            Count = itemCount,
            Durability = itemDurability,
            SerialNumber = 0,
            Coins = coins,
            Deleted = false
        });
        await db.SaveChangesAsync();

        logger.LogInformation("{Name} sent mail to {Recipient} (type={LetterType})", session.Name, recipientName, letterType);

        await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
            ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterSend, Succeeded));

        var recipient = sessionManager.GetByName(recipientName);
        if (recipient != null)
            await userNotificationService.SendUnreadNotificationAsync(recipient);
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

        var letters = await db.MailBoxes
            .Where(mail => letterIds.Contains(mail.LetterId) && mail.RecipientId == session.Name && !mail.Deleted)
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

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var letter = await db.MailBoxes
            .OrderBy(mail => mail.LetterId)
            .FirstOrDefaultAsync(mail => mail.LetterId == letterId
                && mail.RecipientId == session.Name
                && mail.Status == 1
                && mail.Type == 2
                && !mail.Deleted);

        if (letter == null)
        {
            await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, GetItemNoLetter));
            return;
        }

        if (letter.ItemId > 0)
        {
            var slot = session.FindSlotForItem(letter.ItemId, gameDataService, (ushort)letter.Count);
            if (slot < 0)
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, Rejected));
                return;
            }

            var itemData = gameDataService.GetItem(letter.ItemId);
            if (itemData == null || !CanReceiveItem(session, itemData, letter.Count))
            {
                await session.Client.SendPacket(ShoppingMallPacketWriter.Result(
                    ShoppingMallLetterProtocol.StoreLetter, ShoppingMallLetterProtocol.LetterGetItem, Rejected));
                return;
            }

            var slotEntry = session.Inventory[slot];
            var isNewItem = slotEntry.IsEmpty;
            slotEntry.ItemId = letter.ItemId;
            slotEntry.Count += (ushort)letter.Count;
            slotEntry.Durability = isNewItem
                ? letter.Durability
                : (short)(slotEntry.Durability + letter.Durability);

            await userNotificationService.SendStackChangeAsync(
                session,
                (byte)slot,
                letter.ItemId,
                slotEntry.Count,
                slotEntry.Durability,
                isNewItem);
        }

        if (letter.Coins > 0)
        {
            session.Money += letter.Coins;
            await userNotificationService.SendGoldGainAsync(session, letter.Coins);
        }

        letter.Status = 2;
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
