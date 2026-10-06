using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NationTreasuryTests
{
    [Theory]
    [InlineData(NationTreasury.KingView, Nations.Karus, 11399)]
    [InlineData(NationTreasury.KingView, Nations.ElMorad, 11400)]
    [InlineData(NationTreasury.CitizenView, Nations.Karus, 11403)]
    [InlineData(NationTreasury.CitizenView, Nations.ElMorad, 11404)]
    public void TheHeadingFollowsTheViewAndNation(short view, int nation, int text) =>
        Assert.Equal(text, NationTreasury.Heading(view, nation));

    [Fact]
    public void TheSceptreIsTheClientsKingsSceptre()
    {
        Assert.True(NationTreasury.HasSceptre([0, 910074000]));
        Assert.False(NationTreasury.HasSceptre([910074311]));
    }

    [Fact]
    public void TheTaxRateArrowsStayBetweenZeroAndFive()
    {
        Assert.Equal(5, NationTreasury.StepTaxRate(5, 1));
        Assert.Equal(0, NationTreasury.StepTaxRate(0, -1));
        Assert.Equal(3, NationTreasury.StepTaxRate(2, 1));
    }

    [Fact]
    public void CoinsAreGroupedInThousands() => Assert.Equal("845,300,000", NationTreasury.Coins(845_300_000));

    [Fact]
    public void TheIntroductionHoldsTwoHundredLetters()
    {
        Assert.Equal(0, NationTreasury.IntroRefusal(new string('a', 200)));
        Assert.Equal(11416, NationTreasury.IntroRefusal(new string('a', 201)));
        Assert.Equal("Welcome", NationTreasury.IntroOrWelcome("", "Welcome"));
        Assert.Equal("Ours", NationTreasury.IntroOrWelcome("Ours", "Welcome"));
    }
}
