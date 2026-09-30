using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class RebirthPickTests
{
    [Fact]
    public void ARebirthPlacesExactlyTwoPoints()
    {
        var pick = new RebirthPick();
        Assert.Equal(2, pick.Remaining);
        Assert.False(pick.Complete);
        Assert.True(pick.Add(0));
        Assert.True(pick.Add(0));
        Assert.True(pick.Complete);
        Assert.False(pick.Add(1));
        Assert.Equal(new byte[] { 2, 0, 0, 0, 0 }, pick.Payload());
    }

    [Fact]
    public void APointCanBeTakenBackAndMoved()
    {
        var pick = new RebirthPick();
        pick.Add(3);
        pick.Add(4);
        Assert.False(pick.Remove(0));
        Assert.True(pick.Remove(3));
        Assert.Equal(1, pick.Remaining);
        Assert.True(pick.Add(1));
        Assert.Equal(new byte[] { 0, 1, 0, 0, 1 }, pick.Payload());
        pick.Clear();
        Assert.Equal(2, pick.Remaining);
    }

    [Fact]
    public void RowsOutsideTheFiveStatsAreRefused()
    {
        var pick = new RebirthPick();
        Assert.False(pick.Add(5));
        Assert.False(pick.Add(-1));
        Assert.Equal(0, pick.PickedAt(7));
    }

    [Fact]
    public void TheSheetShowsTheRebirthLevelAfterTheLevelAndFoldsThePointsIntoTheBonus()
    {
        var sheet = new CharacterSheet();
        sheet.SeedProgress(83, 0, 1000);
        sheet.SeedStatBonuses(10, 0, 0, 0, 0);
        sheet.SeedRebirth(0, 0, 0, 0, 0, 0);
        Assert.Equal("83", sheet.LevelLabel);

        sheet.ApplyRebirth(new byte[] { 1, 0, 1, 0, 0 });
        Assert.Equal("83/1", sheet.LevelLabel);
        Assert.Equal(1, sheet.RebirthBonusAtRow(0));
        Assert.Equal(1, sheet.RebirthBonusAtRow(2));
        Assert.Equal(11, sheet.StatBonusAtRow(0));
        Assert.Equal(1, sheet.StatBonusAtRow(2));
    }
}
