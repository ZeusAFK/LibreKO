using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class MaestroPotionsTests
{
    [Theory]
    [InlineData(MaestroPotions.Health, 100_000, true)]
    [InlineData(MaestroPotions.Mana, MaestroPotions.MinimumCoins, true)]
    [InlineData(MaestroPotions.Health, MaestroPotions.MinimumCoins - 1, false)]
    [InlineData(MaestroPotions.Mana, 99_999, false)]
    [InlineData(MaestroPotions.Health, 0, false)]
    [InlineData(389010000, 0, true)]
    public void AMaestroPotionNeedsOneHundredThousandNoah(int itemId, int gold, bool usable) =>
        Assert.Equal(usable, MaestroPotions.CanUse(itemId, gold));
}
