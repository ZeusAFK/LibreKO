using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class WarCaptainTests
{
    private const byte WarZone = BattleZoneManager.ZONE_BATTLE4;
    private const byte FameMember = 5;

    private readonly SessionManager sessions = new();
    private readonly IKingSystemRuntimeService kings = Substitute.For<IKingSystemRuntimeService>();
    private readonly IZoneTransitionService zones = Substitute.For<IZoneTransitionService>();
    private readonly WarCaptainService captains;

    public WarCaptainTests()
    {
        captains = new WarCaptainService(sessions, Options.Create(new GameServerSettings()), kings,
            Substitute.For<IGameDataService>(), zones, NullLogger<WarCaptainService>.Instance);
    }

    private UserSession Player(int id, short clan, byte fame, byte zone = WarZone)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id + 1000);
        session.Name = $"Player{id}";
        session.Nation = AccountNation.Karus;
        session.ZoneId = zone;
        session.KnightsId = clan;
        session.KnightsFame = fame;
        return session;
    }

    private void Clan(short id, int points) =>
        sessions.Knights.AddClan(id, new KnightsEntity { Id = id, Name = $"Clan{id}", Nation = (byte)AccountNation.Karus, Points = points });

    [Fact]
    public async Task TheChiefsOfTheTopFiveClansInTheWarBecomeCaptains()
    {
        for (short clan = 1; clan <= 6; clan++) Clan(clan, clan * 100);
        var chiefs = Enumerable.Range(1, 6).Select(i => Player(i, (short)i, ClanRules.FameChief)).ToList();
        var member = Player(10, 6, FameMember);
        var away = Player(11, 5, ClanRules.FameChief, (byte)ZoneId.Moradon);

        await captains.SelectAsync();

        chiefs[0].KnightsFame.Should().Be(ClanRules.FameChief);
        chiefs.Skip(1).Should().OnlyContain(s => s.KnightsFame == WarCaptainService.CaptainFame);
        member.KnightsFame.Should().Be(FameMember);
        away.KnightsFame.Should().Be(ClanRules.FameChief);
        captains.IsCaptain(chiefs[5]).Should().BeTrue();
    }

    [Fact]
    public async Task TheKingInTheWarIsACaptainToo()
    {
        var king = Player(1, 0, 0);
        kings.GetKingData(AccountNation.Karus).Returns(new KingSystemData { Nation = (byte)AccountNation.Karus });
        kings.IsKing(king, Arg.Any<KingSystemData>()).Returns(true);

        await captains.SelectAsync();

        king.KnightsFame.Should().Be(WarCaptainService.CaptainFame);
    }

    [Fact]
    public async Task TheWarEndGivesTheOldRankBack()
    {
        Clan(1, 100);
        var chief = Player(1, 1, ClanRules.FameChief);
        await captains.SelectAsync();

        await captains.ResetAsync();

        chief.KnightsFame.Should().Be(ClanRules.FameChief);
        captains.IsCaptain(chief).Should().BeFalse();
    }

    [Fact]
    public async Task ACaptainLoggingInAfterTheWarIsAChiefAgain()
    {
        var stale = Player(1, 1, WarCaptainService.CaptainFame);

        zones.ZoneAbilitySent += Raise.Event<Func<UserSession, Task>>(stale);

        stale.KnightsFame.Should().Be(ClanRules.FameChief);
    }

    [Fact]
    public void TheAnnouncementTakesUpToFiveNames() =>
        WarCaptainService.Fill("The captain(s) for Karus is(are) %s, %s, %s, %s, %s", ["[A][x]", "[B][y]"])
            .Should().Be("The captain(s) for Karus is(are) [A][x], [B][y]");
}
