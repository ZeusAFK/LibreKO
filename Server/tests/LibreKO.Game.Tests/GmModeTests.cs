using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GmModeTests : GameTestBase
{
    [Fact]
    public async Task GmAuraDefaultsOnAndIsSentToTheOwner()
    {
        using var provider = CreateProvider(_ => { });
        var (gm, sent) = Player(provider, 900, true);
        await provider.GetRequiredService<IAdminPanelPacketCoordinator>().SendGrantAsync(gm);
        States(sent).Should().Equal((gm.CharacterId, true));
    }

    [Fact]
    public async Task ToggleReachesTheOwnerAndNearbyPlayersOnly()
    {
        using var provider = CreateProvider(_ => { });
        var (gm, own) = Player(provider, 900, true);
        var (_, near) = Player(provider, 901, false);
        var (_, elsewhere) = Player(provider, 902, false, zone: 48);
        var admin = provider.GetRequiredService<IAdminPacketCoordinator>();
        await admin.HandleGmCommandAsync(gm, "+gm");
        gm.GmModeEnabled.Should().BeFalse();
        States(own).Should().Equal((900, false));
        States(near).Should().Equal((900, false));
        States(elsewhere).Should().BeEmpty();
        await admin.HandleGmCommandAsync(gm, "+gm");
        States(near).Should().Equal((900, false), (900, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotReplaysTheCurrentStateToANewObserver(bool enabled)
    {
        using var provider = CreateProvider(_ => { });
        var (gm, _) = Player(provider, 900, true);
        gm.GmModeEnabled = enabled;
        var (observer, received) = Player(provider, 901, false);
        await provider.GetRequiredService<IWorldVisibilityService>().SendNearbyUsersToClientAsync(observer);
        States(received).Should().Equal((900, enabled));
    }

    [Fact]
    public async Task PublicDemoPanelDoesNotAuthorizeGmMode()
    {
        using var provider = CreateProvider(_ => { }, null,
            settings => settings.PublicDemo.GrantGameMasterPanelToEveryone = true);
        var (player, sent) = Player(provider, 900, false);
        await provider.GetRequiredService<IAdminPacketCoordinator>().HandleGmCommandAsync(player, "+gm");
        States(sent).Should().BeEmpty();
        player.GmModeEnabled.Should().BeTrue();
    }

    [Fact]
    public void DamageRulesApplyOnlyWhileAGmHasTheModeOn()
    {
        var gm = new UserSession(Substitute.For<IClient>(), 1, 1) { IsGM = true, Hp = 100 };
        GmMode.Dealt(gm, 5000, 0).Should().Be(5000);
        GmMode.Dealt(gm, 5000, 40).Should().Be(5000);
        GmMode.Taken(gm, 400).Should().Be(GmMode.DamageTaken);
        GmMode.Taken(gm, 0).Should().Be(0);

        gm.GmModeEnabled = false;
        GmMode.Dealt(gm, 5000, 40).Should().Be(40);
        GmMode.Taken(gm, 400).Should().Be(400);

        var player = new UserSession(Substitute.For<IClient>(), 2, 2) { IsGM = false, Hp = 100 };
        GmMode.Dealt(player, 5000, 40).Should().Be(40);
        GmMode.Taken(player, 400).Should().Be(400);
    }

    [Fact]
    public async Task GmBasicAttackKillsAMonsterInOneSwing()
    {
        using var provider = CreateProvider(_ => { });
        var (gm, _) = Player(provider, 900, true);
        gm.MaxHp = 100;
        gm.AttackAmount = 100;
        gm.PlayerAttackAmount = 100;
        gm.Stats.TotalHit = 1;
        gm.Stats.TotalHitrate = 1;
        var worm = SpawnWorm(provider, gm, hp: 100000);

        await provider.GetRequiredService<ICombatPacketCoordinator>()
            .HandleAttackAsync(gm.Client, Swing(worm.UniqueId));

        worm.Hp.Should().Be(0);
    }

    [Fact]
    public async Task GmWithTheModeOffFightsNormally()
    {
        using var provider = CreateProvider(_ => { });
        var (gm, _) = Player(provider, 900, true);
        gm.GmModeEnabled = false;
        gm.MaxHp = 100;
        gm.AttackAmount = 100;
        gm.PlayerAttackAmount = 100;
        gm.Stats.TotalHit = 50;
        gm.Stats.TotalHitrate = 10;
        var worm = SpawnWorm(provider, gm, hp: 100000);

        await provider.GetRequiredService<ICombatPacketCoordinator>()
            .HandleAttackAsync(gm.Client, Swing(worm.UniqueId));

        worm.Hp.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GmTakesOneDamagePerMonsterHit()
    {
        using var provider = CreateProvider(_ => { });
        var (gm, _) = Player(provider, 900, true);
        gm.Hp = 1000;
        gm.MaxHp = 1000;
        gm.Stats.TotalAc = 0;
        gm.Stats.TotalEvasionrate = 1;
        var monster = SpawnWorm(provider, gm, hp: 100);
        monster.Attack1 = 5000;
        monster.HitRate = short.MaxValue;
        monster.AttackRange = 5;

        var combat = provider.GetRequiredService<INpcAiCombatService>();
        for (var i = 0; i < 32 && gm.Hp == gm.MaxHp; i++)
            await combat.ExecuteAttackAsync(monster, gm, DateTime.UtcNow.Ticks + i);

        gm.Hp.Should().Be((short)(gm.MaxHp - GmMode.DamageTaken));
    }

    private static Packet Swing(int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_ATTACK);
        packet.WriteByte(1);
        packet.WriteByte(0);
        packet.WriteInt(targetId);
        packet.WriteShort(100);
        packet.WriteShort(1);
        packet.WriteByte(0);
        packet.WriteByte(0);
        return packet;
    }

    private static NpcInstance SpawnWorm(ServiceProvider provider, UserSession beside, int hp) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 750,
            Name = "Worm",
            NpcType = 0,
            ZoneId = beside.ZoneId,
            X = beside.X + 1,
            Z = beside.Z,
            SpawnX = beside.X + 1,
            SpawnZ = beside.Z,
            Hp = hp,
            MaxHp = hp,
            Ac = 5,
            EvadeRate = 1,
            AttackRange = 3,
            SearchRange = 8,
            TracingRange = 20,
        });

    private static (UserSession Session, List<Packet> Sent) Player(
        ServiceProvider provider, short id, bool gm, byte zone = 21)
    {
        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, id, id + 1000);
        player.Name = $"Player{id}";
        player.IsGM = gm;
        player.ZoneId = zone;
        player.X = 100;
        player.Z = 100;
        player.Hp = 100;
        player.Class = 111;
        sessions.Regions.AddToRegion(player);
        return (player, sent);
    }

    private static IEnumerable<(int Id, bool Enabled)> States(List<Packet> packets)
    {
        foreach (var p in packets)
        {
            p.ResetOffset();
            if (p.GetOpcode() != (byte)GameOpcodes.GS_ADMIN_PANEL || p.ReadByte() != 0x13) continue;
            yield return (p.ReadInt(), p.ReadByte() == 1);
        }
    }
}
