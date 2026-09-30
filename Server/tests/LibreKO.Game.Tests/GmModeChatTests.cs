using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GmModeChatTests : GameTestBase
{
    private const byte GeneralChannel = (byte)ChatType.General;

    private static (List<Packet> Sent, UserSession Session) GameMaster(ServiceProvider provider, bool gmMode)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: 300, accountId: 310);
        session.Name = "Zeus";
        session.ZoneId = (byte)ZoneId.Moradon;
        session.Nation = AccountNation.ElMorad;
        session.Level = 83;
        session.IsGM = true;
        session.GmModeEnabled = gmMode;
        session.X = 800;
        session.Z = 500;
        sessions.Regions.AddToRegion(session);
        return (sent, session);
    }

    private static (byte Type, byte Authority) ChatLine(Packet packet)
    {
        packet.ResetOffset();
        var type = packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadSByteString();
        packet.ReadString();
        return (type, packet.ReadByte());
    }

    [Fact]
    public async Task AGameMasterInGmModeSpeaksOnTheGameMasterChannel()
    {
        using var provider = CreateProvider(_ => { });
        var (sent, session) = GameMaster(provider, gmMode: true);

        await provider.GetRequiredService<IChatPacketCoordinator>().HandleAsync(session, GeneralChannel, "Event in ten minutes");

        var line = ChatLine(sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT));
        line.Type.Should().Be(ChatPacketWriter.TypeGameMaster);
        line.Authority.Should().Be(ChatPacketWriter.AuthorityGameMaster);
    }

    [Fact]
    public async Task AGameMasterOutOfGmModeSpeaksLikeAnyPlayer()
    {
        using var provider = CreateProvider(_ => { });
        var (sent, session) = GameMaster(provider, gmMode: false);

        await provider.GetRequiredService<IChatPacketCoordinator>().HandleAsync(session, GeneralChannel, "Anyone selling a bow?");

        var line = ChatLine(sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT));
        line.Type.Should().Be(ChatPacketWriter.TypeGeneral);
        line.Authority.Should().Be(ChatPacketWriter.AuthorityPlayer);
    }
}
