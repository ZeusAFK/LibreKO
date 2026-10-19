using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class ForgottenTempleTests
{
    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameData;
    private readonly IZoneTransitionService zoneTransition;
    private readonly INpcSummonService npcSummon;
    private const int GreenTreasureChestItemId = 379155000;
    private const int BlueTreasureChestItemId = 379156000;
    private const int VolcanicRockNpcId = 9810;
    private const int ShaitanNpcId = 9820;

    private readonly IPlayerProgressionService progression;
    private readonly IUserNotificationService userNotification;
    private readonly ILoyaltyService loyalty;
    private readonly ILogger<ForgottenTempleService> logger;
    private readonly ForgottenTempleService service;

    public ForgottenTempleTests()
    {
        sessionManager = new SessionManager();
        gameData = Substitute.For<IGameDataService>();
        zoneTransition = Substitute.For<IZoneTransitionService>();
        npcSummon = Substitute.For<INpcSummonService>();
        progression = Substitute.For<IPlayerProgressionService>();
        userNotification = Substitute.For<IUserNotificationService>();
        loyalty = Substitute.For<ILoyaltyService>();
        logger = Substitute.For<ILogger<ForgottenTempleService>>();

        gameData.GetItem(GreenTreasureChestItemId).Returns(new ItemData
        {
            Num = GreenTreasureChestItemId,
            Name = "Green Treasure Chest",
            Weight = 10,
            Countable = 1
        });

        gameData.GetItem(BlueTreasureChestItemId).Returns(new ItemData
        {
            Num = BlueTreasureChestItemId,
            Name = "Blue Treasure Chest",
            Weight = 10,
            Countable = 1
        });

        gameData.ForgottenTempleWaves.Returns(new List<ForgottenTempleWaveData>
        {
            new() { Id = 1, Tier = 1, Wave = 1, StartSecond = 30, NpcId = 9800, Count = 1 },
            new() { Id = 2, Tier = 1, Wave = 2, StartSecond = 60, NpcId = ShaitanNpcId, Count = 1 },

            new() { Id = 3, Tier = 2, Wave = 1, StartSecond = 30, NpcId = 9800, Count = 1 },
            new() { Id = 4, Tier = 2, Wave = 2, StartSecond = 60, NpcId = VolcanicRockNpcId, Count = 1 }
        });

        gameData.TempleEventRewards.Returns(new List<TempleEventRewardData>
        {
            new()
            {
                Id = 12,
                Event = TempleEvent.ForgottenTemple,
                Outcome = TempleEventRewardOutcome.Win,
                ItemId = GreenTreasureChestItemId,
                ItemCount = 1,
                MinLevel = 46,
                MaxLevel = 59,
                ExpPercent = 100
            },
            new()
            {
                Id = 13,
                Event = TempleEvent.ForgottenTemple,
                Outcome = TempleEventRewardOutcome.Win,
                ItemId = BlueTreasureChestItemId,
                ItemCount = 1,
                MinLevel = 60,
                MaxLevel = 83,
                ExpPercent = 100
            }
        });

        npcSummon.SummonAsync(Arg.Any<int>(), Arg.Any<byte>(), Arg.Any<ushort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Action<NpcInstance>?>())
            .Returns(callInfo =>
            {
                int npcId = callInfo.ArgAt<int>(0);
                return Task.FromResult<IReadOnlyList<NpcInstance>>([
                    new NpcInstance { UniqueId = 5000 + npcId, NpcId = npcId, ZoneId = ForgottenTempleService.FtZoneId, IsMonster = true }
                ]);
            });

        service = new ForgottenTempleService(
            sessionManager,
            gameData,
            zoneTransition,
            npcSummon,
            progression,
            userNotification,
            loyalty,
            logger);
    }

    private UserSession CreateTestSession(int charId, byte zoneId = 21, int level = 70)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = AccountNation.ElMorad;
        session.Level = (byte)level;
        session.ZoneId = zoneId;
        return session;
    }

    [Fact]
    public void Start_ActivatesEvent_AndInitializesState()
    {
        service.IsActive.Should().BeFalse();

        service.Start(minLevel: 60, maxLevel: 83);

        service.IsActive.Should().BeTrue();
        service.RemainingSeconds.Should().Be(2400);
        service.CurrentWave.Should().Be(0);
        service.CurrentTier.Should().Be(ForgottenTempleTier.HighLevel);
    }

    [Fact]
    public async Task CloseAsync_DeactivatesEvent_AndWarpsPlayersOut()
    {
        service.Start();
        var player = CreateTestSession(101, zoneId: ForgottenTempleService.FtZoneId);

        await service.CloseAsync();

        service.IsActive.Should().BeFalse();
        service.RemainingSeconds.Should().Be(0);
        await zoneTransition.Received(1).ChangeZoneAsync(player, (byte)ZoneId.Moradon, 0f, 0f);
    }

    [Fact]
    public async Task EnterAsync_WhenInactive_BlocksEntry()
    {
        var player = CreateTestSession(101, level: 75);

        await service.EnterAsync(player);

        await zoneTransition.DidNotReceive()
            .ChangeZoneAsync(player, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task EnterAsync_WhenLowLevelTier_AcceptsLevel46And50_BlocksLevel45And70()
    {
        service.Start(minLevel: TempleEventRules.ForgottenTempleLowMinLevel, maxLevel: TempleEventRules.ForgottenTempleLowMaxLevel);
        var validPlayer46 = CreateTestSession(102, level: 46);
        var validPlayer50 = CreateTestSession(103, level: 50);
        var underPlayer45 = CreateTestSession(104, level: 45);
        var overPlayer70 = CreateTestSession(105, level: 70);

        await service.EnterAsync(validPlayer46);
        await service.EnterAsync(validPlayer50);
        await service.EnterAsync(underPlayer45);
        await service.EnterAsync(overPlayer70);

        await zoneTransition.Received(1)
            .ChangeZoneAsync(validPlayer46, ForgottenTempleService.FtZoneId, 150f, 150f);
        await zoneTransition.Received(1)
            .ChangeZoneAsync(validPlayer50, ForgottenTempleService.FtZoneId, 150f, 150f);
        await zoneTransition.DidNotReceive()
            .ChangeZoneAsync(underPlayer45, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
        await zoneTransition.DidNotReceive()
            .ChangeZoneAsync(overPlayer70, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task EnterAsync_WhenHighLevelTier_AcceptsLevel75_BlocksLevel50()
    {
        service.Start(minLevel: 60, maxLevel: 83);
        var underLevelPlayer = CreateTestSession(106, level: 50);
        var validPlayer = CreateTestSession(107, level: 75);

        await service.EnterAsync(underLevelPlayer);
        await service.EnterAsync(validPlayer);

        await zoneTransition.DidNotReceive()
            .ChangeZoneAsync(underLevelPlayer, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
        await zoneTransition.Received(1)
            .ChangeZoneAsync(validPlayer, ForgottenTempleService.FtZoneId, 150f, 150f);
    }

    [Fact]
    public async Task TickAsync_AdvancesWaves_WhenTimeElapsed_FromSeed()
    {
        service.Start(minLevel: 60, maxLevel: 83);
        service.CurrentWave.Should().Be(0);

        for (int i = 0; i <= 30; i++)
        {
            await service.TickAsync();
        }

        service.CurrentWave.Should().Be(1);
        await npcSummon.Received().SummonAsync(
            9800, ForgottenTempleService.FtZoneId, 0, Arg.Any<int>(), Arg.Any<int>(), 1, 0f);
    }

    [Fact]
    public async Task OnNpcKilledAsync_WhenLastWaveMonsterKilled_TriggersVictoryAndRewards()
    {
        service.Start(minLevel: 60, maxLevel: 83);
        var player = CreateTestSession(108, zoneId: ForgottenTempleService.FtZoneId, level: 80);

        var waveSpawned = await service.ForceSpawnWaveAsync(2);
        waveSpawned.Should().BeTrue();

        var bossNpc = new NpcInstance
        {
            UniqueId = 5000 + VolcanicRockNpcId,
            NpcId = VolcanicRockNpcId,
            ZoneId = ForgottenTempleService.FtZoneId,
            IsMonster = true
        };

        await service.OnNpcKilledAsync(bossNpc, player);

        await progression.Received(1).AwardExperienceAsync(
            player,
            Arg.Is<long>(exp => exp > 0));
    }
}
