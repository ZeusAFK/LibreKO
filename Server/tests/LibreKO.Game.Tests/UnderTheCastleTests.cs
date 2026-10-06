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

        _gameData.GetItem(UnderTheCastleService.TrophyOfFlameItemId).Returns(new ItemData
        {
            Num = UnderTheCastleService.TrophyOfFlameItemId,
            Name = "Trophy of Flame"
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

    private UserSession CreateTestSession(int charId, byte zoneId = 21, int level = 80, bool isGm = false)
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
    public void Close_DeactivatesEvent_AndResetsState()
    {
        _service.Start(60);
        _service.IsActive.Should().BeTrue();

        _service.Close();

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
    public async Task OnNpcKilledAsync_EmperorMammoth_AdvancesToStage2AndGrantsTrophy()
    {
        var killer = CreateTestSession(101, zoneId: UnderTheCastleService.UtcZoneId, level: 80);

        _service.Start(60);

        var mammoth = new NpcInstance
        {
            UniqueId = 5001,
            NpcId = UnderTheCastleService.EmperorMammothNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            IsMonster = true
        };

        await _service.OnNpcKilledAsync(mammoth, killer);

        _service.CurrentStage.Should().Be(2);
        await _itemGrant.Received(1).GrantAsync(killer, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 1);
    }

    [Fact]
    public async Task OnNpcKilledAsync_FluwitonFinalBoss_TriggersVictoryAndDoubleTrophy()
    {
        var killer = CreateTestSession(101, zoneId: UnderTheCastleService.UtcZoneId, level: 80);

        _service.Start(60);

        var fluwiton = new NpcInstance
        {
            UniqueId = 5005,
            NpcId = UnderTheCastleService.FluwitonFinalBossNpcId,
            ZoneId = UnderTheCastleService.UtcZoneId,
            IsMonster = true
        };

        await _service.OnNpcKilledAsync(fluwiton, killer);

        await _itemGrant.Received(1).GrantAsync(killer, Arg.Is<ItemData>(i => i.Num == UnderTheCastleService.TrophyOfFlameItemId), 2);
    }
}
