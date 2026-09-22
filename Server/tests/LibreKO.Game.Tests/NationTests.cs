using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NationTests : GameTestBase
{
    [Fact]
    public void KingEventState_TracksBonusesPerNation()
    {
        using var provider = CreateProvider(_ => { });

        var kingEventState = provider.GetRequiredService<IKingEventState>();
        kingEventState.ActivateExpBonus(AccountNation.Karus, 30, TimeSpan.FromMinutes(30));
        kingEventState.ActivateNoahBonus(AccountNation.ElMorad, 2, TimeSpan.FromMinutes(30));

        kingEventState.GetExpBonus(AccountNation.Karus).Should().Be(30);
        kingEventState.GetExpBonus(AccountNation.ElMorad).Should().Be(0);
        kingEventState.GetNoahBonus(AccountNation.Karus).Should().Be(0);
        kingEventState.GetNoahBonus(AccountNation.ElMorad).Should().Be(2);
    }

    [Fact]
    public async Task NationSystemsPacketCoordinator_HandleRankAsync_ReturnsRequestedRankTypeWithEmptyList()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.CreateSession(client, characterId: 91, accountId: 101);

        var request = new Packet(GameOpcodes.GS_RANK);
        request.WriteByte(7);

        var coordinator = provider.GetRequiredService<INationSystemsPacketCoordinator>();
        await coordinator.HandleRankAsync(client, request);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_RANK);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(7);
        sentPacket.ReadShort().Should().Be(0);
    }

    [Fact]
    public async Task NationSystemsPacketCoordinator_HandleKingAsync_Nominate_PersistsCandidate()
    {
        var kingData = new KingSystemData
        {
            Nation = (byte)AccountNation.Karus,
            Type = 1
        };

        using var provider = CreateProvider(
            db => db.Add(kingData),
            gameData =>
            {
                gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>
                {
                    [(byte)AccountNation.Karus] = kingData
                });
            });

        var leaderClient = Substitute.For<IClient>();
        leaderClient.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        leaderClient.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var nomineeClient = Substitute.For<IClient>();
        nomineeClient.Id.Returns(Guid.NewGuid());
        nomineeClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var leader = sessionManager.CreateSession(leaderClient, characterId: 211, accountId: 221);
        leader.Name = "Leader";
        leader.Nation = AccountNation.Karus;
        leader.KnightsId = 700;
        leader.KnightsFame = 1;

        var nominee = sessionManager.CreateSession(nomineeClient, characterId: 212, accountId: 222);
        nominee.Name = "Nominee";
        nominee.Nation = AccountNation.Karus;
        nominee.KnightsId = 701;
        nominee.KnightsFame = 1;

        var request = new Packet(GameOpcodes.GS_KING);
        request.WriteByte(1);
        request.WriteByte(2);
        request.WriteSByteString("Nominee");

        var coordinator = provider.GetRequiredService<INationSystemsPacketCoordinator>();
        await coordinator.HandleKingAsync(leaderClient, request);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_KING);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(2);
        sentPacket.ReadShort().Should().Be(1);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.KingElectionList.SingleAsync();
        entry.Nation.Should().Be((byte)AccountNation.Karus);
        entry.Name.Should().Be("Nominee");
        entry.Knights.Should().Be(701);
    }

    [Fact]
    public async Task NationSystemsPacketCoordinator_HandleKingAsync_ExpEvent_ActivatesBonusAndConsumesTreasury()
    {
        var kingData = new KingSystemData
        {
            Nation = (byte)AccountNation.Karus,
            KingName = "Ruler",
            NationalTreasury = 500_000_000
        };

        using var provider = CreateProvider(
            db => db.Add(kingData),
            gameData =>
            {
                gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>
                {
                    [(byte)AccountNation.Karus] = kingData
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 213, accountId: 223);
        session.Name = "Ruler";
        session.Nation = AccountNation.Karus;

        var request = new Packet(GameOpcodes.GS_KING);
        request.WriteByte(4);
        request.WriteByte(2);
        request.WriteByte(10);

        var coordinator = provider.GetRequiredService<INationSystemsPacketCoordinator>();
        await coordinator.HandleKingAsync(client, request);

        var response = sentPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_KING);
        response.ResetOffset();
        response.ReadByte().Should().Be(4);
        response.ReadByte().Should().Be(2);
        response.ReadByte().Should().Be(1);
        response.ReadByte().Should().Be(10);

        sentPackets.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_NOTICE);
        provider.GetRequiredService<IKingEventState>().GetExpBonus(AccountNation.Karus).Should().Be(10);
        kingData.NationalTreasury.Should().Be(200_000_000);
    }

    [Fact]
    public async Task ZoneTransitionService_ChangeZoneAsync_UpdatesSessionAndSendsTeleportFlow()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>());
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 111, accountId: 121);
        session.ZoneId = 1;
        session.X = 5;
        session.Z = 7;
        session.Nation = AccountNation.ElMorad;
        session.Quest.BindPoint = 99;

        var zoneTransitionService = provider.GetRequiredService<IZoneTransitionService>();
        await zoneTransitionService.ChangeZoneAsync(session, 48, 135.0f, 115.0f);

        session.IsWarping.Should().BeTrue();
        session.ZoneId.Should().Be(48);
        session.X.Should().Be(135.0f);
        session.Z.Should().Be(115.0f);
        session.Quest.BindPoint.Should().Be(-1);

        sentPackets.Should().HaveCount(4);
        sentPackets[0].GetOpcode().Should().Be((byte)GameOpcodes.GS_ZONE_CHANGE);
        sentPackets[1].GetOpcode().Should().Be((byte)GameOpcodes.GS_ZONEABILITY);
        sentPackets[2].GetOpcode().Should().Be((byte)GameOpcodes.GS_WEATHER);
        sentPackets[3].GetOpcode().Should().Be((byte)GameOpcodes.GS_COLLECTION_RACE);

        sentPackets[0].ResetOffset();
        sentPackets[0].ReadByte().Should().Be(3);
        sentPackets[0].ReadShort().Should().Be(48);
        sentPackets[0].ReadShort().Should().Be(0);
        sentPackets[0].ReadUShort().Should().Be(1350);
        sentPackets[0].ReadUShort().Should().Be(1150);
        sentPackets[0].ReadUShort().Should().Be(0);
        sentPackets[0].ReadByte().Should().Be((byte)AccountNation.ElMorad);
        sentPackets[0].ReadUShort().Should().Be(ushort.MaxValue);
    }

    [Theory]
    [InlineData(BattleZoneManager.ZONE_MORADON, 0, 1)]
    [InlineData(BattleZoneManager.ZONE_RONARK_LAND, 1, 0)]
    [InlineData(BattleZoneManager.ZONE_BATTLE1, 1, 0)]
    [InlineData(BattleZoneManager.ZONE_DELOS, 6, 1)]
    public async Task ZoneTransitionService_SendZoneAbility_ClassifiesOnlyKnownPvpZones(
        byte zoneId,
        byte expectedType,
        byte expectedTrade)
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>()));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId: 112, accountId: 122);
        session.ZoneId = zoneId;
        session.Nation = AccountNation.ElMorad;

        await provider.GetRequiredService<IZoneTransitionService>().SendZoneAbilityAsync(session);

        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_ZONEABILITY);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(expectedTrade);
        sentPacket.ReadByte().Should().Be(expectedType);
    }

    [Fact]
    public async Task ChallengePacketCoordinator_HandleAsync_RequestSetsStateAndNotifiesBothPlayers()
    {
        using var provider = CreateProvider(_ => { });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var zoneTransitionService = Substitute.For<IZoneTransitionService>();
        var challengeLogger = Substitute.For<ILogger<ChallengePacketCoordinator>>();
        var coordinator = new ChallengePacketCoordinator(sessionManager, zoneTransitionService, challengeLogger);

        var requesterClient = Substitute.For<IClient>();
        requesterClient.Id.Returns(Guid.NewGuid());
        Packet? requesterPacket = null;
        requesterClient.SendPacket(Arg.Do<Packet>(packet => requesterPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        Packet? targetPacket = null;
        targetClient.SendPacket(Arg.Do<Packet>(packet => targetPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var requester = sessionManager.CreateSession(requesterClient, characterId: 131, accountId: 141);
        requester.Name = "Requester";
        requester.ZoneId = 21;
        requester.Hp = 100;

        var target = sessionManager.CreateSession(targetClient, characterId: 132, accountId: 142);
        target.Name = "Target";
        target.ZoneId = 21;
        target.Hp = 100;

        var packet = new Packet(GameOpcodes.GS_CHALLENGE);
        packet.WriteByte(1);
        packet.WriteSByteString("Target");

        await coordinator.HandleAsync(requesterClient, packet);

        requester.Trade.IsRequestingChallenge.Should().BeTrue();
        requester.Trade.ChallengeUser.Should().Be(target.CharacterId);
        target.Trade.IsChallengeRequested.Should().BeTrue();
        target.Trade.ChallengeUser.Should().Be(requester.CharacterId);

        requesterPacket.Should().NotBeNull();
        requesterPacket!.ResetOffset();
        requesterPacket.ReadByte().Should().Be(5);
        requesterPacket.ReadSByteString().Should().Be("Target");

        targetPacket.Should().NotBeNull();
        targetPacket!.ResetOffset();
        targetPacket.ReadByte().Should().Be(1);
        targetPacket.ReadSByteString().Should().Be("Requester");
    }

}
