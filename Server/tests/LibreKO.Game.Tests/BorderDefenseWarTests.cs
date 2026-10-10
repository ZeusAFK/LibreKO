using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BorderDefenseWarTests
{
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameDataService;
    private readonly IMonsterAggressionPolicy _aggressionPolicy;
    private readonly IZoneTransitionService _zoneTransitionService;
    private readonly InstanceRoomRegistry _instanceRooms;
    private readonly IInstanceEntryService _instanceEntryService;
    private readonly IUserNotificationService _userNotificationService;
    private readonly ILoyaltyService _loyaltyService;
    private readonly IPlayerProgressionService _playerProgressionService;
    private readonly ICombatNotificationService _combatNotificationService;
    private readonly IMagicStatusEffectService _magicStatusEffectService;
    private readonly ILogger<BorderDefenseWarService> _logger;
    private readonly List<NpcPosData> _npcPositions;

    public BorderDefenseWarTests()
    {
        _sessionManager = new SessionManager();
        _gameDataService = Substitute.For<IGameDataService>();
        _aggressionPolicy = Substitute.For<IMonsterAggressionPolicy>();
        _zoneTransitionService = Substitute.For<IZoneTransitionService>();
        _zoneTransitionService.ChangeZoneAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(callInfo =>
            {
                var s = callInfo.ArgAt<UserSession>(0);
                s.ZoneId = callInfo.ArgAt<byte>(1);
                s.X = callInfo.ArgAt<float>(2);
                s.Z = callInfo.ArgAt<float>(3);
                return Task.CompletedTask;
            });
        _instanceRooms = new InstanceRoomRegistry(_sessionManager, Substitute.For<ILogger<InstanceRoomRegistry>>());
        _instanceEntryService = Substitute.For<IInstanceEntryService>();
        _userNotificationService = Substitute.For<IUserNotificationService>();
        _loyaltyService = Substitute.For<ILoyaltyService>();
        _playerProgressionService = Substitute.For<IPlayerProgressionService>();
        _combatNotificationService = Substitute.For<ICombatNotificationService>();
        _magicStatusEffectService = Substitute.For<IMagicStatusEffectService>();
        _logger = Substitute.For<ILogger<BorderDefenseWarService>>();

        _npcPositions =
        [
            new NpcPosData { Index = 1, ZoneId = 84, Room = 1, NpcId = 9840, LeftX = 127, TopZ = 131, NumNPC = 1 },
            new NpcPosData { Index = 2, ZoneId = 84, Room = 1, NpcId = 8161, LeftX = 42, TopZ = 49, NumNPC = 1 },
            new NpcPosData { Index = 3, ZoneId = 84, Room = 1, NpcId = 8162, LeftX = 47, TopZ = 49, NumNPC = 1 }
        ];

        _gameDataService.NpcPositions.Returns(_npcPositions);
        _gameDataService.GetSpawnProto(Arg.Any<NpcPosData>()).Returns(callInfo =>
        {
            var pos = callInfo.Arg<NpcPosData>();
            return new NpcData
            {
                Id = (short)pos.NpcId,
                Name = $"NPC_{pos.NpcId}",
                Hp = 1000,
                Mp = 100,
                IsMonster = true
            };
        });

        _gameDataService.GetItem(Arg.Any<int>()).Returns(callInfo =>
        {
            var itemId = callInfo.Arg<int>();
            return new ItemData
            {
                Num = itemId,
                Name = $"Item_{itemId}",
                Duration = 100,
                Weight = 10,
                Countable = 1
            };
        });

        _instanceEntryService.When(x => x.Populate(Arg.Any<InstanceRoom>()))
            .Do(callInfo =>
            {
                var room = callInfo.Arg<InstanceRoom>();
                foreach (var pos in _npcPositions.Where(p => p.ZoneId == BorderDefenseWarService.BdwZoneId))
                {
                    var proto = _gameDataService.GetSpawnProto(pos)!;
                    var npc = NpcInstance.FromData(proto, pos, 0);
                    npc.Room = room.Id;
                    room.Npcs.Add(npc);
                }
            });
    }

    private BorderDefenseWarService CreateService() =>
        new(
            _sessionManager,
            _gameDataService,
            _aggressionPolicy,
            _zoneTransitionService,
            _instanceRooms,
            _instanceEntryService,
            _userNotificationService,
            _loyaltyService,
            _playerProgressionService,
            _combatNotificationService,
            _magicStatusEffectService,
            _logger);

    private UserSession CreateTestSession(int charId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = _sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = nation;
        session.Level = 60;
        session.ZoneId = 21;
        return session;
    }

    [Fact]
    public async Task StartMatchForCallerAsync_CreatesRoomAndSpawnsAltarAndWarpsPlayer()
    {
        var service = CreateService();
        var caller = CreateTestSession(1, AccountNation.Karus);

        await service.StartMatchForCallerAsync(caller);

        service.HasActiveMatches.Should().BeTrue();
        caller.Room.Should().Be(1);

        await _zoneTransitionService.Received(1)
            .ChangeZoneAsync(caller, BorderDefenseWarService.BdwZoneId, BorderDefenseWarService.KarusStartX, BorderDefenseWarService.KarusStartZ);

        var room = _instanceRooms.Get(1);
        room.Should().NotBeNull();
        room!.Npcs.Should().Contain(n => n.NpcId == BorderDefenseWarService.AltarOfManesProtoId);
    }

    [Fact]
    public async Task AltarOfManes_CapturedAndDeliveredToBase_AwardsPointsAndSchedulesRespawn()
    {
        var service = CreateService();
        var karusPlayer = CreateTestSession(1, AccountNation.Karus);
        var elmoPlayer = CreateTestSession(2, AccountNation.ElMorad);

        await service.StartMatchesAsync([karusPlayer.CharacterId, elmoPlayer.CharacterId], 1800);

        var room = _instanceRooms.Get(1)!;
        var altar = room.Npcs.First(n => n.NpcId == BorderDefenseWarService.AltarOfManesProtoId);

        _gameDataService.GetMagic(BorderDefenseWarService.FragmentOfManesSkillId)
            .Returns(new MagicData { Id = BorderDefenseWarService.FragmentOfManesSkillId, Type1 = 4 });

        await service.OnNpcKilledAsync(altar, karusPlayer);

        await _magicStatusEffectService.Received(1).ExecuteAsync(
            karusPlayer,
            Arg.Any<MagicData>(),
            MagicSkillType.Buff,
            BorderDefenseWarService.FragmentOfManesSkillId,
            karusPlayer.CharacterId,
            Arg.Any<int[]>());

        karusPlayer.X = 30f;
        karusPlayer.Z = 60f;
        await service.CheckCarrierBaseDeliveryAsync(karusPlayer);

        await _magicStatusEffectService.Received(1).CancelAsync(karusPlayer, BorderDefenseWarService.FragmentOfManesSkillId);

        var match = service.GetMatch(1);
        match.Should().NotBeNull();
        match!.KarusScore.Should().Be(10);
        match.AltarRespawnAtUtc.Should().NotBeNull();

        await service.OnPlayerKilledAsync(elmoPlayer, karusPlayer);

        match.KarusScore.Should().Be(11);
        match.KarusKillCount.Should().Be(1);
    }

    [Fact]
    public async Task ScoreReachesTarget_DeclaresWinner_DistributesRewards()
    {
        _gameDataService.TempleEventRewards.Returns([
            new TempleEventRewardData
            {
                Id = 1,
                Event = TempleEvent.BorderDefenseWar,
                Outcome = TempleEventRewardOutcome.Win,
                MinLevel = 20,
                MaxLevel = 83,
                ItemId = BorderDefenseWarService.RedTreasureChestId,
                ItemCount = 1,
                LoyaltyPoints = 500
            },
            new TempleEventRewardData
            {
                Id = 2,
                Event = TempleEvent.BorderDefenseWar,
                Outcome = TempleEventRewardOutcome.Loss,
                MinLevel = 20,
                MaxLevel = 83,
                ItemId = 900017000,
                ItemCount = 1
            }
        ]);

        var service = CreateService();
        var karusPlayer = CreateTestSession(1, AccountNation.Karus);
        var elmoPlayer = CreateTestSession(2, AccountNation.ElMorad);

        await service.StartMatchesAsync([karusPlayer.CharacterId, elmoPlayer.CharacterId], 1800);

        var room = _instanceRooms.Get(1)!;
        var altar = room.Npcs.First(n => n.NpcId == BorderDefenseWarService.AltarOfManesProtoId);

        _gameDataService.GetMagic(BorderDefenseWarService.FragmentOfManesSkillId)
            .Returns(new MagicData { Id = BorderDefenseWarService.FragmentOfManesSkillId, Type1 = 4 });

        await service.OnNpcKilledAsync(altar, karusPlayer);
        karusPlayer.X = 30f;
        karusPlayer.Z = 60f;
        await service.CheckCarrierBaseDeliveryAsync(karusPlayer);

        for (int i = 0; i < 120; i++)
        {
            await service.OnPlayerKilledAsync(elmoPlayer, karusPlayer);
        }

        var match = service.GetMatch(1);
        match.Should().NotBeNull();
        match!.IsFinishing.Should().BeTrue();
        match.WinnerNation.Should().Be(AccountNation.Karus);

        await service.FinishAllMatchesAsync();

        var chestSlot = karusPlayer.Inventory.FirstOrDefault(s => s.ItemId == BorderDefenseWarService.RedTreasureChestId);
        chestSlot.Should().NotBeNull();
        chestSlot!.Count.Should().BeGreaterThan(0);
        await _loyaltyService.Received(1).ChangeAsync(karusPlayer, 500);
    }

    [Fact]
    public void TempleEventSchedule_DailySchedule_MatchesAfternoonAndNight()
    {
        var afternoon = new TempleEventScheduleData { Id = 1, Event = TempleEvent.BorderDefenseWar, Day = null, Hour = 14, Minute = 0 };
        var night = new TempleEventScheduleData { Id = 2, Event = TempleEvent.BorderDefenseWar, Day = null, Hour = 22, Minute = 0 };

        var matchAfternoon = new DateTime(2026, 9, 28, 14, 0, 0);
        var matchNight = new DateTime(2026, 9, 28, 22, 0, 0);
        var mismatchMinute = new DateTime(2026, 9, 28, 14, 1, 0);
        var mismatchHour = new DateTime(2026, 9, 28, 15, 0, 0);

        afternoon.Matches(matchAfternoon).Should().BeTrue();
        afternoon.Matches(mismatchMinute).Should().BeFalse();
        afternoon.Matches(mismatchHour).Should().BeFalse();

        night.Matches(matchNight).Should().BeTrue();
        night.Matches(matchAfternoon).Should().BeFalse();
    }

    [Fact]
    public void TempleEventSchedule_MinLevel_CanBeConfigured()
    {
        var schedule = new TempleEventScheduleData { Id = 1, Event = TempleEvent.BorderDefenseWar, Hour = 14, Minute = 0, MinLevel = 20, MaxLevel = 83, CountdownMinutes = 10 };
        schedule.MinLevel.Should().Be(20);
        schedule.MaxLevel.Should().Be(83);
        schedule.CountdownMinutes.Should().Be(10);
    }

    [Fact]
    public async Task BorderDefenseWarService_StartMatchForCaller_EnforcesMinLevel()
    {
        var service = CreateService();

        _gameDataService.TempleEventSchedules.Returns([
            new TempleEventScheduleData { Id = 1, Event = TempleEvent.BorderDefenseWar, MinLevel = 20, MaxLevel = 83 }
        ]);

        var lowLevelCaller = CreateTestSession(101, AccountNation.Karus);
        lowLevelCaller.Level = 15;

        await service.StartMatchForCallerAsync(lowLevelCaller);

        lowLevelCaller.ZoneId.Should().Be(21);
        await _zoneTransitionService.DidNotReceiveWithAnyArgs()
            .ChangeZoneAsync(lowLevelCaller, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());

        var highLevelCaller = CreateTestSession(102, AccountNation.Karus);
        highLevelCaller.Level = 45;

        await service.StartMatchForCallerAsync(highLevelCaller);

        highLevelCaller.ZoneId.Should().Be(BorderDefenseWarService.BdwZoneId);
    }

    [Fact]
    public async Task BorderDefenseWarService_StartMatchesAsync_FormsAutoPartyForNation()
    {
        var service = CreateService();

        var karus1 = CreateTestSession(1, AccountNation.Karus);
        var karus2 = CreateTestSession(2, AccountNation.Karus);
        var elmo1 = CreateTestSession(3, AccountNation.ElMorad);
        var elmo2 = CreateTestSession(4, AccountNation.ElMorad);

        await service.StartMatchesAsync([1, 2, 3, 4], 600);

        karus1.IsInParty.Should().BeTrue();
        karus2.IsInParty.Should().BeTrue();
        karus1.PartyIndex.Should().Be(karus2.PartyIndex);
        karus1.IsPartyLeader.Should().BeTrue();
        karus2.IsPartyLeader.Should().BeFalse();

        elmo1.IsInParty.Should().BeTrue();
        elmo2.IsInParty.Should().BeTrue();
        elmo1.PartyIndex.Should().Be(elmo2.PartyIndex);
        elmo1.IsPartyLeader.Should().BeTrue();
        elmo2.IsPartyLeader.Should().BeFalse();
    }

    [Fact]
    public async Task FinishAllMatchesAsync_EqualScore_ResolvesWinnerByKills()
    {
        var service = CreateService();
        var karusPlayer = CreateTestSession(1, AccountNation.Karus);
        var elmoPlayer = CreateTestSession(2, AccountNation.ElMorad);

        await service.StartMatchesAsync([karusPlayer.CharacterId, elmoPlayer.CharacterId], 1800);
        var match = service.GetMatch(1)!;

        // Equal scores, but Karus has 2 kills and El Morad has 1 kill
        match.KarusScore = 50;
        match.ElmoradScore = 50;
        match.KarusKillCount = 2;
        match.ElmoradKillCount = 1;

        await service.FinishAllMatchesAsync();

        match.WinnerNation.Should().Be(AccountNation.Karus);
    }

    [Fact]
    public async Task HandleHomeAsync_CarryingFragmentOfManes_BlocksRecallAndSendsNotice()
    {
        var session = CreateTestSession(1, AccountNation.Karus);
        session.Hp = 1000;
        session.MaxHp = 1000;
        session.ZoneId = BorderDefenseWarService.BdwZoneId;
        session.ActiveBuffs[BorderDefenseWarService.FragmentOfManesSkillId] = new ActiveBuff
        {
            MagicId = BorderDefenseWarService.FragmentOfManesSkillId,
            BuffType = BuffType.FragmentOfManes,
            ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks
        };

        var movementService = new WorldMovementService(
            _sessionManager,
            _gameDataService,
            _zoneTransitionService,
            _userNotificationService,
            _combatNotificationService,
            Substitute.For<ICombatLifecycleService>(),
            Substitute.For<IWorldVisibilityService>(),
            Substitute.For<IMiningPacketCoordinator>(),
            Substitute.For<IStealthService>(),
            Substitute.For<ICollectionRaceService>(),
            CreateService(),
            Substitute.For<IDrakiTowerService>(),
            Substitute.For<ILogger<WorldMovementService>>());

        await movementService.HandleHomeAsync(session.Client);

        await session.Client.Received(1).SendPacket(Arg.Is<Packet>(p =>
            p.GetData()[0] == ChatPacketWriter.TypeSystemNotice));
    }
}
