using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillPageTests
{
    private const int Guardian = 106;
    private const int Ranger = 207;
    private const int Druid = 212;
    private const int Kurian = 115;

    [Theory]
    [InlineData(1060, SkillPage.Basic)]
    [InlineData(2110, SkillPage.Basic)]
    [InlineData(1069, SkillPage.Basic)]
    [InlineData(4106, SkillPage.Basic)]
    [InlineData(9106, SkillPage.Basic)]
    [InlineData(4212, SkillPage.Basic)]
    [InlineData(1066, 6)]
    [InlineData(2086, 6)]
    [InlineData(1067, 7)]
    [InlineData(1068, 8)]
    public void PageOf_FollowsRetailBuckets(int tree, int expected) =>
        Assert.Equal(expected, SkillData.PageOf(tree));

    [Fact]
    public void RogueTabs_AreArcheryAssassinateSearch()
    {
        Assert.Equal("Basic Skill", SkillData.PageName(Ranger, SkillPage.Basic));
        Assert.Equal("Archery", SkillData.PageName(Ranger, 5));
        Assert.Equal("Assassinate", SkillData.PageName(Ranger, 6));
        Assert.Equal("Search", SkillData.PageName(Ranger, 7));
        Assert.Equal("Master", SkillData.PageName(Ranger, 8));
    }

    [Fact]
    public void OtherFamilies_UseTheirRetailNames()
    {
        Assert.Equal("Passion", SkillData.PageName(Guardian, 7));
        Assert.Equal("Spirit", SkillData.PageName(Druid, 7));
        Assert.Equal("Devil", SkillData.PageName(Kurian, 7));
        Assert.Equal("Master", SkillData.PageName(Guardian, 8));
    }

    [Fact]
    public void EveryClass_NamesItsPagesDistinctly()
    {
        for (int nation = 1; nation <= 2; nation++)
        for (int local = 1; local <= 15; local++)
        {
            int cls = nation * 100 + local;
            var seen = new HashSet<string>();
            foreach (int page in SkillPage.Order)
                Assert.True(seen.Add(SkillData.PageName(cls, page)),
                    $"class {cls} reuses tab name '{SkillData.PageName(cls, page)}'");
        }
    }

    [Fact]
    public void NationBuffTrees_AreNotMasteryGated()
    {
        Assert.Equal(0, SkillData.MasteryType(4106));
        Assert.Equal(0, SkillData.MasteryType(9106));
        Assert.Equal(0, SkillData.MasteryType(1060));
        Assert.Equal(0, SkillData.MasteryType(1069));
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
            Assert.Equal(tree, SkillData.MasteryType(Guardian * 10 + tree));
    }

    [Fact]
    public void PageOrder_IsBasicThenTheFourMasteries()
    {
        Assert.Equal(new[] { SkillPage.Basic, 5, 6, 7, 8 }, SkillPage.Order);
        Assert.Equal(8, SkillPage.SlotsPerPage);
    }

    [Theory]
    [InlineData(SkillPage.DaggerWeapon, "Dagger, Jamadar")]
    [InlineData(SkillPage.BowWeapon, "Bow")]
    [InlineData(SkillPage.StaffWeapon, "Staff")]
    [InlineData(SkillPage.NoWeapon, "")]
    [InlineData(SkillPage.AnyWeapon, "")]
    [InlineData(SkillPage.EmoteWeapon, "")]
    public void WeaponRequirement_MatchesRetailWording(int code, string expected) =>
        Assert.Equal(expected, SkillData.WeaponRequirementName(code));
}
