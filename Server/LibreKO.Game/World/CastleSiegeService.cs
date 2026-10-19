using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public interface ICastleSiegeService
{
    CastleSiegePhase Phase { get; }
    int WarSecondsLeft { get; }
    Task StartCountdownAsync();
    Task OpenAsync();
    Task CloseAsync();
    Task OnNpcKilledAsync(NpcInstance npc, UserSession? killer);
    Packet StatusPacket();
}

public sealed class CastleSiegeService : BackgroundService, ICastleSiegeService
{
    private const int TickMilliseconds = 1000;
    private const int SecondsPerMinute = 60;
    private const int TextMinutesLeft = 173;
    private const int TextAboutToBegin = 169;
    private const int TextBegun = 171;
    private const int TextEnded = 172;
    private const int TextArtifactDestroyed = 179;

    private readonly SessionManager sessions;
    private readonly IGameDataService gameData;
    private readonly IZoneTransitionService zones;
    private readonly INpcSummonService summon;
    private readonly INpcLifecycleService lifecycle;
    private readonly IServiceScopeFactory scopes;
    private readonly IOptions<GameServerSettings> settings;
    private readonly ScheduleClock scheduleClock;
    private readonly ILogger<CastleSiegeService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    private int countdownSeconds;
    private int warSeconds;
    private int holdSeconds;
    private int lastScheduleMinute = -1;
    private NpcInstance? artifact;

    public CastleSiegeService(
        SessionManager sessions,
        IGameDataService gameData,
        IZoneTransitionService zones,
        INpcSummonService summon,
        INpcLifecycleService lifecycle,
        IServiceScopeFactory scopes,
        IOptions<GameServerSettings> settings,
        ScheduleClock scheduleClock,
        ILogger<CastleSiegeService> logger)
    {
        this.sessions = sessions;
        this.gameData = gameData;
        this.zones = zones;
        this.summon = summon;
        this.lifecycle = lifecycle;
        this.scopes = scopes;
        this.settings = settings;
        this.scheduleClock = scheduleClock;
        this.logger = logger;
        this.zones.ZoneAbilitySent += OnZoneAbilitySentAsync;
    }

    public CastleSiegePhase Phase { get; private set; }

    public int WarSecondsLeft => Phase == CastleSiegePhase.War ? Math.Max(0, warSeconds) : 0;

    private short OwnerClan => gameData.SiegeWarfare?.MasterKnights ?? 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(ScheduleTime.Now(settings.Value.Events, scheduleClock.UtcNow));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Castle siege tick failed");
            }
        }
    }

    internal async Task TickAsync(DateTime now)
    {
        switch (Phase)
        {
            case CastleSiegePhase.None:
                if (gameData.SiegeWarfare is { } siege && now.Minute != lastScheduleMinute
                    && CastleSiegeRules.IsScheduledStart(siege.WarDay, siege.WarTime, siege.WarMinute, now))
                {
                    lastScheduleMinute = now.Minute;
                    await StartCountdownAsync();
                }
                break;
            case CastleSiegePhase.Countdown:
                if (--countdownSeconds <= 0)
                    await OpenAsync();
                else if (countdownSeconds % SecondsPerMinute == 0)
                    await AnnounceMinutesLeftAsync(countdownSeconds / SecondsPerMinute);
                break;
            case CastleSiegePhase.War:
                warSeconds--;
                if (OwnerClan > 0)
                    holdSeconds--;
                if (warSeconds <= 0 || (OwnerClan > 0 && holdSeconds <= 0))
                    await CloseAsync();
                else if (warSeconds % SecondsPerMinute == 0)
                    await BroadcastToDelosAsync(StatusPacket());
                break;
        }
    }

    public async Task StartCountdownAsync()
    {
        if (Phase != CastleSiegePhase.None)
            return;
        Phase = CastleSiegePhase.Countdown;
        countdownSeconds = CastleSiegeRules.CountdownMinutes * SecondsPerMinute;
        logger.LogInformation("Castle siege countdown started");
        await AnnounceMinutesLeftAsync(CastleSiegeRules.CountdownMinutes);
    }

    public async Task OpenAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (Phase == CastleSiegePhase.War)
                return;
            Phase = CastleSiegePhase.War;
            warSeconds = CastleSiegeRules.WarSeconds;
            holdSeconds = OwnerClan > 0 ? CastleSiegeRules.HoldAfterCapture(warSeconds) : 0;
            sessions.Battle.CastleSiegeOpen = true;
            logger.LogInformation("Castle siege opened, owner clan {Clan}", OwnerClan);
        }
        finally
        {
            gate.Release();
        }

        await SpawnArtifactAsync();
        await AnnounceAsync(TextBegun, "Siege has begun");
        foreach (var session in InDelos())
            await zones.SendZoneAbilityAsync(session);
    }

    public async Task CloseAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (Phase == CastleSiegePhase.None)
                return;
            bool wasWar = Phase == CastleSiegePhase.War;
            Phase = CastleSiegePhase.None;
            sessions.Battle.CastleSiegeOpen = false;
            countdownSeconds = warSeconds = holdSeconds = 0;
            if (!wasWar)
                return;
        }
        finally
        {
            gate.Release();
        }

        await RemoveArtifactAsync();
        await BroadcastToDelosAsync(StatusPacket(CastleSiegeRules.StateEnded));
        foreach (var session in InDelos())
        {
            session.SiegeSide = SiegeSide.None;
            await zones.SendZoneAbilityAsync(session);
        }
        await AnnounceAsync(TextEnded, "Siege has ended");
        logger.LogInformation("Castle siege closed, owner clan {Clan}", OwnerClan);
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession? killer)
    {
        if (Phase != CastleSiegePhase.War || npc.NpcId != CastleSiegeRules.ArtifactNpc || npc.ZoneId != (byte)ZoneId.Delos)
            return;

        artifact = null;
        short newOwner = killer?.KnightsId > 0 ? killer.KnightsId : (short)0;
        await SetOwnerAsync(newOwner);
        holdSeconds = newOwner > 0 ? CastleSiegeRules.HoldAfterCapture(warSeconds) : 0;
        logger.LogInformation("Castle captured by clan {Clan} ({Killer})", newOwner, killer?.Name);

        await AnnounceAsync(TextArtifactDestroyed, "Center Artifact has been destroyed");
        foreach (var session in InDelos())
        {
            StampSide(session);
            await zones.ChangeZoneAsync(session, (byte)ZoneId.Delos, 0f, 0f);
        }
        await SpawnArtifactAsync();
        await BroadcastToDelosAsync(StatusPacket());
    }

    public Packet StatusPacket() => StatusPacket(Phase == CastleSiegePhase.War
        ? OwnerClan > 0 ? CastleSiegeRules.StateHeld : CastleSiegeRules.StateUnheld
        : CastleSiegeRules.StateNone);

    private Packet StatusPacket(byte state)
    {
        var owner = OwnerClan > 0 ? sessions.Knights.GetClan(OwnerClan) : null;
        return SiegePacketWriter.WarStatus(
            state,
            OwnerClan,
            owner == null || owner.MarkVersion < 0 ? (short)0 : owner.MarkVersion,
            owner?.AllianceId ?? 0,
            Phase == CastleSiegePhase.War ? holdSeconds : 0,
            Phase == CastleSiegePhase.War ? warSeconds : 0,
            owner?.Name ?? string.Empty);
    }

    private async Task OnZoneAbilitySentAsync(UserSession session)
    {
        if (session.ZoneId != (byte)ZoneId.Delos)
        {
            session.SiegeSide = SiegeSide.None;
            return;
        }
        if (Phase != CastleSiegePhase.War)
        {
            session.SiegeSide = SiegeSide.None;
            await session.Client.SendPacket(StatusPacket());
            return;
        }
        if (session.KnightsId <= 0 && !session.IsGM)
        {
            await zones.ChangeZoneAsync(session, (byte)ZoneId.Moradon, 0f, 0f);
            return;
        }
        StampSide(session);
        await session.Client.SendPacket(StatusPacket());
    }

    private void StampSide(UserSession session)
    {
        var alliance = session.KnightsId > 0 ? sessions.Knights.GetClan(session.KnightsId)?.AllianceId ?? 0 : 0;
        session.SiegeSide = CastleSiegeRules.SideOf(session.KnightsId, alliance, OwnerClan);
    }

    private async Task SetOwnerAsync(short clanId)
    {
        if (gameData.SiegeWarfare is not { } siege)
            return;
        siege.MasterKnights = clanId;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attach(siege);
        db.Entry(siege).Property(s => s.MasterKnights).IsModified = true;
        await db.SaveChangesAsync();
    }

    private async Task SpawnArtifactAsync()
    {
        await RemoveArtifactAsync();
        var spawned = await summon.SummonAsync(CastleSiegeRules.ArtifactNpc, (byte)ZoneId.Delos, 0,
            CastleSiegeRules.ArtifactX, CastleSiegeRules.ArtifactZ, 1, CastleSiegeRules.ArtifactFallbackY);
        artifact = spawned.Count > 0 ? spawned[0] : null;
        if (artifact == null)
            logger.LogWarning("Castle siege artifact {Npc} could not be spawned", CastleSiegeRules.ArtifactNpc);
    }

    private async Task RemoveArtifactAsync()
    {
        if (this.artifact is { IsAlive: true } artifact)
            await lifecycle.DespawnAsync(artifact);
        this.artifact = null;
    }

    private IEnumerable<UserSession> InDelos() =>
        sessions.GetAll().Where(s => s.ZoneId == (byte)ZoneId.Delos).ToList();

    private async Task BroadcastToDelosAsync(Packet packet)
    {
        foreach (var session in InDelos())
            await session.Client.SendPacket(packet);
    }

    private Task AnnounceMinutesLeftAsync(int minutes) => minutes <= 1
        ? AnnounceAsync(TextAboutToBegin, "Castle Siege War is about to begin.")
        : sessions.BroadcastToAll(NoticePacketWriter.Broadcast(
            (gameData.GetServerResource(TextMinutesLeft) ?? "There are %d minutes remaining in the Siege Warfare.")
                .Replace("%d", minutes.ToString())));

    private Task AnnounceAsync(int resourceId, string fallback) =>
        sessions.BroadcastToAll(NoticePacketWriter.Broadcast(gameData.GetServerResource(resourceId) ?? fallback));
}
