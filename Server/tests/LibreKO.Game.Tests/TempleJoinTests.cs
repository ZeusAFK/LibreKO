using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class TempleJoinTests
{
    private const int JoinWindow = 300;
    private readonly SessionManager sessions = new();
    private readonly GameServerSettings settings = new();
    private readonly EventSchedulerService scheduler;

    public TempleJoinTests()
    {
        scheduler = new EventSchedulerService(
            sessions,
            Options.Create(settings),
            Substitute.For<IZoneTransitionService>(),
            Substitute.For<ICollectionRaceService>(),
            Substitute.For<ILotteryService>(),
            Substitute.For<IJuraidMountainService>(),
            Substitute.For<IBorderDefenseWarService>(),
            Substitute.For<IChaosDungeonService>(),
            Substitute.For<IUnderTheCastleService>(),
            Substitute.For<IForgottenTempleService>(),
            Substitute.For<IKrowazService>(),
            Substitute.For<INereidMonumentService>(),
            Substitute.For<IGameDataService>(),
            new ScheduleClock(),
            Substitute.For<ILogger<EventSchedulerService>>());
    }

    private UserSession Player(int characterId, byte level, AccountNation nation = AccountNation.ElMorad)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId: characterId, accountId: characterId * 10);
        session.Level = level;
        session.Nation = nation;
        for (int i = 0; i < session.Inventory.Length; i++)
            session.Inventory[i] = new ItemSlot();
        return session;
    }

    [Fact]
    public void NothingOpenIsTheNotTimeCode()
    {
        scheduler.TryJoinTempleEvent(Player(1, 70)).Should().Be(TempleJoinResult.NotOpen);
    }

    [Fact]
    public async Task APrisonerCannotSignUp()
    {
        await scheduler.CallTempleEventAsync(TempleEvent.ForgottenTemple, JoinWindow, minLevel: 60, maxLevel: 70);
        var prisoner = Player(4, 65);
        prisoner.ZoneId = (byte)ZoneId.Prison;

        scheduler.TryJoinTempleEvent(prisoner).Should().Be(TempleJoinResult.NotOpen, "a sign-up would carry the prisoner out at the start");
    }

    [Fact]
    public async Task LevelsOutsideTheBandAreRefusedEachWithItsOwnCode()
    {
        await scheduler.CallTempleEventAsync(TempleEvent.ForgottenTemple, JoinWindow, minLevel: 60, maxLevel: 70);

        scheduler.TryJoinTempleEvent(Player(1, 59)).Should().Be(TempleJoinResult.LevelTooLow);
        scheduler.TryJoinTempleEvent(Player(2, 71)).Should().Be(TempleJoinResult.LevelTooHigh);
        var player = Player(3, 65);
        scheduler.TryJoinTempleEvent(player).Should().Be(TempleJoinResult.Joined);
        scheduler.TryJoinTempleEvent(player).Should().Be(TempleJoinResult.AlreadyJoined);
    }

    [Fact]
    public async Task ChaosAsksForTheMapOnlyWhenTheServerRequiresIt()
    {
        await scheduler.CallTempleEventAsync(TempleEvent.Chaos, JoinWindow, minLevel: 1, maxLevel: 83);
        scheduler.TryJoinTempleEvent(Player(1, 60)).Should().Be(TempleJoinResult.Joined);

        settings.Events.ChaosNeedsMap = true;
        scheduler.TryJoinTempleEvent(Player(2, 60)).Should().Be(TempleJoinResult.NeedsMapOfChaos);

        var mapHolder = Player(3, 60);
        mapHolder.Inventory[InventoryConstants.SlotMax] = new ItemSlot { ItemId = TempleEventRules.MapOfChaos, Count = 1 };
        scheduler.TryJoinTempleEvent(mapHolder).Should().Be(TempleJoinResult.Joined);
    }

    [Fact]
    public async Task SignUpsAreCountedPerNation()
    {
        await scheduler.CallTempleEventAsync(TempleEvent.BorderDefenseWar, JoinWindow, minLevel: 1, maxLevel: 83);
        scheduler.TryJoinTempleEvent(Player(1, 60, AccountNation.Karus));
        scheduler.TryJoinTempleEvent(Player(2, 60, AccountNation.Karus));
        scheduler.TryJoinTempleEvent(Player(3, 60));

        scheduler.TempleSignUps(TempleEvent.BorderDefenseWar).Should().Be(((ushort)2, (ushort)1));
    }

    [Fact]
    public void TheCountPacketCarriesTheEventAndBothNations()
    {
        var packet = EventPacketWriter.TempleSignUps(TempleEvent.BorderDefenseWar, 2, 1);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT);
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.TempleEventCounter);
        packet.ReadUShort().Should().Be((ushort)TempleEvent.BorderDefenseWar);
        packet.ReadUShort().Should().Be(2);
        packet.ReadUShort().Should().Be(1);
    }
}
