namespace LibreKO.Session;

public sealed class QuestBook
{
    public const int KillGroupCount = 4;

    private readonly List<QuestEntry> questEntries = new();

    public IReadOnlyList<QuestEntry> Entries => questEntries;

    private readonly Dictionary<int, ushort[]> killCounts = new();

    public IReadOnlyDictionary<int, ushort[]> Kills => killCounts;

    private readonly Dictionary<int, QuestObjectives> questObjectives = new();

    public IReadOnlyDictionary<int, QuestObjectives> Objectives => questObjectives;

    private readonly Dictionary<int, QuestStrings> questStrings = new();

    public IReadOnlyDictionary<int, QuestStrings> Strings => questStrings;

    private readonly Dictionary<int, QuestView> questViews = new();

    public IReadOnlyDictionary<int, QuestView> Views => questViews;

    private readonly HashSet<int> trackedQuests = new();

    public IReadOnlySet<int> Tracked => trackedQuests;

    private readonly HashSet<int> targetNpcIds = new();

    public IReadOnlySet<int> TargetNpcs => targetNpcIds;

    public QuestRewardSelection<QuestTransfer> RewardSelection { get; } = new();

    public int StateOf(int questId)
    {
        int idx = questEntries.FindIndex(q => q.QuestId == questId);
        return idx >= 0 ? questEntries[idx].State : 0;
    }

    public IEnumerable<(int ItemId, int Count)> HandIns(int questId) =>
        questViews.TryGetValue(questId, out var view)
            ? view.Transfers.Where(t => t.Take && t.Kind == 0).Select(t => (t.ItemId, t.Count))
            : QuestData.HandIns(questId).Select(t => (t.ItemId, t.Count));

    public IEnumerable<(int ItemId, int Count)> Rewards(int questId, int characterClass) =>
        questViews.TryGetValue(questId, out var view)
            ? view.Transfers.Where(t => !t.Take).Select(t => (t.DisplayItemId, t.Count))
            : QuestData.Rewards(questId, characterClass).Select(t => (t.ItemId, t.Count));

    internal void ApplyLog(List<QuestEntry> entries)
    {
        questViews.Clear();
        questObjectives.Clear();
        questStrings.Clear();
        questEntries.Clear();
        questEntries.AddRange(entries);
    }

    internal void ApplyState(int questId, int state)
    {
        SetEntry(questId, state);
        if (state is QuestStates.Completed or QuestStates.Abandoned) killCounts.Remove(questId);
    }

    internal void ApplyKillCounts(int questId, ushort[] counts) => killCounts[questId] = counts;

    internal void ApplyKillUpdate(int questId, int group, int count)
    {
        if (!killCounts.TryGetValue(questId, out var counts))
        {
            counts = new ushort[KillGroupCount];
            killCounts[questId] = counts;
        }
        int index = group - 1;
        if (index >= 0 && index < counts.Length) counts[index] = (ushort)count;
    }

    internal void ApplyObjectives(QuestObjectives objectives) => questObjectives[objectives.QuestId] = objectives;

    internal void ApplyStrings(List<QuestStrings> texts)
    {
        foreach (var text in texts) questStrings[text.QuestId] = text;
    }

    internal void ApplyView(QuestView view)
    {
        questViews[view.QuestId] = view;
        RewardSelection.Viewed(view.QuestId,
            view.State is QuestViewState.InProgress or QuestViewState.Claimable, view.Options);
        questStrings[view.QuestId] = new QuestStrings(view.QuestId, view.Title, view.Journal);
        questObjectives[view.QuestId] = view.Objectives;
        killCounts[view.QuestId] = view.Counts;
        SetEntry(view.QuestId, view.State switch
        {
            QuestViewState.InProgress => QuestStates.Active,
            QuestViewState.Claimable => QuestStates.ReadyToTurnIn,
            QuestViewState.Completed => QuestStates.Completed,
            _ => QuestStates.Available,
        });
    }

    internal bool Track(int questId, bool tracked) => tracked ? trackedQuests.Add(questId) : trackedQuests.Remove(questId);

    internal void RebuildTargets(int zone)
    {
        targetNpcIds.Clear();
        foreach (var view in questViews.Values)
        {
            if (view.ZoneId != 0 && view.ZoneId != zone) continue;
            if (view.State == QuestViewState.Claimable)
            {
                targetNpcIds.Add(view.NpcId);
                continue;
            }
            if (view.State != QuestViewState.InProgress) continue;
            for (var group = 0; group < view.Objectives.Groups.Length; group++)
            {
                var goal = view.Objectives.Groups[group];
                var done = group < view.Counts.Length ? view.Counts[group] : 0;
                if (done >= goal.Count) continue;
                foreach (var monster in goal.Monsters) targetNpcIds.Add(monster);
            }
        }
    }

    private void SetEntry(int questId, int state)
    {
        int index = questEntries.FindIndex(q => q.QuestId == questId);
        if (index >= 0) questEntries[index] = new QuestEntry(questId, state);
        else questEntries.Add(new QuestEntry(questId, state));
    }

    internal void Clear()
    {
        questEntries.Clear();
        killCounts.Clear();
        questObjectives.Clear();
        questStrings.Clear();
        questViews.Clear();
        trackedQuests.Clear();
        targetNpcIds.Clear();
        RewardSelection.Clear();
    }
}
