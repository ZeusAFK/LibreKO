using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class EventSchedulerService(
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings,
    ILogger<EventSchedulerService> logger) : BackgroundService
{
    private DateTime _lastWarOpen = DateTime.MinValue;
    private bool _banishPending;
    private DateTime _banishTime;

    // Temple event state
    private TempleEvent _templeEvent;
    private byte _templeEventZone;
    private bool _templeEventJoinOpen;
    private DateTime _templeEventStart;
    private DateTime _templeEventEnd;
    private DateTime _lastTempleEventCall = DateTime.MinValue;
    private readonly HashSet<int> _templeParticipants = []; // CharacterIds

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Event scheduler service started");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
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

    private Task TickTempleEvent()
    {
        var now = DateTime.UtcNow;

        if (_templeEventZone == 0)
        {
            var due = DueTempleEvent(now);
            if (due != TempleEvent.None && now - _lastTempleEventCall >= TimeSpan.FromHours(1))
                StartTempleEvent(due, now);
            return Task.CompletedTask;
        }

        if (_templeEventJoinOpen && now >= _templeEventStart)
        {
            _templeEventJoinOpen = false;
            logger.LogInformation(
                "{Contest} closed for entries with {Count} player(s) and runs for {Duration}",
                _templeEvent, _templeParticipants.Count, _templeEventEnd - _templeEventStart);
        }

        if (now >= _templeEventEnd)
        {
            logger.LogInformation("{Contest} in zone {Zone} ended", _templeEvent, _templeEventZone);
            _templeEvent = TempleEvent.None;
            _templeEventZone = 0;
            _templeEventJoinOpen = false;
            _templeParticipants.Clear();
        }

        return Task.CompletedTask;
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

    private void StartTempleEvent(TempleEvent contest, DateTime now)
    {
        _templeEvent = contest;
        _templeEventZone = TempleEventRules.ZoneFor(contest);
        _templeEventJoinOpen = true;
        _templeEventStart = now.AddSeconds(TempleEventRules.JoinWindowSeconds);
        _templeEventEnd = _templeEventStart
            .AddSeconds(TempleEventRules.DurationSecondsFor(contest));
        _lastTempleEventCall = now;
        _templeParticipants.Clear();

        logger.LogInformation(
            "{Contest} called in zone {Zone}; entries are open for {Window}",
            contest, _templeEventZone, TimeSpan.FromSeconds(TempleEventRules.JoinWindowSeconds));
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

    public void CallTempleEvent(TempleEvent contest)
    {
        if (contest == TempleEvent.None)
            return;

        StartTempleEvent(contest, DateTime.UtcNow);
    }
}
