using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BifrostTests
{
    private readonly SessionManager _sessions = new();
    private readonly IZoneTransitionService _zoneTransition = Substitute.For<IZoneTransitionService>();
    private readonly ILogger<BifrostEventService> _logger = Substitute.For<ILogger<BifrostEventService>>();
    private readonly IBifrostEventService _bifrostService;

    public BifrostTests()
    {
        _bifrostService = new BifrostEventService(_sessions, Options.Create(new GameServerSettings()), new ScheduleClock(), _zoneTransition, _logger);
    }

    private static UserSession CreateSession(int id, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        return new UserSession(client, id, id) { Name = name, Hp = 100, MaxHp = 100 };
    }

    [Fact]
    public void Start_InitializesMonumentFightPhase()
    {
        _bifrostService.Start(monumentMinutes: 20, loserDelayMinutes: 15, farmingMinutes: 60);

        _bifrostService.IsActive.Should().BeTrue();
        _bifrostService.Phase.Should().Be(BifrostPhase.MonumentFight);
        _bifrostService.WinnerNation.Should().Be(AccountNation.None);
        _bifrostService.RemainingSecs.Should().Be(20 * 60);
    }

    [Fact]
    public async Task MonumentKilled_ByKarusPlayer_TransitionsToFarmingPhaseWithKarusAsWinner()
    {
        _bifrostService.Start(monumentMinutes: 30, loserDelayMinutes: 30, farmingMinutes: 60);

        var killerSession = CreateSession(1, "KarusHero");
        killerSession.Nation = AccountNation.Karus;

        var monument = new NpcInstance
        {
            NpcId = BifrostRules.MonumentNpcId,
            ZoneId = BifrostRules.ZoneRonarkLand,
            Hp = 0
        };

        await _bifrostService.OnMonumentKilledAsync(monument, killerSession);

        _bifrostService.Phase.Should().Be(BifrostPhase.Farming);
        _bifrostService.WinnerNation.Should().Be(AccountNation.Karus);
        _bifrostService.RemainingSecs.Should().Be(60 * 60);
        _bifrostService.LoserDelayRemainingSecs.Should().Be(30 * 60);
    }

    [Fact]
    public void CanEnter_EnforcesWinnerEarlyAccessAndLoserDelay()
    {
        var karusUser = CreateSession(1, "KarusPlayer");
        karusUser.Nation = AccountNation.Karus;

        var elmoUser = CreateSession(2, "ElmoPlayer");
        elmoUser.Nation = AccountNation.ElMorad;

        // 1. Not active
        _bifrostService.Close();
        _bifrostService.CanEnter(karusUser).Should().Be(BifrostEntryResult.NotActive);

        // 2. Monument phase
        _bifrostService.Start();
        _bifrostService.CanEnter(karusUser).Should().Be(BifrostEntryResult.MonumentNotFallen);

        // 3. Farming phase with Karus winner
        _bifrostService.SetWinner(AccountNation.Karus);
        _bifrostService.CanEnter(karusUser).Should().Be(BifrostEntryResult.Success);
        _bifrostService.CanEnter(elmoUser).Should().Be(BifrostEntryResult.LoserDelayActive);
    }

    [Fact]
    public async Task TryEnterAsync_WarpsPlayerToNationSafetyCoordinates()
    {
        var transitionMock = Substitute.For<IZoneTransitionService>();
        var bifrostService = new BifrostEventService(
            _sessions,
            Options.Create(new GameServerSettings()),
            new ScheduleClock(),
            transitionMock,
            Substitute.For<ILogger<BifrostEventService>>());

        bifrostService.SetWinner(AccountNation.Karus);

        var karusUser = CreateSession(1, "KarusPlayer");
        karusUser.Nation = AccountNation.Karus;

        var elmoUser = CreateSession(2, "ElmoPlayer");
        elmoUser.Nation = AccountNation.ElMorad;

        // Karus enters immediately
        var enteredKarus = await bifrostService.TryEnterAsync(karusUser);
        enteredKarus.Should().BeTrue();
        await transitionMock.Received(1).ChangeZoneAsync(karusUser, BifrostRules.ZoneBifrost, BifrostRules.KarusEntryX, BifrostRules.KarusEntryZ);

        // El Morad blocked during delay
        var enteredElmo = await bifrostService.TryEnterAsync(elmoUser);
        enteredElmo.Should().BeFalse();
    }

    [Fact]
    public async Task MonumentTimeout_ResultsInDrawAndClosesEvent()
    {
        _bifrostService.Start(monumentMinutes: 1, loserDelayMinutes: 1, farmingMinutes: 1);

        for (int i = 0; i < 60; i++)
        {
            await _bifrostService.TickAsync();
        }

        _bifrostService.IsActive.Should().BeFalse();
        _bifrostService.Phase.Should().Be(BifrostPhase.Inactive);
    }

    [Fact]
    public async Task FarmingTimeout_SweepsZone31Players()
    {
        var transitionMock = Substitute.For<IZoneTransitionService>();
        var bifrostService = new BifrostEventService(
            _sessions,
            Options.Create(new GameServerSettings()),
            new ScheduleClock(),
            transitionMock,
            Substitute.For<ILogger<BifrostEventService>>());

        bifrostService.SetWinner(AccountNation.Karus);

        var playerInBifrost = CreateSession(10, "BifrostFarmer");
        playerInBifrost.Nation = AccountNation.Karus;
        playerInBifrost.ZoneId = BifrostRules.ZoneBifrost;
        _sessions.RegisterSession(playerInBifrost);

        // Simulate ticks until farming expires (default 60 minutes)
        for (int i = 0; i < 60 * 60; i++)
        {
            await bifrostService.TickAsync();
        }

        bifrostService.IsActive.Should().BeFalse();
        await transitionMock.Received().ChangeZoneAsync(
            playerInBifrost,
            BifrostRules.ZoneRonarkLand,
            0f,
            0f);
    }

    [Fact]
    public void CharacterReconnectZoneRepair_RepairsZone31_ToMoradon()
    {
        var karusChar = new LibreKO.Common.Domain.Entities.Character
        {
            MapId = BifrostRules.ZoneBifrost,
            Race = (byte)CharacterRace.KarusTuarek
        };

        var repaired = CharacterReconnectZoneRepair.TryRepair(
            karusChar,
            AccountNation.Karus,
            Substitute.For<IGameDataService>());

        repaired.Should().BeTrue();
        karusChar.MapId.Should().Be((byte)CharacterReconnectZoneRepair.SafeReconnectZoneId);
    }

    [Fact]
    public async Task GateNpcInteraction_CallsBifrostTryEnter()
    {
        var bifrostMock = Substitute.For<IBifrostEventService>();
        var session = CreateSession(1, "GateUser");
        session.ZoneId = BifrostRules.ZoneRonarkLand;
        _sessions.RegisterSession(session);

        var gateNpc = new NpcInstance
        {
            NpcId = BifrostRules.GateNpcId,
            ZoneId = BifrostRules.ZoneRonarkLand,
            Hp = 1000,
            MaxHp = 1000,
            X = session.X,
            Z = session.Z
        };
        _sessions.Regions.SpawnNpc(gateNpc);

        var coordinator = new QuestNpcInteractionService(
            _sessions,
            Substitute.For<IGameDataService>(),
            Substitute.For<IQuestDialogRunner>(),
            Substitute.For<IKingSystemRuntimeService>(),
            Substitute.For<IDrakiTowerService>(),
            Substitute.For<IDrakiStageProvider>(),
            Substitute.For<ITouchTrapService>(),
            Substitute.For<IChaosDungeonService>(),
            bifrostMock,
            Substitute.For<ILogger<QuestNpcInteractionService>>(),
            Substitute.For<IFullMoonRiftService>(),
            Substitute.For<IItemGrantService>(),
            Substitute.For<IMagicItemUsageService>());

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(1); // regular click
        packet.WriteInt(gateNpc.UniqueId);

        await coordinator.HandleNpcEventAsync(session.Client, packet);

        await bifrostMock.Received(1).TryEnterAsync(session);
    }

    [Fact]
    public async Task ZoneAbilitySent_SendsRemainingPacket_WhenPlayerEntersRonarkLandAndEventActive()
    {
        var transitionMock = Substitute.For<IZoneTransitionService>();
        var bifrostService = new BifrostEventService(
            _sessions,
            Options.Create(new GameServerSettings()),
            new ScheduleClock(),
            transitionMock,
            Substitute.For<ILogger<BifrostEventService>>());

        bifrostService.Start(monumentMinutes: 20);

        var ronarkPlayer = CreateSession(1, "RonarkArrival");
        ronarkPlayer.ZoneId = BifrostRules.ZoneRonarkLand;

        transitionMock.ZoneAbilitySent += Raise.Event<Func<UserSession, Task>>(ronarkPlayer);

        await ronarkPlayer.Client.Received().SendPacket(Arg.Is<Packet>(p =>
            p.GetOpcode() == (byte)GameOpcodes.GS_BIFROST));
    }

    [Fact]
    public async Task ZoneAbilitySent_DoesNotSendPacket_WhenPlayerEntersOtherZone()
    {
        var transitionMock = Substitute.For<IZoneTransitionService>();
        var bifrostService = new BifrostEventService(
            _sessions,
            Options.Create(new GameServerSettings()),
            new ScheduleClock(),
            transitionMock,
            Substitute.For<ILogger<BifrostEventService>>());

        bifrostService.Start(monumentMinutes: 20);

        var moradonPlayer = CreateSession(1, "MoradonArrival");
        moradonPlayer.ZoneId = (byte)ZoneId.Moradon;

        transitionMock.ZoneAbilitySent += Raise.Event<Func<UserSession, Task>>(moradonPlayer);

        await moradonPlayer.Client.DidNotReceive().SendPacket(Arg.Any<Packet>());
    }
}
