namespace LibreKO.Network.Readers;

internal sealed class ChatReader : IPacketReader
{
    private readonly ChatSession chat;
    private readonly NationChatSession nationChat;
    private readonly RoomMediaSession roomMedia;

    public ChatReader(ChatSession chat, NationChatSession nationChat, RoomMediaSession roomMedia)
    {
        this.chat = chat;
        this.nationChat = nationChat;
        this.roomMedia = roomMedia;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CHAT, HandleChat);
        dispatcher.On(GameOpcodes.GS_CHAT_TARGET, HandleChatTarget);
    }

    private void HandleChat(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var type = (ChatType)p.ReadByte();
        if (type == (ChatType)ChatWire.TypeDeathNotice)
        {
            HandleDeathNotice(p);
            return;
        }
        if (type == ChatType.ChatRoom)
        {
            HandleRoomChat(p);
            return;
        }
        if (type == ChatType.KrowazNotice)
        {
            if (ChatWire.ReadKrowazSeconds(p) is { } seconds) chat.RaiseKrowazNotice(seconds);
            return;
        }
        switch (type)
        {
            case ChatType.MonumentNotice:
                if (MonumentNotice.Read(p) is { } monument) chat.RaiseMonumentNotice(monument);
                return;
            case ChatType.ChaosStoneNotice:
                if (ChaosStoneNotice.Read(p) is { } stone) chat.RaiseChaosStoneNotice(stone);
                return;
            case ChatType.BoxItemNotice:
                if (BoxItemNotice.Read(p) is { } box) chat.RaiseBoxItemNotice(box);
                return;
        }
        int nation = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        int charId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
        string message = p.RemainingBytes >= 2 ? p.ReadString() : "";
        bool isGm = p.RemainingBytes >= 1 && (ChatAuthority)p.ReadByte() == ChatAuthority.GameMaster;

        if (message.Length == 0) return;
        chat.RaiseChat(new ChatLine((byte)type, nation, charId, name, message, isGm));
    }

    private void HandleDeathNotice(Packet p)
    {
        if (ChatWire.ReadDeathNotice(p) is { } notice) chat.RaiseDeathNotice(notice);
    }

    private void HandleChatTarget(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        var type = (ChatTargetType)p.ReadByte();
        if (type == ChatTargetType.ClanAdmission)
        {
            var admission = (ChatTargetResult)p.ReadShort();
            string who = admission == ChatTargetResult.Connected && p.RemainingBytes >= 2 ? p.ReadString() : "";
            bool received = p.RemainingBytes >= 1 && p.ReadByte() != 0;
            chat.RaiseClanConsultation(admission == ChatTargetResult.Connected, who, received);
            return;
        }
        if (type != ChatTargetType.Whisper) return;
        var result = (ChatTargetResult)p.ReadShort();
        string name = p.RemainingBytes >= 2 ? p.ReadString() : "";
        chat.RaiseChatTarget(result, name);
    }

    private void HandleRoomChat(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var kind = (RoomChatKind)p.ReadByte();
        switch (kind)
        {
            case RoomChatKind.Message:
                {
                    if (p.RemainingBytes < 6) return;
                    int sender = p.ReadInt();
                    string name = p.ReadString();
                    if (p.RemainingBytes < 2) return;
                    string text = p.ReadString();
                    int zone = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    if (text.Length == 0) return;
                    roomMedia.RoomTimeline.Add(RoomPost.Line(sender, name, text, DateTime.Now));
                    nationChat.RaiseRoomChat(new RoomChatLine(sender, name, text, zone));
                    return;
                }
            case RoomChatKind.Entered:
            case RoomChatKind.Left:
                {
                    if (p.RemainingBytes < 4) return;
                    int members = p.ReadUShort();
                    nationChat.NationChat.Counted(members);
                    nationChat.RaiseRoomChatNotice(kind, members, p.ReadString());
                    return;
                }
            case RoomChatKind.NewOwner:
            case RoomChatKind.Kicked:
                if (p.RemainingBytes >= 2) nationChat.RaiseRoomChatNotice(kind, 0, p.ReadString());
                return;
        }
    }
}
