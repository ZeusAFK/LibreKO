using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class CharacterSheetTests
{
    private static CharacterSheet WithGold(int gold)
    {
        var s = new CharacterSheet();
        s.SeedWealth(gold, np: 0);
        return s;
    }

    [Fact]
    public void Spend_FloorsAtZero_RatherThanGoingNegative()
    {
        var s = WithGold(100);
        s.Spend(250);
        Assert.Equal(0, s.Gold);
    }

    [Fact]
    public void Spend_DeductsNormally()
    {
        var s = WithGold(1_000);
        s.Spend(250);
        Assert.Equal(750, s.Gold);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void Spend_IgnoresNonPositive_SoABadCountCannotCreditThePlayer(long amount)
    {
        var s = WithGold(100);
        s.Spend(amount);
        Assert.Equal(100, s.Gold);
    }

    [Fact]
    public void Receive_ClampsAtIntMaxValue_RatherThanOverflowingNegative()
    {
        var s = WithGold(int.MaxValue - 10);
        s.Receive(1_000);
        Assert.Equal(int.MaxValue, s.Gold);
    }

    [Fact]
    public void Receive_CreditsNormally()
    {
        var s = WithGold(500);
        s.Receive(250);
        Assert.Equal(750, s.Gold);
    }

    [Fact]
    public void Receive_AcceptsLongAmounts_AsMerchantPayoutsProduce()
    {
        var s = WithGold(0);
        s.Receive(5_000_000_000L);          // price * count can exceed int
        Assert.Equal(int.MaxValue, s.Gold);
    }

    [Fact]
    public void SetGold_IsAuthoritative_AndNeverNegative()
    {
        var s = WithGold(10);
        s.SetGold(4_242);
        Assert.Equal(4_242, s.Gold);
        s.SetGold(-1);
        Assert.Equal(0, s.Gold);
    }

    [Fact]
    public void ApplyPointChange_SetsTheStat_SpendsAPoint_AndTakesNewAp()
    {
        var s = new CharacterSheet();
        s.SeedStats(str: 60, sta: 70, dex: 80, intel: 50, mag: 50, points: 3);

        Assert.True(s.ApplyPointChange(CharacterSheet.TypeDex, newValue: 81, totalHit: 412));

        Assert.Equal(81, s.Dex);
        Assert.Equal(2, s.Points);
        Assert.Equal(412, s.Ap);
    }

    [Fact]
    public void ApplyPointChange_RejectsUnknownType_AndChangesNothing()
    {
        var s = new CharacterSheet();
        s.SeedStats(60, 70, 80, 50, 50, points: 3);
        s.SeedCombat(ap: 100, ac: 200);

        Assert.False(s.ApplyPointChange(wireType: 99, newValue: 999, totalHit: 777));

        Assert.Equal(3, s.Points);
        Assert.Equal(100, s.Ap);
        Assert.Equal(60, s.Str);
    }

    [Fact]
    public void ApplyPointChange_PointsNeverGoNegative()
    {
        var s = new CharacterSheet();
        s.SeedStats(60, 70, 80, 50, 50, points: 0);

        s.ApplyPointChange(CharacterSheet.TypeStr, 61, totalHit: 0);

        Assert.Equal(0, s.Points);
    }

    [Fact]
    public void CanAllocate_TracksUnspentPoints()
    {
        var s = new CharacterSheet();
        s.SeedStats(60, 70, 80, 50, 50, points: 1);
        Assert.True(s.CanAllocate);

        s.ApplyPointChange(CharacterSheet.TypeStr, 61, totalHit: 0);
        Assert.False(s.CanAllocate);
    }

    [Fact]
    public void ApplyReset_WithAllFiveStats_AppliesThem()
    {
        var s = new CharacterSheet();
        s.SeedStats(99, 99, 99, 99, 99, points: 0);

        Assert.True(s.ApplyReset(new[] { 60, 61, 62, 63, 64 }, points: 40, ap: 300));

        Assert.Equal(60, s.Str);
        Assert.Equal(64, s.Mag);
        Assert.Equal(40, s.Points);
        Assert.Equal(300, s.Ap);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 })]
    [InlineData(new int[0])]
    public void ApplyReset_WithAShortArray_LeavesStatsAlone_ButStillAppliesPointsAndAp(int[] stats)
    {
        var s = new CharacterSheet();
        s.SeedStats(99, 98, 97, 96, 95, points: 0);

        Assert.False(s.ApplyReset(stats, points: 40, ap: 300));

        Assert.Equal(99, s.Str);
        Assert.Equal(95, s.Mag);
        Assert.Equal(40, s.Points);
        Assert.Equal(300, s.Ap);
    }

    [Fact]
    public void ApplyReset_ToleratesNull()
    {
        var s = new CharacterSheet();
        s.SeedStats(99, 98, 97, 96, 95, points: 0);

        Assert.False(s.ApplyReset(null, points: 7, ap: 1));

        Assert.Equal(99, s.Str);
        Assert.Equal(7, s.Points);
    }

    [Fact]
    public void ApplyLevel_ReportsALevelUp()
    {
        var s = new CharacterSheet();
        s.SeedProgress(level: 60, exp: 0, maxExp: 100);

        Assert.True(s.ApplyLevel(level: 61, points: 3, exp: 10, maxExp: 200));

        Assert.Equal(61, s.Level);
        Assert.Equal(3, s.Points);
    }

    [Theory]
    [InlineData(60)]    // same level — arrives on zone re-entry
    [InlineData(1)]     // lower level — arrives on rebirth
    public void ApplyLevel_StaysSilentWhenTheLevelDidNotRise(int level)
    {
        var s = new CharacterSheet();
        s.SeedProgress(level: 60, exp: 0, maxExp: 100);

        Assert.False(s.ApplyLevel(level, points: 0, exp: 0, maxExp: 100));

        Assert.Equal(level, s.Level);
    }

    [Fact]
    public void ExpPercent_IsZeroBeforeTheServerSendsACap_NotADivideByZero()
    {
        var s = new CharacterSheet();
        s.SeedProgress(level: 1, exp: 500, maxExp: 0);

        Assert.Equal(0.0, s.ExpPercent);
    }

    [Fact]
    public void ExpPercent_IsAPercentage()
    {
        var s = new CharacterSheet();
        s.SeedProgress(level: 1, exp: 250, maxExp: 1_000);

        Assert.Equal(25.0, s.ExpPercent, precision: 6);
    }

    [Fact]
    public void ApplyDerived_MapsResistsInWireOrder()
    {
        var s = new CharacterSheet();
        s.ApplyDerived(new DerivedStats
        {
            TotalHit = 412, TotalAc = 286,
            FireR = 1, ColdR = 2, LightningR = 3, MagicR = 4, DiseaseR = 5, PoisonR = 6,
        });

        Assert.Equal(412, s.Ap);
        Assert.Equal(286, s.Ac);
        Assert.Equal(1, s.ResistAt(0));
        Assert.Equal(2, s.ResistAt(1));
        Assert.Equal(3, s.ResistAt(2));
        Assert.Equal(4, s.ResistAt(3));
        Assert.Equal(5, s.ResistAt(4));
        Assert.Equal(6, s.ResistAt(5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(CharacterSheet.ResistCount)]
    public void ResistAt_OutOfRange_ReadsZero_BecauseCallersAreUiLoops(int index)
    {
        var s = new CharacterSheet();
        s.SeedResists(1, 2, 3, 4, 5, 6);

        Assert.Equal(0, s.ResistAt(index));
    }

    [Theory]
    [InlineData(0, CharacterSheet.TypeStr)]
    [InlineData(1, CharacterSheet.TypeSta)]
    [InlineData(2, CharacterSheet.TypeDex)]
    [InlineData(3, CharacterSheet.TypeInt)]
    [InlineData(4, CharacterSheet.TypeMag)]
    public void WireTypeForRow_MatchesTheServerStatCodes(int row, int expected) =>
        Assert.Equal(expected, CharacterSheet.WireTypeForRow(row));

    [Theory]
    [InlineData(-1)]
    [InlineData(CharacterSheet.StatCount)]
    public void WireTypeForRow_OutOfRange_IsNotAStat(int row) =>
        Assert.Equal(0, CharacterSheet.WireTypeForRow(row));

    [Fact]
    public void StatAtRow_ReadsInTheSameOrderAsTheWireTypes()
    {
        var s = new CharacterSheet();
        s.SeedStats(str: 10, sta: 20, dex: 30, intel: 40, mag: 50, points: 0);

        Assert.Equal(10, s.StatAtRow(0));
        Assert.Equal(20, s.StatAtRow(1));
        Assert.Equal(30, s.StatAtRow(2));
        Assert.Equal(40, s.StatAtRow(3));
        Assert.Equal(50, s.StatAtRow(4));
        Assert.Equal(0, s.StatAtRow(5));
    }
}
