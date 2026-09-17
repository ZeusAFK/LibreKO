using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const byte MailListSub   = 1;
    public const byte MailReadSub   = 2;
    public const byte MailSendSub   = 3;
    public const byte MailDeleteSub = 4;

    public const int MailSubjectMax = 64;
    public const int MailBodyMax    = 512;

    public event Action<List<MailEntry>>? MailListEvent;
    public event Action<int, bool>? MailReadEvent;
    public event Action<bool>? MailSendEvent;
    public event Action<int, bool>? MailDeleteEvent;

    private void HandleMail(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case MailListSub:
            {
                var list = new List<MailEntry>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                {
                    int id = p.ReadInt();
                    string sender = p.ReadSByteString();
                    string subject = p.ReadSByteString();
                    bool read = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
                    int gold = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    int itemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    list.Add(new MailEntry { Id = id, Sender = sender, Subject = subject, Read = read, Gold = gold, ItemId = itemId });
                }
                MailListEvent?.Invoke(list);
                break;
            }
            case MailReadSub:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int mailId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                MailReadEvent?.Invoke(mailId, ok);
                break;
            }
            case MailSendSub:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                MailSendEvent?.Invoke(ok);
                break;
            }
            case MailDeleteSub:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int mailId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                MailDeleteEvent?.Invoke(mailId, ok);
                break;
            }
        }
    }

    public void SendMailList()
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(MailListSub);
        _conn.Send(p);
    }

    public void SendMailRead(int mailId)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(MailReadSub);
        p.WriteInt(mailId);
        _conn.Send(p);
    }

    public void SendMailSend(string recipient, string subject, string body, int gold, int itemId)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(MailSendSub);
        p.WriteSByteString(recipient ?? "");
        p.WriteSByteString(subject ?? "");
        p.WriteSByteString(body ?? "");
        p.WriteInt(gold);
        p.WriteInt(itemId);
        _conn.Send(p);
    }

    public void SendMailDelete(int mailId)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte(MailDeleteSub);
        p.WriteInt(mailId);
        _conn.Send(p);
    }
}

public struct MailEntry
{
    public int Id;
    public string Sender;
    public string Subject;
    public bool Read;
    public int Gold;
    public int ItemId;
}
