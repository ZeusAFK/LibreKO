using FluentAssertions;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class SpecialAuctionScheduleTests
{
    private static DateTime At(int hour, int minute, int day = 10) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void BiddingRunsUntilFiveToEleven()
    {
        var moment = SpecialAuctionSchedule.At(At(20, 55));

        moment.Phase.Should().Be(AuctionPhase.Open);
        moment.SecondsLeft.Should().Be(2 * 3600);
        moment.Serial.Should().Be(SpecialAuctionSchedule.SerialOf(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void TheLastFiveMinutesBeforeElevenSettleTheDay()
    {
        var moment = SpecialAuctionSchedule.At(At(22, 57));

        moment.Phase.Should().Be(AuctionPhase.Settlement);
        moment.Serial.Should().Be(SpecialAuctionSchedule.SerialOf(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void AfterElevenOnlyRefundsAndClaimsRemain()
    {
        var moment = SpecialAuctionSchedule.At(At(23, 2));

        moment.Phase.Should().Be(AuctionPhase.Preparation);
        moment.Serial.Should().Be(SpecialAuctionSchedule.SerialOf(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void TheNextDaysAuctionOpensAtFivePastEleven()
    {
        var moment = SpecialAuctionSchedule.At(At(23, 5));

        moment.Phase.Should().Be(AuctionPhase.Open);
        moment.Serial.Should().Be(SpecialAuctionSchedule.SerialOf(new DateOnly(2026, 10, 11)));
        moment.SecondsLeft.Should().Be(23 * 3600 + 50 * 60);
    }

    [Fact]
    public void TheDayCyclesThroughThirteenLots()
    {
        int serial = SpecialAuctionSchedule.SerialOf(new DateOnly(2026, 10, 10));

        SpecialAuctionSchedule.DayOf(serial).Should().BeInRange(1, 13);
        SpecialAuctionSchedule.DayOf(serial + 13).Should().Be(SpecialAuctionSchedule.DayOf(serial));
        SpecialAuctionSchedule.DayOf(serial + 1).Should().Be(SpecialAuctionSchedule.DayOf(serial) % 13 + 1);
    }

    [Fact]
    public void TheTableRowFollowsTheGroupAndDay()
    {
        SpecialAuctionSchedule.Row(group: 1, day: 5).Should().Be(5);
        SpecialAuctionSchedule.Row(group: 4, day: 2).Should().Be(44);
    }
}
