using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class ClanStandingTests : GameTestBase
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(71_999, 5)]
    [InlineData(72_000, 4)]
    [InlineData(144_000, 3)]
    [InlineData(360_000, 2)]
    [InlineData(720_000, 1)]
    [InlineData(int.MaxValue, 1)]
    public void GradeFollowsTheRetailPointThresholds(int points, byte grade) =>
        ClanRules.GradeFromPoints(points).Should().Be(grade);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(25, 5)]
    [InlineData(26, 6)]
    [InlineData(45, 9)]
    [InlineData(50, 10)]
    public void AutomaticContributionGrowsWithEveryFiveMembers(int members, int contribution) =>
        ClanRules.AutoContribution(members).Should().Be(contribution);

    [Fact]
    public async Task AClansPointsAreItsMembersNationalPointsAndTheGradeFollows()
    {
        const short clanId = 501;
        using var provider = CreateProvider(db =>
        {
            db.Set<KnightsEntity>().Add(new KnightsEntity
            {
                Id = clanId, Name = "Wolves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad,
                Flag = (byte)ClanType.Training, Members = 2,
            });
            db.Characters.AddRange(
                new Character { AccountId = 1, Slot = 0, Name = "Aurelia", Level = 70, Class = 201, MapId = 2, KnightsId = clanId, Fame = 1, Loyalty = 100_000 },
                new Character { AccountId = 2, Slot = 0, Name = "Dorin", Level = 60, Class = 202, MapId = 2, KnightsId = clanId, Fame = 5, Loyalty = 50_000 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var clan = new KnightsEntity
        {
            Id = clanId, Name = "Wolves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad,
            Flag = (byte)ClanType.Training, Members = 2,
        };
        sessionManager.Knights.AddClan(clanId, clan);

        var standing = provider.GetRequiredService<IClanStandingService>();
        await standing.RefreshClanAsync(clanId);

        clan.Points.Should().Be(150_000);
        clan.Grade.Should().Be(3);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Set<KnightsEntity>().SingleAsync(entity => entity.Id == clanId);
        stored.Points.Should().Be(150_000);
        stored.Grade.Should().Be(3);
    }

    [Fact]
    public async Task AnOnlineMembersLivePointsCountBeforeTheStoredOnes()
    {
        const short clanId = 502;
        using var provider = CreateProvider(db =>
        {
            db.Set<KnightsEntity>().Add(new KnightsEntity
            {
                Id = clanId, Name = "Wolves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad,
                Flag = (byte)ClanType.Training, Members = 1,
            });
            db.Characters.Add(new Character { AccountId = 1, Slot = 0, Name = "Aurelia", Level = 70, Class = 201, MapId = 2, KnightsId = clanId, Fame = 1, Loyalty = 1_000 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var clan = new KnightsEntity
        {
            Id = clanId, Name = "Wolves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad,
            Flag = (byte)ClanType.Training, Members = 1,
        };
        sessionManager.Knights.AddClan(clanId, clan);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessionManager.CreateSession(client, await GetCharacterIdAsync(provider, "Aurelia"), accountId: 1);
        session.Name = "Aurelia";
        session.KnightsId = clanId;
        session.Loyalty = 800_000;

        await provider.GetRequiredService<IClanStandingService>().RefreshAllAsync();

        clan.Points.Should().Be(800_000);
        clan.Grade.Should().Be(1);
    }

    [Fact]
    public async Task OnlyTheFiveRichestFundsAreRankedAndEveryoneIsToldTheStanding()
    {
        using var provider = CreateProvider(db =>
        {
            for (short id = 1; id <= 7; id++)
            {
                db.Set<KnightsEntity>().Add(new KnightsEntity
                {
                    Id = id, Name = $"Clan{id}", Chief = $"Chief{id}", Nation = (byte)AccountNation.Karus,
                    Flag = (byte)ClanType.Accredited5, Members = 1, ClanPointFund = id == 7 ? 0 : id * 10_000,
                });
            }
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        for (short id = 1; id <= 7; id++)
        {
            sessionManager.Knights.AddClan(id, new KnightsEntity
            {
                Id = id, Name = $"Clan{id}", Chief = $"Chief{id}", Nation = (byte)AccountNation.Karus,
                Flag = (byte)ClanType.Accredited5, Members = 1, ClanPointFund = id == 7 ? 0 : id * 10_000,
            });
        }

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Characters.Add(new Character { AccountId = 1, Slot = 0, Name = "Watcher", Level = 70, Class = 201, MapId = 21 });
            await db.SaveChangesAsync();
        }

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var watcher = sessionManager.CreateSession(client, await GetCharacterIdAsync(provider, "Watcher"), accountId: 1);
        watcher.Name = "Watcher";

        await provider.GetRequiredService<IClanStandingService>().RefreshRankingsAsync();

        sessionManager.Knights.GetClan(6)!.Ranking.Should().Be(1);
        sessionManager.Knights.GetClan(5)!.Ranking.Should().Be(2);
        sessionManager.Knights.GetClan(2)!.Ranking.Should().Be(5);
        sessionManager.Knights.GetClan(1)!.Ranking.Should().Be(ClanRules.Unranked);
        sessionManager.Knights.GetClan(7)!.Ranking.Should().Be(ClanRules.Unranked);

        var refresh = sent.Should().ContainSingle(packet => packet.GetData()[0] == (byte)KnightsSubOpcode.AllListRequest).Subject;
        refresh.ResetOffset();
        refresh.ReadByte();
        var count = refresh.ReadShort();
        var seen = new Dictionary<short, (byte Grade, byte Ranking)>();
        for (var i = 0; i < count; i++)
            seen[refresh.ReadShort()] = (refresh.ReadByte(), refresh.ReadByte());
        seen[6].Ranking.Should().Be(1);
        seen[2].Ranking.Should().Be(5);
        seen.Should().NotContainKey(7);
    }

    [Fact]
    public async Task ARisingFundPushesAClanOntoTheBoard()
    {
        using var provider = CreateProvider(db =>
        {
            for (short id = 1; id <= 6; id++)
            {
                db.Set<KnightsEntity>().Add(new KnightsEntity
                {
                    Id = id, Name = $"Clan{id}", Chief = $"Chief{id}", Nation = (byte)AccountNation.Karus,
                    Flag = (byte)ClanType.Accredited5, Members = 1, ClanPointFund = id == 6 ? 100 : id * 10_000,
                });
            }
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        for (short id = 1; id <= 6; id++)
        {
            sessionManager.Knights.AddClan(id, new KnightsEntity
            {
                Id = id, Name = $"Clan{id}", Chief = $"Chief{id}", Nation = (byte)AccountNation.Karus,
                Flag = (byte)ClanType.Accredited5, Members = 1, ClanPointFund = id == 6 ? 100 : id * 10_000,
            });
        }

        var standing = provider.GetRequiredService<IClanStandingService>();
        await standing.RefreshRankingsAsync();
        sessionManager.Knights.GetClan(6)!.Ranking.Should().Be(ClanRules.Unranked);

        sessionManager.Knights.GetClan(6)!.ClanPointFund = 60_000;
        await standing.RefreshRankingsAsync();

        sessionManager.Knights.GetClan(6)!.Ranking.Should().Be(1);
        sessionManager.Knights.GetClan(1)!.Ranking.Should().Be(ClanRules.Unranked);
    }

    [Fact]
    public async Task TheClanTheBoardListsFirstWearsTheRankEvenWithoutAContributionFund()
    {
        using var provider = CreateProvider(db =>
        {
            db.Set<KnightsEntity>().Add(Clan(1, AccountNation.Karus, points: 1_500_000, fund: 0));
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Knights.AddClan(1, Clan(1, AccountNation.Karus, points: 1_500_000, fund: 0));

        await provider.GetRequiredService<IClanStandingService>().RefreshRankingsAsync();

        sessionManager.Knights.GetClan(1)!.Ranking.Should().Be(1);
    }

    [Fact]
    public async Task EachNationKeepsItsOwnFirstPlace()
    {
        using var provider = CreateProvider(db =>
        {
            db.Set<KnightsEntity>().Add(Clan(1, AccountNation.Karus, points: 10, fund: 100));
            db.Set<KnightsEntity>().Add(Clan(2, AccountNation.ElMorad, points: 10, fund: 50));
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Knights.AddClan(1, Clan(1, AccountNation.Karus, points: 10, fund: 100));
        sessionManager.Knights.AddClan(2, Clan(2, AccountNation.ElMorad, points: 10, fund: 50));

        await provider.GetRequiredService<IClanStandingService>().RefreshRankingsAsync();

        sessionManager.Knights.GetClan(1)!.Ranking.Should().Be(1);
        sessionManager.Knights.GetClan(2)!.Ranking.Should().Be(1);
    }

    private static KnightsEntity Clan(short id, AccountNation nation, int points, int fund) => new()
    {
        Id = id,
        Name = $"Clan{id}",
        Chief = $"Chief{id}",
        Nation = (byte)nation,
        Flag = (byte)ClanType.Promoted,
        Members = 1,
        Points = points,
        ClanPointFund = fund,
    };
}
