using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface ICollectionRaceService
{
    CollectionRaceSettingsData? ActiveEvent { get; }
    DateTime EndTime { get; }
    int RemainingSeconds { get; }

    Task StartEventAsync(int eventIndex, UserSession? gm = null);
    Task EndEventAsync(bool forced = false, UserSession? gm = null);
    Task HandleNpcKillAsync(NpcInstance npc, UserSession killer);
    Task HandlePlayerKillAsync(UserSession victim, UserSession killer);
    Task SyncPlayerAsync(UserSession player);
    Task TickAsync();
}

public class CollectionRaceService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IPlayerProgressionService playerProgressionService,
    ILogger<CollectionRaceService> logger) : ICollectionRaceService
{
    public class PlayerProgress
    {
        public int Target1Current { get; set; }
        public int Target2Current { get; set; }
        public int Target3Current { get; set; }
        public int EnemyCurrent { get; set; }
        public bool IsCompleted { get; set; }
    }

    private readonly ConcurrentDictionary<int, PlayerProgress> _progress = new();
    private int _lastAutoCheckMinute = -1;

    public CollectionRaceSettingsData? ActiveEvent { get; private set; }
    public DateTime EndTime { get; private set; } = DateTime.MinValue;
    public int RemainingSeconds => ActiveEvent != null ? (int)Math.Max(0, (EndTime - DateTime.UtcNow).TotalSeconds) : 0;

    public async Task StartEventAsync(int eventIndex, UserSession? gm = null)
    {
        if (!gameDataService.CollectionRaceSettingsTable.TryGetValue(eventIndex, out var settings))
        {
            if (gm != null)
                await SendNoticeAsync(gm, $"Collection Race event index {eventIndex} not found in database.");
            return;
        }

        ActiveEvent = settings;
        var duration = settings.DurationMinutes > 0 ? settings.DurationMinutes : 60;
        EndTime = DateTime.UtcNow.AddMinutes(duration);
        _progress.Clear();

        logger.LogInformation("Collection Race '{Name}' (ID {Id}) started in zone {Zone} for {Duration} minutes.",
            settings.EventName, settings.EventIndex, settings.ZoneId, duration);

        var noticeMsg = $"[Collection Race] '{settings.EventName}' has begun in zone {settings.ZoneId}! Level {settings.MinLevel}-{settings.MaxLevel}. Duration: {duration} mins.";
        await BroadcastNoticeAsync(noticeMsg);

        foreach (var player in sessionManager.GetAll())
        {
            if (player.ZoneId == settings.ZoneId && player.Level >= settings.MinLevel && player.Level <= settings.MaxLevel)
            {
                await SyncPlayerAsync(player);
            }
        }
    }

    public async Task EndEventAsync(bool forced = false, UserSession? gm = null)
    {
        if (ActiveEvent == null)
        {
            if (gm != null)
                await SendNoticeAsync(gm, "No Collection Race is currently active.");
            return;
        }

        var name = ActiveEvent.EventName;
        ActiveEvent = null;
        EndTime = DateTime.MinValue;
        _progress.Clear();

        logger.LogInformation("Collection Race '{Name}' ended. Forced: {Forced}", name, forced);

        var noticeMsg = $"[Collection Race] '{name}' has ended.";
        await BroadcastNoticeAsync(noticeMsg);

        var closePkt = CollectionRacePacketWriter.Close();
        await sessionManager.BroadcastToAll(closePkt);
    }

    public async Task TickAsync()
    {
        var utcNow = DateTime.UtcNow;
        var localNow = DateTime.Now;

        if (ActiveEvent != null)
        {
            if (utcNow >= EndTime)
            {
                await EndEventAsync();
            }
            return;
        }

        // Automatic scheduling check once per minute (aligned to local server time)
        if (localNow.Minute != _lastAutoCheckMinute)
        {
            _lastAutoCheckMinute = localNow.Minute;

            foreach (var settings in gameDataService.CollectionRaceSettingsTable.Values)
            {
                if (!settings.AutoStart)
                    continue;

                if (!IsScheduledNow(settings, localNow))
                    continue;

                await StartEventAsync(settings.EventIndex);
                break;
            }
        }
    }

    private static bool IsScheduledNow(CollectionRaceSettingsData settings, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(settings.AutoHours))
            return false;

        var days = settings.AutoDays.Trim();
        if (!string.Equals(days, "All", StringComparison.OrdinalIgnoreCase))
        {
            var dayNum = ((int)now.DayOfWeek).ToString();
            var dayList = days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!dayList.Contains(dayNum))
                return false;
        }

        var hours = settings.AutoHours.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var hStr in hours)
        {
            if (hStr.Contains(':'))
            {
                var parts = hStr.Split(':');
                if (int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
                {
                    if (h == now.Hour && m == now.Minute)
                        return true;
                }
            }
            else if (int.TryParse(hStr, out var h))
            {
                if (h == now.Hour && now.Minute == 0)
                    return true;
            }
        }

        return false;
    }

    private bool IsTargetMatch(int targetProtoId, NpcInstance npc)
    {
        if (targetProtoId <= 0)
            return false;

        if (npc.NpcId == targetProtoId)
            return true;

        if (gameDataService.NpcTable.TryGetValue(targetProtoId, out var targetNpc))
        {
            if (string.Equals(targetNpc.Name, npc.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public async Task HandleNpcKillAsync(NpcInstance npc, UserSession killer)
    {
        if (ActiveEvent == null || killer.ZoneId != ActiveEvent.ZoneId)
            return;

        if (killer.Level < ActiveEvent.MinLevel || killer.Level > ActiveEvent.MaxLevel)
            return;

        var t1 = ActiveEvent.Target1ProtoId;
        var t2 = ActiveEvent.Target2ProtoId;
        var t3 = ActiveEvent.Target3ProtoId;

        if (!IsTargetMatch(t1, npc) && !IsTargetMatch(t2, npc) && !IsTargetMatch(t3, npc))
            return;

        // Collect party members or killer
        var recipients = new List<UserSession> { killer };
        if (killer.IsInParty)
        {
            var party = sessionManager.Parties.GetParty(killer.PartyIndex);
            if (party != null)
            {
                foreach (var memberId in party.MemberIds)
                {
                    if (memberId <= 0 || memberId == killer.CharacterId) continue;
                    var member = sessionManager.GetByCharacterId(memberId);
                    if (member != null && member.ZoneId == ActiveEvent.ZoneId &&
                        member.Level >= ActiveEvent.MinLevel && member.Level <= ActiveEvent.MaxLevel)
                    {
                        recipients.Add(member);
                    }
                }
            }
        }

        foreach (var player in recipients)
        {
            var progress = _progress.GetOrAdd(player.CharacterId, _ => new PlayerProgress());
            if (progress.IsCompleted)
                continue;

            var changed = false;
            if (t1 > 0 && IsTargetMatch(t1, npc) && progress.Target1Current < ActiveEvent.Target1Count)
            {
                progress.Target1Current++;
                changed = true;
            }
            else if (t2 > 0 && IsTargetMatch(t2, npc) && progress.Target2Current < ActiveEvent.Target2Count)
            {
                progress.Target2Current++;
                changed = true;
            }
            else if (t3 > 0 && IsTargetMatch(t3, npc) && progress.Target3Current < ActiveEvent.Target3Count)
            {
                progress.Target3Current++;
                changed = true;
            }

            if (!changed)
                continue;

            await player.Client.SendPacket(CollectionRacePacketWriter.Progress(
                progress.Target1Current, progress.Target2Current, progress.Target3Current, progress.EnemyCurrent));

            await CheckCompletionAsync(player, progress);
        }
    }

    public async Task HandlePlayerKillAsync(UserSession victim, UserSession killer)
    {
        if (ActiveEvent == null || killer.ZoneId != ActiveEvent.ZoneId)
            return;

        if (killer.Nation == victim.Nation)
            return;

        if (killer.Level < ActiveEvent.MinLevel || killer.Level > ActiveEvent.MaxLevel)
            return;

        if (ActiveEvent.EnemyKillCount <= 0)
            return;

        var progress = _progress.GetOrAdd(killer.CharacterId, _ => new PlayerProgress());
        if (progress.IsCompleted || progress.EnemyCurrent >= ActiveEvent.EnemyKillCount)
            return;

        progress.EnemyCurrent++;
        await killer.Client.SendPacket(CollectionRacePacketWriter.Progress(
            progress.Target1Current, progress.Target2Current, progress.Target3Current, progress.EnemyCurrent));

        await CheckCompletionAsync(killer, progress);
    }

    private async Task CheckCompletionAsync(UserSession player, PlayerProgress progress)
    {
        if (ActiveEvent == null || progress.IsCompleted)
            return;

        var ok1 = ActiveEvent.Target1ProtoId <= 0 || progress.Target1Current >= ActiveEvent.Target1Count;
        var ok2 = ActiveEvent.Target2ProtoId <= 0 || progress.Target2Current >= ActiveEvent.Target2Count;
        var ok3 = ActiveEvent.Target3ProtoId <= 0 || progress.Target3Current >= ActiveEvent.Target3Count;
        var okEnemy = ActiveEvent.EnemyKillCount <= 0 || progress.EnemyCurrent >= ActiveEvent.EnemyKillCount;

        if (ok1 && ok2 && ok3 && okEnemy)
        {
            progress.IsCompleted = true;
            logger.LogInformation("Player {Name} completed Collection Race '{Event}'.", player.Name, ActiveEvent.EventName);

            await AwardRewardsAsync(player);
            await player.Client.SendPacket(CollectionRacePacketWriter.Completed("Collection Race Complete!"));
            await SendNoticeAsync(player, "[Collection Race] Congratulations! You have completed the Collection Race!");

            // Zone announcement
            var announce = $"[Collection Race] Player {player.Name} has completed the Collection Race!";
            foreach (var s in sessionManager.GetAll())
            {
                if (s.ZoneId == ActiveEvent.ZoneId)
                    await SendNoticeAsync(s, announce);
            }
        }
    }

    private async Task AwardRewardsAsync(UserSession session)
    {
        if (ActiveEvent == null)
            return;

        var rewards = gameDataService.CollectionRaceRewardsByEventIndex[ActiveEvent.EventIndex];
        foreach (var reward in rewards)
        {
            if (reward.Rate < 100 && Random.Shared.Next(100) >= reward.Rate)
                continue;

            if (reward.ItemId == InventoryConstants.ItemGold)
            {
                session.Money += reward.ItemCount;
                await userNotificationService.SendGoldGainAsync(session, reward.ItemCount);
            }
            else if (reward.ItemId == InventoryConstants.ItemExperience)
            {
                await playerProgressionService.AwardExperienceAsync(session, reward.ItemCount);
            }
            else if (reward.ItemId == InventoryConstants.ItemLadderPoint)
            {
                session.Loyalty += reward.ItemCount;
                session.MonthlyLoyalty += reward.ItemCount;
                await session.Client.SendPacket(LoyaltyChangePacketWriter.Totals(session.Loyalty, session.MonthlyLoyalty));
            }
            else
            {
                var slot = session.FindSlotForItem(reward.ItemId, gameDataService, (ushort)reward.ItemCount);
                if (slot >= 0)
                {
                    var isNew = session.Inventory[slot].IsEmpty;
                    session.Inventory[slot].ItemId = reward.ItemId;
                    session.Inventory[slot].Count += (ushort)reward.ItemCount;

                    var itemData = gameDataService.GetItem(reward.ItemId);
                    if (isNew && itemData != null)
                        session.Inventory[slot].Durability = itemData.Duration;

                    await userNotificationService.SendStackChangeAsync(
                        session, (byte)slot, reward.ItemId, session.Inventory[slot].Count, session.Inventory[slot].Durability, isNew);
                    session.RecalculateStatsWithBuffs(gameDataService);
                    await userNotificationService.SendWeightChangeAsync(session);
                }
                else
                {
                    await SendNoticeAsync(session, "Inventory is full! Some Collection Race rewards could not be delivered.");
                }
            }
        }
    }

    public async Task SyncPlayerAsync(UserSession player)
    {
        if (ActiveEvent == null || player.ZoneId != ActiveEvent.ZoneId ||
            player.Level < ActiveEvent.MinLevel || player.Level > ActiveEvent.MaxLevel)
        {
            await player.Client.SendPacket(CollectionRacePacketWriter.Close());
            return;
        }

        var progress = _progress.GetOrAdd(player.CharacterId, _ => new PlayerProgress());

        var t1Name = GetTargetName(ActiveEvent.Target1ProtoId);
        var t2Name = GetTargetName(ActiveEvent.Target2ProtoId);
        var t3Name = GetTargetName(ActiveEvent.Target3ProtoId);

        var t1 = new CollectionRacePacketWriter.TargetInfo(ActiveEvent.Target1ProtoId, ActiveEvent.Target1Count, progress.Target1Current, t1Name);
        var t2 = new CollectionRacePacketWriter.TargetInfo(ActiveEvent.Target2ProtoId, ActiveEvent.Target2Count, progress.Target2Current, t2Name);
        var t3 = new CollectionRacePacketWriter.TargetInfo(ActiveEvent.Target3ProtoId, ActiveEvent.Target3Count, progress.Target3Current, t3Name);

        var rewardList = new List<CollectionRacePacketWriter.RewardInfo>();
        var rewards = gameDataService.CollectionRaceRewardsByEventIndex[ActiveEvent.EventIndex];
        foreach (var r in rewards)
        {
            rewardList.Add(new CollectionRacePacketWriter.RewardInfo(r.ItemId, r.ItemCount, GetRewardName(r.ItemId), r.Rate));
        }

        var pkt = CollectionRacePacketWriter.State(
            ActiveEvent.EventIndex,
            ActiveEvent.EventName,
            ActiveEvent.ZoneId,
            RemainingSeconds,
            t1, t2, t3,
            ActiveEvent.EnemyKillCount,
            progress.EnemyCurrent,
            progress.IsCompleted,
            rewardList);

        await player.Client.SendPacket(pkt);
    }

    private string GetTargetName(int protoId)
    {
        if (protoId <= 0) return string.Empty;
        if (gameDataService.MonsterTable.TryGetValue(protoId, out var monster))
            return monster.Name;
        if (gameDataService.NpcTable.TryGetValue(protoId, out var npc))
            return npc.Name;
        return $"Target {protoId}";
    }

    private string GetRewardName(int itemId)
    {
        return itemId switch
        {
            InventoryConstants.ItemGold => "Noah (Gold)",
            InventoryConstants.ItemExperience => "Experience",
            InventoryConstants.ItemLadderPoint => "National Points",
            _ => gameDataService.GetItem(itemId)?.Name ?? $"Item {itemId}"
        };
    }

    private static async Task SendNoticeAsync(UserSession session, string message)
    {
        var pkt = ChatPacketWriter.Say((byte)ChatType.WarSystem, 0, 0, "[Server]", message, false);
        await session.Client.SendPacket(pkt);
    }

    private async Task BroadcastNoticeAsync(string message)
    {
        var pkt = ChatPacketWriter.Say((byte)ChatType.WarSystem, 0, 0, "[Server]", message, false);
        await sessionManager.BroadcastToAll(pkt);
    }
}
