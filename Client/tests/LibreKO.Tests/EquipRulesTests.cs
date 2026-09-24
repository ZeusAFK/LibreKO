using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class EquipRulesTests
{
    private static readonly EquipStats MageMaster = new(Class: 110, Race: 12, Level: 60, Str: 50, Sta: 50, Dex: 50, Intel: 150, Cha: 60);

    [Theory]
    [InlineData(110, 6, false)]
    [InlineData(110, 10, true)]
    [InlineData(109, 10, false)]
    [InlineData(103, 3, true)]
    [InlineData(209, 3, true)]
    [InlineData(206, 6, true)]
    [InlineData(205, 6, false)]
    [InlineData(205, 1, true)]
    [InlineData(213, 1, true)]
    [InlineData(215, 6, true)]
    [InlineData(214, 5, true)]
    [InlineData(106, 15, false)]
    [InlineData(208, 12, false)]
    [InlineData(110, 21, true)]
    [InlineData(109, 21, false)]
    [InlineData(112, 0, true)]
    [InlineData(112, 255, true)]
    public void TheItemClassFollowsTheClientsClassTable(int playerClass, int itemClass, bool expected)
    {
        Assert.Equal(expected, EquipRules.ClassAllows(playerClass, itemClass));
    }

    [Theory]
    [InlineData(110, 70, true)]
    [InlineData(108, 70, false)]
    [InlineData(106, 230, true)]
    [InlineData(110, 230, false)]
    [InlineData(215, 11, true)]
    [InlineData(215, 210, false)]
    public void EachClassIsBarredFromSomeItemKinds(int playerClass, int kind, bool expected)
    {
        Assert.Equal(expected, EquipRules.ForbidsKind(playerClass, kind));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20, true)]
    [InlineData(73, true)]
    [InlineData(72, false)]
    [InlineData(150, true)]
    [InlineData(1, false)]
    [InlineData(12, true)]
    public void AnItemRaceIsOpenOrMustMatch(int itemRace, bool expected)
    {
        Assert.Equal(expected, EquipRules.RaceAllows(itemRace, MageMaster.Race));
    }

    [Theory]
    [InlineData(1, 1304)]
    [InlineData(6, 1309)]
    [InlineData(10, 1313)]
    [InlineData(12, 1315)]
    [InlineData(13, 1427)]
    [InlineData(15, 1429)]
    [InlineData(21, 1316)]
    [InlineData(25, 7807)]
    [InlineData(255, 1430)]
    [InlineData(40, 0)]
    public void TheClassLineNamesTheItemClassWithTheClientsText(int itemClass, int textId)
    {
        Assert.Equal(textId, EquipRules.ClassNameTextId(itemClass));
    }

    [Fact]
    public void AMageCannotEquipAWarriorItem()
    {
        Assert.Equal(EquipRefusal.Class, EquipRules.Check(MageMaster, new ItemData.Item { Class = 6 }));
    }

    [Fact]
    public void AMageCannotEquipABowEvenWithoutAClass()
    {
        Assert.Equal(EquipRefusal.Class, EquipRules.Check(MageMaster, new ItemData.Item { Class = 0, Kind = 70 }));
    }

    [Fact]
    public void TheRaceIsCheckedBeforeTheClass()
    {
        Assert.Equal(EquipRefusal.Race, EquipRules.Check(MageMaster, new ItemData.Item { Race = 1, Class = 6 }));
    }

    [Theory]
    [InlineData(61, 0, 0, 0, EquipRefusal.LevelTooLow)]
    [InlineData(0, 50, 0, 0, EquipRefusal.LevelTooHigh)]
    [InlineData(0, 0, 51, 0, EquipRefusal.Strength)]
    [InlineData(0, 0, 0, 151, EquipRefusal.Intelligence)]
    [InlineData(60, 83, 50, 150, EquipRefusal.None)]
    public void TheLevelRangeAndStatsMustBeMet(int reqLevel, int reqLevelMax, int reqStr, int reqInt, EquipRefusal expected)
    {
        var staff = new ItemData.Item
        {
            Class = 10, ReqLevel = reqLevel, ReqLevelMax = reqLevelMax, ReqStr = reqStr, ReqInt = reqInt,
        };
        Assert.Equal(expected, EquipRules.Check(MageMaster, staff));
    }
}
