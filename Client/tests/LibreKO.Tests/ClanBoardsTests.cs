using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ClanBoardsTests
{
    private static Packet Reply(System.Action<Packet> body)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        body(p);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheStaffStatusListsTheApplicants()
    {
        var p = Reply(w =>
        {
            w.WriteByte((byte)ClanBoardState.Staff);
            w.WriteShort(1);
            w.WriteByte(50);
            w.WriteByte(112);
            w.WriteString("Rin");
            w.WriteString("Hello");
        });

        var status = ClanBoardsWire.ReadBoardStatus(p)!;

        Assert.True(status.Staff);
        Assert.Equal(new[] { new ClanApplicant(50, 112, "Rin", "Hello") }, status.Applicants);
    }

    [Fact]
    public void AnApplicantsStatusNamesTheClanTheyAppliedTo()
    {
        var p = Reply(w =>
        {
            w.WriteByte((byte)ClanBoardState.Clanless);
            w.WriteShort(1);
            w.WriteShort(10);
            w.WriteShort(4);
            w.WriteString("Wolves");
        });

        var status = ClanBoardsWire.ReadBoardStatus(p)!;

        Assert.True(status.Applied);
        Assert.Equal((10, 4, "Wolves"), (status.AppliedClanId, status.AppliedMarkVersion, status.AppliedClanName));
    }

    [Fact]
    public void AdvertRowsReadEveryField()
    {
        var p = Reply(w =>
        {
            w.WriteShort(1);
            w.WriteShort(10);
            w.WriteString("Wolves");
            w.WriteByte(3);
            w.WriteByte(2);
            w.WriteString("Alpha");
            w.WriteShort(4);
            w.WriteString("Join us");
        });

        Assert.Equal(new[] { new ClanAdvert(10, "Wolves", 3, 2, "Alpha", 4, "Join us") }, ClanBoardsWire.ReadAdverts(p));
    }

    [Fact]
    public void WishRowsReadEveryFieldAndDropAnOversizedPage()
    {
        var p = Reply(w =>
        {
            w.WriteShort(1);
            w.WriteString("Rin");
            w.WriteString("Priest");
            w.WriteByte(72);
            w.WriteByte(112);
            w.WriteByte(25);
            w.WriteByte(1);
            w.WriteInt(101);
            w.WriteString("2026-10-09");
        });
        var oversized = Reply(w => w.WriteShort(ClanBoards.RowsPerPage + 1));

        Assert.Equal(new[] { new WishPost("Rin", "Priest", 72, 112, 25, true, 101, "2026-10-09") }, ClanBoardsWire.ReadWishPosts(p));
        Assert.Empty(ClanBoardsWire.ReadWishPosts(oversized));
    }

    [Fact]
    public void TheWishStatusDependsOnTheRole()
    {
        var recruiter = ClanBoardsWire.ReadWishStatus(Reply(w => { w.WriteByte((byte)WishBoardRole.Recruiter); w.WriteString("Kaito"); }));
        var member = ClanBoardsWire.ReadWishStatus(Reply(w => w.WriteByte((byte)WishBoardRole.Member)));
        var seeker = ClanBoardsWire.ReadWishStatus(Reply(w => { w.WriteByte((byte)WishBoardRole.Seeker); w.WriteByte(3); }));

        Assert.Equal("Kaito", recruiter.RequestedName);
        Assert.True(member.Member);
        Assert.Equal(3, seeker.Received);
    }

    [Fact]
    public void ReceivedRequestsReadLeaderBeforeClan()
    {
        var p = Reply(w =>
        {
            w.WriteByte(1);
            w.WriteShort(10);
            w.WriteString("Alpha");
            w.WriteString("Wolves");
            w.WriteByte(3);
            w.WriteByte(2);
            w.WriteShort(4);
            w.WriteString("Come with us");
        });

        Assert.Equal(new[] { new WishRequest(10, "Alpha", "Wolves", 3, 2, 4, "Come with us") }, ClanBoardsWire.ReadWishRequests(p));
    }

    [Theory]
    [InlineData("25", true)]
    [InlineData("1", true)]
    [InlineData("99", true)]
    [InlineData("0", false)]
    [InlineData("100", false)]
    [InlineData("-5", false)]
    [InlineData("2a", false)]
    [InlineData("", false)]
    public void AgeMustBeOneToNinetyNineDigits(string text, bool valid)
    {
        Assert.Equal(valid, ClanBoards.AgeValid(text));
    }

    [Fact]
    public void ResultCodesMapToThe2619Texts()
    {
        Assert.Equal(16510, ClanBoards.ApplyResultText(ClanBoardAction.Register, 0));
        Assert.Equal(16507, ClanBoards.ApplyResultText(ClanBoardAction.Delete, 2));
        Assert.Equal(16504, ClanBoards.AdvertResultText(ClanBoardAction.Register, 3));
        Assert.Equal(18633, ClanBoards.PromoteText(4));
        Assert.Equal(40056, ClanBoards.IntroResultText(ClanBoardAction.Register, 3));
        Assert.Equal(40061, ClanBoards.RequestResultText(ClanBoardAction.Register, 4));
        Assert.Equal(40064, ClanBoards.WishPromoteText(4));
        Assert.Equal(15701, ClanBoards.ClassLineText(5));
    }
}
