using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IClanMatchService
{
    bool Running { get; }
    Task<string> StartAsync(string clanA, string clanB);
    Task StopAsync();
    Task OnPlayerDiedAsync(UserSession victim, UserSession? killer);
}

public sealed class ClanMatchService : BackgroundService, IClanMatchService
{
    public const byte Zone = (byte)ZoneId.PartyClan1;
    public const int MaxFighters = 8;
    public const int MatchSeconds = 1200;
    public const int SendHomeSeconds = 10;
    public const byte NoTeam = 0;
    public const byte TeamA = 1;
    public const byte TeamB = 2;
    private const int TickMilliseconds = 1000;
    private const float StartAX = 132f, StartAZ = 31f, StartBX = 131f, StartBZ = 191f;
    private const float HomeX = 0f, HomeZ = 0f;
    private const short Draw = 0;

    private readonly SessionManager sessions;
    private readonly IZoneTransitionService zones;
    private readonly ILogger<ClanMatchService> logger;
    private readonly Dictionary<int, (short Hp, short Mp)> shown = [];
    private KnightsEntity? clanA, clanB;
    private int scoreA, scoreB, secondsLeft, homeLeft;

    public ClanMatchService(SessionManager sessions, IZoneTransitionService zones, ILogger<ClanMatchService> logger)
    {
        this.sessions = sessions;
        this.zones = zones;
        this.logger = logger;
        zones.ZoneAbilitySent += OnZoneAbilitySentAsync;
        zones.PlayerLeavingZone += (session, _) => LeaveAsync(session);
    }

    public bool Running => clanA != null && secondsLeft > 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Clan match tick failed");
            }
        }
    }

    public async Task<string> StartAsync(string clanAName, string clanBName)
    {
        if (clanA != null)
            return "A clan match is already running.";
        var a = ClanNamed(clanAName);
        var b = ClanNamed(clanBName);
        if (a == null || b == null || a.Id == b.Id)
            return "Name two different clans.";

        clanA = a;
        clanB = b;
        scoreA = scoreB = 0;
        secondsLeft = MatchSeconds;
        homeLeft = 0;
        shown.Clear();
        int moved = 0;
        foreach (var (clan, team, x, z) in new[] { (a, TeamA, StartAX, StartAZ), (b, TeamB, StartBX, StartBZ) })
            foreach (var fighter in sessions.GetAll().Where(s => s.KnightsId == clan.Id && s.Hp > 0 && s.MayBeMovedByOthers).Take(MaxFighters).ToList())
            {
                if (fighter.Hp <= 0 || !fighter.MayBeMovedByOthers)
                    continue;
                fighter.MatchTeam = team;
                moved++;
                await zones.ChangeZoneAsync(fighter, Zone, x, z);
            }
        logger.LogInformation("Clan match {A} vs {B} started with {Count} fighters", a.Name, b.Name, moved);
        return $"Clan match {a.Name} vs {b.Name} started with {moved} fighters.";
    }

    public async Task StopAsync()
    {
        if (clanA != null && secondsLeft > 0)
            await EndAsync();
    }

    internal async Task TickAsync()
    {
        if (clanA == null)
            return;
        if (secondsLeft > 0)
        {
            await SendVitalsAsync();
            if (--secondsLeft <= 0)
                await EndAsync();
            return;
        }
        if (--homeLeft <= 0)
            await SendEveryoneHomeAsync();
    }

    public async Task OnPlayerDiedAsync(UserSession victim, UserSession? killer)
    {
        if (!Running || killer == null || victim.MatchTeam == NoTeam || killer.MatchTeam == NoTeam || victim.MatchTeam == killer.MatchTeam)
            return;
        if (killer.MatchTeam == TeamA) scoreA++;
        else scoreB++;
        await Task.CompletedTask;
    }

    private async Task EndAsync()
    {
        secondsLeft = 0;
        homeLeft = SendHomeSeconds;
        short winner = scoreA == scoreB ? Draw : scoreA > scoreB ? clanA!.Id : clanB!.Id;
        await BroadcastAsync(ClanMatchPacketWriter.Result(winner));
        logger.LogInformation("Clan match ended {A}:{B}", scoreA, scoreB);
    }

    private async Task SendEveryoneHomeAsync()
    {
        var inZone = InZone();
        clanA = clanB = null;
        foreach (var session in inZone)
        {
            session.MatchTeam = NoTeam;
            await zones.ChangeZoneAsync(session, (byte)ZoneId.Moradon, HomeX, HomeZ);
        }
    }

    private async Task OnZoneAbilitySentAsync(UserSession session)
    {
        if (session.ZoneId != Zone || clanA == null)
            return;
        if (session.MatchTeam == NoTeam && !session.IsGM)
        {
            await zones.ChangeZoneAsync(session, (byte)ZoneId.Moradon, HomeX, HomeZ);
            return;
        }
        await session.Client.SendPacket(ClanMatchPacketWriter.Pair(Zone, clanA.Id, clanA.MarkVersion, clanA.Name,
            clanB!.Id, clanB.MarkVersion, clanB.Name));
        await BroadcastAsync(RosterPacket());
    }

    private async Task LeaveAsync(UserSession session)
    {
        if (session.MatchTeam == NoTeam)
            return;
        session.MatchTeam = NoTeam;
        shown.Remove(session.CharacterId);
        if (clanA != null)
            await BroadcastAsync(RosterPacket());
    }

    private Packet RosterPacket() =>
        ClanMatchPacketWriter.Roster(Fighters().Select(s => new ClanMatchPacketWriter.Fighter(
            s.KnightsId, s.CharacterId, s.Name, s.MaxHp, s.Hp, s.Class, s.MaxMp, s.Mp, (byte)s.Nation)).ToList());

    private async Task SendVitalsAsync()
    {
        foreach (var fighter in Fighters())
        {
            if (shown.TryGetValue(fighter.CharacterId, out var last) && last == (fighter.Hp, fighter.Mp))
                continue;
            shown[fighter.CharacterId] = (fighter.Hp, fighter.Mp);
            await BroadcastAsync(ClanMatchPacketWriter.Vitals(fighter.CharacterId, fighter.MaxHp, fighter.Hp, fighter.MaxMp, fighter.Mp));
        }
    }

    private List<UserSession> Fighters() => InZone().Where(s => s.MatchTeam != NoTeam).ToList();

    private List<UserSession> InZone() => sessions.GetAll().Where(s => s.ZoneId == Zone).ToList();

    private async Task BroadcastAsync(Packet packet)
    {
        foreach (var session in InZone())
            await session.Client.SendPacket(packet);
    }

    private KnightsEntity? ClanNamed(string name) =>
        sessions.Knights.GetAll().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
