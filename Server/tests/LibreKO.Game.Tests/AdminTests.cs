using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AdminTests : GameTestBase
{
    [Fact]
    public async Task SessionTerminationService_LogoutAsync_PersistsSessionStateAndMarksCharacterOffline()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.Add(new Account
                {
                    Login = "logout-user",
                    Password = "pw",
                    Nation = AccountNation.Karus,
                    Authority = AccountAuthority.Normal
                });
                db.SaveChanges();

                var accountId = db.Accounts.Single(account => account.Login == "logout-user").Id;
                db.Characters.Add(new Character
                {
                    AccountId = accountId,
                    Slot = 0,
                    Name = "LogoutTarget",
                    Race = 1,
                    Class = 101,
                    Face = 2,
                    Hair = 3,
                    Level = 10,
                    Hp = 100,
                    Mp = 100,
                    MapId = 1,
                    X = 10,
                    Z = 20,
                    IsOnline = true,
                    Items = new byte[InventoryConstants.InventoryTotal * 8],
                    SkillPointData = new byte[9]
                });
            });

        var accountId = await GetAccountIdAsync(provider, "logout-user");
        var characterId = await GetCharacterIdAsync(provider, "LogoutTarget");

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId = characterId;
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId);
        session.Name = "LogoutTarget";
        session.Class = 101;
        session.ZoneId = 2;
        session.Hp = 87;
        session.Mp = 65;
        session.Level = 11;
        session.X = 33;
        session.Z = 44;

        var service = provider.GetRequiredService<ISessionTerminationService>();
        await service.LogoutAsync(client);

        sessionManager.GetByClientId(client.Id).Should().BeNull();
        client.CharacterId.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var character = await db.Characters.SingleAsync(c => c.Id == characterId);
        character.IsOnline.Should().BeFalse();
        character.Level.Should().Be(11);
        character.Hp.Should().Be(87);
        character.Mp.Should().Be(65);
        character.MapId.Should().Be(2);
        character.X.Should().Be(33);
        character.Z.Should().Be(44);
    }

    [Fact]
    public async Task AdminPacketCoordinator_HandleOperatorAsync_CutoffLogsOutTarget()
    {
        using var provider = CreateProvider(
            db =>
            {
                db.Accounts.AddRange(
                    new Account
                    {
                        Login = "gm-user",
                        Password = "pw",
                        Nation = AccountNation.Karus,
                        Authority = AccountAuthority.GameMaster
                    },
                    new Account
                    {
                        Login = "target-user",
                        Password = "pw",
                        Nation = AccountNation.Karus,
                        Authority = AccountAuthority.Normal
                    });
                db.SaveChanges();

                var gmAccountId = db.Accounts.Single(account => account.Login == "gm-user").Id;
                var targetAccountId = db.Accounts.Single(account => account.Login == "target-user").Id;
                db.Characters.AddRange(
                    new Character
                    {
                        AccountId = gmAccountId,
                        Slot = 0,
                        Name = "GM",
                        Race = 1,
                        Class = 101,
                        Face = 1,
                        Hair = 1,
                        Level = 60,
                        Hp = 100,
                        Mp = 100,
                        MapId = 1,
                        IsOnline = true,
                        Items = new byte[InventoryConstants.InventoryTotal * 8],
                        SkillPointData = new byte[9]
                    },
                    new Character
                    {
                        AccountId = targetAccountId,
                        Slot = 0,
                        Name = "Target",
                        Race = 1,
                        Class = 101,
                        Face = 1,
                        Hair = 1,
                        Level = 20,
                        Hp = 90,
                        Mp = 70,
                        MapId = 1,
                        IsOnline = true,
                        Items = new byte[InventoryConstants.InventoryTotal * 8],
                        SkillPointData = new byte[9]
                    });
            });

        var gmAccountId = await GetAccountIdAsync(provider, "gm-user");
        var targetAccountId = await GetAccountIdAsync(provider, "target-user");
        var gmCharacterId = await GetCharacterIdAsync(provider, "GM");
        var targetCharacterId = await GetCharacterIdAsync(provider, "Target");

        var gmClient = Substitute.For<IClient>();
        gmClient.Id.Returns(Guid.NewGuid());
        gmClient.CharacterId = gmCharacterId;
        gmClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        targetClient.CharacterId = targetCharacterId;
        targetClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var gmSession = sessionManager.CreateSession(gmClient, gmCharacterId, gmAccountId);
        gmSession.Name = "GM";
        gmSession.IsGM = true;
        gmSession.ZoneId = 1;

        var targetSession = sessionManager.CreateSession(targetClient, targetCharacterId, targetAccountId);
        targetSession.Name = "Target";
        targetSession.ZoneId = 1;
        targetSession.Hp = 77;
        targetSession.X = 15;
        targetSession.Z = 25;

        var packet = new Packet(GameOpcodes.GS_OPERATOR);
        packet.WriteByte(5);
        packet.WriteSByteString("Target");

        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        await coordinator.HandleOperatorAsync(gmClient, packet);

        sessionManager.GetByCharacterId(targetCharacterId).Should().BeNull();
        targetClient.CharacterId.Should().Be(0);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var targetCharacter = await db.Characters.SingleAsync(c => c.Id == targetCharacterId);
        targetCharacter.IsOnline.Should().BeFalse();
        targetCharacter.Hp.Should().Be(77);
        targetCharacter.X.Should().Be(15);
        targetCharacter.Z.Should().Be(25);
    }

    [Fact]
    public async Task AdminPacketCoordinator_HandleGmCommandAsync_OnlineReturnsCurrentPlayerCount()
    {
        using var provider = CreateProvider(_ => { });

        var gmClient = Substitute.For<IClient>();
        gmClient.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        gmClient.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var otherClient = Substitute.For<IClient>();
        otherClient.Id.Returns(Guid.NewGuid());
        otherClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var gmSession = sessionManager.CreateSession(gmClient, characterId: 300, accountId: 310);
        gmSession.Name = "GM";
        gmSession.IsGM = true;

        var otherSession = sessionManager.CreateSession(otherClient, characterId: 301, accountId: 311);
        otherSession.Name = "Other";

        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        await coordinator.HandleGmCommandAsync(gmSession, "+online");

        // Notices to a single GM session are delivered as WAR_SYSTEM_CHAT
        // (chatType=8) over GS_CHAT for visibility in the main chat window.
        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_CHAT);
        sentPacket.ReadByte().Should().Be(8); // chatType: WAR_SYSTEM_CHAT
        sentPacket.ReadByte();                 // nation
        sentPacket.ReadInt();                  // sender (-1 = none)
        sentPacket.ReadSByteString();          // sender name (empty)
        sentPacket.ReadString().Should().Be("Online players: 2");
    }

    [Fact]
    public async Task PublicDemoSetLevelGrant_LetsANormalPlayerSetTheirOwnLevelOnly()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101)),
            settings => settings.PublicDemo.GrantSetLevelToEveryone = true);
        var session = NormalPlayer(provider);
        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        coordinator.IsOpenToEveryone("+setlevel 60").Should().BeTrue();
        coordinator.IsOpenToEveryone("+SetLevel 60").Should().BeTrue();
        coordinator.IsOpenToEveryone("+gold 1000").Should().BeFalse();

        await coordinator.HandleGmCommandAsync(session, "+setlevel 60");
        session.Level.Should().Be(60);
        session.StatPoints.Should().Be(ProgressionTable.StatPointsForLevel(60));
    }

    [Fact]
    public async Task WithoutTheGrant_ANormalPlayerCannotSetLevel()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101)));
        var session = NormalPlayer(provider);
        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        coordinator.IsOpenToEveryone("+setlevel 60").Should().BeFalse();
        await coordinator.HandleGmCommandAsync(session, "+setlevel 60");
        session.Level.Should().Be(12);
    }

    private static UserSession NormalPlayer(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 420, accountId: 430);
        session.Name = "Demo";
        session.IsGM = false;
        session.Class = 101;
        session.Race = 1;
        session.Level = 12;
        session.Hp = 1;
        session.SkillData = [];
        return session;
    }

    [Fact]
    public async Task AdminPacketCoordinator_HandleGmCommandAsync_SetLevelRebuildsLevelDependentState()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101)));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sentPackets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sentPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 400, accountId: 410);
        session.Name = "GM";
        session.IsGM = true;
        session.Class = 101;
        session.Race = 1;
        session.Level = 12;
        session.Experience = 5_000;
        session.Strength = 120;
        session.Stamina = 110;
        session.Hp = 1;
        session.StatPoints = 0;
        session.SkillPoints[ProgressionTable.MasteryPoolSlot] = 0;
        session.SkillPoints[5] = 6;
        session.SkillPoints[8] = 3;
        session.SkillData = [1, 0, 0, 0];

        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        await coordinator.HandleGmCommandAsync(session, "+setlevel 70");

        session.Level.Should().Be(70);
        session.Experience.Should().Be(0);

        var expectedStats = ProgressionTable.BaseStatsForClass(101);
        session.Strength.Should().Be(expectedStats.Strength);
        session.Stamina.Should().Be(expectedStats.Stamina);
        session.Dexterity.Should().Be(expectedStats.Dexterity);
        session.Intelligence.Should().Be(expectedStats.Intelligence);
        session.Magic.Should().Be(expectedStats.Magic);

        session.StatPoints.Should().Be(ProgressionTable.StatPointsForLevel(70));
        session.SkillPoints[ProgressionTable.MasteryPoolSlot]
            .Should().Be(ProgressionTable.MasteryPointsForLevel(70));
        for (var tree = ProgressionTable.MasteryPoolSlot + 1; tree < ProgressionTable.MasterySlotCount; tree++)
            session.SkillPoints[tree].Should().Be(0);

        session.SkillData.Should().BeEmpty();

        session.MaxHp.Should().Be(AbilityCalculator.CalculateMaxHp(70, expectedStats.Stamina, 1, 0));
        session.Hp.Should().Be(session.MaxHp);
        session.Mp.Should().Be(session.MaxMp);

        var skillBarClear = sentPackets.SingleOrDefault(packet =>
            packet.GetOpcode() == (byte)GameOpcodes.GS_SKILLDATA);
        skillBarClear.Should().NotBeNull();
        skillBarClear!.ResetOffset();
        skillBarClear.ReadShort().Should().Be(0);
        skillBarClear.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task AdminPacketCoordinator_HandleGmCommandAsync_SetLevelRejectsOutOfRangeLevel()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 401, accountId: 411);
        session.Name = "GM";
        session.IsGM = true;
        session.Level = 42;

        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        await coordinator.HandleGmCommandAsync(session, "+setlevel 84");

        session.Level.Should().Be(42);
        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_CHAT);
        sentPacket.ReadByte();
        sentPacket.ReadByte();
        sentPacket.ReadInt();
        sentPacket.ReadSByteString();
        sentPacket.ReadString().Should().Be("Usage: +setlevel <1-83>");
    }

    [Fact]
    public async Task AdminPacketCoordinator_HandleGmCommandAsync_TimeAndWeatherCommandsWork()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 402, accountId: 412);
        session.Name = "GM";
        session.IsGM = true;

        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        await coordinator.HandleGmCommandAsync(session, "+time 12:00");
        await coordinator.HandleGmCommandAsync(session, "+weather clear 0");
        await coordinator.HandleGmCommandAsync(session, "+hp");

        session.Hp.Should().Be(session.MaxHp);
        session.Mp.Should().Be(session.MaxMp);
    }

    [Fact]
    public async Task AdminPacketCoordinator_HandleGmCommandAsync_TpAndSummonAliasesRouteProperly()
    {
        using var provider = CreateProvider(_ => { });

        var gmClient = Substitute.For<IClient>();
        gmClient.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        gmClient.SendPacket(Arg.Do<Packet>(p => sentPacket = p), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var gmSession = sessionManager.CreateSession(gmClient, characterId: 403, accountId: 413);
        gmSession.Name = "GM";
        gmSession.IsGM = true;

        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        // When target is not found, +tp / +warp outputs "Player not found: Target"
        await coordinator.HandleGmCommandAsync(gmSession, "+tp MissingPlayer");
        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.GetOpcode().Should().Be((byte)GameOpcodes.GS_CHAT);
        sentPacket.ReadByte();
        sentPacket.ReadByte();
        sentPacket.ReadInt();
        sentPacket.ReadSByteString();
        sentPacket.ReadString().Should().Be("Player not found: MissingPlayer");

        // When target is not found, +summon outputs "Player not found: Target"
        await coordinator.HandleGmCommandAsync(gmSession, "+summon MissingPlayer");
        sentPacket.ResetOffset();
        sentPacket.ReadByte();
        sentPacket.ReadByte();
        sentPacket.ReadInt();
        sentPacket.ReadSByteString();
        sentPacket.ReadString().Should().Be("Player not found: MissingPlayer");
    }

    [Fact]
    public void AdminPacketCoordinator_RaceValidationAndResolution_WorksCorrectly()
    {
        // Karus Kurian
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(113, 6, AccountNation.Karus).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(113, 1, AccountNation.Karus).Should().BeFalse();
        AdminPanelPacketCoordinator.ResolveRaceForClass(113, 1, AccountNation.Karus).Should().Be(6);

        // El Morad Porutu
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(213, 14, AccountNation.ElMorad).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(213, 11, AccountNation.ElMorad).Should().BeFalse();
        AdminPanelPacketCoordinator.ResolveRaceForClass(213, 11, AccountNation.ElMorad).Should().Be(14);

        // Karus Mage - Male (3) and Female (4)
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(103, 3, AccountNation.Karus).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(103, 4, AccountNation.Karus).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(103, 1, AccountNation.Karus).Should().BeFalse();
        AdminPanelPacketCoordinator.ResolveRaceForClass(103, 4, AccountNation.Karus).Should().Be(4); // Preserves female
        AdminPanelPacketCoordinator.ResolveRaceForClass(103, 1, AccountNation.Karus).Should().Be(3); // Defaults to male

        // El Morad Warrior - Barbarian (11), Man (12), Woman (13)
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(201, 11, AccountNation.ElMorad).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(201, 12, AccountNation.ElMorad).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(201, 13, AccountNation.ElMorad).Should().BeTrue();
        AdminPanelPacketCoordinator.IsValidRaceForClassAndNation(201, 14, AccountNation.ElMorad).Should().BeFalse();
    }
}
