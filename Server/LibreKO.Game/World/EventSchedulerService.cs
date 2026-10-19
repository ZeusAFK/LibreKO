using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public readonly record struct TempleEventStatus(
    TempleEvent Contest, byte MinLevel, byte MaxLevel, bool JoinOpen, int SecondsLeft)
{
    public bool Fits(byte level) => TempleEventRules.Fits(level, MinLevel, MaxLevel);
}

public class EventSchedulerService(
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings,
    IZoneTransitionService zoneTransitionService,
    ICollectionRaceService collectionRaceService,
    ILotteryService lotteryService,
    IJuraidMountainService juraidMountainService,
    IBorderDefenseWarService borderDefenseWarService,
    IChaosDungeonService chaosDungeonService,
    IUnderTheCastleService underTheCastleService,
    IForgottenTempleService forgottenTempleService,
    IKrowazService krowazService,
    INereidMonumentService nereidMonuments,
    IGameDataService gameDataService,
    ScheduleClock scheduleClock,
    ILogger<EventSchedulerService> logger) : BackgroundService
{
    private DateTime lastWarOpen = DateTime.MinValue;
    private bool banishPending;
    private DateTime banishTime;
    private DateTime lastScheduleMinute = DateTime.MinValue;

    private readonly object runsLock = new();
    private readonly Dictionary<TempleEvent, TempleEventRun> runs = [];

    private sealed class TempleEventRun(TempleEvent contest, byte minLevel, byte maxLevel, DateTime calledAt, DateTime start)
    {
        public TempleEvent Contest { get; } = contest;
        public byte Zone { get; } = TempleEventRules.ZoneFor(contest);
        public byte MinLevel { get; } = minLevel;
        public byte MaxLevel { get; } = maxLevel;
        public DateTime CalledAt { get; } = calledAt;
        public DateTime Start { get; } = start;
        public DateTime End { get; } = start.AddSeconds(TempleEventRules.DurationSecondsFor(contest));
        public bool JoinOpen { get; set; } = true;
        public HashSet<int> Participants { get; } = [];

        public bool Fits(byte level) => TempleEventRules.Fits(level, MinLevel, MaxLevel);

        public TempleEventStatus Status(DateTime utcNow) => new(Contest, MinLevel, MaxLevel, JoinOpen,
            (int)Math.Max(0, ((JoinOpen ? Start : End) - utcNow).TotalSeconds));
    }

    public IReadOnlyList<TempleEventStatus> TempleEvents => TempleEventsAt(DateTime.UtcNow);

    internal IReadOnlyList<TempleEventStatus> TempleEventsAt(DateTime utcNow)
    {
        lock (runsLock)
            return runs.Values.OrderBy(r => r.CalledAt).Select(r => r.Status(utcNow)).ToList();
    }

    public bool IsTempleEventRunning(TempleEvent contest)
    {
        lock (runsLock)
            return runs.ContainsKey(contest);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var events = settings.Value.Events;
        if (ScheduleTime.TryZone(events.ScheduleTimeZone, out var zone))
            logger.LogInformation("Event scheduler service started on {Zone} time", zone.Id);
        else
            logger.LogWarning("Event schedule time zone {Zone} is unknown; schedules run on UTC", events.ScheduleTimeZone);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickBattleZone();
                await TickTempleEventsAsync(DateTime.UtcNow, scheduleClock.UtcNow);
                await TickBanish();
                await collectionRaceService.TickAsync();
                await lotteryService.TickAsync();
                await juraidMountainService.TickAsync();
                await borderDefenseWarService.TickAsync();
                await chaosDungeonService.TickAsync();
                await underTheCastleService.TickAsync();
                await forgottenTempleService.TickAsync();
                await krowazService.TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in event scheduler tick");
            }
        }
    }

    private async Task TickBattleZone()
    {
        var battle = sessionManager.Battle;

        if (battle.IsBattleActive)
        {
            // Check if battle duration has expired
            var elapsed = battle.GetElapsedTime();
            var maxDuration = TimeSpan.FromMinutes(settings.Value.Events.BattleDurationMinutes);

            if (elapsed >= maxDuration)
            {
                // Determine winner and close
                byte winner = battle.DetermineWinner();
                battle.Victory = winner;

                logger.LogInformation("Battle zone {Zone} ended. Winner: {Winner} (K:{KDead} E:{EDead})",
                    battle.BattleZone, winner == 1 ? "Karus" : winner == 2 ? "Elmorad" : "Draw",
                    battle.KarusDead, battle.ElmoradDead);

                // Broadcast result to all online players
                await BroadcastBattleResult(winner);
                await nereidMonuments.AnnounceResultAsync(winner);

                var closedZone = battle.BattleZone;
                battle.CloseBattleZone();
                await sessionManager.BroadcastToAll(BattleEventPacketWriter.ZoneClosed(closedZone));
                banishPending = true;
                banishTime = DateTime.UtcNow.AddSeconds(60);
            }
        }
        else if (!banishPending)
        {
            // Check if it's time to open a new battle
            var interval = TimeSpan.FromMinutes(settings.Value.Events.BattleIntervalMinutes);
            if (DateTime.UtcNow - lastWarOpen >= interval && sessionManager.GetAll().Count() >= settings.Value.Events.MinPlayersForWar)
            {
                await OpenNextBattle();
            }
        }
    }

    private async Task OpenNextBattle()
    {
        // Rotate through battle zones
        byte[] zones = [BattleZoneManager.ZONE_BATTLE1, BattleZoneManager.ZONE_BATTLE2,
                        BattleZoneManager.ZONE_BATTLE3, BattleZoneManager.ZONE_BATTLE4,
                        BattleZoneManager.ZONE_BATTLE5, BattleZoneManager.ZONE_BATTLE6];
        byte zone = zones[Random.Shared.Next(zones.Length)];

        if (!sessionManager.Battle.OpenBattleZone(BattleZoneManager.BATTLEZONE_OPEN, zone))
            return;

        lastWarOpen = DateTime.UtcNow;
        logger.LogInformation("Opened battle zone {Zone}", zone);

        // Broadcast war open to all players
        await sessionManager.BroadcastToAll(BattleEventPacketWriter.ZoneOpened(BattleZoneManager.BATTLEZONE_OPEN, zone));
    }

    private async Task BroadcastBattleResult(byte winner)
    {
        // Winner announcement
        if (winner > 0)
            foreach (var packet in BattleEventPacketWriter.Winner(winner))
                await sessionManager.BroadcastToAll(packet);

        // Award loyalty to participants of winning side
        int loyaltyReward = settings.Value.Events.BattleWinLoyalty;
        if (winner > 0 && loyaltyReward > 0)
        {
            foreach (var session in sessionManager.GetAll())
            {
                if (BattleZoneManager.IsBattleZone(session.ZoneId) &&
                    (byte)session.Nation == winner)
                {
                    session.Loyalty += loyaltyReward;
                    session.MonthlyLoyalty += loyaltyReward;

                    var loyaltyPkt = LoyaltyChangePacketWriter.Totals(
                        session.Loyalty, session.MonthlyLoyalty);
                    await session.Client.SendPacket(loyaltyPkt);
                }
            }
        }
    }

    private async Task TickBanish()
    {
        if (!banishPending || DateTime.UtcNow < banishTime) return;

        banishPending = false;
        logger.LogInformation("Banishing players from battle zones");

        // Warp all players in battle zones back to their nation's start position
        foreach (var session in sessionManager.GetAll())
        {
            if (!BattleZoneManager.IsBattleZone(session.ZoneId)) continue;

            // Zone change back to nation zone
            byte homeZone = session.Nation == AccountNation.Karus ? (byte)1 : (byte)2;
            await session.Client.SendPacket(ZoneChangePacketWriter.Loading(homeZone, 0, 0, 0));
        }
    }

    internal async Task TickTempleEventsAsync(DateTime utcNow, DateTime? scheduleUtcNow = null)
    {
        var scheduleNow = ScheduleTime.Now(settings.Value.Events, scheduleUtcNow ?? utcNow);
        var minute = scheduleNow.Date.AddHours(scheduleNow.Hour).AddMinutes(scheduleNow.Minute);
        if (minute != lastScheduleMinute)
        {
            lastScheduleMinute = minute;
            foreach (var schedule in DueSchedules(scheduleNow))
            {
                var contest = schedule.Event;
                byte minLevel = schedule.MinLevel > 0 ? schedule.MinLevel : TempleEventRules.DefaultMinLevel(contest);
                byte maxLevel = schedule.MaxLevel > 0 ? schedule.MaxLevel : TempleEventRules.DefaultMaxLevel(contest);
                int countdown = schedule.CountdownMinutes > 0 ? schedule.CountdownMinutes : TempleEventRules.DefaultCountdownMinutes;
                await StartTempleEventAsync(contest, utcNow, countdown * TempleEventRules.SecondsPerMinute, null, minLevel, maxLevel);
            }
        }

        foreach (var run in RunsSnapshot())
        {
            if (run.JoinOpen && utcNow >= run.Start)
            {
                int entrants;
                lock (runsLock)
                {
                    run.JoinOpen = false;
                    entrants = run.Participants.Count;
                }
                logger.LogInformation("{Contest} closed for entries with {Count} player(s) and runs for {Duration}",
                    run.Contest, entrants, run.End - run.Start);
                await WarpParticipantsToEventAsync(run);
            }

            if (utcNow >= run.End || EndedEarly(run))
            {
                logger.LogInformation("{Contest} in zone {Zone} ended", run.Contest, run.Zone);
                await WarpParticipantsOutAsync(run);
                Remove(run);
            }
        }
    }

    private IEnumerable<TempleEventScheduleData> DueSchedules(DateTime scheduleNow) =>
        (gameDataService.TempleEventSchedules ?? [])
            .Where(s => s.Matches(scheduleNow))
            .GroupBy(s => s.Event)
            .Select(g => g.First());

    private bool EndedEarly(TempleEventRun run) =>
        !run.JoinOpen && run.Contest switch
        {
            TempleEvent.UnderTheCastle => !underTheCastleService.IsActive,
            TempleEvent.ForgottenTemple => !forgottenTempleService.IsActive,
            _ => false,
        };

    private List<TempleEventRun> RunsSnapshot()
    {
        lock (runsLock)
            return runs.Values.OrderBy(r => r.CalledAt).ToList();
    }

    private void Remove(TempleEventRun run)
    {
        lock (runsLock)
        {
            if (runs.TryGetValue(run.Contest, out var current) && ReferenceEquals(current, run))
                runs.Remove(run.Contest);
        }
    }

    private async Task StartTempleEventAsync(
        TempleEvent contest,
        DateTime now,
        int joinWindowSeconds,
        UserSession? autoJoinSession,
        byte minLevel,
        byte maxLevel)
    {
        var run = new TempleEventRun(contest, minLevel, maxLevel, now, now.AddSeconds(joinWindowSeconds));
        lock (runsLock)
        {
            if (runs.ContainsKey(contest))
            {
                logger.LogInformation("{Contest} is already running; this call is skipped", contest);
                return;
            }
            runs[contest] = run;
            if (autoJoinSession != null && run.Fits(autoJoinSession.Level) && !InAnotherRun(run, autoJoinSession.CharacterId))
                run.Participants.Add(autoJoinSession.CharacterId);
        }

        logger.LogInformation(
            "{Contest} (Level {Min}-{Max}) called in zone {Zone}; entries are open for {Window}",
            contest, minLevel, maxLevel, run.Zone, TimeSpan.FromSeconds(joinWindowSeconds));

        string timeStr = joinWindowSeconds >= TempleEventRules.SecondsPerMinute
            ? (joinWindowSeconds / TempleEventRules.SecondsPerMinute == 1 ? "1 Minute" : $"{joinWindowSeconds / TempleEventRules.SecondsPerMinute} Minutes")
            : $"{joinWindowSeconds} Seconds";

        string levelNotice = minLevel > 0
            ? (maxLevel > 0 && maxLevel < ProgressionTable.MaxLevel
                ? $" (Level {minLevel}-{maxLevel})"
                : $" (Level {minLevel}+)")
            : string.Empty;

        var noticePkt = NoticePacketWriter.Broadcast($"### [EVENT] {TempleEventRules.NameFor(contest)}{levelNotice} registration is now OPEN ({timeStr})! ###");
        await sessionManager.BroadcastToAll(noticePkt);

        var signUpPkt = BifrostPacketWriter.Remaining(TempleSubOpcode.BifrostRemaining, joinWindowSeconds, (byte)contest);
        foreach (var s in sessionManager.GetAll())
        {
            if (run.Fits(s.Level))
                await s.Client.SendPacket(signUpPkt);
        }
    }

    private bool InAnotherRun(TempleEventRun run, int characterId) =>
        runs.Values.Any(r => !ReferenceEquals(r, run) && r.Participants.Contains(characterId));

    private static Packet SignUpClosed(TempleEvent contest) =>
        BifrostPacketWriter.Remaining(TempleSubOpcode.BifrostRemaining, 0, (byte)contest);

    private async Task WarpParticipantsToEventAsync(TempleEventRun run)
    {
        List<int> participants;
        lock (runsLock)
            participants = run.Participants.ToList();

        logger.LogInformation("Warping {Count} participants to zone {Zone} for {Contest}",
            participants.Count, run.Zone, run.Contest);

        await sessionManager.BroadcastToAll(SignUpClosed(run.Contest));

        var startPkt = NoticePacketWriter.Broadcast($"### [EVENT] {TempleEventRules.NameFor(run.Contest)} has started! Teleporting registered players... ###");
        await sessionManager.BroadcastToAll(startPkt);

        switch (run.Contest)
        {
            case TempleEvent.JuraidMountain:
                await juraidMountainService.StartMatchesAsync(participants, TempleEventRules.JuraidMountainDurationSeconds);
                return;
            case TempleEvent.BorderDefenseWar:
                await borderDefenseWarService.StartMatchesAsync(participants, TempleEventRules.BorderDefenseWarDurationSeconds);
                return;
            case TempleEvent.Chaos:
                await chaosDungeonService.StartMatchesAsync(participants, TempleEventRules.ChaosDurationSeconds);
                return;
            case TempleEvent.UnderTheCastle:
                underTheCastleService.Start();
                foreach (var session in Sessions(participants))
                    await underTheCastleService.EnterAsync(session);
                return;
            case TempleEvent.ForgottenTemple:
                forgottenTempleService.Start(minLevel: run.MinLevel, maxLevel: run.MaxLevel);
                foreach (var session in Sessions(participants))
                    await forgottenTempleService.EnterAsync(session);
                return;
        }

        foreach (var session in Sessions(participants))
        {
            if (session.ZoneId == run.Zone)
                continue;
            try
            {
                await zoneTransitionService.ChangeZoneAsync(session, run.Zone, 0f, 0f);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp player {CharId} to temple event zone {Zone}", session.CharacterId, run.Zone);
            }
        }
    }

    private IEnumerable<UserSession> Sessions(IEnumerable<int> characterIds) =>
        characterIds.Select(sessionManager.GetByCharacterId).OfType<UserSession>();

    private async Task CloseContestAsync(TempleEvent contest, bool rewarded)
    {
        switch (contest)
        {
            case TempleEvent.JuraidMountain:
                await juraidMountainService.CancelAllMatchesAsync();
                break;
            case TempleEvent.BorderDefenseWar:
                if (rewarded)
                    await borderDefenseWarService.FinishAllMatchesAsync();
                else
                    await borderDefenseWarService.CancelAllMatchesAsync();
                break;
            case TempleEvent.Chaos:
                if (rewarded)
                    await chaosDungeonService.FinishAllMatchesAsync();
                else
                    await chaosDungeonService.CancelAllMatchesAsync();
                break;
            case TempleEvent.UnderTheCastle:
                await underTheCastleService.CloseAsync();
                break;
            case TempleEvent.ForgottenTemple:
                await forgottenTempleService.CloseAsync();
                break;
        }
    }

    private async Task WarpParticipantsOutAsync(TempleEventRun run)
    {
        var endPkt = NoticePacketWriter.Broadcast($"### [EVENT] {TempleEventRules.NameFor(run.Contest)} has ended! Returning participants to Moradon... ###");
        await sessionManager.BroadcastToAll(endPkt);

        await CloseContestAsync(run.Contest, rewarded: true);
        await SendZoneHomeAsync(run.Zone);
    }

    private async Task SendZoneHomeAsync(byte zoneId)
    {
        var playersInEvent = sessionManager.GetAll().Where(s => zoneId != 0 && s.ZoneId == zoneId).ToList();

        logger.LogInformation("Warping {Count} players out of event zone {Zone} back to Moradon", playersInEvent.Count, zoneId);

        foreach (var session in playersInEvent)
        {
            try
            {
                await zoneTransitionService.ChangeZoneAsync(session, (byte)ZoneId.Moradon, 0f, 0f);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp player {Name} out of event zone {Zone}", session.Name, session.ZoneId);
            }
        }
    }

    public TempleEvent SignUpFor(UserSession session)
    {
        lock (runsLock)
            return PickSignUp(session)?.Contest ?? TempleEvent.None;
    }

    private TempleEventRun? PickSignUp(UserSession session)
    {
        var open = runs.Values.Where(r => r.JoinOpen).OrderByDescending(r => r.CalledAt).ToList();
        return open.FirstOrDefault(r => r.Participants.Contains(session.CharacterId))
            ?? open.FirstOrDefault(r => r.Fits(session.Level))
            ?? open.FirstOrDefault();
    }

    public TempleJoinResult TryJoinTempleEvent(UserSession session, TempleEvent contest = TempleEvent.None)
    {
        lock (runsLock)
        {
            var run = contest == TempleEvent.None ? PickSignUp(session) : runs.GetValueOrDefault(contest);
            if (run is not { JoinOpen: true })
                return TempleJoinResult.NotOpen;
            if (run.Participants.Contains(session.CharacterId))
                return TempleJoinResult.AlreadyJoined;
            if (InAnotherRun(run, session.CharacterId))
                return TempleJoinResult.OtherDungeon;
            if (run.MinLevel > 0 && session.Level < run.MinLevel)
                return TempleJoinResult.LevelTooLow;
            if (run.MaxLevel > 0 && session.Level > run.MaxLevel)
                return TempleJoinResult.LevelTooHigh;
            if (run.Contest == TempleEvent.Chaos && settings.Value.Events.ChaosNeedsMap
                && !JobChangeRules.HasItemInBag(session, TempleEventRules.MapOfChaos))
                return TempleJoinResult.NeedsMapOfChaos;

            run.Participants.Add(session.CharacterId);
            return TempleJoinResult.Joined;
        }
    }

    public (ushort Karus, ushort ElMorad) TempleSignUps(TempleEvent contest)
    {
        List<int> participants;
        lock (runsLock)
            participants = runs.TryGetValue(contest, out var run) ? run.Participants.ToList() : [];

        ushort karus = 0, elMorad = 0;
        foreach (var player in Sessions(participants))
        {
            if (player.Nation == AccountNation.Karus) karus++;
            else elMorad++;
        }
        return (karus, elMorad);
    }

    public TempleEvent LeaveTempleEvent(int characterId)
    {
        var leftSignUp = TempleEvent.None;
        lock (runsLock)
        {
            foreach (var run in runs.Values)
            {
                if (run.Participants.Remove(characterId) && run.JoinOpen)
                    leftSignUp = run.Contest;
            }
        }
        return leftSignUp;
    }

    public async Task CallTempleEventAsync(
        TempleEvent contest,
        int joinWindowSeconds = 0,
        UserSession? autoJoinSession = null,
        byte minLevel = 0,
        byte maxLevel = 0)
    {
        if (contest == TempleEvent.None)
            return;

        var schedules = gameDataService.TempleEventSchedules?.Where(s => s.Event == contest).ToList() ?? [];
        if (minLevel == 0)
            minLevel = schedules.Count > 0 ? schedules.Min(s => s.MinLevel) : TempleEventRules.DefaultMinLevel(contest);
        if (maxLevel == 0)
            maxLevel = schedules.Count > 0 ? schedules.Max(s => s.MaxLevel) : TempleEventRules.DefaultMaxLevel(contest);
        if (joinWindowSeconds <= 0)
        {
            var countdown = schedules.FirstOrDefault()?.CountdownMinutes ?? TempleEventRules.DefaultCountdownMinutes;
            joinWindowSeconds = countdown > 0 ? countdown * TempleEventRules.SecondsPerMinute : TempleEventRules.JoinWindowSeconds;
        }

        await StartTempleEventAsync(contest, DateTime.UtcNow, joinWindowSeconds, autoJoinSession, minLevel, maxLevel);
    }

    public async Task<bool> CancelTempleEventAsync(TempleEvent contest = TempleEvent.None)
    {
        List<TempleEventRun> cancelled;
        lock (runsLock)
        {
            cancelled = runs.Values.Where(r => contest == TempleEvent.None || r.Contest == contest).ToList();
            foreach (var run in cancelled)
                runs.Remove(run.Contest);
        }

        foreach (var run in cancelled)
        {
            var contestName = TempleEventRules.NameFor(run.Contest);
            if (run.JoinOpen)
            {
                await sessionManager.BroadcastToAll(SignUpClosed(run.Contest));
                await sessionManager.BroadcastToAll(NoticePacketWriter.Broadcast($"### [EVENT] {contestName} registration has been CANCELLED! ###"));
            }
            else
            {
                await sessionManager.BroadcastToAll(NoticePacketWriter.Broadcast($"### [EVENT] {contestName} has been CANCELLED! Returning players to Moradon... ###"));
            }

            await CloseContestAsync(run.Contest, rewarded: false);
            await SendZoneHomeAsync(run.Zone);
        }

        return cancelled.Count > 0;
    }
}
