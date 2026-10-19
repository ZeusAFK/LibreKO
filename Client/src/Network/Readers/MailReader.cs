namespace LibreKO.Network.Readers;

internal sealed class MailReader : IPacketReader
{
    private readonly MailSession mail;

    private const int AttachmentWireSize = sizeof(byte) + sizeof(int) * 3;

    public MailReader(MailSession mail)
    {
        this.mail = mail;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_MAIL, HandleMail);
    }

    private void HandleMail(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (MailSubOpcode)p.ReadByte();
        switch (sub)
        {
            case MailSubOpcode.List:
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
                        for (int a = 0; a < attachmentCount && p.RemainingBytes >= AttachmentWireSize; a++)
                        {
                            entry.Items.Add(new MailAttachment
                            {
                                Kind = (MailAttachmentKind)p.ReadByte(),
                                ItemId = p.ReadInt(),
                                Count = p.ReadInt(),
                                Claimed = p.ReadInt(),
                            });
                        }
                        if (p.RemainingBytes >= 1) entry.Kind = (MailKind)p.ReadByte();
                        list.Add(entry);
                    }
                    mail.RaiseMailList(list);
                    break;
                }
            case MailSubOpcode.Read:
                {
                    if (p.RemainingBytes < 6) return;
                    bool ok = p.ReadByte() != 0;
                    int id = p.ReadInt();
                    mail.RaiseMailRead(id, ok, p.ReadSByteString());
                    break;
                }
            case MailSubOpcode.Send:
                {
                    if (p.RemainingBytes < 2) return;
                    bool ok = p.ReadByte() != 0;
                    mail.RaiseMailSend(ok, p.ReadSByteString());
                    break;
                }
            case MailSubOpcode.Delete:
                {
                    if (p.RemainingBytes < 6) return;
                    bool ok = p.ReadByte() != 0;
                    int id = p.ReadInt();
                    mail.RaiseMailDelete(id, ok, p.ReadSByteString());
                    break;
                }
            case MailSubOpcode.Claim:
                {
                    if (p.RemainingBytes < 6) return;
                    bool ok = p.ReadByte() != 0;
                    int id = p.ReadInt();
                    mail.RaiseMailClaim(id, ok, p.ReadSByteString());
                    break;
                }
            case MailSubOpcode.Unread:
                {
                    if (p.RemainingBytes < 2) return;
                    mail.ApplyUnread(p.ReadUShort());
                    break;
                }
        }
    }
}
