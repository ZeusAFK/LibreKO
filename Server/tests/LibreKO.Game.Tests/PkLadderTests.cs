using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PkLadderTests : GameTestBase
{
    private const byte RankTypePkZone = 1;
    private const byte RonarkLand = 71;
    private const byte Moradon = 21;
    private const byte PremiumWithLoyalty = 12;
    private const int PremiumLoyalty = 5;

    [Fact]
    public async Task TheLadderListsOnlyPlayersInTheCallersZoneAndRoomBestFirst()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var me = Player(sessions, 1, "Rikka", AccountNation.Karus, RonarkLand, 300, sent);
        Player(sessions, 2, "Velkan", AccountNation.Karus, RonarkLand, 900, null);
        Player(sessions, 3, "Arwen", AccountNation.ElMorad, RonarkLand, 500, null);
        Player(sessions, 4, "Far", AccountNation.Karus, Moradon, 5000, null);
        var otherRoom = Player(sessions, 5, "Roomed", AccountNation.Karus, RonarkLand, 7000, null);
        otherRoom.Room = 3;
        me.DailyPremiumLoyalty = 1500;

        var request = new Packet(GameOpcodes.GS_RANK);
        request.WriteByte(RankTypePkZone);
        request.ResetOffset();
        await provider.GetRequiredService<INationSystemsPacketCoordinator>().HandleRankAsync(me.Client, request);

        var reply = sent.Should().ContainSingle().Subject;
        reply.ResetOffset();
        reply.ReadByte().Should().Be(RankTypePkZone);
        ReadNames(reply).Should().Equal("Velkan", "Rikka");
        ReadNames(reply).Should().Equal("Arwen");
        reply.ReadUShort().Should().Be(2);
        reply.ReadInt().Should().Be(300);
        reply.ReadUShort().Should().Be((ushort)RankPacketWriter.MaxPremiumBonus);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task KillRewardsAddThePremiumLoyaltyAndCountItAsTheShare()
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetPremiumProperty(PremiumWithLoyalty, PremiumPropertyType.Loyalty).Returns(PremiumLoyalty);
        var sessions = new SessionManager();
        var loyalty = new LoyaltyService(
            new TimeWeatherBroadcastService(sessions, Substitute.For<ILogger<TimeWeatherBroadcastService>>()),
            gameData,
            sessions,
            Substitute.For<IClanStandingService>(),
            Substitute.For<IAchievementProgressService>());
        var session = Player(sessions, 1, "Rikka", AccountNation.Karus, RonarkLand, 0, null);
        session.PremiumService = PremiumWithLoyalty;
        session.HeldPremiums[PremiumWithLoyalty] = DateTime.UtcNow.AddDays(1);

        await loyalty.AwardKillAsync(session, 20);
        await loyalty.ChangeAsync(session, 10);

        session.DailyLoyalty.Should().Be(20 + PremiumLoyalty + 10);
        session.DailyPremiumLoyalty.Should().Be(PremiumLoyalty);
    }

    private static IEnumerable<string> ReadNames(Packet packet)
    {
        var names = new List<string>();
        int count = packet.ReadUShort();
        for (int i = 0; i < count; i++)
        {
            names.Add(packet.ReadString());
            packet.ReadByte();
            packet.ReadUShort();
            packet.ReadUShort();
            packet.ReadString();
            packet.ReadInt();
            packet.ReadUShort();
            packet.ReadByte();
        }
        return names;
    }

    private static UserSession Player(SessionManager sessions, int id, string name, AccountNation nation, byte zone,
        int dailyLoyalty, List<Packet>? sent)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        if (sent != null)
            client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId: id, accountId: 100 + id);
        session.Name = name;
        session.Nation = nation;
        session.ZoneId = zone;
        session.DailyLoyalty = dailyLoyalty;
        return session;
    }
}
