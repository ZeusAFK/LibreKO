namespace LibreKO.Network.Requests;

public interface INationChatRequests
{
    void PublicEnter(int channelId);
    void Options(string name, bool isPrivate, string password, int maxMembers);
    void NationChannels();
    void NationRooms();
    void NationPublicLeave();
    void NationCreate(string name, bool isPrivate, string password, int maxMembers);
    void NationPrivateEnter(int roomId, string password);
    void NationPrivateLeave();
    void NationSearch(string word);
    void NationHandOver(int memberId);
    void NationKick(int memberId);
    void NationMembers();
    void RoomChat(string text);
}

public sealed class NationChatRequests : INationChatRequests
{
    private readonly IPacketSink sink;
    private readonly NationChatSession chat;

    public NationChatRequests(IPacketSink sink, NationChatSession chat)
    {
        this.sink = sink;
        this.chat = chat;
    }

    public void NationChannels() => SendNationChat(NationChatSubOpcode.PublicList);

    public void NationRooms() => SendNationChat(NationChatSubOpcode.PrivateList);

    public void NationPublicLeave()
    {
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.PublicLeave);
        p.WriteUShort((ushort)Math.Max(0, chat.NationChat.RoomId));
        sink.Send(p);
    }

    public void NationCreate(string name, bool isPrivate, string password, int maxMembers) =>
            sink.Send(NationChatWire.RoomSettings(NationChatWire.NationChatPacket(NationChatSubOpcode.Create), name, isPrivate, password, maxMembers));

    public void NationPrivateEnter(int roomId, string password)
    {
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.PrivateEnter);
        p.WriteUShort((ushort)roomId);
        p.WriteByte((byte)(password.Length > 0 ? 1 : 0));
        if (password.Length > 0) p.WriteString(password);
        sink.Send(p);
    }

    public void NationPrivateLeave()
    {
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.PrivateLeave);
        p.WriteUShort((ushort)Math.Max(0, chat.NationChat.RoomId));
        sink.Send(p);
    }

    public void NationSearch(string word)
    {
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.Search);
        p.WriteString(word);
        sink.Send(p);
    }

    public void NationHandOver(int memberId) => SendNationMemberAction(NationChatSubOpcode.HandOver, memberId);

    public void NationKick(int memberId) => SendNationMemberAction(NationChatSubOpcode.Kick, memberId);

    public void NationMembers()
    {
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.Members);
        p.WriteUShort((ushort)Math.Max(0, chat.NationChat.RoomId));
        sink.Send(p);
    }

    public void RoomChat(string text)
    {
        if (string.IsNullOrEmpty(text) || !chat.NationChat.InRoom) return;
        text = TextRunes.Truncate(text, ChatWire.MessageMaxLength);
        var p = new Packet(GameOpcodes.GS_CHAT);
        p.WriteByte((byte)ChatType.ChatRoom);
        p.WriteString(text);
        p.WriteUShort((ushort)chat.NationChat.RoomId);
        sink.Send(p);
    }

    private void SendNationMemberAction(NationChatSubOpcode sub, int memberId)
    {
        var p = NationChatWire.NationChatPacket(sub);
        p.WriteUShort((ushort)Math.Max(0, chat.NationChat.RoomId));
        p.WriteInt(memberId);
        sink.Send(p);
    }

    private void SendNationChat(NationChatSubOpcode sub) => sink.Send(NationChatWire.NationChatPacket(sub));

    public void PublicEnter(int channelId)
    {
        chat.PendingPublicChannel = channelId;
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.PublicEnter);
        p.WriteUShort((ushort)channelId);
        sink.Send(p);
    }

    public void Options(string name, bool isPrivate, string password, int maxMembers)
    {
        chat.PendingOptions = (name, isPrivate, password, maxMembers);
        var p = NationChatWire.NationChatPacket(NationChatSubOpcode.Options);
        p.WriteUShort((ushort)Math.Max(0, chat.NationChat.RoomId));
        sink.Send(NationChatWire.RoomSettings(p, name, isPrivate, password, maxMembers));
    }
}
