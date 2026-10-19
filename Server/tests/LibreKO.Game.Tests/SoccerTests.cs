using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class SoccerTests
{
    private const byte Moradon = SoccerService.PitchZone;
    private const float NearBallZ = SoccerPitch.CentreZ - 2f;

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent);

    private readonly SessionManager sessions = new();
    private readonly IWorldMovementService movement = Substitute.For<IWorldMovementService>();
    private readonly SoccerService soccer;
    private readonly NpcInstance ball;

    public SoccerTests()
    {
        var provider = new ServiceCollection().AddSingleton(movement).BuildServiceProvider();
        soccer = new SoccerService(sessions, provider, NullLogger<SoccerService>.Instance);
        ball = new NpcInstance
        {
            NpcId = SoccerService.BallNpcId, NpcType = NpcData.TypeSoccerBall, Name = "Football", IsMonster = true,
            ZoneId = Moradon, X = SoccerPitch.CentreX, Z = SoccerPitch.CentreZ,
        };
        sessions.Regions.SpawnNpc(ball);
    }

    private Player Join(int id, string name, byte zone = Moradon)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id);
        session.Name = name;
        session.ZoneId = zone;
        session.Nation = AccountNation.Karus;
        session.X = SoccerPitch.CentreX;
        session.Z = NearBallZ;
        sessions.Regions.UpdateRegion(session);
        return new Player(session, client, sent);
    }

    private static IEnumerable<byte[]> SoccerPackets(Player player) => player.Sent
        .Where(p => p.GetOpcode() == (byte)GameOpcodes.GS_MINING)
        .Select(p => p.GetData())
        .Where(d => d.Length > 0 && d[0] == SoccerPacketWriter.MiningSub);

    private static int[] KickTo(float x, float z)
    {
        var data = new int[7];
        data[SoccerService.KickXSlot] = (int)x;
        data[SoccerService.KickZSlot] = (int)z;
        return data;
    }

    [Theory]
    [InlineData(672f, 112f, SoccerBallSpot.BlueScores)]
    [InlineData(672f, 203f, SoccerBallSpot.RedScores)]
    [InlineData(672f, 199.5f, SoccerBallSpot.RedScores)]
    [InlineData(690f, 205f, SoccerBallSpot.Out)]
    [InlineData(640f, 160f, SoccerBallSpot.Out)]
    [InlineData(672f, 160f, SoccerBallSpot.InPlay)]
    public void TheBallIsClassifiedByThePitchAndGoalMouths(float x, float z, SoccerBallSpot spot)
    {
        SoccerPitch.Classify(x, z).Should().Be(spot);
    }

    [Fact]
    public void AKickLandsNoFurtherThanTheLongestShot()
    {
        var (x, z) = SoccerPitch.KickLanding(672f, 160f, 672f, 200f);

        x.Should().Be(672f);
        z.Should().BeApproximately(160f + SoccerPitch.MaxKickReach, 0.001f);
        SoccerPitch.KickLanding(672f, 160f, 672f, 166f).Should().Be((672f, 166f));
    }

    [Fact]
    public void ThePacketsFollowThe2619Layouts()
    {
        SoccerPacketWriter.Goal(77, SoccerService.TeamBlue, SoccerService.TeamRed, 2, 1).GetData()
            .Should().Equal(16, 1, 77, 0, 0, 0, 1, 2, 2, 1);
        SoccerPacketWriter.Clock(600).GetData().Should().Equal(16, 2, 0x58, 0x02);
        SoccerPacketWriter.Sync(30, 3, 4).GetData().Should().Equal(16, 3, 0, 30, 0, 3, 4);
        SoccerPacketWriter.End(SoccerService.TeamRed, 1, 2).GetData().Should().Equal(16, 4, 2, 1, 2);
    }

    [Fact]
    public async Task JoiningSetsTheTeamWarpsToKickOffAndResetsTheBoard()
    {
        var rin = Join(1, "Rin");

        (await soccer.JoinAsync(rin.Session, SoccerService.TeamBlue)).Should().Be(SoccerJoinResult.Joined);

        rin.Session.SoccerTeam.Should().Be(SoccerService.TeamBlue);
        SoccerPackets(rin).Should().ContainSingle().Which.Should().Equal(SoccerPacketWriter.Clock(0).GetData());
        rin.Sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE);
        await movement.Received(1).WarpAsync(rin.Session, (ushort)(SoccerPitch.CentreX * 10), (ushort)(SoccerPitch.BlueKickOffZ * 10));
    }

    [Fact]
    public async Task JoiningIsRefusedOutsideMoradonTwiceOrIntoAFullTeam()
    {
        var away = Join(1, "Away", zone: (byte)ZoneId.RonarkLand);
        (await soccer.JoinAsync(away.Session, SoccerService.TeamRed)).Should().Be(SoccerJoinResult.WrongZone);

        for (int i = 0; i < SoccerService.MaxTeamPlayers; i++)
            await soccer.JoinAsync(Join(10 + i, $"Red{i}").Session, SoccerService.TeamRed);
        var late = Join(50, "Late");
        (await soccer.JoinAsync(late.Session, SoccerService.TeamRed)).Should().Be(SoccerJoinResult.TeamFull);
        (await soccer.JoinAsync(late.Session, SoccerService.TeamBlue)).Should().Be(SoccerJoinResult.Joined);
        (await soccer.JoinAsync(late.Session, SoccerService.TeamRed)).Should().Be(SoccerJoinResult.AlreadyIn);
    }

    [Fact]
    public async Task AMatchNeedsBothTeamsUnlessItIsPractice()
    {
        var rin = Join(1, "Rin");
        await soccer.JoinAsync(rin.Session, SoccerService.TeamBlue);

        (await soccer.StartAsync(rin.Session)).Should().BeFalse();
        var kai = Join(2, "Kai");
        await soccer.JoinAsync(kai.Session, SoccerService.TeamRed);
        (await soccer.StartAsync(rin.Session)).Should().BeTrue();
        soccer.Running.Should().BeTrue();
        SoccerPackets(kai).Last().Should().Equal(SoccerPacketWriter.Clock(SoccerService.MatchSeconds).GetData());
    }

    [Fact]
    public async Task ABallKickedIntoTheRedGoalScoresForBlueAndNamesTheKicker()
    {
        var rin = Join(1, "Rin");
        await soccer.JoinAsync(rin.Session, SoccerService.TeamBlue);
        await soccer.StartAsync(rin.Session, practice: true);
        ball.X = 672f;
        ball.Z = 125f;
        rin.Session.Z = 128f;

        (await soccer.KickAsync(rin.Session, ball.UniqueId, KickTo(672f, 112f))).Should().BeTrue();
        rin.Sent.Clear();
        await soccer.TickAsync();

        SoccerPackets(rin).Should().ContainSingle().Which.Should()
            .Equal(SoccerPacketWriter.Goal(rin.Session.CharacterId, SoccerService.TeamBlue, SoccerService.TeamRed, 1, 0).GetData());
        (ball.X, ball.Z).Should().Be((SoccerPitch.CentreX, SoccerPitch.CentreZ));
    }

    [Fact]
    public async Task OnlyAPlayerOnATeamNextToTheBallCanKickIt()
    {
        var rin = Join(1, "Rin");
        (await soccer.KickAsync(rin.Session, ball.UniqueId, KickTo(672f, 170f))).Should().BeFalse();

        await soccer.JoinAsync(rin.Session, SoccerService.TeamBlue);
        rin.Session.Z = SoccerPitch.CentreZ + SoccerPitch.MaxKickerDistance * 2;
        (await soccer.KickAsync(rin.Session, ball.UniqueId, KickTo(672f, 170f))).Should().BeFalse();
    }

    [Fact]
    public async Task TheFinalWhistleSendsTheResultAndClearsTheTeams()
    {
        var rin = Join(1, "Rin");
        var kai = Join(2, "Kai");
        await soccer.JoinAsync(rin.Session, SoccerService.TeamBlue);
        await soccer.JoinAsync(kai.Session, SoccerService.TeamRed);
        await soccer.StartAsync(rin.Session);
        rin.Sent.Clear();

        await soccer.FinishAsync();

        SoccerPackets(rin).Should().ContainSingle().Which.Should().Equal(SoccerPacketWriter.End(SoccerService.Draw, 0, 0).GetData());
        rin.Session.SoccerTeam.Should().Be(SoccerService.TeamNone);
        soccer.TeamOf(kai.Session.CharacterId).Should().Be(SoccerService.TeamNone);
        soccer.Running.Should().BeFalse();
        await movement.Received(1).WarpAsync(kai.Session, (ushort)(SoccerPitch.RedExitX * 10), (ushort)(SoccerPitch.RedExitZ * 10));
    }

    [Fact]
    public async Task AMatchWithOnePlayerLeftEndsOnTheNextTicks()
    {
        var rin = Join(1, "Rin");
        var kai = Join(2, "Kai");
        await soccer.JoinAsync(rin.Session, SoccerService.TeamBlue);
        await soccer.JoinAsync(kai.Session, SoccerService.TeamRed);
        await soccer.StartAsync(rin.Session);

        await soccer.LeaveAsync(kai.Session);
        await soccer.TickAsync();

        soccer.Running.Should().BeFalse();
    }

    [Fact]
    public void TheBallCannotBeHitAndHasNoAi()
    {
        ball.IsAttackable.Should().BeFalse();
        ball.HasAi.Should().BeFalse();
    }
}
