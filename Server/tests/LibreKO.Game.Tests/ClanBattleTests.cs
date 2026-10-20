using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class ClanBattleTests
{
    private const byte Ronark = 71;
    private const short Wolves = 10;
    private const short Doves = 11;

    private sealed record Player(UserSession Session, List<Packet> Sent);

    private static (SessionManager, ClanBattleService) Arrange()
    {
        var sessions = new SessionManager();
        sessions.Knights.AddClan(Wolves, new KnightsEntity { Id = Wolves, Name = "Wolves", Chief = "Alpha", Nation = 1 });
        sessions.Knights.AddClan(Doves, new KnightsEntity { Id = Doves, Name = "Doves", Chief = "Beta", Nation = 1 });
        return (sessions, new ClanBattleService(sessions));
    }

    private static Player Join(SessionManager sessions, int id, string name, short clan, AccountNation nation = AccountNation.Karus)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id);
        session.Name = name;
        session.KnightsId = clan;
        session.Nation = nation;
        session.ZoneId = Ronark;
        session.Hp = 100;
        return new Player(session, sent);
    }

    private static (byte Sub, byte First) Head(Packet packet)
    {
        packet.ResetOffset();
        return (packet.ReadByte(), packet.ReadByte());
    }

    [Fact]
    public async Task AnAcceptedChallengeMakesTheTwoClansEnemies()
    {
        var (sessions, battles) = Arrange();
        var alpha = Join(sessions, 1, "Alpha", Wolves);
        var beta = Join(sessions, 2, "Beta", Doves);
        var wolf = Join(sessions, 3, "Cub", Wolves);

        await battles.ChallengeAsync(alpha.Session, beta.Session.CharacterId);
        Head(beta.Sent.Single()).Should().Be((ClanBattlePacketWriter.ChallengeSub, (byte)ClanBattleResult.Challenged));

        await battles.AnswerAsync(beta.Session, accept: true, alpha.Session.CharacterId);

        wolf.Session.ClanBattleEnemy.Should().Be(Doves);
        beta.Session.ClanBattleEnemy.Should().Be(Wolves);
        PvpRules.CanAttackPlayer(wolf.Session, beta.Session).Should().BeTrue();
        battles.EnemyOf(Wolves).Should().Be(Doves);
    }

    [Fact]
    public async Task AMemberWhoLeavesTheClanMidWarLeavesTheWarBehind()
    {
        var (sessions, battles) = Arrange();
        var alpha = Join(sessions, 1, "Alpha", Wolves);
        var beta = Join(sessions, 2, "Beta", Doves);
        var wolf = Join(sessions, 3, "Cub", Wolves);
        await battles.ChallengeAsync(alpha.Session, beta.Session.CharacterId);
        await battles.AnswerAsync(beta.Session, accept: true, alpha.Session.CharacterId);

        wolf.Session.KnightsId = 0;

        PvpRules.CanAttackPlayer(wolf.Session, beta.Session).Should().BeFalse("the war was the clan's, not the player's");
        await battles.SurrenderAsync(alpha.Session);
        wolf.Session.ClanBattleEnemy.Should().Be(0);
    }

    [Fact]
    public async Task OnlyLeadersOfTheSameNationMayChallenge()
    {
        var (sessions, battles) = Arrange();
        var cub = Join(sessions, 3, "Cub", Wolves);
        var beta = Join(sessions, 2, "Beta", Doves, AccountNation.ElMorad);
        var alpha = Join(sessions, 1, "Alpha", Wolves);

        await battles.ChallengeAsync(cub.Session, beta.Session.CharacterId);
        Head(cub.Sent.Last()).Should().Be((ClanBattlePacketWriter.ChallengeSub, (byte)ClanBattleResult.NotLeader));

        await battles.ChallengeAsync(alpha.Session, beta.Session.CharacterId);
        Head(alpha.Sent.Last()).Should().Be((ClanBattlePacketWriter.ChallengeSub, (byte)ClanBattleResult.OtherNation));
    }

    [Fact]
    public async Task SurrenderEndsTheBattleForBothClans()
    {
        var (sessions, battles) = Arrange();
        var alpha = Join(sessions, 1, "Alpha", Wolves);
        var beta = Join(sessions, 2, "Beta", Doves);
        await battles.ChallengeAsync(alpha.Session, beta.Session.CharacterId);
        await battles.AnswerAsync(beta.Session, accept: true, alpha.Session.CharacterId);

        await battles.SurrenderAsync(alpha.Session);

        alpha.Session.ClanBattleEnemy.Should().Be(0);
        beta.Session.ClanBattleEnemy.Should().Be(0);
        beta.Sent.Select(Head).Should().Contain((ClanBattlePacketWriter.EndSub, (byte)ClanBattleEnd.TheySurrendered));
        alpha.Sent.Select(Head).Should().Contain((ClanBattlePacketWriter.EndSub, (byte)ClanBattleEnd.YouSurrendered));
        PvpRules.CanAttackPlayer(alpha.Session, beta.Session).Should().BeFalse();
    }
}
