using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class RebirthZoneTests : GameTestBase
{
    private const byte Moradon = 21;

    private static (ServiceProvider Provider, UserSession Session, List<Packet> Sent) Arrange(short rebirthLevel, bool gm = false)
    {
        var provider = CreateProvider(
            _ => { },
            gameData => gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>()));
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 111, accountId: 121);
        session.ZoneId = Moradon;
        session.Nation = AccountNation.Karus;
        session.RebirthLevel = rebirthLevel;
        session.IsGM = gm;
        return (provider, session, sent);
    }

    [Theory]
    [InlineData(ZoneId.Ardream)]
    [InlineData(ZoneId.RonarkLandBase)]
    public async Task ARebornCharacterIsTurnedAwayFromArdreamAndRonarkLandBase(ZoneId zone)
    {
        var (provider, session, sent) = Arrange(1);
        using (provider)
        {
            await provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(session, (byte)zone, 100f, 100f);

            session.ZoneId.Should().Be(Moradon);
            session.IsWarping.Should().BeFalse();
            sent.Should().ContainSingle(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT);
            sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public async Task AnUnrebornCharacterOrAGameMasterStillEnters(short rebirthLevel, bool gm)
    {
        var (provider, session, sent) = Arrange(rebirthLevel, gm);
        using (provider)
        {
            await provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(session, (byte)ZoneId.Ardream, 100f, 100f);

            session.ZoneId.Should().Be((byte)ZoneId.Ardream);
            sent.Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ZONE_CHANGE);
        }
    }

    [Fact]
    public async Task RonarkLandItselfStaysOpenToTheReborn()
    {
        var (provider, session, _) = Arrange(15);
        using (provider)
        {
            await provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(session, (byte)ZoneId.RonarkLand, 100f, 100f);
            session.ZoneId.Should().Be((byte)ZoneId.RonarkLand);
        }
    }
}
