using System.Collections.Concurrent;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public enum SoccerJoinResult
{
    Joined,
    WrongZone,
    BadTeam,
    AlreadyIn,
    TeamFull,
}

public interface ISoccerService
{
    byte TeamOf(int characterId);
    bool Running { get; }
    Task<SoccerJoinResult> JoinAsync(UserSession session, byte team);
    Task<bool> StartAsync(UserSession session, bool practice = false);
    Task LeaveAsync(UserSession session);
    Task<bool> KickAsync(UserSession caster, int targetId, int[] data);
    Task TickAsync();
    Task FinishAsync();
}

public sealed class SoccerService(
    SessionManager sessionManager,
    IServiceProvider services,
    ILogger<SoccerService> logger) : BackgroundService, ISoccerService
{
    public const byte TeamNone = 0;
    public const byte TeamBlue = 1;
    public const byte TeamRed = 2;
    public const byte Draw = 0;
    public const int MaxTeamPlayers = 11;
    public const int MatchSeconds = 600;
    public const short BallNpcId = 3201;
    public const byte PitchZone = (byte)ZoneId.Moradon;
    public const int KickOffEmote = 12;
    public const int KickXSlot = 0;
    public const int KickZSlot = 2;
    private const int LastSecond = 1;
    private const int TickSeconds = 1;
    private const float PositionScale = 10f;
    private const ushort BallMoveRate = 60;

    private readonly ConcurrentDictionary<int, byte> teams = new();
    private readonly SemaphoreSlim stateLock = new(1, 1);
    private int secondsLeft;
    private bool practice;
    private byte blue, red;
    private int lastKickerId;

    public bool Running { get; private set; }

    public byte TeamOf(int characterId) => teams.GetValueOrDefault(characterId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (services.GetService<IZoneTransitionService>() is { } zones)
            zones.PlayerLeavingZone += (session, oldZone) => oldZone == PitchZone ? LeaveAsync(session) : Task.CompletedTask;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Soccer tick failed");
            }
        }
    }

    public async Task<SoccerJoinResult> JoinAsync(UserSession session, byte team)
    {
        await stateLock.WaitAsync();
        try
        {
            if (session.ZoneId != PitchZone)
                return SoccerJoinResult.WrongZone;
            if (team is not (TeamBlue or TeamRed))
                return SoccerJoinResult.BadTeam;
            if (teams.ContainsKey(session.CharacterId))
                return SoccerJoinResult.AlreadyIn;
            if (teams.Values.Count(t => t == team) >= MaxTeamPlayers)
                return SoccerJoinResult.TeamFull;
            teams[session.CharacterId] = team;
        }
        finally
        {
            stateLock.Release();
        }

        await session.Client.SendPacket(Running
            ? SoccerPacketWriter.Sync(secondsLeft, blue, red)
            : SoccerPacketWriter.Clock(0));
        await WarpAsync(session, SoccerPitch.CentreX, team == TeamBlue ? SoccerPitch.BlueKickOffZ : SoccerPitch.RedKickOffZ);
        await SetTeamAsync(session, team);
        logger.LogInformation("{Name} joined the {Team} soccer team", session.Name, team == TeamBlue ? "blue" : "red");
        return SoccerJoinResult.Joined;
    }

    public async Task<bool> StartAsync(UserSession session, bool practice = false)
    {
        await stateLock.WaitAsync();
        try
        {
            if (Running)
                return false;
            if (!practice && (!teams.ContainsKey(session.CharacterId)
                              || !teams.Values.Contains(TeamBlue) || !teams.Values.Contains(TeamRed)))
                return false;
            Running = true;
            this.practice = practice;
            secondsLeft = MatchSeconds;
            blue = red = 0;
            lastKickerId = 0;
        }
        finally
        {
            stateLock.Release();
        }

        await KickOffAsync();
        await SendToAudienceAsync(SoccerPacketWriter.Clock(MatchSeconds));
        logger.LogInformation("Soccer match started by {Name}{Practice}", session.Name, practice ? " (practice)" : "");
        return true;
    }

    public async Task LeaveAsync(UserSession session)
    {
        if (!teams.TryRemove(session.CharacterId, out _))
            return;
        await SetTeamAsync(session, TeamNone);
    }

    public async Task<bool> KickAsync(UserSession caster, int targetId, int[] data)
    {
        if (TeamOf(caster.CharacterId) == TeamNone)
            return false;
        var ball = sessionManager.Regions.GetNpc(targetId);
        if (ball == null || !ball.IsSoccerBall || ball.ZoneId != caster.ZoneId)
            return false;
        float reachX = ball.X - caster.X, reachZ = ball.Z - caster.Z;
        if (reachX * reachX + reachZ * reachZ > SoccerPitch.MaxKickerDistance * SoccerPitch.MaxKickerDistance)
            return false;

        var (x, z) = SoccerPitch.KickLanding(ball.X, ball.Z, data[KickXSlot], data[KickZSlot]);
        lastKickerId = caster.CharacterId;
        await MoveBallAsync(ball, x, z);
        return true;
    }

    public async Task TickAsync()
    {
        if (!Running)
            return;
        if (!practice && Players().Count <= 1)
            secondsLeft = Math.Min(secondsLeft, LastSecond);

        if (Ball() is { } ball)
        {
            switch (SoccerPitch.Classify(ball.X, ball.Z))
            {
                case SoccerBallSpot.BlueScores:
                    blue++;
                    await ScoreAsync(TeamRed);
                    break;
                case SoccerBallSpot.RedScores:
                    red++;
                    await ScoreAsync(TeamBlue);
                    break;
                case SoccerBallSpot.Out:
                    await ResetBallAsync();
                    break;
            }
        }

        secondsLeft--;
        if (secondsLeft <= 0)
            await FinishAsync();
    }

    public async Task FinishAsync()
    {
        if (!Running)
            return;
        Running = false;
        byte winner = blue > red ? TeamBlue : red > blue ? TeamRed : Draw;
        var players = Players();
        foreach (var (session, team) in players)
            await WarpAsync(session, team == TeamBlue ? SoccerPitch.BlueExitX : SoccerPitch.RedExitX,
                team == TeamBlue ? SoccerPitch.BlueExitZ : SoccerPitch.RedExitZ);
        await SendToAudienceAsync(SoccerPacketWriter.End(winner, blue, red));
        foreach (var (session, _) in players)
            await SetTeamAsync(session, TeamNone);
        teams.Clear();
        await ResetBallAsync();
        logger.LogInformation("Soccer match ended {Blue} - {Red}", blue, red);
    }

    private async Task ScoreAsync(byte goalTeam)
    {
        byte kickerTeam = TeamOf(lastKickerId);
        await SendToAudienceAsync(SoccerPacketWriter.Goal(lastKickerId, kickerTeam, goalTeam, blue, red));
        await KickOffAsync();
    }

    private async Task KickOffAsync()
    {
        foreach (var (session, team) in Players())
        {
            await WarpAsync(session, SoccerPitch.CentreX, team == TeamBlue ? SoccerPitch.BlueKickOffZ : SoccerPitch.RedKickOffZ);
            await sessionManager.Regions.SendToRegion(session,
                MovementPacketWriter.StateChange(session.CharacterId, (byte)StateChangeType.Action, KickOffEmote), excludeSender: false);
        }
        await ResetBallAsync();
    }

    private List<(UserSession Session, byte Team)> Players()
    {
        var players = new List<(UserSession, byte)>();
        foreach (var (characterId, team) in teams)
            if (sessionManager.GetByCharacterId(characterId) is { } session && session.ZoneId == PitchZone)
                players.Add((session, team));
        return players;
    }

    private async Task SendToAudienceAsync(Packet packet)
    {
        foreach (var session in sessionManager.GetAll())
        {
            if (session.ZoneId != PitchZone)
                continue;
            float dx = session.X - SoccerPitch.CentreX, dz = session.Z - SoccerPitch.CentreZ;
            if (teams.ContainsKey(session.CharacterId) || dx * dx + dz * dz <= SoccerPitch.SpectatorRange * SoccerPitch.SpectatorRange)
                await session.Client.SendPacket(packet);
        }
    }

    private async Task SetTeamAsync(UserSession session, byte team)
    {
        session.SoccerTeam = team;
        await sessionManager.Regions.SendToRegion(session,
            MovementPacketWriter.StateChange(session.CharacterId, (byte)StateChangeType.SoccerTeam, team), excludeSender: false);
    }

    private Task WarpAsync(UserSession session, float x, float z) =>
        services.GetRequiredService<IWorldMovementService>()
            .WarpAsync(session, (ushort)(x * PositionScale), (ushort)(z * PositionScale));

    private NpcInstance? Ball() => sessionManager.Regions.GetNpcByProtoId(PitchZone, BallNpcId);

    private async Task MoveBallAsync(NpcInstance ball, float x, float z)
    {
        ball.X = x;
        ball.Z = z;
        if (sessionManager.Maps != null)
            ball.Y = sessionManager.Maps.GetHeight(ball.ZoneId, x, z);
        sessionManager.Regions.UpdateNpcRegion(ball);
        await sessionManager.Regions.BroadcastFromNpc(ball,
            NpcSpawnPacketWriter.Move(ball.UniqueId, ball.GetPosX, ball.GetPosZ, ball.GetPosY, BallMoveRate));
    }

    private async Task ResetBallAsync()
    {
        if (Ball() is not { } ball)
            return;
        await sessionManager.Regions.BroadcastFromNpc(ball, NpcPacketMapper.BuildInOutPacket(ball, InOutType.Out));
        ball.X = SoccerPitch.CentreX;
        ball.Z = SoccerPitch.CentreZ;
        if (sessionManager.Maps != null)
            ball.Y = sessionManager.Maps.GetHeight(ball.ZoneId, ball.X, ball.Z);
        sessionManager.Regions.UpdateNpcRegion(ball);
        await sessionManager.Regions.BroadcastFromNpc(ball, NpcPacketMapper.BuildInOutPacket(ball, InOutType.In));
    }
}
