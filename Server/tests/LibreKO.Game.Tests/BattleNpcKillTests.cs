using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class BattleNpcKillTests : GameTestBase
{
    private const byte WarZone = BattleZoneManager.ZONE_BATTLE1;
    private const int WarderLoyalty = 500;
    private const int KeeperLoyalty = 1000;
    private const byte ClientKeeperType = 8;

    [Fact]
    public async Task TheClientCannotReportWarKillsOrClaimWarPoints()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, WarZone);
        var (karus, client) = CreatePlayer(provider, characterId: 960, AccountNation.Karus);
        var coordinator = provider.GetRequiredService<IEventSystemsPacketCoordinator>();

        for (var kill = 0; kill < BattleZoneManager.NPC_KILL_VICTORY_COUNT; kill++)
            await coordinator.HandleBattleEventAsync(client, BattleEvent(BattleEventSubOpcode.Result, (byte)AccountNation.Karus));
        await coordinator.HandleBattleEventAsync(client, BattleEvent(BattleEventSubOpcode.MaxUser, ClientKeeperType));
        await coordinator.HandleBattleEventAsync(client, BattleEvent(BattleEventSubOpcode.MapEventResult, (byte)AccountNation.Karus));

        sessionManager.Battle.Victory.Should().Be(0);
        sessionManager.Battle.KilledElmoNpc.Should().Be(0);
        karus.Loyalty.Should().Be(0);
    }

    [Fact]
    public async Task KillingAnEnemyWarderPaysWarPointsAndCountsTowardTheWar()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, WarZone);
        var (karus, _) = CreatePlayer(provider, characterId: 961, AccountNation.Karus);
        var warder = SpawnBattleNpc(sessionManager, BattleZoneManager.FirstWarderSpecialType, EntityNation.ElMorad, karus);

        await provider.GetRequiredService<ICombatLifecycleService>().HandleNpcDeathAsync(warder, karus);

        karus.Loyalty.Should().Be(WarderLoyalty);
        sessionManager.Battle.KilledElmoNpc.Should().Be(1);
        sessionManager.Battle.Victory.Should().Be(0);
    }

    [Fact]
    public async Task AWarderKilledByTwoBlowsAtOnceCountsOnce()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, WarZone);
        var (karus, _) = CreatePlayer(provider, characterId: 965, AccountNation.Karus);
        var warder = SpawnBattleNpc(sessionManager, BattleZoneManager.FirstWarderSpecialType, EntityNation.ElMorad, karus);
        var lifecycle = provider.GetRequiredService<ICombatLifecycleService>();

        await Task.WhenAll(lifecycle.HandleNpcDeathAsync(warder, karus), lifecycle.HandleNpcDeathAsync(warder, karus));
        await lifecycle.HandleNpcDeathAsync(warder, karus);

        karus.Loyalty.Should().Be(WarderLoyalty, "one death pays once");
        sessionManager.Battle.KilledElmoNpc.Should().Be(1);

        warder.Respawn();
        await lifecycle.HandleNpcDeathAsync(warder, karus);
        karus.Loyalty.Should().Be(2 * WarderLoyalty, "a respawned warder can die again");
    }

    [Fact]
    public async Task KillingTheEnemyWardersAndKeeperWinsTheWarOnce()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, WarZone);
        var (karus, _) = CreatePlayer(provider, characterId: 962, AccountNation.Karus);
        var lifecycle = provider.GetRequiredService<ICombatLifecycleService>();

        await lifecycle.HandleNpcDeathAsync(SpawnBattleNpc(sessionManager, BattleZoneManager.FirstWarderSpecialType, EntityNation.ElMorad, karus), karus);
        await lifecycle.HandleNpcDeathAsync(SpawnBattleNpc(sessionManager, BattleZoneManager.LastWarderSpecialType, EntityNation.ElMorad, karus), karus);
        await lifecycle.HandleNpcDeathAsync(SpawnBattleNpc(sessionManager, BattleZoneManager.FirstKeeperSpecialType, EntityNation.ElMorad, karus), karus);

        sessionManager.Battle.Victory.Should().Be((byte)AccountNation.Karus);
        karus.Loyalty.Should().Be(WarderLoyalty * 2 + KeeperLoyalty);
    }

    [Fact]
    public async Task KillingAWarderOfYourOwnNationCountsForNothing()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, WarZone);
        var (karus, _) = CreatePlayer(provider, characterId: 963, AccountNation.Karus);
        var ownWarder = SpawnBattleNpc(sessionManager, BattleZoneManager.FirstWarderSpecialType, EntityNation.Karus, karus);

        await provider.GetRequiredService<ICombatLifecycleService>().HandleNpcDeathAsync(ownWarder, karus);

        karus.Loyalty.Should().Be(0);
        sessionManager.Battle.KilledElmoNpc.Should().Be(0);
        sessionManager.Battle.KilledKarusNpc.Should().Be(0);
    }

    [Fact]
    public async Task ABattleNpcKilledWithNoWarOpenCountsForNothing()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (karus, _) = CreatePlayer(provider, characterId: 964, AccountNation.Karus);
        var warder = SpawnBattleNpc(sessionManager, BattleZoneManager.FirstWarderSpecialType, EntityNation.ElMorad, karus);

        await provider.GetRequiredService<ICombatLifecycleService>().HandleNpcDeathAsync(warder, karus);

        karus.Loyalty.Should().Be(0);
        sessionManager.Battle.KilledElmoNpc.Should().Be(0);
    }

    private static Packet BattleEvent(BattleEventSubOpcode sub, byte value)
    {
        var packet = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        packet.WriteByte((byte)sub);
        packet.WriteByte(value);
        return packet;
    }

    private static NpcInstance SpawnBattleNpc(
        SessionManager sessionManager, short specialType, EntityNation nation, UserSession killer)
    {
        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 11041,
            Name = "Warder",
            IsMonster = true,
            ZoneId = WarZone,
            X = killer.X,
            Z = killer.Z,
            SpawnX = killer.X,
            SpawnZ = killer.Z,
            Hp = 0,
            MaxHp = 100,
            Nation = nation,
            SpecialType = specialType
        });
        npc.TopDamagerCharId = killer.CharacterId;
        npc.DamageMap[killer.CharacterId] = npc.MaxHp;
        return npc;
    }

    private static (UserSession Session, IClient Client) CreatePlayer(
        IServiceProvider provider, int characterId, AccountNation nation)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Soldier{characterId}";
        session.Nation = nation;
        session.ZoneId = WarZone;
        session.Hp = 100;
        session.X = 500;
        session.Z = 500;
        sessionManager.Regions.AddToRegion(session);
        return (session, client);
    }
}
