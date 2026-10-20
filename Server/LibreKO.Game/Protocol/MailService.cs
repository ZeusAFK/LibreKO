using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public readonly record struct MailAttachmentDraft(
    MailAttachmentKind Kind, int ItemId, int Count, short Durability = 0, byte Flag = 0, long ExpiresAt = 0, int UniqueId = 0);

public readonly record struct MailItemPick(byte Slot, ushort Count);

public interface IMailService
{
    Task SendSystemMailAsync(int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments, MailKind kind = MailKind.System);
    void StageSystemMail(AppDbContext unitOfWork, int recipientCharacterId, string subject, string body,
        IReadOnlyList<MailAttachmentDraft>? attachments = null);
    Task SendInboxAsync(UserSession session);
    Task SendUnreadAsync(UserSession session);
    Task ReadAsync(UserSession session, int mailId);
    Task SendAsync(UserSession session, string recipientName, string subject, string body, int gold, IReadOnlyList<MailItemPick> items);
    Task DeleteAsync(UserSession session, int mailId);
    Task ClaimAsync(UserSession session, int mailId);
    Task ClaimAttachmentAsync(UserSession session, int mailId, int attachmentIndex);
}

public class MailService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IPlayerProgressionService playerProgressionService,
    ILoyaltyService loyaltyService,
    ICharacterStatePersister statePersister,
    ILogger<MailService> logger) : IMailService
{
    private const string BusySender = "You cannot send mail while trading, keeping a stall, gathering or dead.";
    private const string NotThatMuchGold = "You do not carry that much gold.";
    private const string CannotAttach = "That item cannot be attached.";

    public async Task SendSystemMailAsync(int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments, MailKind kind = MailKind.System)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Mails.Add(NewMail(null, MailLimits.SystemSenderName, recipientCharacterId, subject, body, attachments, kind));
        await db.SaveChangesAsync();

        var recipient = sessionManager.GetByCharacterId(recipientCharacterId);
        if (recipient != null)
            await NotifyNewMailAsync(recipient, MailLimits.SystemSenderName, db);
    }

    public void StageSystemMail(AppDbContext unitOfWork, int recipientCharacterId, string subject, string body,
        IReadOnlyList<MailAttachmentDraft>? attachments = null) =>
        unitOfWork.Mails.Add(NewMail(null, MailLimits.SystemSenderName, recipientCharacterId, subject, body, attachments ?? [], MailKind.System));

    public async Task SendInboxAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mails = await InboxQuery(db, session.CharacterId)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(MailLimits.InboxMax)
            .ToListAsync();
        await session.Client.SendPacket(MailPacketWriter.Inbox(mails));
    }

    public async Task SendUnreadAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await session.Client.SendPacket(MailPacketWriter.Unread(await UnreadCountAsync(db, session.CharacterId)));
    }

    public async Task ReadAsync(UserSession session, int mailId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mail = await InboxQuery(db, session.CharacterId).FirstOrDefaultAsync(m => m.Id == mailId);
        if (mail == null)
        {
            await session.Client.SendPacket(MailPacketWriter.ReadResult(false, mailId, string.Empty));
            return;
        }

        bool firstRead = mail.ReadAt == null;
        if (firstRead)
        {
            mail.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await session.Client.SendPacket(MailPacketWriter.ReadResult(true, mail.Id, mail.Body));
        if (firstRead)
            await session.Client.SendPacket(MailPacketWriter.Unread(await UnreadCountAsync(db, session.CharacterId)));
    }

    public async Task SendAsync(UserSession session, string recipientName, string subject, string body, int gold, IReadOnlyList<MailItemPick> items)
    {
        recipientName = recipientName.Trim();
        subject = Truncate(subject.Trim(), MailLimits.SubjectMax);
        body = Truncate(body, MailLimits.BodyMax);

        if (session.IsBusy)
        {
            await Fail(session, BusySender);
            return;
        }

        if (recipientName.Length == 0 || subject.Length == 0)
        {
            await Fail(session, "A recipient and a subject are required.");
            return;
        }

        if (string.Equals(recipientName, session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await Fail(session, "You cannot mail yourself.");
            return;
        }

        if (gold < 0)
        {
            await Fail(session, NotThatMuchGold);
            return;
        }

        if (items.Count > MailLimits.ItemAttachmentsMax)
        {
            await Fail(session, $"A mail carries at most {MailLimits.ItemAttachmentsMax} items.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipient = await db.Characters
            .Where(c => c.Name == recipientName && c.DeletionTime == null)
            .Select(c => new { c.Id, c.Name, c.Sealed })
            .FirstOrDefaultAsync();
        if (recipient == null)
        {
            await Fail(session, $"No character named '{recipientName}'.");
            return;
        }

        // A sealed character cannot log in to claim it.
        if (recipient.Sealed)
        {
            await Fail(session, $"{recipient.Name} is sealed and cannot receive mail.");
            return;
        }

        var drafts = new List<MailAttachmentDraft>();
        var taken = new List<(int Slot, ExchangeItem Item)>();
        string? refusal = null;
        var outcome = await statePersister.CommitAsync(session, unitOfWork =>
        {
            refusal = session.WithLock(s => TakeAttachments(s, gold, items, drafts, taken));
            if (refusal != null)
                return Task.FromResult(false);

            unitOfWork.Mails.Add(NewMail(session.CharacterId, session.Name, recipient.Id, subject, body, drafts, MailKind.Player));
            return Task.FromResult(true);
        }, () => session.WithLock(s => GiveBackAttachments(s, gold, taken)));
        if (outcome != StateCommit.Saved)
        {
            await Fail(session, refusal ?? "The mail could not be sent.");
            return;
        }

        foreach (var (slot, _) in taken)
        {
            var entry = session.Inventory[slot];
            await userNotificationService.SendStackChangeAsync(session, (byte)slot, entry.ItemId, entry.Count, entry.Durability);
        }

        if (gold > 0)
            await userNotificationService.SendGoldLossAsync(session, gold);

        if (taken.Count > 0)
        {
            session.RecalculateStatsWithBuffs(gameDataService);
            await userNotificationService.SendWeightChangeAsync(session);
        }

        logger.LogInformation("{Sender} mailed {Recipient}: '{Subject}' with {Attachments} attachments", session.Name, recipient.Name, subject, drafts.Count);
        await session.Client.SendPacket(MailPacketWriter.SendResult(true, $"Mail sent to {recipient.Name}."));

        var online = sessionManager.GetByCharacterId(recipient.Id);
        if (online != null)
            await NotifyNewMailAsync(online, session.Name, db);
    }

    // Checked and taken in one locked step.
    private string? TakeAttachments(UserSession session, int gold, IReadOnlyList<MailItemPick> items,
        List<MailAttachmentDraft> drafts, List<(int Slot, ExchangeItem Item)> taken)
    {
        if (session.IsBusy)
            return BusySender;

        if (gold > session.Money)
            return NotThatMuchGold;

        var picks = new List<(int Slot, ushort Count, ItemData Item)>();
        var seenSlots = new HashSet<int>();
        foreach (var pick in items)
        {
            var slot = pick.Slot;
            if (slot < InventoryConstants.InventoryStart || slot >= InventoryConstants.InventoryStart + InventoryConstants.HaveMax || !seenSlots.Add(slot))
                return CannotAttach;

            var entry = session.Inventory[slot];
            var itemData = entry.IsEmpty ? null : gameDataService.GetItem(entry.ItemId);
            if (itemData == null || pick.Count == 0 || pick.Count > entry.Count)
                return CannotAttach;

            if (!entry.IsTradable || !ExchangeTransferService.IsTradableItem(itemData, entry.ItemId, (byte)(slot - InventoryConstants.InventoryStart)))
                return $"{itemData.Name} cannot be traded, so it cannot be mailed.";

            picks.Add((slot, pick.Count, itemData));
        }

        foreach (var (slot, count, itemData) in picks)
        {
            var entry = session.Inventory[slot];
            drafts.Add(new MailAttachmentDraft(MailAttachmentKind.Item, itemData.Num, count,
                count == entry.Count ? entry.Durability : itemData.Duration, entry.Flag, entry.ExpiresAt, entry.UniqueId));
            taken.Add((slot, new ExchangeItem
            {
                ItemId = entry.ItemId, Count = count, Durability = entry.Durability, Flag = entry.Flag,
                ExpiresAt = entry.ExpiresAt, UniqueId = entry.UniqueId, Countable = itemData.Countable != 0, SrcPos = (byte)slot,
            }));
            entry.Count = (ushort)(entry.Count - count);
            if (entry.Count == 0)
                entry.Clear();
        }

        if (gold > 0)
        {
            session.Money -= gold;
            drafts.Add(new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, gold));
        }

        return null;
    }

    private void GiveBackAttachments(UserSession session, int gold, List<(int Slot, ExchangeItem Item)> taken)
    {
        if (session.ReturnHeldGold(gold).Lost > 0)
            logger.LogError("{Name}'s unsent mail gold found no room to come back", session.Name);
        foreach (var (_, item) in taken)
        {
            if (!session.ReturnEscrowedItem(item))
                logger.LogError("{Name}'s unsent mail item {ItemId} x{Count} found no room to come back", session.Name, item.ItemId, item.Count);
        }
    }

    public async Task DeleteAsync(UserSession session, int mailId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mail = await InboxQuery(db, session.CharacterId).FirstOrDefaultAsync(m => m.Id == mailId);
        if (mail == null)
        {
            await session.Client.SendPacket(MailPacketWriter.DeleteResult(false, mailId, "That mail is gone."));
            return;
        }

        if (mail.HasUnclaimedAttachments)
        {
            await session.Client.SendPacket(MailPacketWriter.DeleteResult(false, mailId, "Claim the attachments before deleting this mail."));
            return;
        }

        mail.Deleted = true;
        await db.SaveChangesAsync();
        await session.Client.SendPacket(MailPacketWriter.DeleteResult(true, mailId, string.Empty));
    }

    public Task ClaimAsync(UserSession session, int mailId) =>
        ClaimAsync(session, mailId, attachmentIndex: null);

    public Task ClaimAttachmentAsync(UserSession session, int mailId, int attachmentIndex) =>
        ClaimAsync(session, mailId, (int?)attachmentIndex);

    private async Task ClaimAsync(UserSession session, int mailId, int? attachmentIndex)
    {
        // A busy player's bag and purse may be counted on by a trade or a stall, so nothing lands in them.
        if (session.IsBusy)
        {
            await session.Client.SendPacket(MailPacketWriter.ClaimResult(false, mailId, "You cannot claim mail while trading, keeping a stall, gathering or dead."));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mail = await InboxQuery(db, session.CharacterId).FirstOrDefaultAsync(m => m.Id == mailId);
        var ordered = mail?.Attachments.OrderBy(a => a.Id).ToList() ?? [];
        var chosen = attachmentIndex is { } index
            ? ordered.Where((_, position) => position == index).ToList()
            : ordered;
        if (mail == null || !mail.HasUnclaimedAttachments || chosen.All(a => a.Remaining == 0))
        {
            await session.Client.SendPacket(MailPacketWriter.ClaimResult(false, mailId, "Nothing to claim."));
            return;
        }

        var delivered = false;
        var itemsDelivered = false;
        var dropped = 0;
        var tried = chosen.Count(a => a.Remaining > 0);
        foreach (var attachment in chosen.Where(a => a.Remaining > 0))
        {
            switch (attachment.Kind)
            {
                case MailAttachmentKind.Gold:
                    // Only what fits under the coin cap is claimed; the rest waits in the mail.
                    var remaining = attachment.Remaining;
                    var credited = session.WithLock(s =>
                    {
                        var take = (int)Math.Clamp(ExchangePacketConstants.CoinMax - (long)s.Money, 0, remaining);
                        s.Money += take;
                        return take;
                    });
                    if (credited > 0)
                    {
                        attachment.ClaimedCount += credited;
                        await userNotificationService.SendGoldGainAsync(session, credited);
                        delivered = true;
                    }
                    break;
                case MailAttachmentKind.Experience:
                    await playerProgressionService.AwardExperienceAsync(session, attachment.Remaining);
                    attachment.ClaimedCount = attachment.Count;
                    delivered = true;
                    break;
                case MailAttachmentKind.NationalPoints:
                    await loyaltyService.ChangeAsync(session, attachment.Remaining);
                    attachment.ClaimedCount = attachment.Count;
                    delivered = true;
                    break;
                default:
                    switch (await DeliverItemAsync(session, attachment))
                    {
                        case ItemDelivery.Delivered:
                            delivered = itemsDelivered = true;
                            break;
                        case ItemDelivery.Dropped:
                            dropped++;
                            break;
                    }
                    break;
            }
        }

        if (itemsDelivered)
        {
            session.RecalculateStatsWithBuffs(gameDataService);
            await userNotificationService.SendWeightChangeAsync(session);
        }

        var complete = mail.Attachments.All(a => a.Remaining == 0);
        if (complete)
            mail.ClaimedAt = DateTime.UtcNow;
        var firstRead = delivered && mail.ReadAt == null;
        if (firstRead)
            mail.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Only the attachments this claim tried are named.
        var goldWaiting = chosen.Any(a => a.Remaining > 0 && a.Kind == MailAttachmentKind.Gold);
        var itemsWaiting = chosen.Any(a => a.Remaining > 0 && a.Kind != MailAttachmentKind.Gold);
        var room = (itemsWaiting, goldWaiting) switch
        {
            (true, true) => "room in your inventory and your purse",
            (false, true) => "room in your purse",
            _ => "room in your inventory",
        };
        var waiting = chosen.Count(a => a.Remaining > 0) == 1 ? "An attachment is" : "Some attachments are";
        var outcome = goldWaiting || itemsWaiting
            ? delivered ? $"{waiting} still waiting. Make {room} and claim again." : $"Not enough {room}."
            : !delivered ? null
            : tried - dropped > 1 ? "Attachments claimed."
            : "Attachment claimed.";
        var lost = dropped switch
        {
            0 => null,
            1 => "An attachment could not be delivered.",
            _ => $"{dropped} attachments could not be delivered.",
        };
        var message = string.Join(" ", new[] { outcome, lost }.Where(part => part != null));
        await session.Client.SendPacket(MailPacketWriter.ClaimResult(delivered, mailId, message));
        if (firstRead)
            await session.Client.SendPacket(MailPacketWriter.Unread(await UnreadCountAsync(db, session.CharacterId)));
    }

    private enum ItemDelivery { NoRoom, Delivered, Dropped }

    // One read of the item, so the claim and its message agree even if the item table reloads meanwhile.
    private async Task<ItemDelivery> DeliverItemAsync(UserSession session, MailAttachment attachment)
    {
        var itemData = gameDataService.GetItem(attachment.ItemId);
        if (itemData == null)
        {
            attachment.ClaimedCount = attachment.Count;
            return ItemDelivery.Dropped;
        }

        var delivered = false;
        while (attachment.Remaining > 0)
        {
            var slot = BagSlotFor(session, attachment, itemData.Countable != 0);
            if (slot < 0)
                break;

            var entry = session.Inventory[slot];
            var isNew = entry.IsEmpty;
            var portion = itemData.Countable == 0 ? 1 : Math.Min(attachment.Remaining, InventoryConstants.MaxStackCount - (isNew ? 0 : entry.Count));
            if (isNew)
            {
                entry.Clear();
                entry.ItemId = attachment.ItemId;
                entry.Count = (ushort)portion;
                entry.Durability = attachment.Durability;
                entry.Flag = attachment.Flag;
                entry.ExpiresAt = attachment.ExpiresAt;
                entry.UniqueId = attachment.UniqueId;
            }
            else
            {
                entry.Count = (ushort)(entry.Count + portion);
            }

            await userNotificationService.SendStackChangeAsync(session, (byte)slot, entry.ItemId, entry.Count, entry.Durability, isNew);
            attachment.ClaimedCount += portion;
            delivered = true;
        }

        return delivered ? ItemDelivery.Delivered : ItemDelivery.NoRoom;
    }

    private static int BagSlotFor(UserSession session, MailAttachment attachment, bool countable)
    {
        var bag = session.Inventory.AsSpan(InventoryConstants.InventoryStart, InventoryConstants.HaveMax);
        if (countable)
        {
            for (var i = 0; i < bag.Length; i++)
            {
                var slot = bag[i];
                if (slot.ItemId == attachment.ItemId && slot.Flag == attachment.Flag && slot.ExpiresAt == attachment.ExpiresAt
                    && slot.UniqueId == attachment.UniqueId && slot.Count < InventoryConstants.MaxStackCount)
                    return InventoryConstants.InventoryStart + i;
            }
        }

        for (var i = 0; i < bag.Length; i++)
        {
            if (bag[i].IsEmpty)
                return InventoryConstants.InventoryStart + i;
        }

        return -1;
    }

    private async Task NotifyNewMailAsync(UserSession recipient, string senderName, AppDbContext db)
    {
        await recipient.Client.SendPacket(MailPacketWriter.Unread(await UnreadCountAsync(db, recipient.CharacterId)));
        await recipient.Client.SendPacket(NoticePacketWriter.Broadcast($"You have new mail from {senderName}."));
    }

    private static Task<int> UnreadCountAsync(AppDbContext db, int characterId) =>
        db.Mails.CountAsync(m => m.RecipientCharacterId == characterId && !m.Deleted && m.ReadAt == null);

    private static IQueryable<Mail> InboxQuery(AppDbContext db, int characterId) =>
        db.Mails.Include(m => m.Attachments).Where(m => m.RecipientCharacterId == characterId && !m.Deleted);

    private static Mail NewMail(int? senderCharacterId, string senderName, int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments, MailKind kind) =>
        new()
        {
            Kind = kind,
            SenderCharacterId = senderCharacterId,
            SenderName = Truncate(senderName, MailLimits.SenderNameMax),
            RecipientCharacterId = recipientCharacterId,
            Subject = Truncate(subject, MailLimits.SubjectMax),
            Body = Truncate(body, MailLimits.BodyMax),
            SentAt = DateTime.UtcNow,
            Attachments = attachments
                .Select(a => new MailAttachment
                {
                    Kind = a.Kind, ItemId = a.ItemId, Count = a.Count, Durability = a.Durability,
                    Flag = a.Flag, ExpiresAt = a.ExpiresAt, UniqueId = a.UniqueId,
                })
                .ToList(),
        };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static Task Fail(UserSession session, string message) =>
        session.Client.SendPacket(MailPacketWriter.SendResult(false, message));
}
