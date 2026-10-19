using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class FriendRepliesTests
{
    private const byte AddSub = 3;
    private const byte RemoveSub = 4;

    private static Packet Reply(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte(sub);
        return p;
    }

    private static Packet Ready(Packet p)
    {
        p.ResetOffset();
        p.ReadByte();
        return p;
    }

    [Fact]
    public void AnAcceptedFriendArrivesWithItsStatus()
    {
        var p = Reply(AddSub);
        p.WriteByte((byte)FriendAddResult.Succeeded);
        p.WriteString("Rikka");
        p.WriteInt(2);
        p.WriteByte(1);

        var reply = FriendReplies.ReadAdd(Ready(p));

        Assert.NotNull(reply);
        Assert.Equal(FriendAddResult.Succeeded, reply.Value.Code);
        Assert.Equal("Rikka", reply.Value.Added.Name);
        Assert.Equal(2, reply.Value.Added.CharId);
        Assert.Equal(1, reply.Value.Added.Status);
    }

    [Fact]
    public void AFailedAddCarriesOnlyItsCode()
    {
        var p = Reply(AddSub);
        p.WriteByte((byte)FriendAddResult.ListFull);

        var reply = FriendReplies.ReadAdd(Ready(p));

        Assert.Equal(FriendAddResult.ListFull, reply!.Value.Code);
        Assert.Equal("", reply.Value.Added.Name);
    }

    [Fact]
    public void ARemovedFriendIsNamed()
    {
        var p = Reply(RemoveSub);
        p.WriteByte((byte)FriendRemoveResult.Succeeded);
        p.WriteString("Rikka");

        Assert.Equal((FriendRemoveResult.Succeeded, "Rikka"), FriendReplies.ReadRemove(Ready(p)));
    }

    [Theory]
    [InlineData(FriendRequestResult.ListFull, 44561)]
    [InlineData(FriendRequestResult.NationMismatch, 44566)]
    [InlineData(FriendRequestResult.Denied, 44568)]
    public void RequestRefusalsMapToTheirTexts(FriendRequestResult code, int text)
    {
        Assert.Equal(text, FriendTexts.RequestResultText(code));
        Assert.Null(FriendTexts.RequestResultText(0));
    }

    [Theory]
    [InlineData(FriendAddResult.Succeeded, 10406)]
    [InlineData(FriendAddResult.Failed, 10400)]
    [InlineData(FriendAddResult.ListFull, 10401)]
    public void AddResultsMapToTheirTexts(FriendAddResult code, int text)
    {
        Assert.Equal(text, FriendTexts.AddResultText(code));
    }
}
