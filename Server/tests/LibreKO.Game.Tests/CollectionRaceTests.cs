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

public class CollectionRaceTests
{
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameDataService;
    private readonly IUserNotificationService _userNotificationService;
    private readonly IPlayerProgressionService _playerProgressionService;
    private readonly ILoyaltyService _loyaltyService;
    private readonly ILogger<CollectionRaceService> _logger;
    private readonly CollectionRaceService _service;

    public CollectionRaceTests()
    {
        _sessionManager = new SessionManager();
        _gameDataService = Substitute.For<IGameDataService>();
        _userNotificationService = Substitute.For<IUserNotificationService>();
        _playerProgressionService = Substitute.For<IPlayerProgressionService>();
        _loyaltyService = Substitute.For<ILoyaltyService>();
        _logger = Substitute.For<ILogger<CollectionRaceService>>();

        var testSettings = new Dictionary<int, CollectionRaceSettingsData>
        {
            [1] = new()
            {
                EventIndex = 1,
                EventName = "Test Moradon Race",
                ZoneId = 21,
                MinLevel = 1,
                MaxLevel = 83,
                DurationMinutes = 60,
                Target1ProtoId = 100,
                Target1Count = 2,
                Target2ProtoId = 101,
                Target2Count = 1,
                EnemyKillCount = 1,
                AutoStart = true,
                AutoHours = "12,18",
                AutoDays = "All"
            }
        };

        var testRewards = new List<CollectionRaceRewardData>
        {
            new() { Id = 1, EventIndex = 1, ItemId = InventoryConstants.ItemGold, ItemCount = 50000, Rate = 100 },
            new() { Id = 2, EventIndex = 1, ItemId = InventoryConstants.ItemExperience, ItemCount = 25000, Rate = 100 }
        };

        _gameDataService.CollectionRaceSettingsTable.Returns(testSettings);
        _gameDataService.CollectionRaceRewardsByEventIndex.Returns(testRewards.ToLookup(x => x.EventIndex));

        _service = new CollectionRaceService(
            _sessionManager,
            _gameDataService,
            _userNotificationService,
            _playerProgressionService,
            _loyaltyService,
            _logger);
    }

    [Fact]
    public async Task StartEventAsync_WithValidIndex_ActivatesEvent()
    {
        await _service.StartEventAsync(1);

        _service.ActiveEvent.Should().NotBeNull();
        _service.ActiveEvent!.EventIndex.Should().Be(1);
        _service.ActiveEvent.EventName.Should().Be("Test Moradon Race");
        _service.RemainingSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EndEventAsync_DeactivatesEvent()
    {
        await _service.StartEventAsync(1);
        _service.ActiveEvent.Should().NotBeNull();

        await _service.EndEventAsync();
        _service.ActiveEvent.Should().BeNull();
        _service.RemainingSeconds.Should().Be(0);
    }

    [Fact]
    public async Task HandleNpcKillAsync_UpdatesTargetProgress_AndCompletesWithRewards()
    {
        await _service.StartEventAsync(1);

        var client = Substitute.For<IClient>();
        var player = _sessionManager.CreateSession(client, 1, 1);
        player.Name = "HeroPlayer";
        player.ZoneId = 21;
        player.Level = 50;
        player.Money = 1000;
        player.Nation = AccountNation.Karus;

        var npc1 = new NpcInstance { NpcId = 100, ZoneId = 21 };
        var npc2 = new NpcInstance { NpcId = 101, ZoneId = 21 };

        // Kill target 1 twice (needed: 2)
        await _service.HandleNpcKillAsync(npc1, player);
        await _service.HandleNpcKillAsync(npc1, player);

        // Kill target 2 once (needed: 1)
        await _service.HandleNpcKillAsync(npc2, player);

        // Kill 1 enemy nation player (needed: 1)
        var victim = new UserSession(Substitute.For<IClient>(), 2, 2)
        {
            ZoneId = 21,
            Level = 50,
            Nation = AccountNation.ElMorad
        };
        await _service.HandlePlayerKillAsync(victim, player);

        // Verify rewards awarded
        player.Money.Should().Be(1000 + 50000);
        await _playerProgressionService.Received(1).AwardExperienceAsync(player, 25000);
    }

    [Fact]
    public async Task HandleNpcKillAsync_IgnoredIfDifferentZoneOrLevel()
    {
        await _service.StartEventAsync(1);

        var client = Substitute.For<IClient>();
        var outOfZonePlayer = _sessionManager.CreateSession(client, 3, 3);
        outOfZonePlayer.Name = "FarPlayer";
        outOfZonePlayer.ZoneId = 1; // Different zone (Active is 21)
        outOfZonePlayer.Level = 50;

        var npc = new NpcInstance { NpcId = 100, ZoneId = 1 };
        await _service.HandleNpcKillAsync(npc, outOfZonePlayer);

        await client.DidNotReceive().SendPacket(Arg.Any<Packet>());
    }

    [Fact]
    public async Task DeliverRewards_NationalPoints_CallsLoyaltyService()
    {
        var testSettings = new Dictionary<int, CollectionRaceSettingsData>
        {
            [2] = new() { EventIndex = 2, EventName = "NP Race", ZoneId = 21, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60, Target1ProtoId = 100, Target1Count = 1 }
        };
        var testRewards = new List<CollectionRaceRewardData>
        {
            new() { Id = 3, EventIndex = 2, ItemId = InventoryConstants.ItemLadderPoint, ItemCount = 500, Rate = 100 }
        };
        _gameDataService.CollectionRaceSettingsTable.Returns(testSettings);
        _gameDataService.CollectionRaceRewardsByEventIndex.Returns(testRewards.ToLookup(x => x.EventIndex));

        await _service.StartEventAsync(2);
        var client = Substitute.For<IClient>();
        var player = _sessionManager.CreateSession(client, 10, 10);
        player.ZoneId = 21;
        player.Level = 50;

        await _service.HandleNpcKillAsync(new NpcInstance { NpcId = 100, ZoneId = 21 }, player);

        await _loyaltyService.Received(1).ChangeAsync(player, 500);
    }

    [Fact]
    public async Task DeliverRewards_NonCountableItem_PlacesOnePerSlot()
    {
        var testSettings = new Dictionary<int, CollectionRaceSettingsData>
        {
            [3] = new() { EventIndex = 3, EventName = "Weapon Race", ZoneId = 21, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60, Target1ProtoId = 100, Target1Count = 1 }
        };
        var testRewards = new List<CollectionRaceRewardData>
        {
            new() { Id = 4, EventIndex = 3, ItemId = 110010000, ItemCount = 2, Rate = 100 }
        };
        var itemData = new ItemData { Num = 110010000, Name = "Dagger", Countable = 0, Duration = 5000 };
        _gameDataService.CollectionRaceSettingsTable.Returns(testSettings);
        _gameDataService.CollectionRaceRewardsByEventIndex.Returns(testRewards.ToLookup(x => x.EventIndex));
        _gameDataService.GetItem(110010000).Returns(itemData);

        await _service.StartEventAsync(3);
        var client = Substitute.For<IClient>();
        var player = _sessionManager.CreateSession(client, 20, 20);
        player.ZoneId = 21;
        player.Level = 50;

        await _service.HandleNpcKillAsync(new NpcInstance { NpcId = 100, ZoneId = 21 }, player);

        // Verify two separate slots were used, each with count 1
        var filledSlots = player.Inventory.Where(s => s.ItemId == 110010000).ToList();
        filledSlots.Should().HaveCount(2);
        filledSlots.All(s => s.Count == 1).Should().BeTrue();
    }
}
