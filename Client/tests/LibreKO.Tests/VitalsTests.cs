using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class VitalsTests
{
    private static Vitals Seeded(int hp = 100, int maxHp = 100, int mp = 50, int maxMp = 50)
    {
        var v = new Vitals();
        v.Seed(hp, maxHp, mp, maxMp);
        return v;
    }

    [Fact]
    public void ApplyHp_OnTheFirstPacket_ReportsNoDamage_EvenAtPartHealth()
    {
        var v = new Vitals();                       // unseeded: MaxHp == 0
        Assert.False(v.Known);

        var change = v.ApplyHp(hp: 300, maxHp: 1089);

        Assert.Equal(0, change.Damage);
        Assert.Equal(0, change.Recovered);
        Assert.Equal(300, v.Hp);
        Assert.Equal(1089, v.MaxHp);
        Assert.True(v.Known);
    }

    [Fact]
    public void ApplyMp_OnTheFirstPacket_ReportsNoDelta()
    {
        var v = new Vitals();
        Assert.Equal(0, v.ApplyMp(mp: 200, maxMp: 1361));
        Assert.Equal(200, v.Mp);
    }

    [Fact]
    public void ApplyHp_ReportsDamage()
    {
        var v = Seeded(hp: 100, maxHp: 100);
        var change = v.ApplyHp(60, 100);

        Assert.Equal(40, change.Damage);
        Assert.Equal(0, change.Recovered);
        Assert.Equal(60, v.Hp);
    }

    [Fact]
    public void ApplyHp_ReportsHealing()
    {
        var v = Seeded(hp: 60, maxHp: 100);
        var change = v.ApplyHp(90, 100);

        Assert.Equal(0, change.Damage);
        Assert.Equal(30, change.Recovered);
    }

    [Fact]
    public void ApplyHp_UnchangedHp_ReportsNeither()
    {
        var v = Seeded(hp: 60, maxHp: 100);
        var change = v.ApplyHp(60, 100);

        Assert.Equal(0, change.Damage);
        Assert.Equal(0, change.Recovered);
    }

    [Fact]
    public void ApplyMp_SignsTheDelta()
    {
        var v = Seeded(mp: 50, maxMp: 100);
        Assert.Equal(-20, v.ApplyMp(30, 100));      // spent
        Assert.Equal(45, v.ApplyMp(75, 100));       // recovered
    }

    [Fact]
    public void ApplyMaxima_PullsCurrentDownToFitTheNewMaximum()
    {
        var v = Seeded(hp: 100, maxHp: 100, mp: 80, maxMp: 80);

        v.ApplyMaxima(maxHp: 70, maxMp: 60);

        Assert.Equal(70, v.Hp);
        Assert.Equal(60, v.Mp);
    }

    [Fact]
    public void ApplyMaxima_LeavesCurrentAloneWhenTheMaximumRises()
    {
        var v = Seeded(hp: 40, maxHp: 100, mp: 20, maxMp: 50);

        v.ApplyMaxima(maxHp: 200, maxMp: 100);

        Assert.Equal(40, v.Hp);
        Assert.Equal(20, v.Mp);
    }

    [Fact]
    public void ApplyMaxima_WithAZeroMaximum_DoesNotWipeCurrent()
    {
        var v = Seeded(hp: 40, maxHp: 100, mp: 20, maxMp: 50);

        v.ApplyMaxima(maxHp: 0, maxMp: 0);

        Assert.Equal(40, v.Hp);
        Assert.Equal(20, v.Mp);
    }

    [Fact]
    public void RestHp_HealsATwentyFourthOfTheMaximum()
    {
        var v = Seeded(hp: 0, maxHp: 2_400);
        Assert.Equal(100, v.RestHp());
        Assert.Equal(100, v.Hp);
    }

    [Fact]
    public void RestHp_HealsAtLeastOne_SoLowLevelsStillProgress()
    {
        var v = Seeded(hp: 0, maxHp: 10);           // 10/24 == 0 in integer maths
        Assert.Equal(1, v.RestHp());
    }

    [Fact]
    public void RestHp_NeverExceedsTheMaximum()
    {
        var v = Seeded(hp: 2_399, maxHp: 2_400);
        Assert.Equal(1, v.RestHp());
        Assert.Equal(2_400, v.Hp);

        Assert.Equal(0, v.RestHp());                // already full
    }

    [Fact]
    public void Rest_IsANoOpBeforeTheServerSendsMaxima()
    {
        var v = new Vitals();
        Assert.Equal(0, v.RestHp());
        Assert.Equal(0, v.RestMp());
    }

    [Fact]
    public void RestMp_MirrorsRestHp()
    {
        var v = Seeded(mp: 0, maxMp: 240);
        Assert.Equal(10, v.RestMp());
    }

    [Theory]
    [InlineData(0, 10, true)]       // free skill
    [InlineData(-5, 10, true)]      // negative cost is free
    [InlineData(10, 10, true)]      // exactly affordable
    [InlineData(11, 10, false)]     // one short
    public void HasMana_GatesOnCost(int cost, int mp, bool expected)
    {
        var v = Seeded(mp: mp, maxMp: 100);
        Assert.Equal(expected, v.HasMana(cost));
    }

    [Theory]
    [InlineData(49, 100, true)]     // under half
    [InlineData(50, 100, false)]    // exactly half is not "below"
    [InlineData(100, 100, false)]
    public void BelowHalfHp_GatesTheTownRecallOffer(int hp, int maxHp, bool expected)
    {
        var v = Seeded(hp: hp, maxHp: maxHp);
        Assert.Equal(expected, v.BelowHalfHp);
    }

    [Fact]
    public void BelowHalfHp_IsFalseBeforeTheServerSendsAMaximum()
    {
        Assert.False(new Vitals().BelowHalfHp);
    }
}
