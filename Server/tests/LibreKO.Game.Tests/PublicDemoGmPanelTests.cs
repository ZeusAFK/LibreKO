using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PublicDemoGmPanelTests : GameTestBase
{
    private const byte ReqState = 1;
    private const byte ReqZone = 6;
    private const byte AckState = 0x10;
    private const byte AckResult = 0x11;
    private const byte AckGrant = 0x12;

    private const byte BattleArenaZone = 48;
    private const byte UnknownZone = 200;

    [Fact]
    public async Task AdminPanel_NormalAccount_IsRefusedWhenPublicDemoIsOff()
    {
        var (provider, client, session, sent) = Arrange(publicDemo: false);
        using (provider)
        {
            await Handle(provider, client, ReqState);

            sent.Should().ContainSingle();
            sent[0].ResetOffset();
            sent[0].GetOpcode().Should().Be((byte)GameOpcodes.GS_ADMIN_PANEL);
            sent[0].ReadByte().Should().Be(AckState);
            sent[0].ReadByte().Should().Be(0);
            session.IsGM.Should().BeFalse();
        }
    }

    [Fact]
    public async Task AdminPanel_NormalAccount_IsGrantedWhenPublicDemoIsOn()
    {
        var (provider, client, session, sent) = Arrange(publicDemo: true);
        using (provider)
        {
            await Handle(provider, client, ReqState);

            sent.Should().ContainSingle();
            sent[0].ResetOffset();
            sent[0].ReadByte().Should().Be(AckState);
            sent[0].ReadByte().Should().Be(1);
            session.IsGM.Should().BeFalse();
        }
    }

    [Fact]
    public async Task SendGrantAsync_AnnouncesPublicDemoGrantOnlyWhileTheSwitchIsOn()
    {
        var (offProvider, _, offSession, offSent) = Arrange(publicDemo: false);
        using (offProvider)
        {
            await offProvider.GetRequiredService<IAdminPanelPacketCoordinator>().SendGrantAsync(offSession);
            offSent.Should().BeEmpty();
        }

        var (onProvider, _, onSession, onSent) = Arrange(publicDemo: true);
        using (onProvider)
        {
            await onProvider.GetRequiredService<IAdminPanelPacketCoordinator>().SendGrantAsync(onSession);

            onSent.Should().ContainSingle();
            onSent[0].ResetOffset();
            onSent[0].GetOpcode().Should().Be((byte)GameOpcodes.GS_ADMIN_PANEL);
            onSent[0].ReadByte().Should().Be(AckGrant);
            onSent[0].ReadByte().Should().Be((byte)AdminPanelGrant.PublicDemo);
            onSent[0].ReadByte().Should().Be(0);
        }
    }

    [Fact]
    public async Task SendGrantAsync_AnnouncesTheSpeedGrantOnItsOwn()
    {
        var (provider, _, session, sent) = Arrange(publicDemo: false, publicDemoSpeed: true);
        using (provider)
        {
            await provider.GetRequiredService<IAdminPanelPacketCoordinator>().SendGrantAsync(session);

            sent.Should().ContainSingle();
            sent[0].ResetOffset();
            sent[0].ReadByte().Should().Be(AckGrant);
            sent[0].ReadByte().Should().Be((byte)AdminPanelGrant.None);
            sent[0].ReadByte().Should().Be(1);
        }
    }

    [Fact]
    public async Task AdminPanel_SpeedGrantAloneDoesNotOpenThePanel()
    {
        var (provider, client, _, sent) = Arrange(publicDemo: false, publicDemoSpeed: true);
        using (provider)
        {
            await Handle(provider, client, ReqState);

            sent.Should().ContainSingle();
            sent[0].ResetOffset();
            sent[0].ReadByte().Should().Be(AckState);
            sent[0].ReadByte().Should().Be(0);
        }
    }

    [Fact]
    public async Task SendGrantAsync_AnnouncesGameMasterGrantWithoutTheSwitch()
    {
        var (provider, _, session, sent) = Arrange(publicDemo: false);
        using (provider)
        {
            session.IsGM = true;

            await provider.GetRequiredService<IAdminPanelPacketCoordinator>().SendGrantAsync(session);

            sent.Should().HaveCount(2);
            sent[0].ResetOffset();
            sent[0].ReadByte().Should().Be(AckGrant);
            sent[0].ReadByte().Should().Be((byte)AdminPanelGrant.GameMaster);
            sent[0].ReadByte().Should().Be(1);
        }
    }

    [Fact]
    public async Task AdminPanel_ZoneChange_MovesAPublicDemoAccountToTheZone()
    {
        var (provider, client, session, sent) = Arrange(publicDemo: true, configureGameData: SeedZones);
        using (provider)
        {
            session.ZoneId = 21;

            await HandleZone(provider, client, BattleArenaZone);

            session.ZoneId.Should().Be(BattleArenaZone);
            session.IsGM.Should().BeFalse();
            sent.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE);

            var result = sent.First(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ADMIN_PANEL);
            result.ResetOffset();
            result.ReadByte().Should().Be(AckResult);
            result.ReadByte().Should().Be(1);
            result.ReadString().Should().Contain("Battle Arena");
        }
    }

    [Fact]
    public async Task AdminPanel_ZoneChange_IsRefusedWhenPublicDemoIsOff()
    {
        var (provider, client, session, sent) = Arrange(publicDemo: false, configureGameData: SeedZones);
        using (provider)
        {
            session.ZoneId = 21;

            await HandleZone(provider, client, BattleArenaZone);

            session.ZoneId.Should().Be(21);
            sent.Should().ContainSingle();
            sent[0].ResetOffset();
            sent[0].ReadByte().Should().Be(AckState);
            sent[0].ReadByte().Should().Be(0);
        }
    }

    [Fact]
    public async Task AdminPanel_ZoneChange_RefusesAZoneTheServerDoesNotHave()
    {
        var (provider, client, session, sent) = Arrange(publicDemo: true, configureGameData: SeedZones);
        using (provider)
        {
            session.ZoneId = 21;

            await HandleZone(provider, client, UnknownZone);

            session.ZoneId.Should().Be(21);
            sent.Should().ContainSingle();
            sent[0].ResetOffset();
            sent[0].ReadByte().Should().Be(AckResult);
            sent[0].ReadByte().Should().Be(0);
        }
    }

    private static (ServiceProvider Provider, IClient Client, UserSession Session, List<Packet> Sent) Arrange(
        bool publicDemo,
        bool publicDemoSpeed = false,
        Action<IGameDataService>? configureGameData = null)
    {
        var provider = CreateProvider(
            _ => { },
            configureGameData,
            settings =>
            {
                settings.PublicDemo.GrantGameMasterPanelToEveryone = publicDemo;
                settings.PublicDemo.GrantGameMasterSpeedToEveryone = publicDemoSpeed;
            });

        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId: 900, accountId: 910);
        session.Name = "DemoPlayer";
        session.Class = 101;

        return (provider, client, session, sent);
    }

    private static Task Handle(ServiceProvider provider, IClient client, byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(sub);
        return provider.GetRequiredService<IAdminPanelPacketCoordinator>().HandleAsync(client, packet);
    }

    private static Task HandleZone(ServiceProvider provider, IClient client, short zoneId)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(ReqZone);
        packet.WriteShort(zoneId);
        return provider.GetRequiredService<IAdminPanelPacketCoordinator>().HandleAsync(client, packet);
    }

    private static void SeedZones(IGameDataService gameData)
    {
        gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>());
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [BattleArenaZone] = new()
            {
                ZoneNo = BattleArenaZone,
                MapName = "Battle Arena",
                InitX = 1_350_000,
                InitZ = 1_150_000,
            },
        });
    }
}
