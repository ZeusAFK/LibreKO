using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GmWarpTests : GameTestBase
{
    private const byte Moradon = 21;
    private const float Start = 100f;
    private const ushort Destination = 9000;
    private const float DestinationUnits = Destination / 10f;

    [Fact]
    public async Task APlayerCannotSendTheGmWarpToMoveAnywhere()
    {
        using var provider = CreateProvider(_ => { });
        var (session, client, sent) = CreatePlayer(provider, characterId: 980);

        await provider.GetRequiredService<IWorldPacketCoordinator>()
            .HandleRecvWarpAsync(client, WarpPacket(Destination, Destination));

        session.X.Should().Be(Start);
        session.Z.Should().Be(Start);
        sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WARP);
    }

    [Fact]
    public async Task AGameMasterStillWarpsWithIt()
    {
        using var provider = CreateProvider(_ => { });
        var (session, client, sent) = CreatePlayer(provider, characterId: 981);
        session.IsGM = true;

        await provider.GetRequiredService<IWorldPacketCoordinator>()
            .HandleRecvWarpAsync(client, WarpPacket(Destination, Destination));

        session.X.Should().Be(DestinationUnits);
        session.Z.Should().Be(DestinationUnits);
        sent.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WARP);
    }

    private static Packet WarpPacket(ushort x, ushort z)
    {
        var packet = new Packet(GameOpcodes.GS_WARP);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        return packet;
    }

    private static (UserSession Session, IClient Client, List<Packet> Sent) CreatePlayer(
        IServiceProvider provider, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.Hp = 100;
        session.X = Start;
        session.Z = Start;
        sessionManager.Regions.AddToRegion(session);
        return (session, client, sent);
    }
}
