using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class LoyaltyAwardTests
{
    [Fact]
    public void MostZonesShareTheStandardAward()
    {
        var award = LoyaltyAwards.For((byte)ZoneId.RonarkLand);

        award.Killer.Should().Be(64);
        award.Victim.Should().Be(-50);
    }

    [Fact]
    public void AnEarlyBattleZoneAwardsLess()
    {
        var award = LoyaltyAwards.For((byte)ZoneId.Ardream);

        award.Killer.Should().Be(32);
        award.Victim.Should().Be(-25, "the low-level zone takes less from the loser too");
    }

    [Fact]
    public void KillingYourRivalPaysTheBonusAndTakesItFromThem()
    {
        var plain = LoyaltyAwards.For((byte)ZoneId.RonarkLand);
        var rival = LoyaltyAwards.WithRivalryBonus(plain, killerHadRival: true);

        rival.Killer.Should().Be(plain.Killer + LoyaltyAwards.RivalryBonus);
        rival.Victim.Should().Be(plain.Victim - LoyaltyAwards.RivalryBonus);
    }

    [Fact]
    public void WithoutARivalTheAwardIsUnchanged()
    {
        var plain = LoyaltyAwards.For((byte)ZoneId.RonarkLand);

        LoyaltyAwards.WithRivalryBonus(plain, killerHadRival: false).Should().Be(plain);
    }

    [Fact]
    public void ASoloKillerKeepsTheWholeAward()
    {
        var award = LoyaltyAwards.For((byte)ZoneId.RonarkLand);

        LoyaltyAwards.PerPartyMember(award, 1).Should().Be(award);
        LoyaltyAwards.PerPartyMember(award, 0).Should().Be(award);
    }

    [Fact]
    public void ASmallerPartyPaysEachMemberMore()
    {
        var award = LoyaltyAwards.For((byte)ZoneId.RonarkLand);

        var two = LoyaltyAwards.PerPartyMember(award, 2).Killer;
        var four = LoyaltyAwards.PerPartyMember(award, 4).Killer;
        var eight = LoyaltyAwards.PerPartyMember(award, 8).Killer;

        two.Should().BeGreaterThan(four);
        four.Should().BeGreaterThan(eight);
        two.Should().BeLessThan(award.Killer);
    }

    [Fact]
    public void TheVictimLosesTheSameWhoeverKilledThem()
    {
        var award = LoyaltyAwards.For((byte)ZoneId.RonarkLand);

        LoyaltyAwards.PerPartyMember(award, 6).Victim.Should().Be(award.Victim);
    }
}
