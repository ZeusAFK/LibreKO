using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AdminPanelCashTests
{
    private const byte ReqCash = 19;

    private static (AdminPanelPacketCoordinator Coordinator, UserSession Gm, IClient Client, List<Packet> Sent) Arrange()
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var gm = sessions.CreateSession(client, 1, 1);
        gm.Name = "Zeus";
        gm.IsGM = true;

        var coordinator = new AdminPanelPacketCoordinator(
            sessions,
            Substitute.For<IGameDataService>(),
            Substitute.For<IUserNotificationService>(),
            Substitute.For<ICombatNotificationService>(),
            Substitute.For<IZoneTransitionService>(),
            Substitute.For<IWorldMovementService>(),
            Substitute.For<INpcSpawnRowService>(),
            Substitute.For<INpcSpawnRowStore>(),
            Substitute.For<IHostEnvironment>(),
            Substitute.For<ICollectionRaceService>(),
            Substitute.For<IPlayerProgressionService>(),
            Substitute.For<ILoyaltyService>(),
            Substitute.For<IItemGrantService>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new GameServerSettings()),
            Substitute.For<ILogger<AdminPanelPacketCoordinator>>());
        return (coordinator, gm, client, sent);
    }

    private static Packet CashRequest(int amount)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(ReqCash);
        packet.WriteInt(amount);
        packet.ResetOffset();
        return packet;
    }

    private static int LastBalance(List<Packet> sent)
    {
        var balance = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_SHOPPING_MALL);
        balance.ResetOffset();
        balance.ReadByte().Should().Be(1);
        balance.ReadByte().Should().Be(5);
        return balance.ReadInt();
    }

    [Fact]
    public async Task AddingCashRaisesTheBalance_AndSendsIt()
    {
        var (coordinator, gm, client, sent) = Arrange();
        gm.KnightCash = 500;

        await coordinator.HandleAsync(client, CashRequest(10_000));

        gm.KnightCash.Should().Be(10_500);
        LastBalance(sent).Should().Be(10_500);
    }

    [Fact]
    public async Task TakingMoreCashThanThereIsStopsAtZero()
    {
        var (coordinator, gm, client, sent) = Arrange();
        gm.KnightCash = 500;

        await coordinator.HandleAsync(client, CashRequest(-1_000));

        gm.KnightCash.Should().Be(0);
        LastBalance(sent).Should().Be(0);
    }
}
