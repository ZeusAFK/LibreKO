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
    Task SendSystemMailAsync(int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments);
    Task SendInboxAsync(UserSession session);
    Task SendUnreadAsync(UserSession session);
    Task ReadAsync(UserSession session, int mailId);
    Task SendAsync(UserSession session, string recipientName, string subject, string body, int gold, IReadOnlyList<MailItemPick> items);
    Task DeleteAsync(UserSession session, int mailId);
    Task ClaimAsync(UserSession session, int mailId);
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
    public async Task SendSystemMailAsync(int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Mails.Add(NewMail(null, MailLimits.SystemSenderName, recipientCharacterId, subject, body, attachments));
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

        if (mail.ReadAt == null)
        {
            mail.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await session.Client.SendPacket(MailPacketWriter.ReadResult(true, mail.Id, mail.Body));
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

        db.Mails.Add(NewMail(session.CharacterId, session.Name, recipient.Id, subject, body, drafts));
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

    public async Task ClaimAsync(UserSession session, int mailId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mail = await InboxQuery(db, session.CharacterId).FirstOrDefaultAsync(m => m.Id == mailId);
        if (mail == null || !mail.HasUnclaimedAttachments)
        {
            await session.Client.SendPacket(MailPacketWriter.ClaimResult(false, mailId, "Nothing to claim."));
            return;
        }

        var itemAttachments = mail.Attachments.Where(a => a.Kind == MailAttachmentKind.Item).ToList();
        if (itemAttachments.Count > FreeGridSlots(session))
        {
            await session.Client.SendPacket(MailPacketWriter.ClaimResult(false, mailId, "Not enough room in your inventory."));
            return;
        }

        foreach (var attachment in mail.Attachments)
        {
            switch (attachment.Kind)
            {
                case MailAttachmentKind.Gold:
                    var newMoney = Math.Min((long)session.Money + attachment.Count, int.MaxValue);
                    var delta = (int)(newMoney - session.Money);
                    session.Money = (int)newMoney;
                    if (delta > 0)
                        await userNotificationService.SendGoldGainAsync(session, delta);
                    break;
                case MailAttachmentKind.Experience:
                    await playerProgressionService.AwardExperienceAsync(session, attachment.Count);
                    break;
                case MailAttachmentKind.NationalPoints:
                    await loyaltyService.ChangeAsync(session, attachment.Count);
                    break;
                default:
                    await DeliverItemAsync(session, attachment);
                    break;
            }
        }

        mail.ClaimedAt = DateTime.UtcNow;
        mail.ReadAt ??= mail.ClaimedAt;
        await db.SaveChangesAsync();

        await session.Client.SendPacket(MailPacketWriter.ClaimResult(true, mailId, "Attachments claimed."));
    }

    private async Task DeliverItemAsync(UserSession session, MailAttachment attachment)
    {
        var itemData = gameDataService.GetItem(attachment.ItemId);
        if (itemData == null)
            return;

        var remaining = attachment.Count;
        while (remaining > 0)
        {
            var portion = itemData.Countable == 0 ? 1 : remaining;
            var slot = session.FindSlotForItem(attachment.ItemId, gameDataService, (ushort)Math.Min(portion, ushort.MaxValue));
            if (slot < 0)
            {
                await SendNoticeAsync(session, "Inventory is full! Some attachments could not be delivered.");
                return;
            }

            var entry = session.Inventory[slot];
            var isNew = entry.IsEmpty;
            entry.ItemId = attachment.ItemId;
            entry.Count = (ushort)Math.Min(entry.Count + portion, ushort.MaxValue);
            if (isNew)
                entry.Durability = attachment.Durability > 0 ? attachment.Durability : itemData.Duration;
            await userNotificationService.SendStackChangeAsync(session, (byte)slot, entry.ItemId, entry.Count, entry.Durability, isNew);
            remaining -= portion;
        }

        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
    }

    private static int FreeGridSlots(UserSession session)
    {
        var free = 0;
        for (var index = InventoryConstants.InventoryStart; index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; index++)
        {
            if (session.Inventory[index].IsEmpty)
                free++;
        }

        return free;
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

    private static Mail NewMail(int? senderCharacterId, string senderName, int recipientCharacterId, string subject, string body, IReadOnlyList<MailAttachmentDraft> attachments) =>
        new()
        {
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

    private static Task SendNoticeAsync(UserSession session, string message) =>
        session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, message));
}
