using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class EventSchedulerTests
{
    private readonly SessionManager _sessionManager;
    private readonly IOptions<GameServerSettings> _settings;
    private readonly IZoneTransitionService _zoneTransition;
    private readonly ICollectionRaceService _collectionRace;
    private readonly ILotteryService _lottery;
    private readonly IJuraidMountainService _juraidMountain;
    private readonly IBorderDefenseWarService _bdw;
    private readonly IUnderTheCastleService _utc;
    private readonly IForgottenTempleService _ft;
    private readonly IGameDataService _gameData;
    private readonly ILogger<EventSchedulerService> _logger;
    private readonly EventSchedulerService _scheduler;

    public EventSchedulerTests()
    {
        _sessionManager = new SessionManager();
        _settings = Options.Create(new GameServerSettings());
        _zoneTransition = Substitute.For<IZoneTransitionService>();
        _collectionRace = Substitute.For<ICollectionRaceService>();
        _lottery = Substitute.For<ILotteryService>();
        _juraidMountain = Substitute.For<IJuraidMountainService>();
        _bdw = Substitute.For<IBorderDefenseWarService>();
        _utc = Substitute.For<IUnderTheCastleService>();
        _ft = Substitute.For<IForgottenTempleService>();
        _gameData = Substitute.For<IGameDataService>();
        _logger = Substitute.For<ILogger<EventSchedulerService>>();

        _scheduler = new EventSchedulerService(
            _sessionManager,
            _settings,
            _zoneTransition,
            _collectionRace,
            _lottery,
            _juraidMountain,
            _bdw,
            _utc,
            _ft,
            _gameData,
            _logger);
    }

    private (UserSession session, List<Packet> sentPackets) CreateTestSession(int charId, int level)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var packets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => packets.Add(p)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = _sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = AccountNation.ElMorad;
        session.Level = (byte)level;
        session.ZoneId = 21;
        return (session, packets);
    }

    [Fact]
    public async Task CallTempleEventAsync_OnlySendsPopupToEligibleLevelPlayers()
    {
        var (lowPlayer, lowPackets) = CreateTestSession(101, 50);
        var (highPlayer, highPackets) = CreateTestSession(102, 70);

        await _scheduler.CallTempleEventAsync(
            TempleEvent.ForgottenTemple,
            joinWindowSeconds: 300,
            autoJoinSession: null,
            minLevel: 60,
            maxLevel: 83);

        // lowPlayer (level 50) is out of range (60-83) -> receives no BifrostRemaining popup
        lowPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_BIFROST);

        // highPlayer (level 70) is in range (60-83) -> receives BifrostRemaining popup
        highPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_BIFROST);
    }


    [Fact]
    public async Task CallTempleEventAsync_OutOfRangeCaller_IsNotAutoJoined()
    {
        var (lowPlayer, _) = CreateTestSession(103, 50);

        await _scheduler.CallTempleEventAsync(
            TempleEvent.ForgottenTemple,
            joinWindowSeconds: 300,
            autoJoinSession: lowPlayer,
            minLevel: 60,
            maxLevel: 83);

        // Out of range caller cannot be auto-joined; trying to join fails
        _scheduler.TryJoinTempleEvent(lowPlayer, out var reason).Should().BeFalse();
        reason.Should().Contain("too low");
    }

    [Fact]
    public async Task CallTempleEventAsync_InRangeCaller_IsAutoJoined()
    {
        var (highPlayer, _) = CreateTestSession(104, 75);

        await _scheduler.CallTempleEventAsync(
            TempleEvent.ForgottenTemple,
            joinWindowSeconds: 300,
            autoJoinSession: highPlayer,
            minLevel: 60,
            maxLevel: 83);

        // In range caller is auto-joined; trying to re-join indicates already registered
        _scheduler.TryJoinTempleEvent(highPlayer, out var reason).Should().BeFalse();
        reason.Should().Contain("already registered");
    }
}
