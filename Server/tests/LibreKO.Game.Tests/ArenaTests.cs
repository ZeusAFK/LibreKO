using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ArenaTests : GameTestBase
{
    private const byte Moradon = BattleZoneManager.ZONE_MORADON;

    private const float PartyArenaX = 710f;
    private const float PartyArenaZ = 386f;
    private const float PersonArenaX = 710f;
    private const float PersonArenaZ = 466f;
    private const float TownSquareX = 816f;
    private const float TownSquareZ = 531f;

    [Theory]
    [InlineData(Moradon, PartyArenaX, PartyArenaZ, ArenaZones.MoradonPartyArena)]
    [InlineData(Moradon, PersonArenaX, PersonArenaZ, ArenaZones.MoradonPersonArena)]
    [InlineData(Moradon, 680f, 356f, ArenaZones.MoradonPartyArena)]
    [InlineData(Moradon, 736f, 496f, ArenaZones.MoradonPersonArena)]
    [InlineData(Moradon, TownSquareX, TownSquareZ, ArenaZones.NoArena)]
    [InlineData(Moradon, 710f, 424f, ArenaZones.NoArena)]
    [InlineData(BattleZoneManager.ZONE_RONARK_LAND, PartyArenaX, PartyArenaZ, ArenaZones.NoArena)]
    public void ArenaZones_GetArenaId_MatchesRetailRegionBounds(byte zoneId, float x, float z, byte expected)
        => ArenaZones.GetArenaId(zoneId, x, z).Should().Be(expected);

    [Fact]
    public void ZoneTransitionService_GetZoneAbilityType_ArenaOverridesNeutralTown()
    {
        ZoneTransitionService.GetZoneAbilityType(Moradon, ArenaZones.NoArena)
            .Should().Be(ZoneAbilityType.Neutral);
        ZoneTransitionService.GetZoneAbilityType(Moradon, ArenaZones.MoradonPersonArena)
            .Should().Be(ZoneAbilityType.FreeForAll);
    }

    [Fact]
    public async Task ZoneTransitionService_SendZoneAbility_SendsFreeForAllInsideAnArena()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.KingSystemTable.Returns(new Dictionary<byte, Common.Domain.Entities.GameData.KingSystemData>()));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId: 8100, accountId: 8101);
        session.ZoneId = Moradon;
        session.Nation = AccountNation.ElMorad;
        session.X = PersonArenaX;
        session.Z = PersonArenaZ;

        await provider.GetRequiredService<IZoneTransitionService>().SendZoneAbilityAsync(session);

        session.ArenaId.Should().Be(ArenaZones.MoradonPersonArena);
        sentPacket.Should().NotBeNull();
        sentPacket!.GetOpcode().Should().Be((byte)GameOpcodes.GS_ZONEABILITY);
        sentPacket.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be((byte)ZoneAbilityType.FreeForAll);
    }

    [Fact]
    public void PvpRules_CanAttackPlayer_FreeForAllInsideOneArenaOnly()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var a = CreateSession(sessionManager, 8200, AccountNation.Karus, Moradon);
        var b = CreateSession(sessionManager, 8201, AccountNation.Karus, Moradon);

        a.ArenaId = ArenaZones.MoradonPersonArena;
        b.ArenaId = ArenaZones.MoradonPersonArena;
        PvpRules.CanAttackPlayer(a, b).Should().BeTrue();
        PvpRules.IsEnemy(a, b).Should().BeTrue();

        b.ArenaId = ArenaZones.MoradonPartyArena;
        PvpRules.CanAttackPlayer(a, b).Should().BeFalse();

        b.ArenaId = ArenaZones.NoArena;
        PvpRules.CanAttackPlayer(a, b).Should().BeFalse();
        PvpRules.CanAttackPlayer(b, a).Should().BeFalse();
    }

    [Fact]
    public void PvpRules_CanAttackPlayer_ACorpseIsNotAttackable()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var karus = CreateSession(sessionManager, 8500, AccountNation.Karus, BattleZoneManager.ZONE_ARDREAM);
        var elmo = CreateSession(sessionManager, 8501, AccountNation.ElMorad, BattleZoneManager.ZONE_ARDREAM);

        PvpRules.CanAttackPlayer(karus, elmo).Should().BeTrue();

        elmo.Hp = 0;
        PvpRules.CanAttackPlayer(karus, elmo).Should().BeFalse();
        PvpRules.IsEnemy(karus, elmo).Should().BeTrue();
    }

    [Fact]
    public void PvpRules_CanAttackPlayer_BlockedInTheNeutralTownOutsideAnArena()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var karus = CreateSession(sessionManager, 8300, AccountNation.Karus, Moradon);
        var elmo = CreateSession(sessionManager, 8301, AccountNation.ElMorad, Moradon);

        PvpRules.CanAttackPlayer(karus, elmo).Should().BeFalse();

        karus.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        elmo.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        PvpRules.CanAttackPlayer(karus, elmo).Should().BeTrue();
    }

    [Fact]
    public async Task CombatLifecycleService_HandlePlayerDeathAsync_ArenaDeathCostsNoPoints()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var victim = CreateSession(sessionManager, 8400, AccountNation.Karus, Moradon);
        victim.Hp = 0;
        victim.MaxHp = 100;
        victim.Loyalty = 500;
        victim.ArenaId = ArenaZones.MoradonPersonArena;
        sessionManager.Regions.AddToRegion(victim);

        var killer = CreateSession(sessionManager, 8401, AccountNation.ElMorad, Moradon);
        killer.Hp = 100;
        killer.MaxHp = 100;
        killer.Loyalty = 500;
        killer.ArenaId = ArenaZones.MoradonPersonArena;
        sessionManager.Regions.AddToRegion(killer);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandlePlayerDeathAsync(victim, killer);

        victim.Loyalty.Should().Be(500);
        killer.Loyalty.Should().Be(500);
        victim.HasRival.Should().BeFalse();
        killer.HasRival.Should().BeFalse();
    }

    [Theory]
    [InlineData(ArenaZones.MoradonPartyArena)]
    [InlineData(ArenaZones.MoradonPersonArena)]
    public void ArenaZones_TryGetExit_LandsOutsideEveryArena(byte arenaId)
    {
        ArenaZones.TryGetExit(arenaId, out var x, out var z).Should().BeTrue();

        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dz = -2; dz <= 2; dz++)
                ArenaZones.GetArenaId(Moradon, x + dx, z + dz).Should().Be(ArenaZones.NoArena);
        }
    }

    [Fact]
    public void ArenaZones_TryGetExit_UnknownArenaHasNoExit()
        => ArenaZones.TryGetExit(ArenaZones.NoArena, out _, out _).Should().BeFalse();

    [Fact]
    public async Task CombatLifecycleService_HandleRegeneAsync_ArenaDeathRespawnsAtTheArenaExit()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var victim = CreateSession(sessionManager, 8500, AccountNation.Karus, Moradon);
        victim.Hp = 0;
        victim.MaxHp = 100;
        victim.X = PersonArenaX;
        victim.Z = PersonArenaZ;
        victim.ArenaId = ArenaZones.MoradonPersonArena;
        sessionManager.Regions.AddToRegion(victim);

        await provider.GetRequiredService<ICombatLifecycleService>()
            .HandleRegeneAsync(victim.Client, victim, 1);

        ArenaZones.TryGetExit(ArenaZones.MoradonPersonArena, out var exitX, out var exitZ);
        victim.X.Should().BeInRange(exitX - 2f, exitX + 2f);
        victim.Z.Should().BeInRange(exitZ - 2f, exitZ + 2f);
        victim.Hp.Should().Be(victim.MaxHp);
        victim.ArenaId.Should().Be(ArenaZones.NoArena);
    }

    private static UserSession CreateSession(
        SessionManager sessionManager, int characterId, AccountNation nation, byte zoneId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1);
        session.Name = $"Fighter{characterId}";
        session.Nation = nation;
        session.ZoneId = zoneId;
        session.X = 710f;
        session.Z = 466f;
        session.MaxHp = 100;
        session.Hp = 100;
        return session;
    }
}
