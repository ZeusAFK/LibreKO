using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Common.Tests;

public class ClanDonationCalculatorTests
{
    [Theory]
    [InlineData(ClanType.Accredited4, 7_000, 252_000)]
    [InlineData(ClanType.Accredited3, 10_000, 360_000)]
    [InlineData(ClanType.Accredited2, 15_000, 540_000)]
    [InlineData(ClanType.Accredited1, 20_000, 720_000)]
    [InlineData(ClanType.Royal5, 25_000, 900_000)]
    [InlineData(ClanType.Royal4, 30_000, 1_080_000)]
    [InlineData(ClanType.Royal3, 35_000, 1_260_000)]
    [InlineData(ClanType.Royal2, 40_000, 1_440_000)]
    [InlineData(ClanType.Royal1, 45_000, 1_620_000)]
    public void EachPaidGradeCostsThirtySixNationalPointsPerClanPoint(
        ClanType grade, int clanPoints, int nationalPoints)
    {
        ClanDonationCalculator.ClanPointsFor(grade).Should().Be(clanPoints);
        ClanDonationCalculator.NationalPointsFor(grade).Should().Be(nationalPoints);
    }

    [Theory]
    [InlineData(ClanType.Training)]
    [InlineData(ClanType.Promoted)]
    [InlineData(ClanType.Accredited5)]
    public void TheFreeGradesCostNothing(ClanType grade)
    {
        ClanDonationCalculator.NationalPointsFor(grade).Should().Be(0);
    }

    [Fact]
    public void AReachableGradeOneCostsTheAdvertisedTotal()
    {
        var toAccredited1 = new[]
        {
            ClanType.Accredited4, ClanType.Accredited3, ClanType.Accredited2, ClanType.Accredited1,
        };
        toAccredited1.Sum(ClanDonationCalculator.ClanPointsFor).Should().Be(52_000);
        toAccredited1.Sum(ClanDonationCalculator.NationalPointsFor).Should().Be(1_872_000);

        var everyPaidGrade = Enum.GetValues<ClanType>().Where(g => g >= ClanType.Accredited4);
        everyPaidGrade.Sum(ClanDonationCalculator.ClanPointsFor).Should().Be(227_000);
    }

    [Fact]
    public void ALeaverGetsBackThirtyPercent()
    {
        ClanDonationCalculator.RefundFor(1_000, keepsEverything: false).Should().Be(300);
        ClanDonationCalculator.RefundFor(999, keepsEverything: false).Should().Be(299);
        ClanDonationCalculator.RefundFor(0, keepsEverything: false).Should().Be(0);
    }

    [Fact]
    public void ARecoveryItemGivesBackEverything()
    {
        ClanDonationCalculator.RefundFor(1_000, keepsEverything: true).Should().Be(1_000);
    }

    [Fact]
    public void AFundThatCoversTheDonationJustPaysIt()
    {
        ClanDonationCalculator.WithdrawDonation(ClanType.Royal1, 500_000, 200_000)
            .Should().Be((ClanType.Royal1, 300_000));
    }

    [Fact]
    public void AnUnpaidGradeNeverDemotes()
    {
        ClanDonationCalculator.WithdrawDonation(ClanType.Training, 100, 400)
            .Should().Be((ClanType.Training, 0));
        ClanDonationCalculator.WithdrawDonation(ClanType.Promoted, 900, 400)
            .Should().Be((ClanType.Promoted, 500));
    }

    [Fact]
    public void AShortFundDemotesOneGradeAndCreditsItsPrice()
    {
        ClanDonationCalculator.WithdrawDonation(ClanType.Accredited4, 0, 252_000)
            .Should().Be((ClanType.Accredited5, 0));
        ClanDonationCalculator.WithdrawDonation(ClanType.Accredited4, 0, 200_000)
            .Should().Be((ClanType.Accredited5, 52_000));
    }

    [Fact]
    public void ALargeDonationCascadesDownSeveralGrades()
    {
        var (grade, fund) = ClanDonationCalculator.WithdrawDonation(
            ClanType.Royal5, 0, 900_000 + 720_000 + 540_000);

        grade.Should().Be(ClanType.Accredited3);
        fund.Should().Be(0);

        ClanDonationCalculator.WithdrawDonation(ClanType.Royal5, 0, 900_000 + 720_000 + 100_000)
            .Should().Be((ClanType.Accredited3, 440_000));
    }

    [Fact]
    public void TheCascadeStopsAtTheLowestPaidGrade()
    {
        ClanDonationCalculator.WithdrawDonation(ClanType.Accredited5, 10_000, 999_999_999)
            .Should().Be((ClanType.Accredited5, 0));

        var (grade, fund) = ClanDonationCalculator.WithdrawDonation(
            ClanType.Royal1, 0, int.MaxValue);
        grade.Should().Be(ClanType.Accredited5);
        fund.Should().Be(0);
    }

    [Fact]
    public void NothingDonatedChangesNothing()
    {
        ClanDonationCalculator.WithdrawDonation(ClanType.Royal3, 1_234, 0)
            .Should().Be((ClanType.Royal3, 1_234));
    }
}
