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

public class EventSchedulerService(
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings,
    IZoneTransitionService zoneTransitionService,
    ICollectionRaceService collectionRaceService,
    ILotteryService lotteryService,
    IJuraidMountainService juraidMountainService,
    IBorderDefenseWarService borderDefenseWarService,
    IUnderTheCastleService underTheCastleService,
    IGameDataService gameDataService,
    ILogger<EventSchedulerService> logger) : BackgroundService
{
    private DateTime _lastWarOpen = DateTime.MinValue;
    private bool _banishPending;
    private DateTime _banishTime;
    private int _lastAutoCheckMinute = -1;

    private TempleEvent _templeEvent;
    private byte _templeEventZone;
    private byte _templeEventMinLevel;
    private byte _templeEventMaxLevel;
    private bool _templeEventJoinOpen;
    private DateTime _templeEventStart;
    private DateTime _templeEventEnd;
    private DateTime _lastTempleEventCall = DateTime.MinValue;
    private readonly HashSet<int> _templeParticipants = [];

    public bool IsTempleEventJoinOpen => _templeEventJoinOpen;
    public TempleEvent CurrentTempleEvent => _templeEvent;
    public int TempleRemainingJoinSeconds => _templeEventJoinOpen ? (int)Math.Max(0, (_templeEventStart - DateTime.UtcNow).TotalSeconds) : 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Event scheduler service started");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickBattleZone();
                await TickTempleEvent();
                await TickBanish();
                await collectionRaceService.TickAsync();
                await lotteryService.TickAsync();
                await juraidMountainService.TickAsync();
                await borderDefenseWarService.TickAsync();
                await underTheCastleService.TickAsync();
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

                battle.CloseBattleZone();
                _banishPending = true;
                _banishTime = DateTime.UtcNow.AddSeconds(60);
            }
        }
        else if (!_banishPending)
        {
            // Check if it's time to open a new battle
            var interval = TimeSpan.FromMinutes(settings.Value.Events.BattleIntervalMinutes);
            if (DateTime.UtcNow - _lastWarOpen >= interval && sessionManager.GetAll().Count() >= settings.Value.Events.MinPlayersForWar)
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

        if (!sessionManager.Battle.OpenBattleZone(BattleZoneManager.NATION_BATTLE, zone))
            return;

        _lastWarOpen = DateTime.UtcNow;
        logger.LogInformation("Opened battle zone {Zone}", zone);

        // Broadcast war open to all players
        var pkt = BattleEventPacketWriter.Opened(
            BattleZoneManager.BATTLEZONE_OPEN, zone,
            (short)settings.Value.Events.BattleDurationMinutes);
        await sessionManager.BroadcastToAll(pkt);
    }

    private async Task BroadcastBattleResult(byte winner)
    {
        // Winner announcement
        var pkt = BattleEventPacketWriter.Notice(
            winner > 0 ? BattleZoneManager.DECLARE_WINNER : BattleZoneManager.BATTLEZONE_CLOSE,
            winner);
        await sessionManager.BroadcastToAll(pkt);

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
        if (!_banishPending || DateTime.UtcNow < _banishTime) return;

        _banishPending = false;
        logger.LogInformation("Banishing players from battle zones");

        // Warp all players in battle zones back to their nation's start position
        foreach (var session in sessionManager.GetAll())
        {
            if (!BattleZoneManager.IsBattleZone(session.ZoneId)) continue;

            // Send banish notification
            var banishPkt = BattleEventPacketWriter.Banished(BattleZoneManager.DECLARE_BAN);
            await session.Client.SendPacket(banishPkt);

            // Zone change back to nation zone
            byte homeZone = session.Nation == AccountNation.Karus ? (byte)1 : (byte)2;
            await session.Client.SendPacket(ZoneChangePacketWriter.Loading(homeZone, 0, 0, 0));
        }
    }

    private async Task TickTempleEvent()
    {
        var utcNow = DateTime.UtcNow;
        var scheduleNow = settings.Value.Events.UseLocalTimeForSchedules ? DateTime.Now : utcNow;

        if (_templeEventZone == 0)
        {
            if (scheduleNow.Minute != _lastAutoCheckMinute)
            {
                var due = DueTempleEvent(scheduleNow, out var scheduledMinLevel, out var scheduledMaxLevel, out var scheduledJoinWindowSeconds);
                _lastAutoCheckMinute = scheduleNow.Minute;
                if (due != TempleEvent.None)
                {
                    await StartTempleEventAsync(due, utcNow, scheduledJoinWindowSeconds, null, scheduledMinLevel, scheduledMaxLevel);
                }
            }

            return;
        }

        if (_templeEventJoinOpen && utcNow >= _templeEventStart)
        {
            _templeEventJoinOpen = false;
            logger.LogInformation(
                "{Contest} closed for entries with {Count} player(s) and runs for {Duration}",
                _templeEvent, _templeParticipants.Count, _templeEventEnd - _templeEventStart);

            await WarpParticipantsToEventAsync();
        }

        if (utcNow >= _templeEventEnd)
        {
            logger.LogInformation("{Contest} in zone {Zone} ended", _templeEvent, _templeEventZone);
            await WarpParticipantsOutAsync(_templeEventZone);
            _templeEvent = TempleEvent.None;
            _templeEventZone = 0;
            _templeEventMinLevel = 0;
            _templeEventMaxLevel = 0;
            _templeEventJoinOpen = false;
            _templeParticipants.Clear();
        }
    }

    private TempleEvent DueTempleEvent(DateTime now, out byte minLevel, out byte maxLevel, out int joinWindowSeconds)
    {
        minLevel = 0;
        maxLevel = 0;
        joinWindowSeconds = TempleEventRules.JoinWindowSeconds;

        // 1. Check database-driven Temple Event schedules (Juraid Mountain, BDW)
        var schedule = gameDataService.TempleEventSchedules?.FirstOrDefault(s => s.Matches(now));
        if (schedule != null)
        {
            var contest = schedule.Event;
            byte defMin = contest switch
            {
                TempleEvent.JuraidMountain => TempleEventRules.JuraidMountainDefaultMinLevel,
                TempleEvent.UnderTheCastle => TempleEventRules.UnderTheCastleDefaultMinLevel,
                _ => TempleEventRules.BorderDefenseWarDefaultMinLevel,
            };
            byte defMax = contest switch
            {
                TempleEvent.JuraidMountain => TempleEventRules.JuraidMountainDefaultMaxLevel,
                TempleEvent.UnderTheCastle => TempleEventRules.UnderTheCastleDefaultMaxLevel,
                _ => TempleEventRules.BorderDefenseWarDefaultMaxLevel,
            };

            minLevel = schedule.MinLevel > 0 ? schedule.MinLevel : defMin;
            maxLevel = schedule.MaxLevel > 0 ? schedule.MaxLevel : defMax;
            int countdownMin = schedule.CountdownMinutes > 0 ? schedule.CountdownMinutes : TempleEventRules.DefaultCountdownMinutes;
            joinWindowSeconds = countdownMin * 60;
            return contest;
        }

        // 2. Configuration-driven start hours (Chaos)
        if (now.Minute == TempleEventRules.StartMinuteOfHour)
        {
            var events = settings.Value.Events;
            if (events.ChaosStartHours.Contains(now.Hour))
                return TempleEvent.Chaos;
        }

        return TempleEvent.None;
    }

    private async Task StartTempleEventAsync(
        TempleEvent contest,
        DateTime now,
        int joinWindowSeconds = TempleEventRules.JoinWindowSeconds,
        UserSession? autoJoinSession = null,
        byte minLevel = 0,
        byte maxLevel = 0)
    {
        _templeEvent = contest;
        _templeEventZone = TempleEventRules.ZoneFor(contest);
        _templeEventMinLevel = minLevel;
        _templeEventMaxLevel = maxLevel;
        _templeEventJoinOpen = true;
        _templeEventStart = now.AddSeconds(joinWindowSeconds);
        _templeEventEnd = _templeEventStart
            .AddSeconds(TempleEventRules.DurationSecondsFor(contest));
        _lastTempleEventCall = now;
        _templeParticipants.Clear();
        if (autoJoinSession != null)
        {
            _templeParticipants.Add(autoJoinSession.CharacterId);
        }

        logger.LogInformation(
            "{Contest} (Level {Min}-{Max}) called in zone {Zone}; entries are open for {Window}",
            contest, _templeEventMinLevel, _templeEventMaxLevel, _templeEventZone, TimeSpan.FromSeconds(joinWindowSeconds));

        string contestName = TempleEventRules.NameFor(contest);

        string timeStr = joinWindowSeconds >= 60
            ? (joinWindowSeconds / 60 == 1 ? "1 Minute" : $"{joinWindowSeconds / 60} Minutes")
            : $"{joinWindowSeconds} Seconds";

        string levelNotice = _templeEventMinLevel > 0
            ? $" (Level {_templeEventMinLevel}+)"
            : string.Empty;

        var noticePkt = NoticePacketWriter.Broadcast($"### [EVENT] {contestName}{levelNotice} registration is now OPEN ({timeStr})! ###");
        await sessionManager.BroadcastToAll(noticePkt);

        var bifrostPkt = BifrostPacketWriter.Remaining(TempleSubOpcode.BifrostRemaining, joinWindowSeconds, (byte)contest);
        await sessionManager.BroadcastToAll(bifrostPkt);
    }

    private async Task WarpParticipantsToEventAsync()
    {
        logger.LogInformation("Warping {Count} participants to zone {Zone} for {Contest}",
            _templeParticipants.Count, _templeEventZone, _templeEvent);

        var closeBifrostPkt = BifrostPacketWriter.Remaining(TempleSubOpcode.BifrostRemaining, 0);
        await sessionManager.BroadcastToAll(closeBifrostPkt);

        var startPkt = NoticePacketWriter.Broadcast($"### [EVENT] {TempleEventRules.NameFor(_templeEvent)} has started! Teleporting registered players... ###");
        await sessionManager.BroadcastToAll(startPkt);

        if (_templeEvent == TempleEvent.JuraidMountain)
        {
            await juraidMountainService.StartMatchesAsync(_templeParticipants.ToList(), TempleEventRules.JuraidMountainDurationSeconds);
            return;
        }

        if (_templeEvent == TempleEvent.BorderDefenseWar)
        {
            await borderDefenseWarService.StartMatchesAsync(_templeParticipants.ToList(), TempleEventRules.BorderDefenseWarDurationSeconds);
            return;
        }

        if (_templeEvent == TempleEvent.UnderTheCastle)
        {
            underTheCastleService.Start();
            foreach (var charId in _templeParticipants)
            {
                var session = sessionManager.GetByCharacterId(charId);
                if (session != null)
                    await underTheCastleService.EnterAsync(session);
            }
            return;
        }

        foreach (var charId in _templeParticipants)
        {
            var session = sessionManager.GetByCharacterId(charId);
            if (session != null && session.ZoneId != _templeEventZone)
            {
                try
                {
                    await zoneTransitionService.ChangeZoneAsync(session, _templeEventZone, 0f, 0f);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to warp player {CharId} to temple event zone {Zone}", charId, _templeEventZone);
                }
            }
        }
    }

    private async Task WarpParticipantsOutAsync(byte zoneId)
    {
        var endPkt = NoticePacketWriter.Broadcast($"### [EVENT] {TempleEventRules.NameFor(_templeEvent)} has ended! Returning participants to Moradon... ###");
        await sessionManager.BroadcastToAll(endPkt);

        if (_templeEvent == TempleEvent.JuraidMountain || juraidMountainService.HasActiveMatches)
        {
            await juraidMountainService.CancelAllMatchesAsync();
        }

        if (_templeEvent == TempleEvent.BorderDefenseWar || borderDefenseWarService.HasActiveMatches)
        {
            await borderDefenseWarService.FinishAllMatchesAsync();
        }

        if (_templeEvent == TempleEvent.UnderTheCastle || underTheCastleService.IsActive)
        {
            underTheCastleService.Close();
        }

        var playersInEvent = sessionManager.GetAll()
            .Where(s => zoneId != 0 && s.ZoneId == zoneId)
            .ToList();

        logger.LogInformation("Warping {Count} players out of event zones back to Moradon", playersInEvent.Count);

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

    public bool TryJoinTempleEvent(UserSession session) => TryJoinTempleEvent(session, out _);

    public bool TryJoinTempleEvent(UserSession session, out string? reason)
    {
        reason = null;
        if (_templeEventZone == 0 || !_templeEventJoinOpen)
        {
            reason = "Registration is not currently open.";
            return false;
        }

        if (_templeParticipants.Contains(session.CharacterId))
        {
            reason = "You have already registered for this event.";
            return false;
        }

        if (_templeEventMinLevel > 0 && session.Level < _templeEventMinLevel)
        {
            reason = $"Your level ({session.Level}) is too low. Minimum level is {_templeEventMinLevel}.";
            return false;
        }

        if (_templeEventMaxLevel > 0 && session.Level > _templeEventMaxLevel)
        {
            reason = $"Your level ({session.Level}) is too high. Maximum level is {_templeEventMaxLevel}.";
            return false;
        }

        _templeParticipants.Add(session.CharacterId);
        return true;
    }

    public void LeaveTempleEvent(int characterId)
    {
        _templeParticipants.Remove(characterId);
    }

    public byte TempleEventZone => _templeEventZone;

    public TempleEvent TempleEventInProgress => _templeEvent;

    public bool TempleEventAcceptingEntries => _templeEventJoinOpen;

    public async Task CallTempleEventAsync(
        TempleEvent contest,
        int joinWindowSeconds = 0,
        UserSession? autoJoinSession = null,
        byte minLevel = 0,
        byte maxLevel = 0)
    {
        if (contest == TempleEvent.None)
            return;

        if (contest is TempleEvent.JuraidMountain or TempleEvent.BorderDefenseWar or TempleEvent.UnderTheCastle)
        {
            var matchingSchedules = gameDataService.TempleEventSchedules?.Where(s => s.Event == contest).ToList();
            byte defMin = contest switch
            {
                TempleEvent.JuraidMountain => TempleEventRules.JuraidMountainDefaultMinLevel,
                TempleEvent.UnderTheCastle => TempleEventRules.UnderTheCastleDefaultMinLevel,
                _ => TempleEventRules.BorderDefenseWarDefaultMinLevel,
            };
            byte defMax = contest switch
            {
                TempleEvent.JuraidMountain => TempleEventRules.JuraidMountainDefaultMaxLevel,
                TempleEvent.UnderTheCastle => TempleEventRules.UnderTheCastleDefaultMaxLevel,
                _ => TempleEventRules.BorderDefenseWarDefaultMaxLevel,
            };

            if (minLevel == 0)
                minLevel = matchingSchedules != null && matchingSchedules.Count > 0
                    ? matchingSchedules.Min(s => s.MinLevel)
                    : defMin;
            if (maxLevel == 0)
                maxLevel = matchingSchedules != null && matchingSchedules.Count > 0
                    ? matchingSchedules.Max(s => s.MaxLevel)
                    : defMax;

            if (joinWindowSeconds <= 0)
            {
                var defaultCountdown = matchingSchedules?.FirstOrDefault()?.CountdownMinutes ?? TempleEventRules.DefaultCountdownMinutes;
                joinWindowSeconds = defaultCountdown > 0 ? defaultCountdown * 60 : TempleEventRules.JoinWindowSeconds;
            }
        }
        else if (joinWindowSeconds <= 0)
        {
            joinWindowSeconds = TempleEventRules.JoinWindowSeconds;
        }

        await StartTempleEventAsync(contest, DateTime.UtcNow, joinWindowSeconds, autoJoinSession, minLevel, maxLevel);
    }

    public async Task<bool> CancelTempleEventAsync()
    {
        if (_templeEvent == TempleEvent.None)
        {
            return false;
        }

        var closeBifrostPkt = BifrostPacketWriter.Remaining(TempleSubOpcode.BifrostRemaining, 0);
        await sessionManager.BroadcastToAll(closeBifrostPkt);

        var contestName = TempleEventRules.NameFor(_templeEvent);

        if (_templeEventJoinOpen)
        {
            var cancelNotice = NoticePacketWriter.Broadcast($"### [EVENT] {contestName} registration has been CANCELLED! ###");
            await sessionManager.BroadcastToAll(cancelNotice);
        }
        else
        {
            var cancelNotice = NoticePacketWriter.Broadcast($"### [EVENT] {contestName} has been CANCELLED! Returning players to Moradon... ###");
            await sessionManager.BroadcastToAll(cancelNotice);
            if (_templeEventZone != 0)
            {
                await WarpParticipantsOutAsync(_templeEventZone);
            }
        }

        if (_templeEvent == TempleEvent.JuraidMountain || juraidMountainService.HasActiveMatches)
        {
            await juraidMountainService.CancelAllMatchesAsync();
        }

        if (_templeEvent == TempleEvent.BorderDefenseWar || borderDefenseWarService.HasActiveMatches)
        {
            await borderDefenseWarService.CancelAllMatchesAsync();
        }

        if (_templeEvent == TempleEvent.UnderTheCastle || underTheCastleService.IsActive)
        {
            underTheCastleService.Close();
        }

        byte[] eventZones = [(byte)ZoneId.JuradMountain, (byte)ZoneId.BorderDefenseWar, (byte)ZoneId.ChaosDungeon, (byte)ZoneId.UnderCastle];
        foreach (var ez in eventZones)
        {
            var playersInZone = sessionManager.GetAll().Where(s => s.ZoneId == ez).ToList();
            foreach (var s in playersInZone)
            {
                try
                {
                    await zoneTransitionService.ChangeZoneAsync(s, (byte)ZoneId.Moradon, 0f, 0f);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to warp player {Name} out of event zone {Zone}", s.Name, ez);
                }
            }
        }

        _templeEvent = TempleEvent.None;
        _templeEventZone = 0;
        _templeEventJoinOpen = false;
        _templeParticipants.Clear();
        return true;
    }
}
