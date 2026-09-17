namespace LibreKO.Game.World;

public enum QuestStatus : byte
{
    NotStarted = 0,
    Active = 1,
    Completed = 2,
    ReadyToTurnIn = 3,
    Abandoned = 4,
}

public class QuestState
{
    public readonly record struct ViewVersion(object Definition, byte State, int Zone, string Language, int Day, string Counts);
    public Dictionary<int, ViewVersion> ViewVersions { get; } = [];
    public HashSet<int> AvailableNotifications { get; } = [];
    public HashSet<int> StartedNotifications { get; } = [];
    public HashSet<int> ReadyNotifications { get; } = [];
    public Dictionary<int, (LibreKO.Quests.Runtime.QuestProgram Program, int[] Events, LibreKO.Quests.Runtime.QuestViewState State)> NotificationReplies { get; } = [];
    public int ViewZone { get; set; } = -1;
    public SemaphoreSlim ViewRefresh { get; } = new(1, 1);
    public Dictionary<short, byte> QuestMap { get; } = [];
    public Dictionary<short, int> DailyCompletionDays { get; } = [];
    public ushort[] KillCounts { get; } = new ushort[4]; // kill counts for 4 quest mob groups
    public Dictionary<short, ushort[]> QuestKillCountsMap { get; } = [];
    public short ActiveQuestId { get; set; }
    public string ActiveQuestScript { get; set; } = string.Empty;
    public int EventNpcId { get; set; }         // NPC proto ID for current interaction
    public int EventNpcUniqueId { get; set; }   // NPC instance ID for current interaction
    public short BindPoint { get; set; }        // Respawn point (object event index)
    public int[] SelectMessageEvents { get; } = new int[LibreKO.Quests.Binding.QuestVocabulary.MaxDialogButtons];
    public int[] SelectMessageRewards { get; } = new int[LibreKO.Quests.Binding.QuestVocabulary.MaxDialogButtons];
    public bool IsScriptDialog { get; set; }
    public byte SelectMessageFlag { get; set; }

    public QuestState()
    {
        Array.Fill(SelectMessageEvents, -1);
        Array.Fill(SelectMessageRewards, -1);
    }

    public QuestStatus StatusOf(short questId) =>
        QuestMap.TryGetValue(questId, out var status) ? (QuestStatus)status : QuestStatus.NotStarted;

    public bool IsCompleted(short questId) => StatusOf(questId) == QuestStatus.Completed;

    public bool RefreshDailyQuest(short questId, DateOnly today)
    {
        if (!IsCompleted(questId))
            return false;
        if (!DailyCompletionDays.TryGetValue(questId, out var completed))
        {
            DailyCompletionDays[questId] = today.DayNumber;
            return true;
        }
        if (completed >= today.DayNumber)
            return false;
        QuestMap[questId] = (byte)QuestStatus.NotStarted;
        DailyCompletionDays.Remove(questId);
        RemoveQuestKillCounts(questId);
        return true;
    }

    public ushort[] GetOrCreateQuestKillCounts(short questId)
    {
        if (!QuestKillCountsMap.TryGetValue(questId, out var counts))
        {
            counts = new ushort[4];
            QuestKillCountsMap[questId] = counts;
        }

        return counts;
    }

    public ushort[] GetQuestKillCounts(short questId)
        => QuestKillCountsMap.TryGetValue(questId, out var counts)
            ? counts
            : [0, 0, 0, 0];

    public void ResetQuestKillCounts(short questId)
    {
        Array.Clear(GetOrCreateQuestKillCounts(questId));
        if (ActiveQuestId == questId)
            Array.Clear(KillCounts);
    }

    public void RemoveQuestKillCounts(short questId)
    {
        QuestKillCountsMap.Remove(questId);
        if (ActiveQuestId == questId)
            Array.Clear(KillCounts);
    }

    public void SyncActiveQuestKillCounts()
    {
        Array.Clear(KillCounts);
        if (ActiveQuestId <= 0)
            return;

        var counts = GetQuestKillCounts(ActiveQuestId);
        Array.Copy(counts, KillCounts, Math.Min(KillCounts.Length, counts.Length));
    }
}
