namespace LibreKO.Network.Requests;

public interface IFriendsRequests
{
    void FriendListRequest();
    void FriendAdd(string name);
    void FriendRemove(string name);
    void FriendAnswer(string requester, bool accept);
}

public sealed class FriendsRequests : IFriendsRequests
{
    private readonly IPacketSink sink;

    public FriendsRequests(IPacketSink sink) => this.sink = sink;

    public void FriendListRequest()
    {
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte((byte)FriendSubOpcode.StatusList);
        sink.Send(p);
    }

    public void FriendAdd(string name) => SendFriendName(FriendSubOpcode.Add, name);

    public void FriendRemove(string name) => SendFriendName(FriendSubOpcode.Remove, name);

    public void FriendAnswer(string requester, bool accept)
    {
        if (string.IsNullOrEmpty(requester)) return;
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte((byte)FriendSubOpcode.Request);
        p.WriteString(requester);
        p.WriteByte((byte)(accept ? 1 : 0));
        sink.Send(p);
    }

    private void SendFriendName(FriendSubOpcode sub, string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (name.Length > FriendsWire.MaxNameLength) name = name.Substring(0, FriendsWire.MaxNameLength);
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte((byte)sub);
        p.WriteString(name);
        sink.Send(p);
    }
}
