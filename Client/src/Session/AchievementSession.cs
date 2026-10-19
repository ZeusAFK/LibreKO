namespace LibreKO.Session;

public sealed class AchievementSession
{
    private readonly List<AchievementEntry> entries = new();

    public IReadOnlyList<AchievementEntry> Entries => entries;

    public AchievementSummary? Summary { get; private set; }

    public event Action<List<AchievementEntry>>? AchievementListEvent;

    internal void ApplyList(List<AchievementEntry> list)
    {
        foreach (var entry in list)
        {
            int index = entries.FindIndex(known => known.Id == entry.Id);
            if (index >= 0) entries[index] = entry;
            else entries.Add(entry);
        }
        AchievementListEvent?.Invoke(list);
    }

    public event Action<AchievementSummary>? AchievementSummaryEvent;

    internal void ApplySummary(AchievementSummary summary)
    {
        Summary = summary;
        AchievementSummaryEvent?.Invoke(summary);
    }

    internal void Clear()
    {
        entries.Clear();
        Summary = null;
    }

    public event Action<int, AchievementClaimResult>? AchievementClaimEvent;

    internal void RaiseAchievementClaim(int arg1, AchievementClaimResult arg2) => AchievementClaimEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? TitleChangedEvent;

    internal void RaiseTitleChanged(int arg1, int arg2) => TitleChangedEvent?.Invoke(arg1, arg2);

    public event Action<AchievementTitleAnswer>? TitleAnswerEvent;

    internal void RaiseTitleAnswer(AchievementTitleAnswer achievementTitleAnswer) => TitleAnswerEvent?.Invoke(achievementTitleAnswer);

    public int DisplayTitleId { get; private set; }

    internal void ApplyTitleAnswer(AchievementTitleAnswer answer)
    {
        if (answer.Result == AchievementTitleResult.Done && answer.Sub == AchievementSubOpcode.SelectDisplayTitle) DisplayTitleId = answer.TitleId;
        if (answer.Result == AchievementTitleResult.Done && answer.Sub == AchievementSubOpcode.ResetDisplayTitle) DisplayTitleId = 0;
        RaiseTitleAnswer(answer);
    }

    internal void ApplyOwnTitle(int titleId) => DisplayTitleId = titleId;
}
