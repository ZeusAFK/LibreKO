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
    private const int Threads = 8;
    private const short ClanId = 90;
    private const int PlayerId = 660;
    private const int Small = 10;
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    [Fact]
    public void FundChangesArrivingTogetherAllCount()
    {
        var clan = Clan(fund: Rounds);

        Together(() =>
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
        player.KnightsId = ClanId;
        player.Loyalty = ClanRules.DonorKeepsNationalPoints + Rounds;
        var loyalty = provider.GetRequiredService<ILoyaltyService>();
        var accepted = 0;

        Together(() =>
        {
            for (var twice = 0; twice < 2; twice++)
                if (loyalty.TryDonateToKnightsAsync(player, ClanId, 1).GetAwaiter().GetResult())
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

        Together(() =>
        {
            loyalty.ChangeAsync(player, 1).GetAwaiter().GetResult();
            loyalty.TryDonateToKnightsAsync(player, ClanId, 1).GetAwaiter().GetResult().Should().BeTrue();
        });

        player.KnightsPoints.Should().Be(2 * Rounds, "each gain gives its point to the clan and each donation gives one");
        player.Loyalty.Should().Be(ClanRules.DonorKeepsNationalPoints);
        clan.ClanPointFund.Should().Be(Rounds);
    }

    [Fact]
    public async Task APlayerNoLongerInTheClanDonatesNothingToIt()
    {
        using var provider = Provider();
        var player = Player(provider);
        player.Loyalty = ClanRules.DonorKeepsNationalPoints + Rounds;

        (await provider.GetRequiredService<ILoyaltyService>().TryDonateToKnightsAsync(player, ClanId, 1)).Should().BeFalse(
            "a player put out of the clan as they donated gives it nothing");
        player.Loyalty.Should().Be(ClanRules.DonorKeepsNationalPoints + Rounds);
    }

    [Fact]
    public void AGainForAPlayerPutOutOfTheClanMeanwhileContributesNothingToIt()
    {
        using var provider = Provider();
        var clan = Clan(fund: 0);
        provider.GetRequiredService<SessionManager>().Knights.AddClan(ClanId, clan);
        var player = Player(provider);
        player.KnightsId = ClanId;
        var loyalty = provider.GetRequiredService<ILoyaltyService>();
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() => player.WithLock(s =>
        {
            holding.Set();
            release.Wait();
            s.KnightsId = 0;
        }));
        holder.Start();
        holding.Wait();
        var gainer = new Thread(() => loyalty.ChangeAsync(player, 1).GetAwaiter().GetResult());

        gainer.Start();
        SpinWait.SpinUntil(() => gainer.ThreadState.HasFlag(ThreadState.WaitSleepJoin), LockWait).Should().BeTrue("the gain waits for the player's lock");
        release.Set();
        holder.Join();
        gainer.Join();

        clan.ClanPointFund.Should().Be(0, "the player was out of the clan by the time the gain was counted");
        player.KnightsPoints.Should().Be(0);
        player.Loyalty.Should().Be(1);
    }

    // Every thread is released at once and runs its share of the rounds, so the steps overlap from the first round.
    private static void Together(Action round)
    {
        using var start = new Barrier(Threads);
        var threads = Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            for (var step = 0; step < Rounds / Threads; step++)
                round();
        })).ToList();
        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());
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
