using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ZoneLeavingTests : GameTestBase
{
    private const byte Moradon = 21;
    private static readonly TimeSpan SlowHandlerDelay = TimeSpan.FromMilliseconds(100);

    private static (ServiceProvider Provider, UserSession Session) Arrange()
    {
        var provider = CreateProvider(
            _ => { },
            gameData => gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>()));
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 131, accountId: 141);
        session.ZoneId = Moradon;
        session.Nation = AccountNation.Karus;
        return (provider, session);
    }

    [Fact]
    public async Task EveryLeaveHandlerFinishesBeforeTheZoneChanges()
    {
        var (provider, session) = Arrange();
        using (provider)
        {
            var zones = provider.GetRequiredService<IZoneTransitionService>();
            var seen = new List<(string Handler, byte OldZone)>();
            zones.PlayerLeavingZone += async (_, oldZone) =>
            {
                await Task.Delay(SlowHandlerDelay);
                seen.Add(("slow", oldZone));
            };
            zones.PlayerLeavingZone += (_, oldZone) =>
            {
                seen.Add(("fast", oldZone));
                return Task.CompletedTask;
            };

            await zones.ChangeZoneAsync(session, (byte)ZoneId.Ardream, 100f, 100f);

            seen.Should().Equal(("slow", Moradon), ("fast", Moradon));
            session.ZoneId.Should().Be((byte)ZoneId.Ardream);
        }
    }

    [Fact]
    public async Task StayingInTheSameZoneRaisesNoLeave()
    {
        var (provider, session) = Arrange();
        using (provider)
        {
            var zones = provider.GetRequiredService<IZoneTransitionService>();
            var raised = false;
            zones.PlayerLeavingZone += (_, _) =>
            {
                raised = true;
                return Task.CompletedTask;
            };

            await zones.ChangeZoneAsync(session, Moradon, 100f, 100f);

            raised.Should().BeFalse();
        }
    }
}
