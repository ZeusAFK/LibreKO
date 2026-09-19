using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillItemTests
{
    private const int MagicShieldScroll = 379064000;
    private const int JudgmentScroll = 379066000;
    private const int StoneOfRogue = 379060000;
    private const int StoneOfPriest = 379062000;
    private const int Arrow = 391010000;

    [Theory]
    [InlineData(2, MagicShieldScroll, StoneOfRogue)]
    [InlineData(2, JudgmentScroll, StoneOfPriest)]
    [InlineData(35, MagicShieldScroll, MagicShieldScroll)]
    [InlineData(2, StoneOfRogue, StoneOfRogue)]
    [InlineData(62, Arrow, Arrow)]
    [InlineData(2, 0, 0)]
    public void AMasterScrollSkillConsumesItsClassStoneAndNothingElseChanges(int level, int useItem, int expected)
    {
        Assert.Equal(expected, SkillData.ConsumedItemFor(level, useItem));
        Assert.Equal(expected != useItem, SkillData.IsMasterScrollSkill(level, useItem));
    }
}
