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
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameData;
    private readonly IZoneTransitionService _zoneTransition;
    private readonly INpcSpawnRowService _spawnRows;
    private readonly INpcLifecycleService _lifecycle;
    private readonly IItemGrantService _itemGrant;
    private readonly ILogger<UnderTheCastleService> _logger;
    private readonly UnderTheCastleService _service;

    public UnderTheCastleTests()
    {
        _sessionManager = new SessionManager();
        _gameData = Substitute.For<IGameDataService>();
        _zoneTransition = Substitute.For<IZoneTransitionService>();
        _spawnRows = Substitute.For<INpcSpawnRowService>();
        _lifecycle = Substitute.For<INpcLifecycleService>();
        _itemGrant = Substitute.For<IItemGrantService>();
        _logger = Substitute.For<ILogger<UnderTheCastleService>>();

        _gameData.GetItem(Arg.Any<int>()).Returns(ci =>
        {
            var num = ci.Arg<int>();
            return new ItemData
            {
                Num = num,
                Name = $"Item_{num}"
            };
        });

        _gameData.NpcPositions.Returns(new List<NpcPosData>
        {
            new() { Index = 1000, ZoneId = UnderTheCastleService.UtcZoneId, NpcId = 9565, Room = 86 },
            new() { Index = 1001, ZoneId = UnderTheCastleService.UtcZoneId, NpcId = UnderTheCastleService.Gate1DoorNpcId, TrapNumber = 1, Room = 86 },
            new() { Index = 1002, ZoneId = UnderTheCastleService.UtcZoneId, NpcId = UnderTheCastleService.EmperorMammothNpcId, Room = 86 },
        });

        _service = new UnderTheCastleService(
            _sessionManager,
            _gameData,
            _zoneTransition,
            _spawnRows,
            _lifecycle,
            _itemGrant,
            _logger);
    }

    private UserSession CreateTestSession(int charId, byte zoneId = 21, int level = 80, bool isGm = false, float x = 0f, float z = 0f)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = _sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
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
        _service.IsActive.Should().BeFalse();

        _service.Start(60);

        _service.IsActive.Should().BeTrue();
        _service.RemainingSeconds.Should().Be(3600);
        _service.CurrentStage.Should().Be(1);
    }

    [Fact]
    public async Task CloseAsync_DeactivatesEvent_AndResetsState()
    {
        _service.Start(60);
        _service.IsActive.Should().BeTrue();

        await _service.CloseAsync();

        _service.IsActive.Should().BeFalse();
        _service.RemainingSeconds.Should().Be(0);
    }

    [Fact]
    public async Task EnterAsync_WhenClosed_BlocksNonGm()
    {
        var player = CreateTestSession(101, level: 80, isGm: false);

        await _service.EnterAsync(player);

        await _zoneTransition.DidNotReceive().ChangeZoneAsync(player, UnderTheCastleService.UtcZoneId, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task EnterAsync_WhenActive_WarpsPlayerToCamp()
    {
        var player = CreateTestSession(101, level: 80, isGm: false);

        _service.Start(60);

        await _service.EnterAsync(player);

        await _zoneTransition.Received(1).ChangeZoneAsync(player, UnderTheCastleService.UtcZoneId, 69f, 64f);
    }

    [Fact]
    public async Task DespawnedGate_HasRespawnTypeNever_AndIsNotReturnedByGetDeadNpcsReadyToRespawn()
    {
        var realLifecycle = new NpcLifecycleService(_sessionManager, NullLogger<NpcLifecycleService>.Instance);
        var gate = new NpcInstance
        {
            NpcId = UnderTheCastleService.Gate1DoorNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            TrapNumber = 1,
            RespawnType = NpcRespawnType.Never,
            RespawnDelayMs = 1000
        };

        _sessionManager.Regions.SpawnNpc(gate);
        gate.RespawnType.Should().Be(NpcRespawnType.Never);

        await realLifecycle.DespawnAsync(gate);

        gate.IsDead.Should().BeTrue();
        gate.CanRespawn.Should().BeFalse();

        var farFutureTicks = gate.DeathTimeTicks + TimeSpan.FromMinutes(10).Ticks;
        var readyToRespawn = _sessionManager.Regions.GetDeadNpcsReadyToRespawn(farFutureTicks).ToList();

        readyToRespawn.Should().NotContain(gate);
    }

    [Fact]
    public async Task OnNpcKilledAsync_LocationBasedStageRewards_DistributesTrophiesAndItemsCorrectly()
    {
        // Mammoth is at Stage 1: Area (121, 297, radius 80)
        float bossX = 120f;
        bossZ = 295f;

        // Player 1: Inside area AND near boss (<= 15m) -> 2 Trophies + Twinkling Star Glitter
        var player1 = CreateTestSession(101, zoneId: UnderTheCastleService.UtcZoneId, x: 122f, z: 296f);
        // Player 2: Inside area (dist ~ 50m) but FAR from boss (> 15m) -> 1 Trophy + Twinkling Star Glitter
        var player2 = CreateTestSession(102, zoneId: UnderTheCastleService.UtcZoneId, x: 170f, z: 297f);
        // Player 3: Completely outside area and outside boss radius -> 0 Trophies, no craft items
        var player3 = CreateTestSession(103, zoneId: UnderTheCastleService.UtcZoneId, x: 500f, z: 500f);

        _service.Start(60);

        var mammoth = new NpcInstance
        {
            UniqueId = 5001,
            NpcId = UnderTheCastleService.EmperorMammothNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            X = bossX,
            Z = bossZ,
            IsMonster = true
        };

        await _service.OnNpcKilledAsync(mammoth, player1);

        _service.CurrentStage.Should().Be(2);

        // Player 1 got 2 trophies and 1 star glitter
        await _itemGrant.Received(1).GrantAsync(player1, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 2);
        await _itemGrant.Received(1).GrantAsync(player1, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TwinklingStarGlitterItemId), 1);

        // Player 2 got 1 trophy and 1 star glitter
        await _itemGrant.Received(1).GrantAsync(player2, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 1);
        await _itemGrant.Received(1).GrantAsync(player2, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TwinklingStarGlitterItemId), 1);

        // Player 3 received nothing
        await _itemGrant.DidNotReceive().GrantAsync(player3, Arg.Any<ItemData>(), Arg.Any<int>());
    }

    [Fact]
    public async Task OnNpcKilledAsync_FinalBoss_TriggersVictoryAndGrantsAllStage4Rewards()
    {
        // Final Boss: PluwitonFinalBossNpcId at Stage 4 (803, 839, radius 110)
        float bossX = 803f;
        float bossZ = 839f;

        var player = CreateTestSession(101, zoneId: UnderTheCastleService.UtcZoneId, x: 805f, z: 840f);

        _service.Start(60);

        // Advance from stage 1 -> 2 -> 3 -> 4
        var mammoth = new NpcInstance { UniqueId = 5001, NpcId = UnderTheCastleService.EmperorMammothNpcId, ZoneId = UnderTheCastleService.UtcZoneId, X = 121f, Z = 297f, IsMonster = true };
        await _service.OnNpcKilledAsync(mammoth, player);
        _service.CurrentStage.Should().Be(2);

        var crasher = new NpcInstance { UniqueId = 5002, NpcId = UnderTheCastleService.CrasherGimmickNpcId, ZoneId = UnderTheCastleService.UtcZoneId, X = 520f, Z = 494f, IsMonster = true };
        await _service.OnNpcKilledAsync(crasher, player);
        _service.CurrentStage.Should().Be(3);

        var flw = new NpcInstance { UniqueId = 5003, NpcId = UnderTheCastleService.ShackledLordFluwitonNpcId, ZoneId = UnderTheCastleService.UtcZoneId, X = 642f, Z = 351f, IsMonster = true };
        await _service.OnNpcKilledAsync(flw, player);
        _service.CurrentStage.Should().Be(4);

        _itemGrant.ClearReceivedCalls();

        var finalBoss = new NpcInstance
        {
            UniqueId = 5004,
            NpcId = UnderTheCastleService.PluwitonFinalBossNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            X = bossX,
            Z = bossZ,
            IsMonster = true
        };

        await _service.OnNpcKilledAsync(finalBoss, player);

        _service.CurrentStage.Should().Be(5);

        // In area and near boss -> 2 trophies
        await _itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 2);
        // Stage 4 rewards: PlwitoonsTear, HornOfPluwiton, TwinklingStarGlitter
        await _itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.PlwitoonsTearItemId), 1);
        await _itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.HornOfPluwitonItemId), 1);
        await _itemGrant.Received(1).GrantAsync(player, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TwinklingStarGlitterItemId), 1);
    }
}
