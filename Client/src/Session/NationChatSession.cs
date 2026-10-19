namespace LibreKO.Session;

public sealed class NationChatSession
{
    public (string Name, bool Locked, int Members, int Max) PendingRoom { get; internal set; } = ("", false, 0, 0);

    public void NameNextRoom(string name, bool locked, int members, int maxMembers) =>
        PendingRoom = (name, locked, members, maxMembers);

    public int PendingPublicChannel { get; internal set; } = NationChatRules.NoRoom;

    public (string Name, bool Private, string Password, int Max)? PendingOptions { get; internal set; }

    public NationChatState NationChat { get; } = new();

    public event Action<List<NationChannel>>? NationChannelsEvent;

    internal void RaiseNationChannels(List<NationChannel> list) => NationChannelsEvent?.Invoke(list);

    public event Action<List<NationRoom>>? NationRoomsEvent;

    internal void RaiseNationRooms(List<NationRoom> list) => NationRoomsEvent?.Invoke(list);

    public event Action<NationChatSubOpcode, byte>? NationChatResultEvent;

    internal void RaiseNationChatResult(NationChatSubOpcode arg1, byte arg2) => NationChatResultEvent?.Invoke(arg1, arg2);

    public event Action<List<NationRoom>>? NationSearchEvent;

    internal void RaiseNationSearch(List<NationRoom> list) => NationSearchEvent?.Invoke(list);

    public NationRoomMembers? Members { get; private set; }

    public event Action<NationRoomMembers>? NationMembersEvent;

    internal void ApplyMembers(NationRoomMembers members)
    {
        Members = members;
        NationMembersEvent?.Invoke(members);
    }

    public event Action? NationChatStateEvent;

    internal void RaiseNationChatState() => NationChatStateEvent?.Invoke();

    public event Action<RoomChatLine>? RoomChatEvent;

    internal void RaiseRoomChat(RoomChatLine roomChatLine) => RoomChatEvent?.Invoke(roomChatLine);

    public event Action<RoomChatKind, int, string>? RoomChatNoticeEvent;

    internal void RaiseRoomChatNotice(RoomChatKind arg1, int arg2, string arg3) => RoomChatNoticeEvent?.Invoke(arg1, arg2, arg3);
}
