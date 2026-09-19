using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GmFxTests : GameTestBase
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
        await admin.HandleGmCommandAsync(gm, "+gmfx");
        gm.GmFxEnabled.Should().BeFalse();
        States(own).Should().Equal((900, false));
        States(near).Should().Equal((900, false));
        States(elsewhere).Should().BeEmpty();
        await admin.HandleGmCommandAsync(gm, "+gmfx");
        States(near).Should().Equal((900, false), (900, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotReplaysTheCurrentStateToANewObserver(bool enabled)
    {
        using var provider = CreateProvider(_ => { });
        var (gm, _) = Player(provider, 900, true);
        gm.GmFxEnabled = enabled;
        var (observer, received) = Player(provider, 901, false);
        await provider.GetRequiredService<IWorldVisibilityService>().SendNearbyUsersToClientAsync(observer);
        States(received).Should().Equal((900, enabled));
    }

    [Fact]
    public async Task PublicDemoPanelDoesNotAuthorizeGmAura()
    {
        using var provider = CreateProvider(_ => { }, null,
            settings => settings.PublicDemo.GrantGameMasterPanelToEveryone = true);
        var (player, sent) = Player(provider, 900, false);
        await provider.GetRequiredService<IAdminPacketCoordinator>().HandleGmCommandAsync(player, "+gmfx");
        States(sent).Should().BeEmpty();
        player.GmFxEnabled.Should().BeTrue();
    }

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
