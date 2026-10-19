using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class FriendsReaderTests
{
    private const string Friend = "Rikka";

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private static (PacketDispatcher Dispatcher, GameSession Session) Readers()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoConnection(), session, new LoginState()), () => 0);
        return (dispatcher, session);
    }

    private static Packet Reply(FriendSubOpcode sub, byte code, string? name = null)
    {
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte((byte)sub);
        p.WriteByte(code);
        if (name != null) p.WriteString(name);
        if (sub == FriendSubOpcode.Add && name != null)
        {
            p.WriteInt(2);
            p.WriteByte(1);
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AnAcceptedFriendJoinsTheSessionList()
    {
        var (dispatcher, session) = Readers();
        List<FriendEntry>? announced = null;
        session.Friends.FriendListEvent += list => announced = list;

        dispatcher.Dispatch(Reply(FriendSubOpcode.Add, (byte)FriendAddResult.Succeeded, Friend));

        Assert.True(session.Friends.IsFriend("rikka"));
        Assert.Single(session.Friends.Friends);
        Assert.NotNull(announced);
    }

    [Fact]
    public void ARefusedAddLeavesTheListAlone()
    {
        var (dispatcher, session) = Readers();

        dispatcher.Dispatch(Reply(FriendSubOpcode.Add, (byte)FriendAddResult.ListFull));

        Assert.Empty(session.Friends.Friends);
    }

    [Fact]
    public void ARemovedFriendLeavesTheSessionList()
    {
        var (dispatcher, session) = Readers();
        dispatcher.Dispatch(Reply(FriendSubOpcode.Add, (byte)FriendAddResult.Succeeded, Friend));

        dispatcher.Dispatch(Reply(FriendSubOpcode.Remove, (byte)FriendRemoveResult.Succeeded, Friend));

        Assert.False(session.Friends.IsFriend(Friend));
        Assert.Empty(session.Friends.Friends);
    }

    [Fact]
    public void TheNextCharacterStartsWithoutTheLastOnesFriends()
    {
        var (dispatcher, session) = Readers();
        dispatcher.Dispatch(Reply(FriendSubOpcode.Add, (byte)FriendAddResult.Succeeded, Friend));

        session.ResetCharacterState();

        Assert.False(session.Friends.IsFriend(Friend));
    }
}
