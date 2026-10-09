using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PresetPlanTests
{
    private const int CreationBonus = 10;

    [Theory]
    [InlineData(101, 65, 65, 60, 50, 50)]
    [InlineData(206, 65, 65, 60, 50, 50)]
    [InlineData(107, 60, 60, 70, 50, 50)]
    [InlineData(202, 60, 60, 70, 50, 50)]
    [InlineData(110, 50, 50, 70, 70, 50)]
    [InlineData(203, 50, 50, 70, 70, 50)]
    [InlineData(111, 50, 60, 60, 70, 50)]
    [InlineData(212, 50, 60, 60, 70, 50)]
    [InlineData(113, 65, 65, 60, 50, 50)]
    [InlineData(215, 65, 65, 60, 50, 50)]
    public void EveryClassTierKeepsItsFamilyRedistributionBase(int cls, int str, int sta, int dex, int intel, int mag)
    {
        var basis = StarterStats.BaseForClass(cls);
        Assert.Equal(new[] { str, sta, dex, intel, mag },
            Enumerable.Range(0, CharacterSheet.StatCount).Select(basis.StatAtRow));
    }

    [Theory]
    [InlineData(1, 101, 65, 65, 60, 50, 50)]
    [InlineData(2, 102, 60, 60, 70, 50, 50)]
    [InlineData(2, 104, 50, 60, 60, 70, 50)]
    [InlineData(3, 103, 50, 50, 70, 70, 50)]
    [InlineData(4, 103, 50, 50, 70, 70, 50)]
    [InlineData(4, 104, 50, 60, 60, 70, 50)]
    [InlineData(6, 113, 65, 65, 60, 50, 50)]
    [InlineData(11, 201, 65, 65, 60, 50, 50)]
    [InlineData(12, 201, 65, 65, 60, 50, 50)]
    [InlineData(12, 202, 60, 60, 70, 50, 50)]
    [InlineData(12, 203, 50, 50, 70, 70, 50)]
    [InlineData(12, 204, 50, 60, 60, 70, 50)]
    [InlineData(13, 201, 65, 65, 60, 50, 50)]
    [InlineData(13, 202, 60, 60, 70, 50, 50)]
    [InlineData(13, 203, 50, 50, 70, 70, 50)]
    [InlineData(13, 204, 50, 60, 60, 70, 50)]
    [InlineData(14, 213, 65, 65, 60, 50, 50)]
    public void EveryCreationRollKeepsItsStats(int race, int cls, int str, int sta, int dex, int intel, int mag)
    {
        Assert.Equal(new StarterStats.Roll(str, sta, dex, intel, mag, CreationBonus), StarterStats.For(race, cls));
    }

    [Theory]
    [InlineData(Nations.Karus)]
    [InlineData(Nations.ElMorad)]
    public void EveryAllowedRaceAndClassHasACreationRoll(int nation)
    {
        foreach (int race in StarterStats.RacesFor(nation))
            foreach (int cls in StarterStats.ClassesFor(race))
                Assert.NotNull(StarterStats.For(race, cls));
    }

    [Fact]
    public void SavedAllocationsDisplayAndSendTotalsWithoutChangingTheirMeaning()
    {
        var plan = new PresetPlan();
        int[] stored = [185, 55, 12, 0, 0];
        plan.SetStats(stored);
        Assert.Equal(new[] { 250, 120, 72, 50, 50 }, Enumerable.Range(0, CharacterSheet.StatCount).Select(i => plan.StatValue(206, i)));
        Assert.True(plan.TryStatValues(206, 302, out var request, out int remaining));
        Assert.Equal(new[] { 250, 120, 72, 50, 50 }, request);
        Assert.Equal(50, remaining);
        Assert.Equal(stored, plan.Stats);
    }

    [Fact]
    public void EmptyPlanMeansClassBaseWithAllPointsRemaining()
    {
        var plan = new PresetPlan();
        Assert.True(plan.TryStatValues(210, 302, out var request, out int remaining));
        Assert.Equal(new[] { 50, 50, 70, 70, 50 }, request);
        Assert.Equal(302, remaining);
    }

    [Fact]
    public void PlanningBeforeRedistributionCanReachTheCapFromTheActualBase()
    {
        var plan = new PresetPlan();
        Assert.Equal(205, plan.StatBudget(212, 0, 302));
        plan.Stats[0] = 205;
        Assert.Equal(CharacterSheet.StatMax, plan.StatValue(212, 0));
        Assert.True(plan.TryStatValues(212, 302, out _, out int remaining));
        Assert.Equal(97, remaining);
        plan.Stats[0]++;
        Assert.False(plan.TryStatValues(212, 302, out _, out _));
    }

    [Fact]
    public void OtherStatsShareTheSameAvailablePool()
    {
        var plan = new PresetPlan();
        plan.Stats[0] = 190;
        plan.Stats[1] = 100;
        Assert.Equal(12, plan.StatBudget(206, 2, 302));
        plan.Stats[2] = 13;
        Assert.False(plan.TryStatValues(206, 302, out _, out _));
    }

    [Fact]
    public void AnOverspentPoolLeavesNoBudget()
    {
        var plan = new PresetPlan();
        plan.Stats[0] = 400;
        Assert.Equal(0, plan.StatBudget(206, 1, 302));
    }

    [Fact]
    public void MatchingTotalDoesNotAllowTheWrongClassBaseToBeApplied()
    {
        var sheet = new CharacterSheet();
        sheet.SeedStats(70, 60, 60, 50, 50, 302);
        Assert.False(PresetPlan.IsRedistributed(206, sheet));
        sheet.SeedStats(65, 65, 60, 50, 50, 302);
        Assert.True(PresetPlan.IsRedistributed(206, sheet));
    }

    [Theory]
    [InlineData(-1, 302)]
    [InlineData(191, 302)]
    [InlineData(20, 10)]
    [InlineData(int.MaxValue, 302)]
    [InlineData(0, -1)]
    public void InvalidStoredAllocationsCannotProduceAStatRequest(int strengthAllocation, int points)
    {
        var plan = new PresetPlan();
        plan.Stats[0] = strengthAllocation;
        Assert.False(plan.TryStatValues(106, points, out _, out _));
    }
}
