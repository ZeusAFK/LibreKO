using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IAchievementProgressService
{
    Task SendListAsync(UserSession session, IReadOnlyList<int>? requestedIds = null);
    IReadOnlyList<int> ApplyProgress(UserSession session);
    Task<bool> CompleteAsync(UserSession session, AchievementData definition);
    Task SendSummaryAsync(UserSession session);
    Task ReportMonsterKillAsync(UserSession session, int npcId);
    Task ReportPlayerKillAsync(UserSession session);
    Task RefreshAsync(UserSession session);
}

public class AchievementProgressService(
    IGameDataService gameDataService,
    IKingSystemRuntimeService kingSystemRuntimeService,
    IUserNotificationService userNotification,
    ILogger<AchievementProgressService> logger) : IAchievementProgressService
{
    private static readonly byte[] RonarkZones =
        [BattleZoneManager.ZONE_RONARK_LAND, BattleZoneManager.ZONE_RONARK_LAND_BASE];

    public Task SendListAsync(UserSession session, IReadOnlyList<int>? requestedIds = null)
    {
        var table = gameDataService.AchievementTable;
        var rows = requestedIds is null
            ? table.Values.Select(definition => RowFor(session, definition)).ToList()
            : requestedIds.Select(id => RowFor(session, id, table)).ToList();

        return session.Client.SendPacket(AchievementPacketWriter.List(rows));
    }

    public Task SendSummaryAsync(UserSession session)
    {
        var achievedPerTab = new int[AchievementPacketWriter.TabCount];
        var points = 0;
        var recent = new List<int>();

        foreach (var definition in gameDataService.AchievementTable.Values)
        {
            if (session.Achievements.Of(definition.Id).State == AchievementProgressState.InProgress)
                continue;

            points += definition.Points;
            var tab = (int)definition.Tab;
            if (tab >= 0 && tab < achievedPerTab.Length)
                achievedPerTab[tab]++;
            recent.Add(definition.Id);
        }

        recent.Reverse();

        return session.Client.SendPacket(AchievementPacketWriter.ProfileSummary(
            new AchievementPacketWriter.Summary(
                PlayMinutes: session.AccumulatedPlayMinutes(),
                MonstersDefeated: session.MonstersDefeated,
                PlayersDefeated: session.PlayersDefeated,
                Deaths: session.Deaths,
                Points: points,
                RecentlyAchieved: recent.Take(AchievementPacketWriter.RecentlyAchievedSlots).ToList(),
                AchievedPerTab: achievedPerTab)));
    }

    private static AchievementPacketWriter.Row RowFor(
        UserSession session, int achievementId, IReadOnlyDictionary<int, AchievementData> table) =>
        table.TryGetValue(achievementId, out var definition)
            ? RowFor(session, definition)
            : new AchievementPacketWriter.Row(
                achievementId, AchievementProgressState.InProgress, 0, 0);

    private static AchievementPacketWriter.Row RowFor(UserSession session, AchievementData definition)
    {
        var entry = session.Achievements.Of(definition.Id);
        return new AchievementPacketWriter.Row(
            definition.Id, entry.State, entry.Progress, definition.Target);
    }

    public Task ReportMonsterKillAsync(UserSession session, int npcId)
    {
        var inRonark = Array.IndexOf(RonarkZones, session.ZoneId) >= 0;

        return CountAsync(session, definition => definition.ConditionTable switch
        {
            AchievementConditionTable.Monster => definition.CountsNpc(npcId),
            AchievementConditionTable.War => inRonark
                && definition.WarKind == WarAchievementKind.RonarkMonsterKill,
            _ => false,
        });
    }

    public Task ReportPlayerKillAsync(UserSession session) =>
        CountAsync(session, definition =>
            definition.ConditionTable == AchievementConditionTable.War
            && definition.WarKind == WarAchievementKind.PlayerKill);

    public IReadOnlyList<int> ApplyProgress(UserSession session)
    {
        var changed = new List<int>();
        ApplyNormal(session, changed);

        while (ApplyCompletion(session, changed))
        {
        }

        return changed;
    }

    public async Task RefreshAsync(UserSession session)
    {
        var changed = ApplyProgress(session);
        if (changed.Count > 0)
            await PushChangedAsync(session, changed);
    }

    private async Task PushChangedAsync(UserSession session, IReadOnlyList<int> changed)
    {
        foreach (var achievementId in changed)
        {
            if (gameDataService.AchievementTable.TryGetValue(achievementId, out var definition))
                await CompleteAsync(session, definition);
        }

        await SendListAsync(session, changed.Count > AchievementPacketWriter.PushedRowsMax
            ? changed.Take(AchievementPacketWriter.PushedRowsMax).ToList()
            : changed);
    }

    public async Task<bool> CompleteAsync(UserSession session, AchievementData definition)
    {
        var result = definition.RewardItemId != 0
            ? await GrantRewardAsync(session, definition)
            : AchievementPacketWriter.ClaimIssued;

        if (result == AchievementPacketWriter.ClaimIssued)
        {
            session.Achievements.MarkClaimed(definition.Id);
            logger.LogInformation("{Name} completed achievement {Id} ({Achievement})",
                session.Name, definition.Id, definition.Name);

            if (definition.TitleId != 0)
            {
                session.InvalidateTitleBonuses();
                session.RecalculateStatsWithBuffs(gameDataService);
                await userNotification.SendStatUpdateAsync(session);
            }
        }

        await session.Client.SendPacket(
            AchievementPacketWriter.ClaimResult(definition.Id, result));

        return result == AchievementPacketWriter.ClaimIssued;
    }

    private async Task<sbyte> GrantRewardAsync(UserSession session, AchievementData definition)
    {
        var itemData = gameDataService.GetItem(definition.RewardItemId);
        if (itemData == null)
        {
            logger.LogWarning("Achievement {Id} rewards unknown item {ItemId}",
                definition.Id, definition.RewardItemId);
            return AchievementPacketWriter.ClaimItemMissing;
        }

        var count = (ushort)(definition.RewardItemCount < 1 ? 1 : definition.RewardItemCount);
        if (session.Stats.ItemWeight + (long)itemData.Weight * count > session.Stats.MaxWeight)
            return AchievementPacketWriter.ClaimInventoryFull;

        var slotIndex = session.FindSlotForItem(definition.RewardItemId, gameDataService, count);
        if (slotIndex < 0)
            return AchievementPacketWriter.ClaimInventoryFull;

        var slot = session.Inventory[slotIndex];
        var isNewItem = slot.IsEmpty;
        slot.ItemId = definition.RewardItemId;
        slot.Count += count;
        if (isNewItem)
            slot.Durability = itemData.Duration;

        await userNotification.SendStackChangeAsync(
            session, (byte)slotIndex, definition.RewardItemId, slot.Count, slot.Durability, isNewItem);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotification.SendWeightChangeAsync(session);
        return AchievementPacketWriter.ClaimIssued;
    }

    private void ApplyNormal(UserSession session, List<int> changed)
    {
        var isKing = kingSystemRuntimeService.IsKing(
            session, kingSystemRuntimeService.GetKingData(session.Nation));

        foreach (var definition in gameDataService.AchievementTable.Values)
        {
            if (definition.ConditionTable != AchievementConditionTable.Normal)
                continue;

            var value = definition.NormalKind switch
            {
                NormalAchievementKind.King => isKing ? 1 : 0,
                NormalAchievementKind.NationalContribution => session.Loyalty,
                NormalAchievementKind.Level => session.Level,
                NormalAchievementKind.KnightsContribution => session.KnightsPoints,
                _ => 0,
            };

            if (session.Achievements.Reach(definition, value))
                changed.Add(definition.Id);
        }
    }

    private bool ApplyCompletion(UserSession session, List<int> changed)
    {
        var completed = false;
        foreach (var definition in gameDataService.AchievementTable.Values)
        {
            if (definition.ConditionTable != AchievementConditionTable.Completion)
                continue;

            var met = 0;
            foreach (var requiredId in definition.RequiredIds)
            {
                if (IsRequirementMet(session, definition.CompletionKind, requiredId))
                    met++;
            }

            if (session.Achievements.Reach(definition, met))
            {
                changed.Add(definition.Id);
                completed = true;
            }
        }

        return completed;
    }

    private static bool IsRequirementMet(
        UserSession session, CompletionAchievementKind kind, int requiredId) => kind switch
    {
        CompletionAchievementKind.Quests => session.Quest.IsCompleted((short)requiredId),
        CompletionAchievementKind.Achievements => session.Achievements.IsReached(requiredId),
        _ => false,
    };

    private async Task CountAsync(UserSession session, Func<AchievementData, bool> matches)
    {
        var changed = new List<int>();
        foreach (var definition in gameDataService.AchievementTable.Values)
        {
            if (matches(definition) && session.Achievements.Advance(definition, 1))
                changed.Add(definition.Id);
        }

        if (changed.Count > 0)
        {
            while (ApplyCompletion(session, changed))
            {
            }

            await PushChangedAsync(session, changed);
        }
    }
}
