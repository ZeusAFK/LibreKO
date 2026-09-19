using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class DeathStateTests : GameTestBase
{
    [Fact]
    public async Task HandlePlayerDeathAsync_CostsNothingInAnArenaAndSaysNothingAboutExperience()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var victimPackets = new List<Packet>();
        var victim = CreateSession(sessionManager, 9100, AccountNation.Karus, victimPackets);
        victim.Hp = 0;
        victim.MaxHp = 100;
        victim.ArenaId = ArenaZones.MoradonPersonArena;
        sessionManager.Regions.AddToRegion(victim);

        var killer = CreateSession(sessionManager, 9101, AccountNation.ElMorad, []);
        killer.Hp = 100;
        killer.MaxHp = 100;
        killer.ArenaId = ArenaZones.MoradonPersonArena;
        sessionManager.Regions.AddToRegion(killer);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandlePlayerDeathAsync(victim, killer);

        victim.DeathExpLoss.Should().Be(0);
        victimPackets.Should().NotContain(
            p => p.GetOpcode() == (byte)GameOpcodes.GS_EXP_CHANGE,
            because: "an arena death costs no experience, so there is nothing to tell the client");
    }

    [Fact]
    public async Task HandlePlayerDeathAsync_ReportsTheLossOnceAsANewTotal()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(10).Returns(1000L);
                gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
            });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var victimPackets = new List<Packet>();
        var victim = CreateSession(sessionManager, 9300, AccountNation.Karus, victimPackets);
        victim.Level = 10;
        victim.Experience = 500;
        victim.Loyalty = 0;
        victim.ZoneId = 2;
        victim.Hp = 0;
        victim.MaxHp = 100;
        sessionManager.Regions.AddToRegion(victim);

        var killer = CreateSession(sessionManager, 9301, AccountNation.ElMorad, []);
        killer.ZoneId = 2;
        killer.Hp = 100;
        killer.MaxHp = 100;
        sessionManager.Regions.AddToRegion(killer);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandlePlayerDeathAsync(victim, killer);

        var expPackets = victimPackets
            .Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXP_CHANGE)
            .ToList();

        expPackets.Should().ContainSingle(
            because: "the client derives the loss line from the change in the total, so a second "
                   + "packet would print a second line and overwrite the total it just stored");

        var expPacket = expPackets[0];
        expPacket.ResetOffset();
        expPacket.ReadByte().Should().Be((byte)ExperienceSubOpcode.CurrentExperience);
        expPacket.ReadLong().Should().Be(victim.Experience);
        victim.Experience.Should().Be(490);
        victim.DeathExpLoss.Should().Be(10);
    }

    [Fact]
    public async Task HandleRegeneAsync_ForfeitsThePendingDeathExpLoss()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var session = CreateSession(sessionManager, 9200, AccountNation.Karus, []);
        session.Hp = 0;
        session.MaxHp = 100;
        session.ZoneId = BattleZoneManager.ZONE_MORADON;
        session.X = 710f;
        session.Z = 466f;
        session.ArenaId = ArenaZones.MoradonPersonArena;
        session.DeathExpLoss = 4321;
        sessionManager.Regions.AddToRegion(session);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandleRegeneAsync(session.Client, session, 1);

        session.DeathExpLoss.Should().Be(0);
        session.Hp.Should().Be(session.MaxHp);
    }

    [Fact]
    public async Task HandleRegeneAsync_AnswersALiveSessionSoTheClientCanLeaveItsDeadState()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var sent = new List<Packet>();
        var session = CreateSession(sessionManager, 9400, AccountNation.Karus, sent);
        session.MaxHp = 100;
        session.Hp = 100;
        sessionManager.Regions.AddToRegion(session);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandleRegeneAsync(session.Client, session, 1);

        sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_HP_CHANGE);
    }

    [Fact]
    public async Task HandlePlayerDeathAsync_WithoutAKillerStillBroadcastsTheDeath()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var sent = new List<Packet>();
        var victim = CreateSession(sessionManager, 9500, AccountNation.Karus, sent);
        victim.MaxHp = 100;
        victim.Hp = 0;
        sessionManager.Regions.AddToRegion(victim);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandlePlayerDeathAsync(victim, killer: null);

        var deadPacket = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_DEAD);
        deadPacket.ResetOffset();
        deadPacket.ReadInt().Should().Be(victim.CharacterId);
        deadPacket.ReadInt().Should().Be(CombatLifecycleService.NoKillerId);
    }

    [Theory]
    [InlineData(0, 7777L, (short)0, 7777L)]
    [InlineData(120, 7777L, (short)120, 0L)]
    public void HydrateSession_KeepsAZeroHpCharacterDeadAcrossARelog(
        int storedHp, long storedLoss, short expectedHp, long expectedLoss)
    {
        using var provider = CreateProvider(_ => { });
        var mapper = provider.GetRequiredService<IUserSessionCharacterMapper>();
        var session = NewSession();

        mapper.HydrateSession(
            session, NewCharacter(storedHp, storedLoss), new Account(), new Warehouse(),
            maxHp: 250, maxMp: 100, provider.GetRequiredService<IGameDataService>());

        session.Hp.Should().Be(expectedHp);
        session.DeathExpLoss.Should().Be(expectedLoss);
    }

    [Theory]
    [InlineData((short)0, 999L, 999L)]
    [InlineData((short)50, 999L, 0L)]
    public void ApplyToCharacter_PersistsThePendingLossOnlyWhileDead(
        short sessionHp, long sessionLoss, long expectedStoredLoss)
    {
        var mapper = new UserSessionCharacterMapper();
        var session = NewSession();
        session.Hp = sessionHp;
        session.MaxHp = 100;
        session.DeathExpLoss = sessionLoss;

        var row = NewCharacter(hp: 100, deathExpLoss: 0);
        mapper.ApplyToCharacter(session, row);

        row.Hp.Should().Be(sessionHp);
        row.DeathExpLoss.Should().Be(expectedStoredLoss);
    }

    private static UserSession NewSession()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return new UserSession(client, 9300, 9301);
    }

    private static Character NewCharacter(int hp, long deathExpLoss) => new()
    {
        Name = "Corpse",
        Hp = hp,
        Mp = 0,
        DeathExpLoss = deathExpLoss,
        Level = 30,
        MapId = BattleZoneManager.ZONE_MORADON,
    };

    private static UserSession CreateSession(
        SessionManager sessionManager, int characterId, AccountNation nation, List<Packet> sent)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1);
        session.Name = $"Corpse{characterId}";
        session.Nation = nation;
        session.ZoneId = BattleZoneManager.ZONE_MORADON;
        session.X = 710f;
        session.Z = 466f;
        return session;
    }
}
