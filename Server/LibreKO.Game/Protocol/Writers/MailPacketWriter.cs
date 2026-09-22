using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MailPacketWriter
{
    public const byte SubList = 1;
    public const byte SubRead = 2;
    public const byte SubSend = 3;
    public const byte SubDelete = 4;
    public const byte SubClaim = 5;
    public const byte SubUnread = 6;

    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public const byte AttachmentsNone = 0;
    public const byte AttachmentsPending = 1;
    public const byte AttachmentsClaimed = 2;

    public static Packet Inbox(IReadOnlyList<Mail> mails)
    {
        var packet = Sub(SubList);
        packet.WriteUShort((ushort)mails.Count);
        foreach (var mail in mails)
        {
            packet.WriteInt(mail.Id);
            packet.WriteSByteString(mail.SenderName);
            packet.WriteSByteString(mail.Subject);
            packet.WriteByte((byte)(mail.ReadAt != null ? 1 : 0));
            packet.WriteByte(mail.Attachments.Count == 0 ? AttachmentsNone
                : mail.ClaimedAt != null ? AttachmentsClaimed
                : AttachmentsPending);
            packet.WriteLong(new DateTimeOffset(mail.SentAt, TimeSpan.Zero).ToUnixTimeSeconds());
            packet.WriteByte((byte)mail.Attachments.Count);
            foreach (var attachment in mail.Attachments)
            {
                packet.WriteByte((byte)attachment.Kind);
                packet.WriteInt(attachment.ItemId);
                packet.WriteInt(attachment.Count);
            }
        }

        return packet;
    }

    public static Packet ReadResult(bool ok, int mailId, string body)
    {
        var packet = Sub(SubRead);
        packet.WriteByte(ok ? Succeeded : Failed);
        packet.WriteInt(mailId);
        packet.WriteSByteString(body);
        return packet;
    }

    public static Packet SendResult(bool ok, string message)
    {
        var packet = Sub(SubSend);
        packet.WriteByte(ok ? Succeeded : Failed);
        packet.WriteSByteString(message);
        return packet;
    }

    public static Packet DeleteResult(bool ok, int mailId, string message)
    {
        var packet = Sub(SubDelete);
        packet.WriteByte(ok ? Succeeded : Failed);
        packet.WriteInt(mailId);
        packet.WriteSByteString(message);
        return packet;
    }

    public static Packet ClaimResult(bool ok, int mailId, string message)
    {
        var packet = Sub(SubClaim);
        packet.WriteByte(ok ? Succeeded : Failed);
        packet.WriteInt(mailId);
        packet.WriteSByteString(message);
        return packet;
    }

    public static Packet Unread(int count)
    {
        var packet = Sub(SubUnread);
        packet.WriteUShort((ushort)Math.Min(count, ushort.MaxValue));
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MAIL);
        packet.WriteByte(sub);
        return packet;
    }
}
