using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const byte MailSubList = 1;
    public const byte MailSubRead = 2;
    public const byte MailSubSend = 3;
    public const byte MailSubDelete = 4;
    public const byte MailSubClaim = 5;
    public const byte MailSubUnread = 6;

    public const int MailSubjectMax = 64;
    public const int MailBodyMax = 512;
    public const int MailItemAttachmentsMax = 4;

    public event Action<List<MailEntry>>? MailListEvent;
    public event Action<int, bool, string>? MailReadEvent;
    public event Action<bool, string>? MailSendEvent;
    public event Action<int, bool, string>? MailDeleteEvent;
    public event Action<int, bool, string>? MailClaimEvent;
    public event Action<int>? MailUnreadEvent;

    public int MailUnread { get; private set; }

    private void HandleMail(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = p.ReadByte();
        switch (sub)
        {
            case MailSubList:
            {
                if (p.RemainingBytes < 2) return;
                int count = p.ReadUShort();
                var list = new List<MailEntry>(count);
                for (int i = 0; i < count && p.RemainingBytes >= 17; i++)
                {
                    var entry = new MailEntry
                    {
                        Id = p.ReadInt(),
                        Sender = p.ReadSByteString(),
                        Subject = p.ReadSByteString(),
                        Read = p.ReadByte() != 0,
                        Attachments = (MailAttachmentState)p.ReadByte(),
                        SentAt = DateTimeOffset.FromUnixTimeSeconds(p.ReadLong()).UtcDateTime,
                    };
                    int attachmentCount = p.ReadByte();
                    for (int a = 0; a < attachmentCount && p.RemainingBytes >= 9; a++)
                    {
                        entry.Items.Add(new MailAttachment
                        {
                            Kind = (MailAttachmentKind)p.ReadByte(),
                            ItemId = p.ReadInt(),
                            Count = p.ReadInt(),
                        });
                    }
                    list.Add(entry);
                }
                MailListEvent?.Invoke(list);
                break;
            }
            case MailSubRead:
            {
                if (p.RemainingBytes < 6) return;
                bool ok = p.ReadByte() != 0;
                int id = p.ReadInt();
                MailReadEvent?.Invoke(id, ok, p.ReadSByteString());
                break;
            }
            case MailSubSend:
            {
                if (p.RemainingBytes < 2) return;
                bool ok = p.ReadByte() != 0;
                MailSendEvent?.Invoke(ok, p.ReadSByteString());
                break;
            }
            case MailSubDelete:
            {
                if (p.RemainingBytes < 6) return;
                bool ok = p.ReadByte() != 0;
                int id = p.ReadInt();
                MailDeleteEvent?.Invoke(id, ok, p.ReadSByteString());
                break;
            }
            case MailSubClaim:
            {
                if (p.RemainingBytes < 6) return;
                bool ok = p.ReadByte() != 0;
                int id = p.ReadInt();
                MailClaimEvent?.Invoke(id, ok, p.ReadSByteString());
                break;
            }
            case MailSubUnread:
            {
                if (p.RemainingBytes < 2) return;
                MailUnread = p.ReadUShort();
                MailUnreadEvent?.Invoke(MailUnread);
                break;
            }
        }
    }

    public void SendMailList() => SendMailByte(MailSubList);

    public void SendMailRead(int mailId) => SendMailId(MailSubRead, mailId);

    public void SendMailDelete(int mailId) => SendMailId(MailSubDelete, mailId);

    public void SendMailClaim(int mailId) => SendMailId(MailSubClaim, mailId);

    public void SendMailSend(string recipient, string subject, string body, int gold, IReadOnlyList<MailItemPick> items)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(MailSubSend);
        p.WriteSByteString(recipient);
        p.WriteSByteString(subject);
        p.WriteSByteString(body);
        p.WriteInt(gold);
        p.WriteByte((byte)items.Count);
        foreach (var item in items)
        {
            p.WriteByte(item.Slot);
            p.WriteUShort(item.Count);
        }
        _conn.Send(p);
    }

    private void SendMailByte(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(sub);
        _conn.Send(p);
    }

    private void SendMailId(byte sub, int mailId)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(sub);
        p.WriteInt(mailId);
        _conn.Send(p);
    }
}

public enum MailAttachmentKind : byte
{
    Item = 0,
    Gold = 1,
    Experience = 2,
    NationalPoints = 3,
}

public enum MailAttachmentState : byte
{
    None = 0,
    Pending = 1,
    Claimed = 2,
}

public class MailAttachment
{
    public MailAttachmentKind Kind;
    public int ItemId;
    public int Count;
}

public readonly record struct MailItemPick(byte Slot, ushort Count);

public class MailEntry
{
    public int Id;
    public string Sender = string.Empty;
    public string Subject = string.Empty;
    public bool Read;
    public MailAttachmentState Attachments;
    public DateTime SentAt;
    public List<MailAttachment> Items = [];
}
