using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class QuestClassRewardTests
{
    private const int KarusMage = 103;
    private const int ElmoradMasterMage = 210;
    private const int KarusWarrior = 101;
    private const int KarusKurian = 113;

    private static readonly QuestData.ItemStack[] WarriorBlades = [new(972010790, 1), new(972050433, 1)];
    private static readonly QuestData.ItemStack[] MageStaffs = [new(972030357, 1), new(972030365, 1)];

    private static readonly QuestData.ClassReward[] ShadowSeekerHunt =
    [
        new(1, WarriorBlades),
        new(3, MageStaffs),
    ];

    [Fact]
    public void AMageIsShownTheMageRewardsNotTheFirstClassRow()
    {
        Assert.Equal(MageStaffs, QuestData.RewardsForClass(ShadowSeekerHunt, WarriorBlades, KarusMage));
        Assert.Equal(MageStaffs, QuestData.RewardsForClass(ShadowSeekerHunt, WarriorBlades, ElmoradMasterMage));
        Assert.Equal(WarriorBlades, QuestData.RewardsForClass(ShadowSeekerHunt, MageStaffs, KarusWarrior));
    }

    [Fact]
    public void AQuestWithOneRewardRowShowsItToEveryone()
    {
        Assert.Equal(WarriorBlades, QuestData.RewardsForClass([], WarriorBlades, KarusMage));
    }

    [Fact]
    public void AKurianTakesTheWarriorRowAsTheRetailScriptsDo()
    {
        Assert.Equal(WarriorBlades, QuestData.RewardsForClass(ShadowSeekerHunt, MageStaffs, KarusKurian));
    }

    [Fact]
    public void AQuestWithAHelperPerClassIsOfferedToEachOfThem()
    {
        int[] everyClass = [1, 2, 3, 4];
        Assert.True(QuestData.OfferedToClass(everyClass, 1, KarusMage));
        Assert.False(QuestData.OfferedToClass([1], 1, KarusMage));
        Assert.True(QuestData.OfferedToClass([], 5, KarusMage));
    }
}
