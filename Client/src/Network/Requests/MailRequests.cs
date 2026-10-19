namespace LibreKO.Network.Requests;

public interface IMailRequests
{
    void List();
    void Read(int mailId);
    void Delete(int mailId);
    void Claim(int mailId);
    void ClaimAttachment(int mailId, int attachmentIndex);
    void Send(string recipient, string subject, string body, int gold, IReadOnlyList<MailItemPick> items);
}

public sealed class MailRequests : IMailRequests
{
    private readonly IPacketSink sink;

    public MailRequests(IPacketSink sink) => this.sink = sink;

    public void List() => SendMailByte(MailSubOpcode.List);

    public void Read(int mailId) => SendMailId(MailSubOpcode.Read, mailId);

    public void Delete(int mailId) => SendMailId(MailSubOpcode.Delete, mailId);

    public void Claim(int mailId) => SendMailId(MailSubOpcode.Claim, mailId);

    public void ClaimAttachment(int mailId, int attachmentIndex)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte((byte)MailSubOpcode.ClaimAttachment);
        p.WriteInt(mailId);
        p.WriteByte((byte)attachmentIndex);
        sink.Send(p);
    }

    public void Send(string recipient, string subject, string body, int gold, IReadOnlyList<MailItemPick> items)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte((byte)MailSubOpcode.Send);
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
        sink.Send(p);
    }

    private void SendMailByte(MailSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte((byte)sub);
        sink.Send(p);
    }

    private void SendMailId(MailSubOpcode sub, int mailId)
    {
        var p = new Packet(GameOpcodes.GS_MAIL);
        p.WriteByte((byte)sub);
        p.WriteInt(mailId);
        sink.Send(p);
    }
}
