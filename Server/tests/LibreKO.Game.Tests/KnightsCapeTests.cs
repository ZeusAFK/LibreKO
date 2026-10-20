using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class KnightsCapeTests : GameTestBase
{
    private const short ClanId = 77;
    private const byte Chief = 1;
    private const int NoCape = -1;
    private const int Red = 0x0000FF;
    private const short BoughtCape = 5;
    private const int CapePrice = 1_000_000;
    private const int CapePoints = 5_000;

    [Theory]
    [InlineData(KnightsCapePacketCoordinator.PaintCostClanPoints - 1, false)]
    [InlineData(KnightsCapePacketCoordinator.PaintCostClanPoints, true)]
    public async Task PaintAskedForWithATicketStillCostsClanPoints(int fund, bool painted)
    {
        using var provider = CreateProvider(db =>
        {
            db.Knights.Add(new KnightsEntity { Id = ClanId, Name = "Painters", Chief = "Boss", Nation = 1, Flag = (byte)ClanType.Accredited5 });
            db.SaveChanges();
        });
        var sessions = provider.GetRequiredService<SessionManager>();
        var clan = new KnightsEntity
        {
            Id = ClanId, Name = "Painters", Chief = "Boss", Nation = 1, Flag = (byte)ClanType.Accredited5, ClanPointFund = fund,
        };
        sessions.Knights.AddClan(ClanId, clan);
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var chief = sessions.CreateSession(client, 1, 1);
        (chief.Name, chief.Hp, chief.KnightsId, chief.KnightsFame) = ("Boss", 100, ClanId, Chief);
        var request = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        request.WriteByte(KnightsCapePacketCoordinator.OpcodeTicketPurchase);
        request.WriteShort(NoCape);
        request.WriteInt(Red);
        request.ResetOffset();

        await provider.GetRequiredService<IKnightsCapePacketCoordinator>().HandleAsync(client, request);

        (clan.CapeR != 0).Should().Be(painted, "a ticket buys a cape, not free paint");
        clan.ClanPointFund.Should().Be(painted ? 0 : fund);
    }

    [Fact]
    public async Task ACapeTheChiefCannotPayForLeavesTheClanPointsAlone()
    {
        using var provider = CreateProvider(db =>
        {
            db.Knights.Add(new KnightsEntity { Id = ClanId, Name = "Painters", Chief = "Boss", Nation = 1, Flag = (byte)ClanType.Accredited5 });
            db.SaveChanges();
        }, gameData => gameData.KnightsCapeTable.Returns(new Dictionary<short, Common.Domain.Entities.GameData.KnightsCapeData>
        {
            [BoughtCape] = new() { CapeIndex = BoughtCape, BuyPrice = CapePrice, BuyLoyalty = CapePoints, Ranking = (byte)ClanType.Accredited5 },
        }));
        var sessions = provider.GetRequiredService<SessionManager>();
        var clan = new KnightsEntity
        {
            Id = ClanId, Name = "Painters", Chief = "Boss", Nation = 1, Flag = (byte)ClanType.Accredited5, ClanPointFund = CapePoints, Cape = NoCape,
        };
        sessions.Knights.AddClan(ClanId, clan);
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var chief = sessions.CreateSession(client, 1, 1);
        (chief.Name, chief.Hp, chief.KnightsId, chief.KnightsFame, chief.Money) = ("Boss", 100, ClanId, Chief, CapePrice - 1);
        var request = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        request.WriteByte(0);
        request.WriteShort(BoughtCape);
        request.WriteInt(0);
        request.ResetOffset();

        await provider.GetRequiredService<IKnightsCapePacketCoordinator>().HandleAsync(client, request);

        clan.Cape.Should().Be(NoCape);
        clan.ClanPointFund.Should().Be(CapePoints, "the points taken for a cape that was not paid for go back");
        chief.Money.Should().Be(CapePrice - 1);
    }
}
