using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMailPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class MailPacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<MailPacketCoordinator> logger) : IMailPacketCoordinator
{
    private const byte MailSubList = 1;
    private const byte MailSubRead = 2;
    private const byte MailSubSend = 3;
    private const byte MailSubDelete = 4;

    private const int MailSubjectMax = 64;
    private const int MailBodyMax = 512;

    private sealed class Mail
    {
        public int MailId;
        public string MailSender = "";
        public string MailSubject = "";
        public string MailBody = "";
        public bool MailRead;
        public int MailGold;
        public int MailItemId;
    }

    // recipient charId -> their inbox (newest first). In-memory; resets on restart.
    private static readonly Dictionary<int, List<Mail>> MailInboxes = new();
    private static int _mailNextId = 1;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case MailSubList:
                await SendInboxAsync(session);
                break;
            case MailSubRead:
                await HandleReadAsync(session, packet);
                break;
            case MailSubSend:
                await HandleSendAsync(session, packet);
                break;
            case MailSubDelete:
                await HandleDeleteAsync(session, packet);
                break;
        }
    }

    private async Task SendInboxAsync(UserSession session)
    {
        var entries = GetInbox(session.CharacterId)
            .Select(mail => new MailPacketWriter.InboxEntry(
                mail.MailId, mail.MailSender, mail.MailSubject,
                mail.MailRead, mail.MailGold, mail.MailItemId))
            .ToList();

        await session.Client.SendPacket(MailPacketWriter.Inbox(MailSubList, entries));
    }

    private async Task HandleReadAsync(UserSession session, Packet packet)
    {
        int mailId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var inbox = GetInbox(session.CharacterId);
        var mail = inbox.Find(m => m.MailId == mailId);

        if (mail == null)
        {
            await session.Client.SendPacket(MailPacketWriter.MailResult(
                MailSubRead, MailPacketWriter.Failed, mailId));
            return;
        }

        mail.MailRead = true;
        // Collect attached gold once (item attachment delivery is the follow-up via the inventory path).
        if (mail.MailGold > 0)
        {
            session.Money += mail.MailGold;
            await userNotification.SendGoldGainAsync(session, mail.MailGold);
            logger.LogDebug("{Name} collected {Gold} gold from mail {Id}", session.Name, mail.MailGold, mailId);
            mail.MailGold = 0;
        }
        await session.Client.SendPacket(MailPacketWriter.MailResult(
            MailSubRead, MailPacketWriter.Succeeded, mailId));
    }

    private async Task HandleSendAsync(UserSession session, Packet packet)
    {
        string recipientName = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : "";
        string subject = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : "";
        string body = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : "";
        int gold = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        int itemId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        var recipient = string.IsNullOrWhiteSpace(recipientName) ? null : sessionManager.GetByName(recipientName);
        if (recipient == null || recipientName.Length < 2)
        {
            await session.Client.SendPacket(MailPacketWriter.Result(
                MailSubSend, MailPacketWriter.Failed));
            return;
        }

        if (subject.Length > MailSubjectMax) subject = subject[..MailSubjectMax];
        if (body.Length > MailBodyMax) body = body[..MailBodyMax];

        // The sender pays the attached gold up-front (the recipient collects it on read). Reject if short.
        int attachGold = gold < 0 ? 0 : gold;
        if (attachGold > 0)
        {
            if (session.Money < attachGold)
            {
                await session.Client.SendPacket(MailPacketWriter.Result(
                    MailSubSend, MailPacketWriter.Failed));
                return;
            }
            session.Money -= attachGold;
            await userNotification.SendGoldLossAsync(session, attachGold);
        }

        var mail = new Mail
        {
            MailId = _mailNextId++,
            MailSender = session.Name,
            MailSubject = subject,
            MailBody = body,
            MailRead = false,
            MailGold = attachGold,
            MailItemId = itemId < 0 ? 0 : itemId,
        };
        GetInbox(recipient.CharacterId).Insert(0, mail);

        logger.LogDebug("{Sender} mailed {Recipient} (mail {Id})", session.Name, recipient.Name, mail.MailId);
        await session.Client.SendPacket(MailPacketWriter.Result(
            MailSubSend, MailPacketWriter.Succeeded));

        // Live push: if the recipient is online, refresh their inbox immediately.
        await SendInboxAsync(recipient);
    }

    private async Task HandleDeleteAsync(UserSession session, Packet packet)
    {
        int mailId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var inbox = GetInbox(session.CharacterId);
        int removed = inbox.RemoveAll(m => m.MailId == mailId);

        await session.Client.SendPacket(MailPacketWriter.MailResult(
            MailSubDelete,
            removed > 0 ? MailPacketWriter.Succeeded : MailPacketWriter.Failed,
            mailId));
    }

    private static List<Mail> GetInbox(int charId)
    {
        if (!MailInboxes.TryGetValue(charId, out var list))
        {
            list = new List<Mail>();
            MailInboxes[charId] = list;
        }
        return list;
    }
}
