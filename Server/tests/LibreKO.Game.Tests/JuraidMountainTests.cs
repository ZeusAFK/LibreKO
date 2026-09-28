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

public class JuraidMountainTests
{
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameDataService;
    private readonly IMonsterAggressionPolicy _aggressionPolicy;
    private readonly IZoneTransitionService _zoneTransitionService;
    private readonly InstanceRoomRegistry _instanceRooms;
    private readonly IUserNotificationService _userNotificationService;
    private readonly ILoyaltyService _loyaltyService;
    private readonly ICombatNotificationService _combatNotificationService;
    private readonly ILogger<JuraidMountainService> _logger;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly List<NpcPosData> _npcPositions;

    public JuraidMountainTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _sessionManager = new SessionManager();
        _gameDataService = Substitute.For<IGameDataService>();
        _aggressionPolicy = Substitute.For<IMonsterAggressionPolicy>();
        _zoneTransitionService = Substitute.For<IZoneTransitionService>();
        _zoneTransitionService.ChangeZoneAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(callInfo =>
            {
                var s = callInfo.Arg<UserSession>();
                s.ZoneId = callInfo.Arg<byte>();
                s.X = callInfo.Arg<float>();
                s.Z = callInfo.Arg<float>();
                return Task.CompletedTask;
            });
        _instanceRooms = new InstanceRoomRegistry(_sessionManager, Substitute.For<ILogger<InstanceRoomRegistry>>());
        _userNotificationService = Substitute.For<IUserNotificationService>();
        _loyaltyService = Substitute.For<ILoyaltyService>();
        _combatNotificationService = Substitute.For<ICombatNotificationService>();
        _logger = Substitute.For<ILogger<JuraidMountainService>>();

        _npcPositions =
        [
            // Karus Stage 1: LeftX < 400, TopZ between 540 and 650
            new NpcPosData { Index = 1, ZoneId = 87, Room = 1, NpcId = 8101, LeftX = 224, TopZ = 600, NumNPC = 2 },
            // Karus Bridge 1: Trap 1
            new NpcPosData { Index = 2, ZoneId = 87, Room = 1, NpcId = 8110, LeftX = 224, TopZ = 645, NumNPC = 1, TrapNumber = 1 },
            // Karus Stage 2: LeftX < 400, TopZ >= 800
            new NpcPosData { Index = 3, ZoneId = 87, Room = 1, NpcId = 8102, LeftX = 224, TopZ = 850, NumNPC = 2 },
            // Karus Bridge 2: Trap 2
            new NpcPosData { Index = 4, ZoneId = 87, Room = 1, NpcId = 8110, LeftX = 309, TopZ = 848, NumNPC = 1, TrapNumber = 2 },
            // Karus Stage 3: LeftX 400..600, TopZ >= 800
            new NpcPosData { Index = 5, ZoneId = 87, Room = 1, NpcId = 8103, LeftX = 500, TopZ = 850, NumNPC = 2 },
            // Karus Bridge 3: Trap 3 (to Devabird center)
            new NpcPosData { Index = 6, ZoneId = 87, Room = 1, NpcId = 8110, LeftX = 512, TopZ = 767, NumNPC = 1, TrapNumber = 3 },
            // Devabird in center
            new NpcPosData { Index = 7, ZoneId = 87, Room = 1, NpcId = 8106, LeftX = 510, TopZ = 510, NumNPC = 1 }
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
    }

    private UserSession CreateTestSession(int charId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = _sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = nation;
        session.Level = 70;
        session.ZoneId = 21; // Moradon
        return session;
    }

    [Fact]
    public async Task StartMatchForCallerAsync_CreatesRoomAndSpawnsMonstersAndBridges()
    {
        var service = new JuraidMountainService(
            _sessionManager,
            _gameDataService,
            _aggressionPolicy,
            _zoneTransitionService,
            _instanceRooms,
            _userNotificationService,
            _loyaltyService,
            _combatNotificationService,
            _logger);

        var caller = CreateTestSession(1, AccountNation.Karus);

        await service.StartMatchForCallerAsync(caller);

        service.HasActiveMatches.Should().BeTrue();
        caller.Room.Should().Be(1);

        // Verify participant was warped to Karus start coordinates (224, 272)
        await _zoneTransitionService.Received(1)
            .ChangeZoneAsync(caller, (byte)ZoneId.JuradMountain, 224f, 272f);

        // Verify NPCs were spawned in instance room
        var room = _instanceRooms.Get(1);
        room.Should().NotBeNull();
        // 2 Stage1 + 1 Bridge1 + 2 Stage2 + 1 Bridge2 + 2 Stage3 + 1 Bridge3 + 1 Devabird = 10 NPCs spawned
        room!.Npcs.Should().HaveCount(10);
    }

    [Fact]
    public async Task StageClear_UnlocksBridgeProgressively()
    {
        var service = new JuraidMountainService(
            _sessionManager,
            _gameDataService,
            _aggressionPolicy,
            _zoneTransitionService,
            _instanceRooms,
            _userNotificationService,
            _loyaltyService,
            _combatNotificationService,
            _logger);

        var karusPlayer = CreateTestSession(1, AccountNation.Karus);
        await service.StartMatchForCallerAsync(karusPlayer);

        var room = _instanceRooms.Get(1)!;
        var stage1Monsters = room.Npcs.Where(n => n.NpcId == 8101).ToList();
        stage1Monsters.Should().HaveCount(2);

        var bridge1 = room.Npcs.FirstOrDefault(n => n.NpcId == 8110 && n.TrapNumber == 1);
        bridge1.Should().NotBeNull();

        // Kill first Stage 1 monster -> Bridge 1 should still be closed
        await service.OnNpcKilledAsync(stage1Monsters[0], karusPlayer);
        bridge1!.GateOpen.Should().BeFalse();

        // Kill second Stage 1 monster -> Stage 1 cleared! Bridge 1 should open
        await service.OnNpcKilledAsync(stage1Monsters[1], karusPlayer);
        bridge1.GateOpen.Should().BeTrue();
    }

    [Fact]
    public async Task DevabirdKilled_DeclaresWinner_DistributesSilveryGemsAndLoyalty()
    {
        var service = new JuraidMountainService(
            _sessionManager,
            _gameDataService,
            _aggressionPolicy,
            _zoneTransitionService,
            _instanceRooms,
            _userNotificationService,
            _loyaltyService,
            _combatNotificationService,
            _logger);

        var karusPlayer = CreateTestSession(1, AccountNation.Karus);
        var elmoPlayer = CreateTestSession(2, AccountNation.ElMorad);

        await service.StartMatchesAsync([karusPlayer.CharacterId, elmoPlayer.CharacterId], 600);

        var room = _instanceRooms.Get(1)!;
        var devabird = room.Npcs.FirstOrDefault(n => n.NpcId == 8106);
        devabird.Should().NotBeNull();

        // Simulate Karus killing Devabird
        await service.OnNpcKilledAsync(devabird!, karusPlayer);

        // Winning nation (Karus) receives 500 NP
        await _loyaltyService.Received(1).ChangeAsync(karusPlayer, JuraidMountainService.LoyaltyWinBonus);

        // Karus receives 2x Silvery Gem (389196000)
        var karusGemSlot = karusPlayer.Inventory.FirstOrDefault(s => s.ItemId == JuraidMountainService.SilveryGemItemId);
        karusGemSlot.Should().NotBeNull();
        karusGemSlot!.Count.Should().Be(2);

        // Losing nation (El Morad) receives 1x Black Gem (389205000)
        var elmoGemSlot = elmoPlayer.Inventory.FirstOrDefault(s => s.ItemId == JuraidMountainService.BlackGemItemId);
        elmoGemSlot.Should().NotBeNull();
        elmoGemSlot!.Count.Should().Be(1);
    }

    [Fact]
    public void PrintDungeon05Details()
    {
        var smd = SmdFile.Load("D:/LibreKO/Server/LibreKO.Game/Map/In_dungeon05.smd");
        smd.Should().NotBeNull();

        float[] zTests = [200f, 250f, 272f, 300f, 350f, 400f, 450f, 480f, 500f, 515f, 550f, 600f, 645f, 700f, 749f, 800f, 850f];
        _output.WriteLine("=== KARUS LINE (X=224) ===");
        foreach (var z in zTests)
        {
            float h = smd!.GetHeight(224f, z);
            _output.WriteLine($"  (224, {z}): H={h}");
        }

        _output.WriteLine("\n=== EL MORAD LINE (X=800) ===");
        foreach (var z in zTests)
        {
            float h = smd!.GetHeight(800f, z);
            _output.WriteLine($"  (800, {z}): H={h}");
        }
    }

    [Fact]
    public void BridgeWalkabilityCheck()
    {
        var smd = SmdFile.Load("D:/LibreKO/Server/LibreKO.Game/Map/In_dungeon05.smd");
        smd.Should().NotBeNull();

        _output.WriteLine("=== BRIDGE 1 (X=224, Z=635..665) ===");
        for (float z = 635f; z <= 665f; z += 1f)
        {
            float h = smd!.GetHeight(224f, z);
            _output.WriteLine($"  (224, {z:000}): H={h}");
        }

        _output.WriteLine("\n=== BRIDGE 4 ELMO (X=800, Z=360..390) ===");
        for (float z = 360f; z <= 390f; z += 1f)
        {
            float h = smd!.GetHeight(800f, z);
            _output.WriteLine($"  (800, {z:000}): H={h}");
        }

        _output.WriteLine("\n=== BRIDGE 5 ELMO (X=690..730, Z=172) ===");
        for (float x = 690f; x <= 730f; x += 1f)
        {
            float h = smd!.GetHeight(x, 172f);
            _output.WriteLine($"  ({x:000}, 172): H={h}");
        }

        _output.WriteLine("\n=== BRIDGE 2 KARUS (X=290..325, Z=848) ===");
        for (float x = 290f; x <= 325f; x += 1f)
        {
            float h = smd!.GetHeight(x, 848f);
            _output.WriteLine($"  ({x:000}, 848): H={h}");
        }
    }

    [Fact]
    public void PrintCenterRoomDetails()
    {
        var smd = SmdFile.Load("D:/LibreKO/Server/LibreKO.Game/Map/In_dungeon05.smd");
        smd.Should().NotBeNull();

        _output.WriteLine("=== CENTER DEVA LINE (X=510, Z=200..800) ===");
        for (float z = 200f; z <= 800f; z += 25f)
        {
            float h = smd!.GetHeight(510f, z);
            _output.WriteLine($"  (510, {z}): H={h}");
        }

        _output.WriteLine("=== DEVA ROOM AREA (X=480..540, Z=480..540) ===");
        for (float x = 480f; x <= 540f; x += 15f)
        {
            for (float z = 480f; z <= 540f; z += 15f)
            {
                float h = smd!.GetHeight(x, z);
                _output.WriteLine($"  ({x}, {z}): H={h}");
            }
        }
    }

    [Fact]
    public void JuraidMountainSchedule_DailySchedule_MatchesAfternoonAndNight()
    {
        var afternoon = new JuraidMountainScheduleData { Id = 1, Day = null, Hour = 13, Minute = 0 };
        var night = new JuraidMountainScheduleData { Id = 2, Day = null, Hour = 21, Minute = 0 };

        var matchAfternoon = new DateTime(2026, 9, 28, 13, 0, 0);
        var matchNight = new DateTime(2026, 9, 28, 21, 0, 0);
        var mismatchMinute = new DateTime(2026, 9, 28, 13, 1, 0);
        var mismatchHour = new DateTime(2026, 9, 28, 14, 0, 0);

        afternoon.Matches(matchAfternoon).Should().BeTrue();
        afternoon.Matches(mismatchMinute).Should().BeFalse();
        afternoon.Matches(mismatchHour).Should().BeFalse();

        night.Matches(matchNight).Should().BeTrue();
        night.Matches(matchAfternoon).Should().BeFalse();
    }

    [Fact]
    public void JuraidMountainSchedule_SpecificDay_MatchesOnlyOnThatDay()
    {
        var sundaySchedule = new JuraidMountainScheduleData { Id = 3, Day = DayOfWeek.Sunday, Hour = 13, Minute = 0 };

        var sunday = new DateTime(2026, 9, 27, 13, 0, 0); // Sunday
        var monday = new DateTime(2026, 9, 28, 13, 0, 0); // Monday

        sunday.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        monday.DayOfWeek.Should().Be(DayOfWeek.Monday);

        sundaySchedule.Matches(sunday).Should().BeTrue();
        sundaySchedule.Matches(monday).Should().BeFalse();
    }

    [Fact]
    public void JuraidMountainSchedule_MinLevel_DefaultsTo40()
    {
        var schedule = new JuraidMountainScheduleData { Id = 1, Hour = 13, Minute = 0 };
        schedule.MinLevel.Should().Be(40);
        schedule.MaxLevel.Should().Be(83);
        schedule.CountdownMinutes.Should().Be(10);
    }

    [Fact]
    public void JuraidMountainSchedule_CountdownMinutes_CanBeConfigured()
    {
        var schedule = new JuraidMountainScheduleData { Id = 1, CountdownMinutes = 5 };
        schedule.CountdownMinutes.Should().Be(5);
    }

    [Fact]
    public async Task JuraidMountainService_StartMatchForCaller_EnforcesMinLevel()
    {
        var service = new JuraidMountainService(
            _sessionManager,
            _gameDataService,
            _aggressionPolicy,
            _zoneTransitionService,
            _instanceRooms,
            _userNotificationService,
            _loyaltyService,
            _combatNotificationService,
            _logger);

        _gameDataService.JuraidMountainSchedules.Returns([
            new JuraidMountainScheduleData { Id = 1, MinLevel = 40, MaxLevel = 83 }
        ]);

        var lowLevelCaller = CreateTestSession(101, AccountNation.Karus);
        lowLevelCaller.Level = 35;

        await service.StartMatchForCallerAsync(lowLevelCaller);

        lowLevelCaller.ZoneId.Should().Be(21);
        await _zoneTransitionService.DidNotReceiveWithAnyArgs()
            .ChangeZoneAsync(lowLevelCaller, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());

        var highLevelCaller = CreateTestSession(102, AccountNation.Karus);
        highLevelCaller.Level = 45;

        await service.StartMatchForCallerAsync(highLevelCaller);

        highLevelCaller.ZoneId.Should().Be((byte)ZoneId.JuradMountain);
    }
}


