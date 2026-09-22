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

public class CollectionRaceTests
{
    private const int MoradonRace = 1;
    private const int LufersonRace = 2;
    private const int CollectRace = 3;
    private const int ScheduledRace = 4;
    private const byte Moradon = 21;
    private const byte Luferson = 1;
    private const int Kecoon = 100;
    private const int KecoonWarrior = 101;
    private const int Apple = 810418000;
    private const int Dagger = 110010000;

    private readonly SessionManager _sessionManager = new();
    private readonly IGameDataService _gameDataService = Substitute.For<IGameDataService>();
    private readonly IUserNotificationService _userNotificationService = Substitute.For<IUserNotificationService>();
    private readonly IPlayerProgressionService _playerProgressionService = Substitute.For<IPlayerProgressionService>();
    private readonly IMailService _mailService = Substitute.For<IMailService>();
    private readonly CollectionRaceService _service;

    private readonly Dictionary<int, CollectionRaceData> _races = new()
    {
        [MoradonRace] = new() { Id = MoradonRace, Name = "Test Moradon Race", ZoneId = Moradon, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60 },
        [LufersonRace] = new() { Id = LufersonRace, Name = "Test Luferson Race", ZoneId = Luferson, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60 },
        [CollectRace] = new() { Id = CollectRace, Name = "Apple Harvest", ZoneId = Moradon, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60 },
        [ScheduledRace] = new() { Id = ScheduledRace, Name = "Scheduled", ZoneId = Luferson, MinLevel = 1, MaxLevel = 83, DurationMinutes = 60, AutoStart = true },
    };

    private readonly List<CollectionRaceObjectiveData> _objectives =
    [
        new() { Id = 1, RaceId = MoradonRace, Ordinal = 0, Kind = CollectionRaceObjectiveKind.Monster, TargetId = Kecoon, Count = 2 },
        new() { Id = 2, RaceId = MoradonRace, Ordinal = 1, Kind = CollectionRaceObjectiveKind.Monster, TargetId = KecoonWarrior, Count = 1 },
        new() { Id = 3, RaceId = MoradonRace, Ordinal = 2, Kind = CollectionRaceObjectiveKind.EnemyPlayer, TargetId = 0, Count = 1 },
        new() { Id = 4, RaceId = LufersonRace, Ordinal = 0, Kind = CollectionRaceObjectiveKind.Monster, TargetId = Kecoon, Count = 1 },
        new() { Id = 5, RaceId = CollectRace, Ordinal = 0, Kind = CollectionRaceObjectiveKind.Item, TargetId = Apple, Count = 3 },
        new() { Id = 6, RaceId = ScheduledRace, Ordinal = 0, Kind = CollectionRaceObjectiveKind.Monster, TargetId = Kecoon, Count = 1 },
    ];

    private readonly List<CollectionRaceRewardData> _rewards =
    [
        new() { Id = 1, RaceId = MoradonRace, ItemId = InventoryConstants.ItemGold, ItemCount = 50000 },
        new() { Id = 2, RaceId = MoradonRace, ItemId = InventoryConstants.ItemExperience, ItemCount = 25000 },
        new() { Id = 3, RaceId = MoradonRace, ItemId = InventoryConstants.ItemLadderPoint, ItemCount = 100 },
        new() { Id = 4, RaceId = MoradonRace, ItemId = Dagger, ItemCount = 1 },
        new() { Id = 5, RaceId = CollectRace, ItemId = InventoryConstants.ItemGold, ItemCount = 1000 },
        new() { Id = 6, RaceId = LufersonRace, ItemId = InventoryConstants.ItemGold, ItemCount = 500 },
    ];

    private readonly List<CollectionRaceScheduleData> _schedules = [];

    public CollectionRaceTests()
    {
        _gameDataService.CollectionRaceTable.Returns(_races);
        _gameDataService.CollectionRaceObjectivesByRace.Returns(_ => _objectives.ToLookup(x => x.RaceId));
        _gameDataService.CollectionRaceRewardsByRace.Returns(_ => _rewards.ToLookup(x => x.RaceId));
        _gameDataService.CollectionRaceSchedulesByRace.Returns(_ => _schedules.ToLookup(x => x.RaceId));
        _gameDataService.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>());
        _gameDataService.GetItem(Dagger).Returns(new ItemData { Num = Dagger, Name = "Dagger", Countable = 0, Duration = 5000 });

        _service = new CollectionRaceService(
            _sessionManager,
            _gameDataService,
            _userNotificationService,
            _playerProgressionService,
            _mailService,
            Substitute.For<ILogger<CollectionRaceService>>());
    }

    private UserSession CreatePlayer(int id, byte zoneId, AccountNation nation = AccountNation.Karus, IClient? client = null)
    {
        var player = _sessionManager.CreateSession(client ?? Substitute.For<IClient>(), id, id);
        player.Name = $"Player{id}";
        player.ZoneId = zoneId;
        player.Level = 50;
        player.Nation = nation;
        return player;
    }

    [Fact]
    public async Task StartRaceAsync_ActivatesOneRacePerZone()
    {
        await _service.StartRaceAsync(MoradonRace);
        await _service.StartRaceAsync(CollectRace);
        await _service.StartRaceAsync(LufersonRace);

        _service.ActiveRaces.Should().HaveCount(2);
        _service.GetActive(Moradon)!.Race.Id.Should().Be(MoradonRace);
        _service.GetActive(Luferson)!.Race.Id.Should().Be(LufersonRace);
        _service.GetActive(Moradon)!.RemainingSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EndRaceAsync_DeactivatesOnlyThatRace()
    {
        await _service.StartRaceAsync(MoradonRace);
        await _service.StartRaceAsync(LufersonRace);

        await _service.EndRaceAsync(MoradonRace);

        _service.GetActive(Moradon).Should().BeNull();
        _service.GetActive(Luferson).Should().NotBeNull();

        await _service.EndAllAsync();
        _service.ActiveRaces.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleNpcKillAsync_UpdatesObjectives_AndCompletes()
    {
        await _service.StartRaceAsync(MoradonRace);
        var player = CreatePlayer(1, Moradon);

        var kecoon = new NpcInstance { NpcId = Kecoon, ZoneId = Moradon };
        var warrior = new NpcInstance { NpcId = KecoonWarrior, ZoneId = Moradon };

        await _service.HandleNpcKillAsync(kecoon, player);
        await _service.HandleNpcKillAsync(kecoon, player);
        await _service.HandleNpcKillAsync(kecoon, player);
        await _service.HandleNpcKillAsync(warrior, player);

        var progress = _service.GetActive(Moradon)!.ProgressOf(player);
        progress.Current.Should().Equal(2, 1, 0);
        progress.IsCompleted.Should().BeFalse();

        var victim = new UserSession(Substitute.For<IClient>(), 2, 2) { ZoneId = Moradon, Level = 50, Nation = AccountNation.ElMorad };
        await _service.HandlePlayerKillAsync(victim, player);

        progress.IsCompleted.Should().BeTrue();
        await _mailService.DidNotReceive().SendSystemMailAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>());
    }

    [Fact]
    public async Task EndRaceAsync_MailsEveryRewardToEachCompleter_AndNobodyElse()
    {
        await _service.StartRaceAsync(MoradonRace);
        var winner = CreatePlayer(1, Moradon);
        var partial = CreatePlayer(2, Moradon);

        var kecoon = new NpcInstance { NpcId = Kecoon, ZoneId = Moradon };
        await _service.HandleNpcKillAsync(kecoon, winner);
        await _service.HandleNpcKillAsync(kecoon, winner);
        await _service.HandleNpcKillAsync(new NpcInstance { NpcId = KecoonWarrior, ZoneId = Moradon }, winner);
        var victim = new UserSession(Substitute.For<IClient>(), 9, 9) { ZoneId = Moradon, Level = 50, Nation = AccountNation.ElMorad };
        await _service.HandlePlayerKillAsync(victim, winner);
        await _service.HandleNpcKillAsync(kecoon, partial);

        IReadOnlyList<MailAttachmentDraft>? mailed = null;
        await _mailService.SendSystemMailAsync(winner.CharacterId, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Do<IReadOnlyList<MailAttachmentDraft>>(a => mailed = a));

        await _service.EndRaceAsync(MoradonRace);

        await _mailService.Received(1).SendSystemMailAsync(winner.CharacterId, "Collection Race: Test Moradon Race", Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>());
        await _mailService.DidNotReceive().SendSystemMailAsync(partial.CharacterId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>());
        mailed.Should().NotBeNull();
        mailed!.Select(a => (a.Kind, a.Count)).Should().Equal(
            (MailAttachmentKind.Gold, 50000),
            (MailAttachmentKind.Experience, 25000),
            (MailAttachmentKind.NationalPoints, 100),
            (MailAttachmentKind.Item, 1));
        mailed[3].ItemId.Should().Be(Dagger);
        mailed[3].Durability.Should().Be(5000);
    }

    [Fact]
    public async Task HandleNpcKillAsync_PartyMembersShareProgress_AndEachCompleterIsMailed()
    {
        await _service.StartRaceAsync(LufersonRace);

        var leader = CreatePlayer(1, Luferson);
        var member = CreatePlayer(2, Luferson);
        var elsewhere = CreatePlayer(3, Moradon);
        var party = _sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[party.FindEmptySlot()] = (short)member.CharacterId;
        party.MemberIds[party.FindEmptySlot()] = (short)elsewhere.CharacterId;
        leader.PartyIndex = member.PartyIndex = elsewhere.PartyIndex = party.Index;

        await _service.HandleNpcKillAsync(new NpcInstance { NpcId = Kecoon, ZoneId = Luferson }, leader);
        await _service.EndRaceAsync(LufersonRace);

        await _mailService.Received(1).SendSystemMailAsync(leader.CharacterId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>());
        await _mailService.Received(1).SendSystemMailAsync(member.CharacterId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>());
        await _mailService.DidNotReceive().SendSystemMailAsync(elsewhere.CharacterId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<MailAttachmentDraft>>());
    }

    [Fact]
    public async Task HandleNpcKillAsync_IgnoredIfDifferentZoneOrLevel()
    {
        await _service.StartRaceAsync(MoradonRace);

        var client = Substitute.For<IClient>();
        var farPlayer = CreatePlayer(3, Luferson, client: client);
        await _service.HandleNpcKillAsync(new NpcInstance { NpcId = Kecoon, ZoneId = Luferson }, farPlayer);

        var lowClient = Substitute.For<IClient>();
        var tooLow = CreatePlayer(4, Moradon, client: lowClient);
        _races[MoradonRace].MinLevel = 60;
        await _service.HandleNpcKillAsync(new NpcInstance { NpcId = Kecoon, ZoneId = Moradon }, tooLow);

        await client.DidNotReceive().SendPacket(Arg.Any<Packet>());
        await lowClient.DidNotReceive().SendPacket(Arg.Any<Packet>());
    }

    [Fact]
    public async Task HandleItemGainAsync_CountsInventory_ConsumesItemsOnCompletion()
    {
        await _service.StartRaceAsync(CollectRace);
        var player = CreatePlayer(5, Moradon);
        var slot = player.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId = Apple;
        slot.Count = 2;

        await _service.HandleItemGainAsync(player, Apple);
        var progress = _service.GetActive(Moradon)!.ProgressOf(player);
        progress.Current.Should().Equal(2);
        progress.IsCompleted.Should().BeFalse();

        slot.Count = 5;
        await _service.HandleItemGainAsync(player, Apple);

        progress.IsCompleted.Should().BeTrue();
        slot.Count.Should().Be(2);
    }

    [Fact]
    public async Task SyncPlayerAsync_SendsCloseOutsideRace_AndStateInside()
    {
        await _service.StartRaceAsync(MoradonRace);

        var farClient = Substitute.For<IClient>();
        var farPlayer = CreatePlayer(6, Luferson, client: farClient);
        await _service.SyncPlayerAsync(farPlayer);
        await farClient.Received(1).SendPacket(Arg.Is<Packet>(p => p.GetData()[0] == CollectionRacePacketWriter.SubClose));

        var client = Substitute.For<IClient>();
        var player = CreatePlayer(7, Moradon, client: client);
        await _service.SyncPlayerAsync(player);
        await client.Received(1).SendPacket(Arg.Is<Packet>(p => p.GetData()[0] == CollectionRacePacketWriter.SubState));
    }

    [Fact]
    public async Task LevelChange_ResyncsThePlayer()
    {
        await _service.StartRaceAsync(MoradonRace);
        var client = Substitute.For<IClient>();
        var player = CreatePlayer(8, Moradon, client: client);

        _playerProgressionService.LevelChanged += Raise.Event<Func<UserSession, Task>>(player);

        await client.Received(1).SendPacket(Arg.Is<Packet>(p => p.GetData()[0] == CollectionRacePacketWriter.SubState));
    }

    [Fact]
    public async Task TickAsync_StartsScheduledRaceAtItsMinute()
    {
        var now = DateTime.UtcNow;
        _schedules.Add(new CollectionRaceScheduleData { Id = 1, RaceId = ScheduledRace, Day = null, Hour = (byte)now.Hour, Minute = (byte)now.Minute });
        _schedules.Add(new CollectionRaceScheduleData { Id = 2, RaceId = LufersonRace, Day = null, Hour = (byte)now.Hour, Minute = (byte)now.Minute });

        await _service.TickAsync();

        _service.GetActive(Luferson)!.Race.Id.Should().Be(ScheduledRace);
        _service.ActiveRaces.Should().HaveCount(1);
    }
}
