namespace LibreKO.Network.Readers;

internal sealed class FriendsReader : IPacketReader
{
    private readonly FriendsSession friends;

    public FriendsReader(FriendsSession friends)
    {
        this.friends = friends;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_FRIEND_PROCESS, HandleFriendProcess);
    }

    private void HandleFriendProcess(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (FriendSubOpcode)p.ReadByte();
        switch (sub)
        {
            case FriendSubOpcode.StatusList:
                {
                    if (p.RemainingBytes < 2) return;
                    int count = p.ReadUShort();
                    var entries = new List<FriendEntry>(count);
                    for (int i = 0; i < count && p.RemainingBytes >= 7; i++)
                        entries.Add(ReadFriendStatus(p));
                    friends.ApplyList(entries);
                    break;
                }
            case FriendSubOpcode.Details:
                {
                    if (p.RemainingBytes < 2) return;
                    int count = p.ReadUShort();
                    var details = new List<FriendDetails>(count);
                    for (int i = 0; i < count && p.RemainingBytes >= 7; i++)
                    {
                        string name = p.ReadString().Trim();
                        byte level = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                        short cls = p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;
                        byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                        byte zone = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                        details.Add(new FriendDetails(name, level, cls, nation, zone));
                    }
                    friends.ApplyDetails(details);
                    break;
                }
            case FriendSubOpcode.Add:
                if (FriendReplies.ReadAdd(p) is { } added)
                {
                    if (added.Code == FriendAddResult.Succeeded && added.Added.Name.Length > 0) friends.ApplyAdded(added.Added);
                    friends.RaiseFriendAddResult(added.Code, added.Added.Name, added.Added);
                }
                break;
            case FriendSubOpcode.Remove:
                if (FriendReplies.ReadRemove(p) is { } removed)
                {
                    if (removed.Code == FriendRemoveResult.Succeeded) friends.ApplyRemoved(removed.Name);
                    friends.RaiseFriendRemoveResult(removed.Code, removed.Name);
                }
                break;
            case FriendSubOpcode.LoggedIn:
                if (p.RemainingBytes >= 2) friends.RaiseFriendLoggedIn(p.ReadString().Trim());
                break;
            case FriendSubOpcode.Request:
                if (p.RemainingBytes >= 2) friends.RaiseFriendRequest(p.ReadString().Trim());
                break;
            case FriendSubOpcode.RequestResult:
                if (p.RemainingBytes >= 1) friends.RaiseFriendRequestResult((FriendRequestResult)p.ReadByte());
                break;
        }
    }

    private static FriendEntry ReadFriendStatus(Packet p) => FriendReplies.ReadStatus(p);
}
