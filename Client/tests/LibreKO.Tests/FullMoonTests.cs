using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class FullMoonTests
{
    [Theory]
    [InlineData(1, 40070)]
    [InlineData(2, 37310)]
    [InlineData(4, 37312)]
    [InlineData(6, 37314)]
    [InlineData(7, 37315)]
    [InlineData(9, 37318)]
    [InlineData(10, 37317)]
    [InlineData(12, 37319)]
    [InlineData(99, 16506)]
    public void EntryCodesUseThe2619Texts(int code, int text) => Assert.Equal(text, FullMoon.EntryText(code).Id);

    [Fact]
    public void TowerAndStageLinesSwitchOnTheLastOne()
    {
        Assert.Equal(37301, FullMoon.TowerText(2).Id);
        Assert.Equal(37302, FullMoon.TowerText(0).Id);
        Assert.Equal(37303, FullMoon.StageLine(5, 2).Id);
        Assert.Equal(new[] { 2, 3 }, FullMoon.StageLine(5, 2).Args);
        Assert.Equal(37304, FullMoon.StageLine(5, 5).Id);
    }

    [Fact]
    public void TheBoardRefusesAnotherPartyTypeOrAnotherOpenBoard()
    {
        Assert.Equal(37306, FullMoon.OpenRefusal(true, 3, PartyBoardType.Normal, null)?.Id);
        Assert.Equal(37307, FullMoon.OpenRefusal(false, 0, PartyBoardType.Normal, PartyBoardType.Normal)?.Id);
        Assert.Null(FullMoon.OpenRefusal(true, 3, PartyBoardType.FullMoon, PartyBoardType.FullMoon));
        Assert.Null(FullMoon.OpenRefusal(false, 0, PartyBoardType.Normal, null));
    }
}
