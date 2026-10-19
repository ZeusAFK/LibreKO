using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class UnderTheCastleTests
{
    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameData;
    private readonly IZoneTransitionService zoneTransition;
    private readonly INpcSpawnRowService spawnRows;
    private readonly INpcLifecycleService lifecycle;
    private readonly IItemGrantService itemGrant;
    private readonly ILogger<UnderTheCastleService> logger;
    private readonly UnderTheCastleService service;

    public UnderTheCastleTests()
    {
        sessionManager = new SessionManager();
        gameData = Substitute.For<IGameDataService>();
        zoneTransition = Substitute.For<IZoneTransitionService>();
        spawnRows = Substitute.For<INpcSpawnRowService>();
        lifecycle = Substitute.For<INpcLifecycleService>();
        itemGrant = Substitute.For<IItemGrantService>();
        logger = Substitute.For<ILogger<UnderTheCastleService>>();

        gameData.GetItem(Arg.Any<int>()).Returns(ci =>
        {
            var num = ci.Arg<int>();
            return new ItemData
            {
                Num = num,
                Name = $"Item_{num}"
            };
        });

        gameData.NpcPositions.Returns(new List<NpcPosData>
        {
            new() { Index = 1000, ZoneId = UnderTheCastleService.UtcZoneId, NpcId = 9565, Room = 86 },
            new() { Index = 1001, ZoneId = UnderTheCastleService.UtcZoneId, NpcId = UnderTheCastleService.Gate1DoorNpcId, TrapNumber = 1, Room = 86 },
            new() { Index = 1002, ZoneId = UnderTheCastleService.UtcZoneId, NpcId = UnderTheCastleService.EmperorMammothNpcId, Room = 86 },
        });

        service = new UnderTheCastleService(
            sessionManager,
            gameData,
            zoneTransition,
            spawnRows,
            lifecycle,
            itemGrant,
            logger);
    }

    private UserSession CreateTestSession(int charId, byte zoneId = 21, int level = 80, bool isGm = false, float x = 0f, float z = 0f)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = AccountNation.Karus;
        session.Level = (byte)level;
        session.ZoneId = zoneId;
        session.IsGM = isGm;
        session.X = x;
        session.Z = z;
        return session;
    }

    [Fact]
    public void Start_ActivatesEvent_AndSetsDefaults()
    {
        service.IsActive.Should().BeFalse();

        service.Start(60);

        service.IsActive.Should().BeTrue();
        service.RemainingSeconds.Should().Be(3600);
        service.CurrentStage.Should().Be(1);
    }

    [Fact]
    public async Task CloseAsync_DeactivatesEvent_AndResetsState()
    {
        service.Start(60);
        service.IsActive.Should().BeTrue();

        await service.CloseAsync();

        service.IsActive.Should().BeFalse();
        service.RemainingSeconds.Should().Be(0);
    }

    [Fact]
    public async Task EnterAsync_WhenClosed_BlocksNonGm()
    {
        var player = CreateTestSession(101, level: 80, isGm: false);

        await service.EnterAsync(player);

        await zoneTransition.DidNotReceive().ChangeZoneAsync(player, UnderTheCastleService.UtcZoneId, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task EnterAsync_WhenActive_WarpsPlayerToCamp()
    {
        var player = CreateTestSession(101, level: 80, isGm: false);

        service.Start(60);

        await service.EnterAsync(player);

        await zoneTransition.Received(1).ChangeZoneAsync(player, UnderTheCastleService.UtcZoneId, 69f, 64f);
    }

    [Fact]
    public async Task DespawnedGate_HasRespawnTypeNever_AndIsNotReturnedByGetDeadNpcsReadyToRespawn()
    {
        var realLifecycle = new NpcLifecycleService(sessionManager, NullLogger<NpcLifecycleService>.Instance);
        var gate = new NpcInstance
        {
            NpcId = UnderTheCastleService.Gate1DoorNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            TrapNumber = 1,
            RespawnType = NpcRespawnType.Normal,
            RespawnDelayMs = 1000
        };

        spawnRows.Spawn(Arg.Any<NpcPosData>()).Returns([gate]);

        service.Start(60);

        gate.RespawnType.Should().Be(NpcRespawnType.Never);

        sessionManager.Regions.SpawnNpc(gate);
        await realLifecycle.DespawnAsync(gate);

        gate.IsDead.Should().BeTrue();
        gate.CanRespawn.Should().BeFalse();

        var farFutureTicks = gate.DeathTimeTicks + TimeSpan.FromMinutes(10).Ticks;
        var readyToRespawn = sessionManager.Regions.GetDeadNpcsReadyToRespawn(farFutureTicks).ToList();

        readyToRespawn.Should().NotContain(gate);
    }

    [Fact]
    public async Task OnNpcKilledAsync_LocationBasedStageRewards_DistributesTrophiesAndItemsCorrectly()
    {
        // Mammoth is at Stage 1: Area (121, 297, radius 80)
        float bossX = 120f;
        float bossZ = 295f;

        // Player 1: Inside area AND near boss (<= 15m) -> 2 Trophies + Twinkling Star Glitter
        var player1 = CreateTestSession(101, zoneId: UnderTheCastleService.UtcZoneId, x: 122f, z: 296f);
        // Player 2: Inside area (dist ~ 50m) but FAR from boss (> 15m) -> 1 Trophy + Twinkling Star Glitter
        var player2 = CreateTestSession(102, zoneId: UnderTheCastleService.UtcZoneId, x: 170f, z: 297f);
        // Player 3: Completely outside area and outside boss radius -> 0 Trophies, no craft items
        var player3 = CreateTestSession(103, zoneId: UnderTheCastleService.UtcZoneId, x: 500f, z: 500f);

        service.Start(60);

        var mammoth = new NpcInstance
        {
            UniqueId = 5001,
            NpcId = UnderTheCastleService.EmperorMammothNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            X = bossX,
            Z = bossZ,
            IsMonster = true
        };

        await service.OnNpcKilledAsync(mammoth, player1);

        service.CurrentStage.Should().Be(2);

        // Player 1 got 2 trophies and 1 star glitter
        await itemGrant.Received(1).GrantAsync(player1, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 2);
        await itemGrant.Received(1).GrantAsync(player1, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TwinklingStarGlitterItemId), 1);

        // Player 2 got 1 trophy and 1 star glitter
        await itemGrant.Received(1).GrantAsync(player2, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 1);
        await itemGrant.Received(1).GrantAsync(player2, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TwinklingStarGlitterItemId), 1);

        // Player 3 received nothing
        await itemGrant.DidNotReceive().GrantAsync(player3, Arg.Any<ItemData>(), Arg.Any<int>());
    }

    [Fact]
    public async Task OnNpcKilledAsync_FinalBoss_TriggersVictoryAndGrantsAllStage4Rewards()
    {
        // Final Boss: PluwitonFinalBossNpcId at Stage 4 (803, 839, radius 110)
        float bossX = 803f;
        float bossZ = 839f;

        var player = CreateTestSession(101, zoneId: UnderTheCastleService.UtcZoneId, x: 805f, z: 840f);

        service.Start(60);

        // Advance from stage 1 -> 2 -> 3 -> 4
        var mammoth = new NpcInstance { UniqueId = 5001, NpcId = UnderTheCastleService.EmperorMammothNpcId, ZoneId = UnderTheCastleService.UtcZoneId, X = 121f, Z = 297f, IsMonster = true };
        await service.OnNpcKilledAsync(mammoth, player);
        service.CurrentStage.Should().Be(2);

        var crasher = new NpcInstance { UniqueId = 5002, NpcId = UnderTheCastleService.CrasherGimmickNpcId, ZoneId = UnderTheCastleService.UtcZoneId, X = 520f, Z = 494f, IsMonster = true };
        await service.OnNpcKilledAsync(crasher, player);
        service.CurrentStage.Should().Be(3);

        var flw = new NpcInstance { UniqueId = 5003, NpcId = UnderTheCastleService.ShackledLordFluwitonNpcId, ZoneId = UnderTheCastleService.UtcZoneId, X = 642f, Z = 351f, IsMonster = true };
        await service.OnNpcKilledAsync(flw, player);
        service.CurrentStage.Should().Be(4);

        itemGrant.ClearReceivedCalls();

        var finalBoss = new NpcInstance
        {
            UniqueId = 5004,
            NpcId = UnderTheCastleService.PluwitonFinalBossNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            X = bossX,
            Z = bossZ,
            IsMonster = true
        };

        await service.OnNpcKilledAsync(finalBoss, player);

        service.CurrentStage.Should().Be(5);

        // In area and near boss -> 2 trophies
        await itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 2);
        // Stage 4 rewards: PlwitoonsTear, HornOfPluwiton, TwinklingStarGlitter
        await itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.PlwitoonsTearItemId), 1);
        await itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.HornOfPluwitonItemId), 1);
        await itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TwinklingStarGlitterItemId), 1);
    }
}
