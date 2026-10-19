using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class UserVoteTests
{
    [Fact]
    public void TheAskCarriesTargetReasonAndTheOtherText()
    {
        var p = Reply(PartySubOpcode.UserVote);
        p.WriteShort(1);
        p.WriteString("Rikka");
        p.WriteByte(UserVote.ReasonOther);
        p.WriteString("afk at the bridge");
        p.ResetOffset();
        p.ReadByte();

        var (result, ask) = UserVoteReply.ReadStart(p);

        Assert.Equal(1, result);
        Assert.Equal(new UserVoteAsk("Rikka", UserVote.ReasonOther, "afk at the bridge"), ask);
    }

    [Fact]
    public void ARefusalCarriesOnlyItsCode()
    {
        var p = Reply(PartySubOpcode.UserVote);
        p.WriteShort(-4);
        p.ResetOffset();
        p.ReadByte();

        var (result, ask) = UserVoteReply.ReadStart(p);

        Assert.Equal(-4, result);
        Assert.Null(ask);
        Assert.Equal(43783, UserVote.RefusalText(result));
    }

    [Fact]
    public void TheWarnedPlayerGetsTheCountAndLimit()
    {
        var p = Reply(PartySubOpcode.UserVoteResult);
        p.WriteShort(1);
        p.WriteByte(2);
        p.WriteShort(2);
        p.WriteShort(3);
        p.ResetOffset();
        p.ReadByte();

        Assert.Equal(new UserVoteOutcome(true, 2, 3, true), UserVoteReply.ReadResult(p));
    }

    [Fact]
    public void AVoidedVoteIsNotAWarning()
    {
        var p = Reply(PartySubOpcode.UserVoteResult);
        p.WriteShort(0);
        p.WriteByte(0);
        p.ResetOffset();
        p.ReadByte();

        Assert.False(UserVoteReply.ReadResult(p).Warned);
    }

    [Fact]
    public void TheOtherReasonFitsSixtyBytes()
    {
        Assert.True(UserVote.TextFits(new string('a', UserVote.OtherTextMaxBytes)));
        Assert.False(UserVote.TextFits(new string('a', UserVote.OtherTextMaxBytes + 1)));
        Assert.True(UserVote.TextFits(new string('é', UserVote.OtherTextMaxLetters)));
        Assert.False(UserVote.TextFits(new string('é', UserVote.OtherTextMaxLetters + 1)));
    }

    [Fact]
    public void TheCooldownCountsFiveMinutes()
    {
        Assert.Equal(200, UserVote.CooldownLeft(100, 200));
        Assert.Equal(0, UserVote.CooldownLeft(100, 500));
    }

    private static Packet Reply(PartySubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte((byte)sub);
        return p;
    }
}
