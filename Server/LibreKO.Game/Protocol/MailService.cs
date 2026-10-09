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

public readonly record struct MailAttachmentDraft(MailAttachmentKind Kind, int ItemId, int Count, short Durability = 0);

public readonly record struct MailItemPick(byte Slot, ushort Count);

public interface IMailService
{
    Task SendSystemMailAsync(int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments, MailKind kind = MailKind.System);
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
    ILogger<MailService> logger) : IMailService
{
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

        var firstRead = mail.ReadAt == null;
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

        if (gold < 0 || gold > session.Money)
        {
            await Fail(session, "You do not carry that much gold.");
            return;
        }

        if (items.Count > MailLimits.ItemAttachmentsMax)
        {
            await Fail(session, $"A mail carries at most {MailLimits.ItemAttachmentsMax} items.");
            return;
        }

        var drafts = new List<MailAttachmentDraft>();
        var picks = new List<(int Slot, ushort Count, ItemData Item)>();
        var seenSlots = new HashSet<int>();
        foreach (var pick in items)
        {
            var slot = pick.Slot;
            if (slot < InventoryConstants.InventoryStart || slot >= InventoryConstants.InventoryStart + InventoryConstants.HaveMax || !seenSlots.Add(slot))
            {
                await Fail(session, "That item cannot be attached.");
                return;
            }

            var entry = session.Inventory[slot];
            var itemData = entry.IsEmpty ? null : gameDataService.GetItem(entry.ItemId);
            if (itemData == null || pick.Count == 0 || pick.Count > entry.Count)
            {
                await Fail(session, "That item cannot be attached.");
                return;
            }

            if (!entry.IsTradable || !ExchangeTransferService.IsTradableItem(itemData, entry.ItemId, (byte)(slot - InventoryConstants.InventoryStart)))
            {
                await Fail(session, $"{itemData.Name} cannot be traded, so it cannot be mailed.");
                return;
            }

            picks.Add((slot, pick.Count, itemData));
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipient = await db.Characters
            .Where(c => c.Name == recipientName)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync();
        if (recipient == null)
        {
            await Fail(session, $"No character named '{recipientName}'.");
            return;
        }

        foreach (var (slot, count, itemData) in picks)
        {
            var entry = session.Inventory[slot];
            var durability = entry.Durability;
            entry.Count = (ushort)(entry.Count - count);
            if (entry.Count == 0)
                entry.Clear();
            drafts.Add(new MailAttachmentDraft(MailAttachmentKind.Item, itemData.Num, count, entry.IsEmpty ? durability : itemData.Duration));
            await userNotificationService.SendStackChangeAsync(session, (byte)slot, entry.ItemId, entry.Count, entry.Durability);
        }

        if (gold > 0)
        {
            session.Money -= gold;
            drafts.Add(new MailAttachmentDraft(MailAttachmentKind.Gold, InventoryConstants.ItemGold, gold));
            await userNotificationService.SendGoldLossAsync(session, gold);
        }

        if (picks.Count > 0)
        {
            session.RecalculateStatsWithBuffs(gameDataService);
            await userNotificationService.SendWeightChangeAsync(session);
        }

        db.Mails.Add(NewMail(session.CharacterId, session.Name, recipient.Id, subject, body, drafts, MailKind.Player));
        await db.SaveChangesAsync();

        logger.LogInformation("{Sender} mailed {Recipient}: '{Subject}' with {Attachments} attachments", session.Name, recipient.Name, subject, drafts.Count);
        await session.Client.SendPacket(MailPacketWriter.SendResult(true, $"Mail sent to {recipient.Name}."));

        var online = sessionManager.GetByCharacterId(recipient.Id);
        if (online != null)
            await NotifyNewMailAsync(online, session.Name, db);
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
        foreach (var attachment in chosen.Where(a => a.Remaining > 0))
        {
            switch (attachment.Kind)
            {
                case MailAttachmentKind.Gold:
                    var newMoney = Math.Min((long)session.Money + attachment.Remaining, int.MaxValue);
                    var delta = (int)(newMoney - session.Money);
                    session.Money = (int)newMoney;
                    if (delta > 0)
                        await userNotificationService.SendGoldGainAsync(session, delta);
                    attachment.ClaimedCount = attachment.Count;
                    delivered = true;
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
                    if (await DeliverItemAsync(session, attachment))
                        delivered = itemsDelivered = true;
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

        var message = complete ? "Attachments claimed."
            : delivered ? "Some attachments are still waiting. Make room in your inventory and claim again."
            : "Not enough room in your inventory.";
        await session.Client.SendPacket(MailPacketWriter.ClaimResult(delivered, mailId, message));
        if (firstRead)
            await session.Client.SendPacket(MailPacketWriter.Unread(await UnreadCountAsync(db, session.CharacterId)));
    }

    private async Task<bool> DeliverItemAsync(UserSession session, MailAttachment attachment)
    {
        var itemData = gameDataService.GetItem(attachment.ItemId);
        if (itemData == null)
        {
            attachment.ClaimedCount = attachment.Count;
            return false;
        }

        var delivered = false;
        while (attachment.Remaining > 0)
        {
            var slot = session.FindSlotForItem(attachment.ItemId, gameDataService);
            if (slot < 0)
                return delivered;

            var entry = session.Inventory[slot];
            var portion = itemData.Countable == 0 ? 1 : Math.Min(attachment.Remaining, InventoryConstants.MaxStackCount - entry.Count);
            var isNew = entry.IsEmpty;
            entry.ItemId = attachment.ItemId;
            entry.Count += (ushort)portion;
            if (isNew)
                entry.Durability = attachment.Durability > 0 ? attachment.Durability : itemData.Duration;
            await userNotificationService.SendStackChangeAsync(session, (byte)slot, entry.ItemId, entry.Count, entry.Durability, isNew);
            attachment.ClaimedCount += portion;
            delivered = true;
        }

        return delivered;
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
                .Select(a => new MailAttachment { Kind = a.Kind, ItemId = a.ItemId, Count = a.Count, Durability = a.Durability })
                .ToList(),
        };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static Task Fail(UserSession session, string message) =>
        session.Client.SendPacket(MailPacketWriter.SendResult(false, message));
}
