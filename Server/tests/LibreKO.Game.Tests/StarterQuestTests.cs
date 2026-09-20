using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class StarterSeedQuestTests
{
    [Fact]
    public void UserSessionCharacterMapper_HydrateSession_LeavesQuestAcceptanceToTheScripts()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = new UserSession(client, characterId: 1, accountId: 1);
        var mapper = new UserSessionCharacterMapper();
        var gameData = Substitute.For<IGameDataService>();

        mapper.HydrateSession(
            session,
            new Character
            {
                Name = "Seedless",
                Race = 11,
                Class = 201,
                Level = 1,
                Face = 1,
                Hair = 1,
                Hp = 100,
                Mp = 100,
                MapId = 21
            },
            new Account
            {
                Nation = AccountNation.ElMorad
            },
            new Warehouse(),
            maxHp: 100,
            maxMp: 100,
            gameData);

        session.Quest.QuestMap.Should().NotContainKey(500);
    }

    [Fact]
    public async Task CombatRewardService_AwardNpcKillAsync_ChecksQuestKillForRewardRecipientWithoutActiveQuest()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = new SessionManager();
        var killer = sessionManager.CreateSession(client, characterId: 10, accountId: 20);
        killer.ZoneId = 21;

        var npc = new NpcInstance
        {
            IsMonster = true,
            UniqueId = 100,
            NpcId = 750,
            ZoneId = 21,
            Hp = 0,
            MaxHp = 10
        };
        npc.DamageMap[killer.CharacterId] = 10;
        npc.TopDamagerCharId = killer.CharacterId;

        var settings = Options.Create(new GameServerSettings());
        var gameData = Substitute.For<IGameDataService>();
        var kingEventState = Substitute.For<IKingEventState>();
        var playerProgressionService = Substitute.For<IPlayerProgressionService>();
        var questPacketCoordinator = Substitute.For<IQuestPacketCoordinator>();
        var userNotificationService = Substitute.For<IUserNotificationService>();

        var combatRewardLogger = Substitute.For<ILogger<CombatRewardService>>();
        var timeWeather = new TimeWeatherBroadcastService(
            sessionManager,
            Substitute.For<ILogger<TimeWeatherBroadcastService>>());

        var service = new CombatRewardService(
            settings,
            sessionManager,
            gameData,
            kingEventState,
            timeWeather,
            playerProgressionService,
            questPacketCoordinator,
            Substitute.For<IAchievementProgressService>(),
            userNotificationService,
            combatRewardLogger);

        await service.AwardNpcKillAsync(npc, killer);

        await questPacketCoordinator.Received(1).CheckQuestKillAsync(killer, 750);

        var victim = new UserSession(Substitute.For<IClient>(), 2, 2) { Nation = AccountNation.ElMorad };
        killer.Nation = AccountNation.Karus;
        await service.AwardPlayerKillAsync(victim, killer);
        await questPacketCoordinator.Received(1).CheckQuestKillAsync(killer, (int)AccountNation.ElMorad);

        var ally = new UserSession(Substitute.For<IClient>(), 3, 3) { Nation = AccountNation.Karus };
        await service.AwardPlayerKillAsync(ally, killer);
        await questPacketCoordinator.DidNotReceive().CheckQuestKillAsync(killer, (int)AccountNation.Karus);
    }

    [Fact]
    public async Task UndeclaredQuestDoesNotGainSpecialTreatmentOnAWormKill()
    {
        var client = Substitute.For<IClient>();
        var session = new UserSession(client, 1, 1);
        session.Quest.QuestMap[500] = 1;
        var data = Substitute.For<IGameDataService>();
        var runner = Substitute.For<IQuestDialogRunner>();
        var service = new QuestProgressionService(new SessionManager(), data, runner,
            Substitute.For<IQuestDefinitionSource>(), Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<QuestProgressionService>>());
        await service.CheckQuestKillAsync(session, 750);
        session.Quest.QuestMap[500].Should().Be(1);
        await runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default, default!);
    }

    private static Packet ClonePacket(Packet packet)
    {
        var clone = new Packet(packet.GetOpcode());
        clone.WriteBytes(packet.GetData());
        clone.ResetOffset();
        return clone;
    }
}
