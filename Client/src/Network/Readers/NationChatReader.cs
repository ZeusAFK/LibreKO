namespace LibreKO.Network.Readers;

internal sealed class NationChatReader : IPacketReader
{
    private readonly NationChatSession nationChat;
    private readonly RoomMediaSession roomMedia;
    private readonly IRoomMediaRequests roomMediaRequests;

    public NationChatReader(NationChatSession nationChat, RoomMediaSession roomMedia, IRoomMediaRequests roomMediaRequests)
    {
        this.nationChat = nationChat;
        this.roomMedia = roomMedia;
        this.roomMediaRequests = roomMediaRequests;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_NATION_CHAT, HandleNationChat);
    }

    private void HandleNationChat(Packet p)
    {
        if (p.RemainingBytes < 2 || p.ReadByte() != NationChatWire.NationRooms) return;
        var sub = (NationChatSubOpcode)p.ReadByte();
        switch (sub)
        {
            case NationChatSubOpcode.PublicList:
                nationChat.RaiseNationChannels(NationChatReplies.ReadChannels(p));
                return;
            case NationChatSubOpcode.PrivateList:
                nationChat.RaiseNationRooms(NationChatReplies.ReadRooms(p));
                return;
            case NationChatSubOpcode.Members:
                {
                    byte result = ReadResult(p);
                    if (result == 0 && NationChatReplies.ReadMembers(p) is { } members)
                        nationChat.ApplyMembers(members);
                    else nationChat.RaiseNationChatResult(sub, result);
                    return;
                }
        }

        byte code = ReadResult(p);
        switch (sub)
        {
            case NationChatSubOpcode.PublicEnter:
                if (code == 0) EnterChatRoom(() => nationChat.NationChat.EnteredPublic(nationChat.PendingPublicChannel));
                nationChat.PendingPublicChannel = NationChatRules.NoRoom;
                break;
            case NationChatSubOpcode.PublicLeave:
            case NationChatSubOpcode.PrivateLeave:
                if (code == 0) LeaveChatRoom();
                break;
            case NationChatSubOpcode.Create:
                if (code == NationChatWire.Created && NationChatReplies.ReadRoomInfo(p) is { } created)
                    EnterChatRoom(() => nationChat.NationChat.OwnRoom(created.Id, created.Name, created.Private, created.Password, created.MaxMembers));
                break;
            case NationChatSubOpcode.PrivateEnter:
                if (code == 0)
                {
                    int roomId = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    EnterChatRoom(() => nationChat.NationChat.EnteredPrivate(roomId));
                }
                break;
            case NationChatSubOpcode.Search:
                if (code == 0) nationChat.RaiseNationSearch(NationChatReplies.ReadRooms(p));
                break;
            case NationChatSubOpcode.HandOver:
                if (code == 0) nationChat.NationChat.LostOwnership();
                else if (code == NationChatWire.YouOwnIt && NationChatReplies.ReadRoomInfo(p) is { } owned)
                    nationChat.NationChat.OwnRoom(owned.Id, owned.Name, owned.Private, owned.Password, owned.MaxMembers);
                break;
            case NationChatSubOpcode.Kick:
                if (code == NationChatRules.KickedCode) LeaveChatRoom();
                break;
            case NationChatSubOpcode.Options:
                if (code == 0 && nationChat.PendingOptions is { } options && nationChat.NationChat.InPrivate)
                    nationChat.NationChat.Configured(options.Name, options.Private, options.Password, options.Max);
                nationChat.PendingOptions = null;
                break;
        }
        nationChat.RaiseNationChatResult(sub, code);
        nationChat.RaiseNationChatState();
    }

    private static byte ReadResult(Packet p) => p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;

    private void EnterChatRoom(Action enter)
    {
        roomMedia.RoomTimeline.Clear();
        roomMedia.DropVoice();
        enter();
        if (!nationChat.NationChat.Owner && nationChat.PendingRoom.Name.Length > 0)
            nationChat.NationChat.Describe(nationChat.PendingRoom.Name, nationChat.PendingRoom.Locked, nationChat.PendingRoom.Members + 1, nationChat.PendingRoom.Max);
        nationChat.PendingRoom = ("", false, 0, 0);
        roomMedia.ShowRoomPanel();
        roomMedia.ClearVoicePeers();
        roomMediaRequests.RoomHistory(0);
        roomMediaRequests.VoiceRoster();
    }

    private void LeaveChatRoom()
    {
        nationChat.NationChat.Left();
        roomMedia.LeftRoom();
    }
}
