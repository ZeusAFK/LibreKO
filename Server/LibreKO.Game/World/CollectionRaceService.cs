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
    IReadOnlyCollection<ActiveCollectionRace> ActiveRaces { get; }
    ActiveCollectionRace? GetActive(byte zoneId);

    Task StartRaceAsync(int raceId, UserSession? gm = null);
    Task EndRaceAsync(int raceId, bool forced = false, UserSession? gm = null);
    Task EndAllAsync(UserSession? gm = null);
    Task HandleNpcKillAsync(NpcInstance npc, UserSession killer);
    Task HandlePlayerKillAsync(UserSession victim, UserSession killer);
    Task HandleItemGainAsync(UserSession player, int itemId);
    Task SyncPlayerAsync(UserSession player);
    Task TickAsync();
}

public sealed class ActiveCollectionRace(CollectionRaceData race, IReadOnlyList<CollectionRaceObjectiveData> objectives, DateTime endTime)
{
    public CollectionRaceData Race { get; } = race;
    public IReadOnlyList<CollectionRaceObjectiveData> Objectives { get; } = objectives;
    public DateTime EndTime { get; } = endTime;
    public int RemainingSeconds => (int)Math.Max(0, (EndTime - DateTime.UtcNow).TotalSeconds);
    public ConcurrentDictionary<int, CollectionRaceProgress> Progress { get; } = new();

    public bool IsEligible(UserSession player) =>
        player.ZoneId == Race.ZoneId && player.Level >= Race.MinLevel && player.Level <= Race.MaxLevel;

    public CollectionRaceProgress ProgressOf(UserSession player) =>
        Progress.GetOrAdd(player.CharacterId, _ => new CollectionRaceProgress(Objectives.Count));
}

public sealed class CollectionRaceProgress(int objectiveCount)
{
    public int[] Current { get; } = new int[objectiveCount];
    public bool IsCompleted { get; set; }
}

public class CollectionRaceService : ICollectionRaceService
{
    private const string EnemyPlayersObjectiveName = "Enemy players";

    private readonly SessionManager sessionManager;
    private readonly IGameDataService gameDataService;
    private readonly IUserNotificationService userNotificationService;
    private readonly IMailService mailService;
    private readonly ILogger<CollectionRaceService> logger;

    private readonly ConcurrentDictionary<byte, ActiveCollectionRace> _activeByZone = new();
    private int _lastAutoCheckMinute = -1;

    public CollectionRaceService(
        SessionManager sessionManager,
        IGameDataService gameDataService,
        IUserNotificationService userNotificationService,
        IPlayerProgressionService playerProgressionService,
        IMailService mailService,
        ILogger<CollectionRaceService> logger)
    {
        this.sessionManager = sessionManager;
        this.gameDataService = gameDataService;
        this.userNotificationService = userNotificationService;
        this.mailService = mailService;
        this.logger = logger;
        playerProgressionService.LevelChanged += SyncPlayerAsync;
    }

    public IReadOnlyCollection<ActiveCollectionRace> ActiveRaces => _activeByZone.Values.ToArray();

    public ActiveCollectionRace? GetActive(byte zoneId) => _activeByZone.GetValueOrDefault(zoneId);

    public async Task StartRaceAsync(int raceId, UserSession? gm = null)
    {
        if (!gameDataService.CollectionRaceTable.TryGetValue(raceId, out var race))
        {
            if (gm != null)
                await SendNoticeAsync(gm, $"Collection Race {raceId} not found in database.");
            return;
        }

        var objectives = gameDataService.CollectionRaceObjectivesByRace[raceId].OrderBy(o => o.Ordinal).ToList();
        if (objectives.Count == 0)
        {
            if (gm != null)
                await SendNoticeAsync(gm, $"Collection Race {raceId} has no objectives.");
            return;
        }

        var duration = race.DurationMinutes > 0 ? race.DurationMinutes : CollectionRaceData.DefaultDurationMinutes;
        var now = DateTime.UtcNow;
        var startedAt = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var active = new ActiveCollectionRace(race, objectives, startedAt.AddMinutes(duration));
        if (!_activeByZone.TryAdd(race.ZoneId, active))
        {
            if (gm != null)
                await SendNoticeAsync(gm, $"Zone {race.ZoneId} already has an active Collection Race ('{_activeByZone[race.ZoneId].Race.Name}').");
            return;
        }

        logger.LogInformation("Collection Race '{Name}' (ID {Id}) started in zone {Zone} for {Duration} minutes.",
            race.Name, race.Id, race.ZoneId, duration);

        await BroadcastNoticeAsync($"[Collection Race] '{race.Name}' has begun in zone {race.ZoneId}! Level {race.MinLevel}-{race.MaxLevel}. Duration: {duration} mins.");

        foreach (var player in sessionManager.GetAll())
        {
            if (active.IsEligible(player))
                await SyncPlayerAsync(player);
        }
    }

    public async Task EndRaceAsync(int raceId, bool forced = false, UserSession? gm = null)
    {
        var active = _activeByZone.Values.FirstOrDefault(a => a.Race.Id == raceId);
        if (active == null)
        {
            if (gm != null)
                await SendNoticeAsync(gm, $"Collection Race {raceId} is not active.");
            return;
        }

        await EndAsync(active, forced);
    }

    public async Task EndAllAsync(UserSession? gm = null)
    {
        var races = ActiveRaces;
        if (races.Count == 0)
        {
            if (gm != null)
                await SendNoticeAsync(gm, "No Collection Race is currently active.");
            return;
        }

        foreach (var active in races)
            await EndAsync(active, forced: true);
    }

    private async Task EndAsync(ActiveCollectionRace active, bool forced)
    {
        if (!_activeByZone.TryRemove(new KeyValuePair<byte, ActiveCollectionRace>(active.Race.ZoneId, active)))
            return;

        logger.LogInformation("Collection Race '{Name}' ended. Forced: {Forced}", active.Race.Name, forced);
        await BroadcastNoticeAsync($"[Collection Race] '{active.Race.Name}' has ended.");
        await MailRewardsAsync(active);

        var closePkt = CollectionRacePacketWriter.Close();
        foreach (var player in sessionManager.GetAll())
        {
            if (player.ZoneId == active.Race.ZoneId)
                await player.Client.SendPacket(closePkt);
        }
    }

    public async Task TickAsync()
    {
        var now = DateTime.UtcNow;

        foreach (var active in ActiveRaces)
        {
            if (now >= active.EndTime)
                await EndAsync(active, forced: false);
        }

        if (now.Minute == _lastAutoCheckMinute)
            return;

        _lastAutoCheckMinute = now.Minute;

        foreach (var race in gameDataService.CollectionRaceTable.Values)
        {
            if (!race.AutoStart || _activeByZone.ContainsKey(race.ZoneId))
                continue;

            if (gameDataService.CollectionRaceSchedulesByRace[race.Id].Any(s => s.Matches(now)))
                await StartRaceAsync(race.Id);
        }
    }

    private bool IsTargetMatch(int targetId, NpcInstance npc)
    {
        if (targetId <= 0)
            return false;

        if (npc.NpcId == targetId)
            return true;

        return gameDataService.NpcTable.TryGetValue(targetId, out var targetNpc)
            && string.Equals(targetNpc.Name, npc.Name, StringComparison.OrdinalIgnoreCase);
    }

    public async Task HandleNpcKillAsync(NpcInstance npc, UserSession killer)
    {
        var active = GetActive(killer.ZoneId);
        if (active == null || !active.IsEligible(killer))
            return;

        if (!active.Objectives.Any(o => o.Kind == CollectionRaceObjectiveKind.Monster && IsTargetMatch(o.TargetId, npc)))
            return;

        var recipients = new List<UserSession> { killer };
        if (killer.IsInParty)
        {
            var party = sessionManager.Parties.GetParty(killer.PartyIndex);
            if (party != null)
            {
                foreach (var memberId in party.MemberIds)
                {
                    if (memberId <= 0 || memberId == killer.CharacterId)
                        continue;
                    var member = sessionManager.GetByCharacterId(memberId);
                    if (member != null && active.IsEligible(member))
                        recipients.Add(member);
                }
            }
        }

        foreach (var player in recipients)
        {
            var progress = active.ProgressOf(player);
            if (progress.IsCompleted)
                continue;

            var changed = false;
            for (var i = 0; i < active.Objectives.Count; i++)
            {
                var objective = active.Objectives[i];
                if (objective.Kind != CollectionRaceObjectiveKind.Monster || progress.Current[i] >= objective.Count || !IsTargetMatch(objective.TargetId, npc))
                    continue;

                progress.Current[i]++;
                changed = true;
                break;
            }

            if (!changed)
                continue;

            await SendProgressAsync(player, active, progress);
            await CheckCompletionAsync(player, active, progress);
        }
    }

    public async Task HandlePlayerKillAsync(UserSession victim, UserSession killer)
    {
        var active = GetActive(killer.ZoneId);
        if (active == null || !active.IsEligible(killer) || killer.Nation == victim.Nation)
            return;

        var progress = active.ProgressOf(killer);
        if (progress.IsCompleted)
            return;

        var changed = false;
        for (var i = 0; i < active.Objectives.Count; i++)
        {
            var objective = active.Objectives[i];
            if (objective.Kind != CollectionRaceObjectiveKind.EnemyPlayer || progress.Current[i] >= objective.Count)
                continue;

            progress.Current[i]++;
            changed = true;
            break;
        }

        if (!changed)
            return;

        await SendProgressAsync(killer, active, progress);
        await CheckCompletionAsync(killer, active, progress);
    }

    public async Task HandleItemGainAsync(UserSession player, int itemId)
    {
        var active = GetActive(player.ZoneId);
        if (active == null || !active.IsEligible(player))
            return;

        if (!active.Objectives.Any(o => o.Kind == CollectionRaceObjectiveKind.Item && o.TargetId == itemId))
            return;

        var progress = active.ProgressOf(player);
        if (progress.IsCompleted || !RefreshItemProgress(player, active, progress))
            return;

        await SendProgressAsync(player, active, progress);
        await CheckCompletionAsync(player, active, progress);
    }

    private static bool RefreshItemProgress(UserSession player, ActiveCollectionRace active, CollectionRaceProgress progress)
    {
        var changed = false;
        for (var i = 0; i < active.Objectives.Count; i++)
        {
            var objective = active.Objectives[i];
            if (objective.Kind != CollectionRaceObjectiveKind.Item)
                continue;

            var current = Math.Min(objective.Count, CountItem(player, objective.TargetId));
            if (current == progress.Current[i])
                continue;

            progress.Current[i] = current;
            changed = true;
        }

        return changed;
    }

    private static int CountItem(UserSession player, int itemId)
    {
        var total = 0;
        for (var index = InventoryConstants.InventoryStart; index < player.Inventory.Length; index++)
        {
            if (player.Inventory[index].ItemId == itemId)
                total += player.Inventory[index].Count;
        }

        return total;
    }

    private async Task RemoveItemsAsync(UserSession player, int itemId, int count)
    {
        var remaining = count;
        for (var index = InventoryConstants.InventoryStart; index < player.Inventory.Length && remaining > 0; index++)
        {
            var slot = player.Inventory[index];
            if (slot.ItemId != itemId)
                continue;

            var taken = Math.Min((int)slot.Count, remaining);
            slot.Count -= (ushort)taken;
            remaining -= taken;
            if (slot.Count == 0)
                slot.Clear();

            await userNotificationService.SendStackChangeAsync(player, (byte)index, slot.ItemId, slot.Count, slot.Durability);
        }

        player.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(player);
    }

    private static Task SendProgressAsync(UserSession player, ActiveCollectionRace active, CollectionRaceProgress progress) =>
        player.Client.SendPacket(CollectionRacePacketWriter.Progress(active.Race.Id, progress.Current));

    private async Task CheckCompletionAsync(UserSession player, ActiveCollectionRace active, CollectionRaceProgress progress)
    {
        if (progress.IsCompleted)
            return;

        for (var i = 0; i < active.Objectives.Count; i++)
        {
            if (progress.Current[i] < active.Objectives[i].Count)
                return;
        }

        progress.IsCompleted = true;
        logger.LogInformation("Player {Name} completed Collection Race '{Race}'.", player.Name, active.Race.Name);

        foreach (var objective in active.Objectives)
        {
            if (objective.Kind == CollectionRaceObjectiveKind.Item)
                await RemoveItemsAsync(player, objective.TargetId, objective.Count);
        }

        await player.Client.SendPacket(CollectionRacePacketWriter.Completed("Collection Race Complete!"));
        await SendNoticeAsync(player, "[Collection Race] Congratulations! Your rewards arrive by mail when the race ends.");

        var announce = $"[Collection Race] Player {player.Name} has completed the Collection Race!";
        foreach (var s in sessionManager.GetAll())
        {
            if (s.ZoneId == active.Race.ZoneId)
                await SendNoticeAsync(s, announce);
        }
    }

    private async Task MailRewardsAsync(ActiveCollectionRace active)
    {
        var rewards = gameDataService.CollectionRaceRewardsByRace[active.Race.Id].ToList();
        var zoneName = gameDataService.ZoneInfoTable.TryGetValue(active.Race.ZoneId, out var zone) && !string.IsNullOrWhiteSpace(zone.MapName)
            ? zone.MapName
            : $"zone {active.Race.ZoneId}";

        foreach (var (characterId, progress) in active.Progress)
        {
            if (!progress.IsCompleted)
                continue;

            var attachments = new List<MailAttachmentDraft>();
            foreach (var reward in rewards)
            {
                if (reward.Rate < CollectionRaceRewardData.CertainRate && Random.Shared.Next(CollectionRaceRewardData.CertainRate) >= reward.Rate)
                    continue;

                attachments.Add(reward.ItemId switch
                {
                    InventoryConstants.ItemGold => new MailAttachmentDraft(MailAttachmentKind.Gold, reward.ItemId, reward.ItemCount),
                    InventoryConstants.ItemExperience => new MailAttachmentDraft(MailAttachmentKind.Experience, reward.ItemId, reward.ItemCount),
                    InventoryConstants.ItemLadderPoint => new MailAttachmentDraft(MailAttachmentKind.NationalPoints, reward.ItemId, reward.ItemCount),
                    _ => new MailAttachmentDraft(MailAttachmentKind.Item, reward.ItemId, reward.ItemCount, gameDataService.GetItem(reward.ItemId)?.Duration ?? 0),
                });
            }

            await mailService.SendSystemMailAsync(
                characterId,
                $"Collection Race: {active.Race.Name}",
                $"You completed the Collection Race '{active.Race.Name}' in {zoneName}. Your rewards are attached to this mail.",
                attachments);
        }
    }

    public async Task SyncPlayerAsync(UserSession player)
    {
        var active = GetActive(player.ZoneId);
        if (active == null || !active.IsEligible(player))
        {
            await player.Client.SendPacket(CollectionRacePacketWriter.Close());
            return;
        }

        var progress = active.ProgressOf(player);
        if (!progress.IsCompleted && RefreshItemProgress(player, active, progress))
            await CheckCompletionAsync(player, active, progress);

        var objectives = new List<CollectionRacePacketWriter.ObjectiveInfo>(active.Objectives.Count);
        for (var i = 0; i < active.Objectives.Count; i++)
        {
            var o = active.Objectives[i];
            objectives.Add(new CollectionRacePacketWriter.ObjectiveInfo(o.Kind, o.TargetId, o.Count, progress.Current[i], ObjectiveName(o)));
        }

        var rewards = gameDataService.CollectionRaceRewardsByRace[active.Race.Id]
            .Select(r => new CollectionRacePacketWriter.RewardInfo(r.ItemId, r.ItemCount, GetRewardName(r.ItemId), r.Rate))
            .ToList();

        var pkt = CollectionRacePacketWriter.State(
            active.Race.Id,
            active.Race.Name,
            active.Race.ZoneId,
            active.RemainingSeconds,
            progress.IsCompleted,
            objectives,
            rewards);

        await player.Client.SendPacket(pkt);
    }

    private string ObjectiveName(CollectionRaceObjectiveData objective) => objective.Kind switch
    {
        CollectionRaceObjectiveKind.EnemyPlayer => EnemyPlayersObjectiveName,
        CollectionRaceObjectiveKind.Item => gameDataService.GetItem(objective.TargetId)?.Name ?? $"Item {objective.TargetId}",
        _ => GetTargetName(objective.TargetId),
    };

    private string GetTargetName(int npcId)
    {
        if (gameDataService.MonsterTable.TryGetValue(npcId, out var monster))
            return monster.Name;
        if (gameDataService.NpcTable.TryGetValue(npcId, out var npc))
            return npc.Name;
        return $"Target {npcId}";
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
        var pkt = ChatPacketWriter.SystemNotice((byte)session.Nation, message);
        await session.Client.SendPacket(pkt);
    }

    private async Task BroadcastNoticeAsync(string message)
    {
        var pkt = NoticePacketWriter.Broadcast(message);
        await sessionManager.BroadcastToAll(pkt);
    }
}
