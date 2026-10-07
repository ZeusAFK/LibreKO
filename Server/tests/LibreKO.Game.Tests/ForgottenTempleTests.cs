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
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameData;
    private readonly IZoneTransitionService _zoneTransition;
    private readonly INpcSummonService _npcSummon;
    private const int GreenTreasureChestItemId = 379155000;
    private const int BlueTreasureChestItemId = 379156000;
    private const int VolcanicRockNpcId = 9810;
    private const int ShaitanNpcId = 9820;

    private readonly IPlayerProgressionService _progression;
    private readonly IUserNotificationService _userNotification;
    private readonly ILoyaltyService _loyalty;
    private readonly ILogger<ForgottenTempleService> _logger;
    private readonly ForgottenTempleService _service;

    public ForgottenTempleTests()
    {
        _sessionManager = new SessionManager();
        _gameData = Substitute.For<IGameDataService>();
        _zoneTransition = Substitute.For<IZoneTransitionService>();
        _npcSummon = Substitute.For<INpcSummonService>();
        _progression = Substitute.For<IPlayerProgressionService>();
        _userNotification = Substitute.For<IUserNotificationService>();
        _loyalty = Substitute.For<ILoyaltyService>();
        _logger = Substitute.For<ILogger<ForgottenTempleService>>();

        _gameData.GetItem(GreenTreasureChestItemId).Returns(new ItemData
        {
            Num = GreenTreasureChestItemId,
            Name = "Green Treasure Chest",
            Weight = 10,
            Countable = 1
        });

        _gameData.GetItem(BlueTreasureChestItemId).Returns(new ItemData
        {
            Num = BlueTreasureChestItemId,
            Name = "Blue Treasure Chest",
            Weight = 10,
            Countable = 1
        });


        _gameData.ForgottenTempleWaves.Returns(new List<ForgottenTempleWaveData>
        {
            // Low tier (Tier 1)
            new() { Id = 1, Tier = 1, Wave = 1, StartSecond = 30, NpcId = 9800, Count = 1 },
            new() { Id = 2, Tier = 1, Wave = 2, StartSecond = 60, NpcId = ShaitanNpcId, Count = 1 },

            // High tier (Tier 2)
            new() { Id = 3, Tier = 2, Wave = 1, StartSecond = 30, NpcId = 9800, Count = 1 },
            new() { Id = 4, Tier = 2, Wave = 2, StartSecond = 60, NpcId = VolcanicRockNpcId, Count = 1 }
        });

        _gameData.TempleEventRewards.Returns(new List<TempleEventRewardData>
        {
            new()
            {
                Id = 12,
                Event = TempleEvent.ForgottenTemple,
                Outcome = TempleEventRewardOutcome.Win,
                ItemId = GreenTreasureChestItemId,
                ItemCount = 1,
                MinLevel = 35,
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


        _npcSummon.SummonAsync(Arg.Any<int>(), Arg.Any<byte>(), Arg.Any<ushort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<float>(), Arg.Any<Action<NpcInstance>?>())
            .Returns(callInfo =>
            {
                int npcId = callInfo.ArgAt<int>(0);
                return Task.FromResult<IReadOnlyList<NpcInstance>>([
                    new NpcInstance { UniqueId = 5000 + npcId, NpcId = npcId, ZoneId = ForgottenTempleService.FtZoneId, IsMonster = true }
                ]);
            });

        _service = new ForgottenTempleService(
            _sessionManager,
            _gameData,
            _zoneTransition,
            _npcSummon,
            _progression,
            _userNotification,
            _loyalty,
            _logger);
    }

    private UserSession CreateTestSession(int charId, byte zoneId = 21, int level = 70)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = _sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = AccountNation.ElMorad;
        session.Level = (byte)level;
        session.ZoneId = zoneId;
        return session;
    }

    [Fact]
    public void Start_ActivatesEvent_AndInitializesState()
    {
        _service.IsActive.Should().BeFalse();

        _service.Start(minLevel: 60, maxLevel: 83);

        _service.IsActive.Should().BeTrue();
        _service.RemainingSeconds.Should().Be(2400);
        _service.CurrentWave.Should().Be(0);
        _service.CurrentTier.Should().Be(ForgottenTempleTier.HighLevel);
    }

    [Fact]
    public async Task CloseAsync_DeactivatesEvent_AndWarpsPlayersOut()
    {
        _service.Start();
        var player = CreateTestSession(101, zoneId: ForgottenTempleService.FtZoneId);


        await _service.CloseAsync();

        _service.IsActive.Should().BeFalse();
        _service.RemainingSeconds.Should().Be(0);
        await _zoneTransition.Received(1).ChangeZoneAsync(player, (byte)ZoneId.Moradon, 0f, 0f);
    }

    [Fact]
    public async Task EnterAsync_WhenInactive_BlocksEntry()
    {
        var player = CreateTestSession(101, level: 75);

        await _service.EnterAsync(player);

        await _zoneTransition.DidNotReceive()
            .ChangeZoneAsync(player, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task EnterAsync_WhenLowLevelTier_AcceptsLevel35And50_BlocksLevel30And70()
    {
        _service.Start(minLevel: 35, maxLevel: 59);
        var validPlayer35 = CreateTestSession(102, level: 35);
        var validPlayer50 = CreateTestSession(103, level: 50);
        var underPlayer30 = CreateTestSession(104, level: 30);
        var overPlayer70 = CreateTestSession(105, level: 70);

        await _service.EnterAsync(validPlayer35);
        await _service.EnterAsync(validPlayer50);
        await _service.EnterAsync(underPlayer30);
        await _service.EnterAsync(overPlayer70);

        await _zoneTransition.Received(1)
            .ChangeZoneAsync(validPlayer35, ForgottenTempleService.FtZoneId, 150f, 150f);
        await _zoneTransition.Received(1)
            .ChangeZoneAsync(validPlayer50, ForgottenTempleService.FtZoneId, 150f, 150f);
        await _zoneTransition.DidNotReceive()
            .ChangeZoneAsync(underPlayer30, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
        await _zoneTransition.DidNotReceive()
            .ChangeZoneAsync(overPlayer70, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task EnterAsync_WhenHighLevelTier_AcceptsLevel75_BlocksLevel50()
    {
        _service.Start(minLevel: 60, maxLevel: 83);
        var underLevelPlayer = CreateTestSession(106, level: 50);
        var validPlayer = CreateTestSession(107, level: 75);

        await _service.EnterAsync(underLevelPlayer);
        await _service.EnterAsync(validPlayer);

        await _zoneTransition.DidNotReceive()
            .ChangeZoneAsync(underLevelPlayer, ForgottenTempleService.FtZoneId, Arg.Any<float>(), Arg.Any<float>());
        await _zoneTransition.Received(1)
            .ChangeZoneAsync(validPlayer, ForgottenTempleService.FtZoneId, 150f, 150f);
    }

    [Fact]
    public async Task TickAsync_AdvancesWaves_WhenTimeElapsed_FromSeed()
    {
        _service.Start(minLevel: 60, maxLevel: 83);
        _service.CurrentWave.Should().Be(0);

        // Tick 31 times to reach StartSecond 30 for Wave 1
        for (int i = 0; i <= 30; i++)
        {
            await _service.TickAsync();
        }

        _service.CurrentWave.Should().Be(1);
        await _npcSummon.Received().SummonAsync(
            9800, ForgottenTempleService.FtZoneId, 0, Arg.Any<int>(), Arg.Any<int>(), 1, 0f);
    }

    [Fact]
    public async Task OnNpcKilledAsync_WhenLastWaveMonsterKilled_TriggersVictoryAndRewards()
    {
        _service.Start(minLevel: 60, maxLevel: 83);
        var player = CreateTestSession(108, zoneId: ForgottenTempleService.FtZoneId, level: 80);


        // Force spawn last wave (Wave 2)
        var waveSpawned = await _service.ForceSpawnWaveAsync(2);
        waveSpawned.Should().BeTrue();

        var bossNpc = new NpcInstance
        {
            UniqueId = 5000 + VolcanicRockNpcId,
            NpcId = VolcanicRockNpcId,
            ZoneId = ForgottenTempleService.FtZoneId,
            IsMonster = true
        };


        await _service.OnNpcKilledAsync(bossNpc, player);

        // EXP awarded for victory
        await _progression.Received(1).AwardExperienceAsync(
            player,
            Arg.Is<long>(exp => exp > 0));
    }
}
