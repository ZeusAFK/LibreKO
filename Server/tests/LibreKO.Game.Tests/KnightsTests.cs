using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class KnightsTests : GameTestBase
{

    [Fact]
    public async Task KnightsPacketCoordinator_HandleProcessAsync_OfficerAdmitPersistsMemberState()
    {
        const short clanId = 77;

        using var provider = CreateProvider(
            db =>
            {
                db.Set<KnightsEntity>().Add(new KnightsEntity
                {
                    Id = clanId,
                    Name = "Heroes",
                    Chief = "Leader",
                    Nation = (byte)AccountNation.Karus,
                    Flag = 1,
                    Members = 1
                });

                db.Characters.AddRange(
                    new Character
                    {
                        AccountId = 1,
                        Slot = 0,
                        Name = "Leader",
                        Level = 70,
                        Class = 101,
                        MapId = 1,
                        KnightsId = clanId,
                        Fame = 3
                    },
                    new Character
                    {
                        AccountId = 2,
                        Slot = 0,
                        Name = "Recruit",
                        Level = 60,
                        Class = 102,
                        MapId = 1
                    });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Knights.AddClan(clanId, new KnightsEntity
        {
            Id = clanId,
            Name = "Heroes",
            Chief = "Leader",
            Nation = (byte)AccountNation.Karus,
            Flag = 1,
            Members = 1
        });

        var leaderId = await GetCharacterIdAsync(provider, "Leader");
        var recruitId = await GetCharacterIdAsync(provider, "Recruit");

        var leaderClient = Substitute.For<IClient>();
        leaderClient.Id.Returns(Guid.NewGuid());
        leaderClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var recruitClient = Substitute.For<IClient>();
        recruitClient.Id.Returns(Guid.NewGuid());
        recruitClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var leader = sessionManager.CreateSession(leaderClient, leaderId, accountId: 1);
        leader.Name = "Leader";
        leader.Nation = AccountNation.Karus;
        leader.KnightsId = clanId;
        leader.KnightsFame = 3;
        leader.KnightsName = "Heroes";

        var recruit = sessionManager.CreateSession(recruitClient, recruitId, accountId: 2);
        recruit.Name = "Recruit";
        recruit.Nation = AccountNation.Karus;

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte(0x06);
        packet.WriteString("Recruit");

        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();
        await coordinator.HandleProcessAsync(leaderClient, packet);

        recruit.KnightsId.Should().Be(clanId);
        recruit.KnightsFame.Should().Be(5);
        recruit.KnightsName.Should().Be("Heroes");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recruitCharacter = await db.Characters.SingleAsync(character => character.Name == "Recruit");
        var clan = await db.Set<KnightsEntity>().SingleAsync(entity => entity.Id == clanId);

        recruitCharacter.KnightsId.Should().Be(clanId);
        recruitCharacter.Fame.Should().Be(5);
        clan.Members.Should().Be(2);
    }

    [Fact]
    public async Task KnightsPacketCoordinator_HandleProcessAsync_RemoveClearsOfflineMemberAndViceChiefSlot()
    {
        const short clanId = 88;

        using var provider = CreateProvider(
            db =>
            {
                db.Set<KnightsEntity>().Add(new KnightsEntity
                {
                    Id = clanId,
                    Name = "Defenders",
                    Chief = "Leader",
                    Nation = (byte)AccountNation.Karus,
                    Flag = 1,
                    Members = 2
                });

                db.Characters.AddRange(
                    new Character
                    {
                        AccountId = 3,
                        Slot = 0,
                        Name = "Leader",
                        Level = 70,
                        Class = 101,
                        MapId = 1,
                        KnightsId = clanId,
                        Fame = 1
                    },
                    new Character
                    {
                        AccountId = 4,
                        Slot = 0,
                        Name = "OfflineVice",
                        Level = 65,
                        Class = 102,
                        MapId = 1,
                        KnightsId = clanId,
                        Fame = 2
                    });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Knights.AddClan(clanId, new KnightsEntity
        {
            Id = clanId,
            Name = "Defenders",
            Chief = "Leader",
            Nation = (byte)AccountNation.Karus,
            Flag = 1,
            Members = 2
        });

        var leaderId = await GetCharacterIdAsync(provider, "Leader");

        var leaderClient = Substitute.For<IClient>();
        leaderClient.Id.Returns(Guid.NewGuid());
        leaderClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var leader = sessionManager.CreateSession(leaderClient, leaderId, accountId: 3);
        leader.Name = "Leader";
        leader.Nation = AccountNation.Karus;
        leader.KnightsId = clanId;
        leader.KnightsFame = 1;
        leader.KnightsName = "Defenders";

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte(0x04);
        packet.WriteString("OfflineVice");

        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();
        await coordinator.HandleProcessAsync(leaderClient, packet);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var removedCharacter = await db.Characters.SingleAsync(character => character.Name == "OfflineVice");
        var clan = await db.Set<KnightsEntity>().SingleAsync(entity => entity.Id == clanId);

        removedCharacter.KnightsId.Should().Be(0);
        removedCharacter.Fame.Should().Be(0);
        clan.Members.Should().Be(1);
    }

    [Fact]
    public async Task KnightsPacketCoordinator_HandleProcessAsync_DonatePersistsClanFundAndLoyalty()
    {
        const short clanId = 89;

        using var provider = CreateProvider(
            db =>
            {
                db.Set<KnightsEntity>().Add(new KnightsEntity
                {
                    Id = clanId,
                    Name = "Defenders",
                    Chief = "Leader",
                    Nation = (byte)AccountNation.Karus,
                    Flag = 1,
                    Members = 1,
                    ClanPointFund = 100
                });

                db.Characters.Add(new Character
                {
                    AccountId = 5,
                    Slot = 0,
                    Name = "Leader",
                    Level = 70,
                    Class = 101,
                    MapId = 1,
                    KnightsId = clanId,
                    Fame = 1,
                    Loyalty = 500
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Knights.AddClan(clanId, new KnightsEntity
        {
            Id = clanId,
            Name = "Defenders",
            Chief = "Leader",
            Nation = (byte)AccountNation.Karus,
            Flag = 1,
            Members = 1,
            ClanPointFund = 100
        });

        var leaderId = await GetCharacterIdAsync(provider, "Leader");

        var leaderClient = Substitute.For<IClient>();
        leaderClient.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        leaderClient.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var leader = sessionManager.CreateSession(leaderClient, leaderId, accountId: 5);
        leader.Name = "Leader";
        leader.Nation = AccountNation.Karus;
        leader.KnightsId = clanId;
        leader.KnightsFame = 1;
        leader.KnightsName = "Defenders";
        leader.Loyalty = 500;

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte(0x3D);
        packet.WriteInt(125);

        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();
        await coordinator.HandleProcessAsync(leaderClient, packet);

        leader.Loyalty.Should().Be(375);
        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(0x3D);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(375);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clan = await db.Set<KnightsEntity>().SingleAsync(entity => entity.Id == clanId);
        var character = await db.Characters.SingleAsync(entry => entry.Name == "Leader");

        clan.ClanPointFund.Should().Be(225);
        character.Loyalty.Should().Be(375);
    }

    [Fact]
    public async Task KnightsPacketCoordinator_HandleProcessAsync_CreateRepairsStaleClanMembershipAndSucceeds()
    {
        const short staleClanId = 321;

        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character
                {
                    AccountId = 6,
                    Slot = 0,
                    Name = "Founder",
                    Level = 70,
                    Class = 101,
                    MapId = 1,
                    Money = 750000,
                    KnightsId = staleClanId,
                    Fame = 1
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var characterId = await GetCharacterIdAsync(provider, "Founder");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId: 6);
        session.Name = "Founder";
        session.Nation = AccountNation.Karus;
        session.Level = 70;
        session.Money = 750000;
        session.KnightsId = staleClanId;
        session.KnightsFame = 1;
        session.KnightsName = "GhostClan";
        session.Fame = 1;

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte(0x01);
        packet.WriteString("Renewed");

        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();
        await coordinator.HandleProcessAsync(client, packet);

        sentPackets.Should().ContainSingle();
        var sentPacket = sentPackets[0];
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(0x01);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(characterId);
        sentPacket.ReadShort().Should().Be(session.KnightsId);
        sentPacket.ReadString().Should().Be("Renewed");
        sentPacket.ReadByte().Should().Be(0);
        sentPacket.ReadByte().Should().Be(0);
        sentPacket.ReadInt().Should().Be(250000);
        sentPacket.RemainingBytes.Should().Be(0);

        session.KnightsId.Should().BeGreaterThan(0);
        session.KnightsId.Should().NotBe(staleClanId);
        session.KnightsFame.Should().Be(1);
        session.KnightsName.Should().Be("Renewed");
        session.Money.Should().Be(250000);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var founder = await db.Characters.SingleAsync(character => character.Name == "Founder");
        var clan = await db.Set<KnightsEntity>().SingleAsync(entity => entity.Name == "Renewed");

        founder.KnightsId.Should().Be((short)clan.Id);
        founder.Fame.Should().Be(1);
        founder.Money.Should().Be(250000);
        clan.Chief.Should().Be("Founder");
        clan.Members.Should().Be(1);
    }

    [Fact]
    public async Task KnightsPacketCoordinator_HandleProcessAsync_Top10WithoutClans_ReturnsFixedPlaceholderEntries()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Characters.Add(new Character
                {
                    AccountId = 7,
                    Slot = 0,
                    Name = "Viewer",
                    Level = 70,
                    Class = 101,
                    MapId = 1
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var characterId = await GetCharacterIdAsync(provider, "Viewer");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId: 7);
        session.Name = "Viewer";
        session.Nation = AccountNation.Karus;

        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)KnightsSubOpcode.Top10);

        var coordinator = provider.GetRequiredService<IKnightsPacketCoordinator>();
        await coordinator.HandleProcessAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be((byte)KnightsSubOpcode.Top10);
        sentPacket.ReadShort().Should().Be(0);

        for (short nationIndex = 0; nationIndex < 2; nationIndex++)
        {
            for (short rank = 0; rank < 5; rank++)
            {
                sentPacket.ReadShort().Should().Be(-1);
                sentPacket.ReadString().Should().BeEmpty();
                sentPacket.ReadShort().Should().Be(-1);
                sentPacket.ReadShort().Should().Be(rank);
            }
        }

        sentPacket.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task GameSessionInitializer_UsesStoredOfficerFameWhenClanHasNoIndexedRoleMatch()
    {
        const short clanId = 99;

        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "officer-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "officer-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "Officer",
                    Level = 55,
                    Class = 101,
                    MapId = 1,
                    Hp = 120,
                    Mp = 80,
                    KnightsId = clanId,
                    Fame = 3
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Knights.AddClan(clanId, new KnightsEntity
        {
            Id = clanId,
            Name = "Sentinels",
            Chief = "Leader",
            Nation = (byte)AccountNation.Karus,
            Flag = 1,
            Members = 5
        });

        var accountId = await GetAccountIdAsync(provider, "officer-user");
        var characterId = await GetCharacterIdAsync(provider, "Officer");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.AccountId.Returns(accountId);
        client.CharacterId.Returns(characterId);

        var initializer = provider.GetRequiredService<IGameSessionInitializer>();
        var session = await initializer.InitializeAsync(client);

        session.Should().NotBeNull();
        session!.KnightsFame.Should().Be(3);
        session.KnightsName.Should().Be("Sentinels");
    }

}
