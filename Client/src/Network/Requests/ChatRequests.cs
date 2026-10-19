namespace LibreKO.Network.Requests;

public interface IChatRequests
{
    void ClanConsultation(int clanId);
    void PlayerConsultation(string name);
    void Send(string message, byte type = 1);
    void Target(string targetName);
    void Block(bool block);
}

public sealed class ChatRequests : IChatRequests
{
    private readonly IPacketSink sink;
    private readonly ChatSession chat;

    public ChatRequests(IPacketSink sink, ChatSession chat)
    {
        this.sink = sink;
        this.chat = chat;
    }

    public void ClanConsultation(int clanId)
    {
        var p = new Packet(GameOpcodes.GS_CHAT_TARGET);
        p.WriteByte((byte)ChatTargetType.ClanAdmission);
        p.WriteByte((byte)ChatConsultKind.Clan);
        p.WriteShort((short)clanId);
        sink.Send(p);
    }

    public void PlayerConsultation(string name)
    {
        var p = new Packet(GameOpcodes.GS_CHAT_TARGET);
        p.WriteByte((byte)ChatTargetType.ClanAdmission);
        p.WriteByte((byte)ChatConsultKind.Player);
        p.WriteString(name);
        sink.Send(p);
    }

    public void Send(string message, byte type = 1)
    {
        if (string.IsNullOrEmpty(message)) return;
        message = Domain.TextRunes.Truncate(message, ChatWire.MessageMaxLength);
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte(type);
        p.WriteString(message);
        sink.Send(p);
    }

    public void Target(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return;
        if (targetName.Length > 20) targetName = targetName.Substring(0, 20);
        var p = new Packet(GameOpcodes.GS_CHAT_TARGET);
        p.WriteByte((byte)ChatTargetType.Whisper);
        p.WriteString(targetName);
        sink.Send(p);
    }

    public void Block(bool block)
    {
        chat.ApplyWhispersBlocked(block);
        var p = new Packet(GameOpcodes.GS_CHAT_TARGET);
        p.WriteByte((byte)ChatTargetType.BlockToggle);
        p.WriteByte(block ? (byte)1 : (byte)0);
        sink.Send(p);
    }
}
