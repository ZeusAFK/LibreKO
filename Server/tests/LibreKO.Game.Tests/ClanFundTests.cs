using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ClanFundTests : GameTestBase
{
    private const int Rounds = 20_000;
    private const short ClanId = 90;
    private const int PlayerId = 660;
    private const int Small = 10;

    [Fact]
    public void FundChangesArrivingTogetherAllCount()
    {
        var clan = Clan(fund: Rounds);

        Parallel.For(0, Rounds, _ =>
        {
            ClanRules.AddToFund(clan, 1);
            ClanRules.TrySpendFund(clan, 1).Should().BeTrue();
        });

        clan.ClanPointFund.Should().Be(Rounds);
    }

    [Fact]
    public void AFundNeitherPaysMoreThanItHoldsNorGoesBelowEmpty()
    {
        var clan = Clan(fund: Small);

        ClanRules.TrySpendFund(clan, Small + 1).Should().BeFalse();
        ClanRules.TrySpendFund(clan, -1).Should().BeFalse();
        clan.ClanPointFund.Should().Be(Small);
        ClanRules.AddToFund(clan, -(Small + 1));
        clan.ClanPointFund.Should().Be(0);
    }

    [Fact]
    public void DonationsSentTogetherSpendNoMoreThanTheBalanceAllows()
    {
        using var provider = Provider();
        var player = Player(provider);
        player.Loyalty = ClanRules.DonorKeepsNationalPoints + Rounds;
        var loyalty = provider.GetRequiredService<ILoyaltyService>();
        var accepted = 0;

        Parallel.For(0, 2 * Rounds, _ =>
        {
            if (loyalty.TryDonateToKnightsAsync(player, 1).GetAwaiter().GetResult())
                Interlocked.Increment(ref accepted);
        });

        accepted.Should().Be(Rounds);
        player.Loyalty.Should().Be(ClanRules.DonorKeepsNationalPoints);
        player.KnightsPoints.Should().Be(Rounds);
    }

    [Fact]
    public void ContributionsAndDonationsArrivingTogetherAllCount()
    {
        using var provider = Provider();
        var clan = Clan(fund: 0);
        provider.GetRequiredService<SessionManager>().Knights.AddClan(ClanId, clan);
        var player = Player(provider);
        player.KnightsId = ClanId;
        player.Loyalty = ClanRules.DonorKeepsNationalPoints + Rounds;
        var loyalty = provider.GetRequiredService<ILoyaltyService>();

        Parallel.For(0, Rounds, _ =>
        {
            loyalty.ChangeAsync(player, 1).GetAwaiter().GetResult();
            loyalty.TryDonateToKnightsAsync(player, 1).GetAwaiter().GetResult().Should().BeTrue();
        });

        player.KnightsPoints.Should().Be(2 * Rounds, "each gain gives its point to the clan and each donation gives one");
        player.Loyalty.Should().Be(ClanRules.DonorKeepsNationalPoints);
        clan.ClanPointFund.Should().Be(Rounds);
    }

    private static KnightsEntity Clan(int fund) => new()
    {
        Id = ClanId, Name = "Owls", Chief = "Hoot", Nation = (byte)AccountNation.Karus, Flag = (byte)ClanType.Accredited5,
        ClanPointMethod = ClanRules.AutomaticAccumulation, Members = 1, ClanPointFund = fund,
    };

    private static ServiceProvider Provider() => CreateProvider(_ => { },
        configureServices: services => services.AddSingleton(Substitute.For<IAchievementProgressService>()));

    private static UserSession Player(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, PlayerId, PlayerId);
        session.Name = "Donor";
        session.Nation = AccountNation.Karus;
        return session;
    }
}
