namespace LibreKO.Domain;

public sealed class QuestRewardSelection<TOption> where TOption : notnull
{
    private readonly Dictionary<int, TOption> chosen = new();

    public void Choose(int questId, TOption option) => chosen[questId] = option;

    public bool Chosen(int questId, out TOption option) => chosen.TryGetValue(questId, out option!);

    public int ChoiceIndex(int questId, IReadOnlyList<TOption> options)
    {
        if (!chosen.TryGetValue(questId, out var option)) return -1;
        for (var index = 0; index < options.Count; index++)
            if (EqualityComparer<TOption>.Default.Equals(options[index], option)) return index;
        return -1;
    }

    public void TurnedIn(int questId) => chosen.Remove(questId);

    public void Clear() => chosen.Clear();

    public void Viewed(int questId, bool choosing, IReadOnlyList<TOption> options)
    {
        if (!choosing || ChoiceIndex(questId, options) < 0) chosen.Remove(questId);
    }
}
