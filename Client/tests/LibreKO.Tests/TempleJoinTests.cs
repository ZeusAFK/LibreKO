using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class TempleJoinTests
{
    [Theory]
    [InlineData((byte)TempleJoinResult.AlreadyJoined, 17999)]
    [InlineData((byte)TempleJoinResult.NeedsMapOfChaos, 18801)]
    [InlineData((byte)TempleJoinResult.LadderPoints, 41005)]
    [InlineData((byte)TempleJoinResult.ApplyLadderPoints, 41005)]
    [InlineData((byte)TempleJoinResult.LevelTooLow, 44543)]
    [InlineData((byte)TempleJoinResult.DuplicateIp, 44422)]
    [InlineData((byte)TempleJoinResult.NotOpen, 44545)]
    [InlineData((byte)TempleJoinResult.Joined, 0)]
    [InlineData((byte)TempleJoinResult.LevelTooHigh, 0)]
    public void ResultCodesUseTheirTexts(byte result, int text) =>
        Assert.Equal(text, TempleJoin.TextId((TempleJoinResult)result));

    [Fact]
    public void BothJoinedAndAlreadyJoinedLeaveYouSignedUp()
    {
        Assert.True(TempleJoin.SignedUp(TempleJoinResult.Joined));
        Assert.True(TempleJoin.SignedUp(TempleJoinResult.AlreadyJoined));
        Assert.False(TempleJoin.SignedUp(TempleJoinResult.NotOpen));
    }

    [Fact]
    public void ChaosCountsEveryoneTogether()
    {
        Assert.Equal("Signed up: 5", TempleJoin.CountLine(true, 2, 3));
        Assert.Equal("Karus 2  ·  El Morad 3", TempleJoin.CountLine(false, 2, 3));
    }
}
