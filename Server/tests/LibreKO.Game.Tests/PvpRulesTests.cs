using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PvpRulesTests : GameTestBase
{
    private const byte KarusEslant = (byte)ZoneId.KarusEslant1;
    private const float KarusLandingX = 90f;
    private const float KarusLandingZ = 770f;
    private const float DelosCentreX = 500f;
    private const float DelosCentreZ = 180f;
    private const float MoradonPartyArenaX = 700f;
    private const float MoradonPartyArenaZ = 380f;

    [Theory]
    [InlineData(BattleZoneManager.ZONE_RONARK_LAND)]
    [InlineData(BattleZoneManager.ZONE_ARDREAM)]
    [InlineData(BattleZoneManager.ZONE_CHRONO_LANDS)]
    [InlineData(BattleZoneManager.ZONE_BIFROST)]
    public void YourOwnNationIsNeverATargetHoweverHostileTheZone(byte zoneId)
    {
        using var provider = CreateProvider(_ => { });
        var (a, b) = CreatePair(provider, zoneId, AccountNation.Karus, AccountNation.Karus);

        PvpRules.CanAttackPlayer(a, b).Should().BeFalse();
        PvpRules.CanAttackPlayer(b, a).Should().BeFalse();
    }

    [Theory]
    [InlineData(BattleZoneManager.ZONE_RONARK_LAND, true)]
    [InlineData(BattleZoneManager.ZONE_CHRONO_LANDS, true)]
    [InlineData(BattleZoneManager.ZONE_BATTLE1, true)]
    [InlineData(BattleZoneManager.ZONE_DESPERATION_ABYSS, true)]
    [InlineData(KarusEslant, false)]
    [InlineData(BattleZoneManager.ZONE_KARUS, false)]
    [InlineData(BattleZoneManager.ZONE_MORADON, false)]
    public void TheEnemyNationIsATargetOnlyWhereTheZoneSaysSo(byte zoneId, bool expected)
    {
        using var provider = CreateProvider(_ => { });
        var (karus, elmorad) = CreatePair(provider, zoneId, AccountNation.Karus, AccountNation.ElMorad);

        PvpRules.CanAttackPlayer(karus, elmorad).Should().Be(expected);
    }

    [Fact]
    public void AnArenaMakesEvenYourOwnNationATarget()
    {
        using var provider = CreateProvider(_ => { });
        var (a, b) = CreatePair(
            provider, BattleZoneManager.ZONE_MORADON, AccountNation.Karus, AccountNation.Karus);

        a.ArenaId = ArenaZones.MoradonPersonArena;
        b.ArenaId = ArenaZones.MoradonPersonArena;

        PvpRules.CanAttackPlayer(a, b).Should().BeTrue();
    }

    [Fact]
    public void ThePartyArenaSparesYourOwnParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (a, b) = CreatePair(
            provider, BattleZoneManager.ZONE_MORADON, AccountNation.Karus, AccountNation.ElMorad);

        a.ArenaId = ArenaZones.MoradonPartyArena;
        b.ArenaId = ArenaZones.MoradonPartyArena;

        var party = sessionManager.Parties.CreateParty((short)a.CharacterId);
        party.MemberIds[1] = (short)b.CharacterId;
        a.PartyIndex = party.Index;
        b.PartyIndex = party.Index;

        PvpRules.CanAttackPlayer(a, b).Should().BeFalse("party mates share the party arena");

        b.PartyIndex = -1;
        PvpRules.CanAttackPlayer(a, b).Should().BeTrue();
    }

    [Fact]
    public void TheChaosDungeonSparesNobody()
    {
        using var provider = CreateProvider(_ => { });
        var (a, b) = CreatePair(
            provider, BattleZoneManager.ZONE_CHAOS_DUNGEON, AccountNation.Karus, AccountNation.Karus);

        PvpRules.CanAttackPlayer(a, b).Should().BeTrue();
    }

    [Fact]
    public async Task DyingInAPkZoneNamesYourKillerAndFillsYourAnger()
    {
        using var provider = CreateProvider(_ => { });
        var (victim, killer) = CreatePair(
            provider, BattleZoneManager.ZONE_RONARK_LAND, AccountNation.Karus, AccountNation.ElMorad);

        await KillAsync(provider, victim, killer);

        victim.RivalId.Should().Be(killer.CharacterId);
        killer.RivalId.Should().Be(-1, "the killer gains nothing but the kill");
        victim.AngerGauge.Should().Be(1);
    }

    [Fact]
    public async Task DyingOutsideAPkZoneStartsNoRivalry()
    {
        using var provider = CreateProvider(_ => { });
        var (victim, killer) = CreatePair(
            provider, BattleZoneManager.ZONE_BIFROST, AccountNation.Karus, AccountNation.ElMorad);

        await KillAsync(provider, victim, killer);

        victim.RivalId.Should().Be(-1);
        victim.AngerGauge.Should().Be(0);
    }

    [Fact]
    public async Task DyingToYourOwnSkillMakesNobodyYourRival()
    {
        using var provider = CreateProvider(_ => { });
        var victim = CreatePlayer(provider, 812, BattleZoneManager.ZONE_RONARK_LAND, AccountNation.Karus);
        var loyalty = victim.Loyalty;

        await KillAsync(provider, victim, victim);

        victim.RivalId.Should().Be(-1, "a player can never be their own rival");
        victim.Loyalty.Should().Be(loyalty);
    }

    [Fact]
    public async Task TakingRevengeEndsTheRivalry()
    {
        using var provider = CreateProvider(_ => { });
        var (first, second) = CreatePair(
            provider, BattleZoneManager.ZONE_RONARK_LAND, AccountNation.Karus, AccountNation.ElMorad);

        await KillAsync(provider, first, second);
        first.RivalId.Should().Be(second.CharacterId);

        first.Hp = first.MaxHp;
        await KillAsync(provider, second, first);

        first.RivalId.Should().Be(-1, "the score is settled");
    }

    [Fact]
    public async Task TheAngerGaugeStopsWhenItIsFull()
    {
        using var provider = CreateProvider(_ => { });
        var (victim, killer) = CreatePair(
            provider, BattleZoneManager.ZONE_RONARK_LAND, AccountNation.Karus, AccountNation.ElMorad);

        for (var death = 0; death < 8; death++)
        {
            victim.Hp = victim.MaxHp;
            victim.RivalId = -1;
            await KillAsync(provider, victim, killer);
        }

        victim.AngerGauge.Should().Be(5);
        victim.HasFullAngerGauge.Should().BeTrue();
    }

    [Fact]
    public void NobodyMaySwingWhileStandingOnGroundTheEnemyHolds()
    {
        using var provider = CreateProvider(_ => { });
        var (karus, elmorad) = CreatePair(
            provider, BattleZoneManager.ZONE_BIFROST, AccountNation.Karus, AccountNation.ElMorad);

        Place(elmorad, KarusLandingX, KarusLandingZ);

        PvpRules.CanAttackPlayer(elmorad, karus).Should().BeFalse();
        PvpRules.CanAttackPlayer(karus, elmorad).Should().BeTrue("a trespasser is still fair game");
    }

    [Fact]
    public void ANationIsUntouchableOnItsOwnLandingGround()
    {
        using var provider = CreateProvider(_ => { });
        var (karus, elmorad) = CreatePair(
            provider, BattleZoneManager.ZONE_BIFROST, AccountNation.Karus, AccountNation.ElMorad);

        Place(karus, KarusLandingX, KarusLandingZ);
        PvpRules.CanAttackPlayer(elmorad, karus).Should().BeFalse();

        Place(karus, KarusLandingX, KarusLandingZ + 200f);
        PvpRules.CanAttackPlayer(elmorad, karus).Should().BeTrue();
    }

    [Fact]
    public void TheSharedGroundInDelosShieldsBothNations()
    {
        using var provider = CreateProvider(_ => { });
        var (karus, elmorad) = CreatePair(
            provider, BattleZoneManager.ZONE_DELOS, AccountNation.Karus, AccountNation.ElMorad);

        Place(karus, DelosCentreX, DelosCentreZ);
        Place(elmorad, DelosCentreX, DelosCentreZ);

        ZoneSafetyAreas.IsInOwnSafetyArea(karus).Should().BeTrue();
        ZoneSafetyAreas.IsInOwnSafetyArea(elmorad).Should().BeTrue();
        ZoneSafetyAreas.IsInEnemySafetyArea(karus).Should().BeTrue();

        Place(karus, DelosCentreX + 200f, DelosCentreZ);
        ZoneSafetyAreas.IsInOwnSafetyArea(karus).Should().BeFalse();
    }

    private static void Place(UserSession session, float x, float z)
    {
        session.X = x;
        session.Z = z;
    }

    private static Task KillAsync(ServiceProvider provider, UserSession victim, UserSession killer)
    {
        victim.Hp = 0;
        return provider.GetRequiredService<ICombatLifecycleService>()
            .HandlePlayerDeathAsync(victim, killer);
    }

    private static (UserSession First, UserSession Second) CreatePair(
        ServiceProvider provider, byte zoneId, AccountNation firstNation, AccountNation secondNation)
        => (CreatePlayer(provider, 810, zoneId, firstNation),
            CreatePlayer(provider, 811, zoneId, secondNation));

    private static UserSession CreatePlayer(
        ServiceProvider provider, int characterId, byte zoneId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 100);
        session.Name = $"Fighter{characterId}";
        session.Class = 101;
        session.Level = 40;
        session.Nation = nation;
        session.ZoneId = zoneId;
        session.X = MoradonPartyArenaX;
        session.Z = MoradonPartyArenaZ;
        session.Hp = 300;
        session.MaxHp = 300;
        session.Loyalty = 100;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }
}
