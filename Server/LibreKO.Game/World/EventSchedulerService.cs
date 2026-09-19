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
    ILogger<EventSchedulerService> logger) : BackgroundService
{
    private DateTime _lastWarOpen = DateTime.MinValue;
    private bool _banishPending;
    private DateTime _banishTime;

    private TempleEvent _templeEvent;
    private byte _templeEventZone;
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
        var now = DateTime.UtcNow;

        if (_templeEventZone == 0)
        {
            var due = DueTempleEvent(now);
            if (due != TempleEvent.None && now - _lastTempleEventCall >= TimeSpan.FromHours(1))
                await StartTempleEventAsync(due, now);
            return;
        }

        if (_templeEventJoinOpen && now >= _templeEventStart)
        {
            _templeEventJoinOpen = false;
            logger.LogInformation(
                "{Contest} closed for entries with {Count} player(s) and runs for {Duration}",
                _templeEvent, _templeParticipants.Count, _templeEventEnd - _templeEventStart);

            await WarpParticipantsToEventAsync();
        }

        if (now >= _templeEventEnd)
        {
            logger.LogInformation("{Contest} in zone {Zone} ended", _templeEvent, _templeEventZone);
            await WarpParticipantsOutAsync(_templeEventZone);
            _templeEvent = TempleEvent.None;
            _templeEventZone = 0;
            _templeEventJoinOpen = false;
            _templeParticipants.Clear();
        }
    }

    private TempleEvent DueTempleEvent(DateTime now)
    {
        if (now.Minute != TempleEventRules.StartMinuteOfHour)
            return TempleEvent.None;

        var events = settings.Value.Events;
        if (events.ChaosStartHours.Contains(now.Hour))
            return TempleEvent.Chaos;
        if (events.BorderDefenseWarStartHours.Contains(now.Hour))
            return TempleEvent.BorderDefenseWar;
        if (events.JuraidMountainStartHours.Contains(now.Hour))
            return TempleEvent.JuraidMountain;

        return TempleEvent.None;
    }

    private async Task StartTempleEventAsync(TempleEvent contest, DateTime now, int joinWindowSeconds = TempleEventRules.JoinWindowSeconds, UserSession? autoJoinSession = null)
    {
        _templeEvent = contest;
        _templeEventZone = TempleEventRules.ZoneFor(contest);
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
            "{Contest} called in zone {Zone}; entries are open for {Window}",
            contest, _templeEventZone, TimeSpan.FromSeconds(joinWindowSeconds));

        string contestName = TempleEventRules.NameFor(contest);

        string timeStr = joinWindowSeconds >= 60
            ? (joinWindowSeconds / 60 == 1 ? "1 Minute" : $"{joinWindowSeconds / 60} Minutes")
            : $"{joinWindowSeconds} Seconds";

        var noticePkt = NoticePacketWriter.Broadcast($"### [EVENT] {contestName} registration is now OPEN ({timeStr})! ###");
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
        var playersInZone = sessionManager.GetAll().Where(s => s.ZoneId == zoneId).ToList();
        logger.LogInformation("Warping {Count} players out of event zone {Zone} back to Moradon",
            playersInZone.Count, zoneId);

        var endPkt = NoticePacketWriter.Broadcast($"### [EVENT] {TempleEventRules.NameFor(_templeEvent)} has ended! Returning participants to Moradon... ###");
        await sessionManager.BroadcastToAll(endPkt);

        foreach (var session in playersInZone)
        {
            try
            {
                await zoneTransitionService.ChangeZoneAsync(session, (byte)ZoneId.Moradon, 0f, 0f);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp player {Name} out of event zone {Zone}", session.Name, zoneId);
            }
        }
    }

    public bool TryJoinTempleEvent(UserSession session)
    {
        if (_templeEventZone == 0 || !_templeEventJoinOpen) return false;
        if (_templeParticipants.Contains(session.CharacterId)) return false;

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

    public async Task CallTempleEventAsync(TempleEvent contest, int joinWindowSeconds = TempleEventRules.JoinWindowSeconds, UserSession? autoJoinSession = null)
    {
        if (contest == TempleEvent.None)
            return;

        await StartTempleEventAsync(contest, DateTime.UtcNow, joinWindowSeconds, autoJoinSession);
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

        byte[] eventZones = [(byte)ZoneId.JuradMountain, (byte)ZoneId.BorderDefenseWar, (byte)ZoneId.ChaosDungeon];
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
