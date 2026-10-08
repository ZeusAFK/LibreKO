namespace LibreKO.Domain;

public sealed class QuestRewardSelection<TOption, TReceipt> where TOption : notnull
{
    private readonly Dictionary<int, TOption> _chosen = new();
    private readonly Dictionary<int, TReceipt> _received = new();

    public void Choose(int questId, TOption option) => _chosen[questId] = option;

    public bool Chosen(int questId, out TOption option) => _chosen.TryGetValue(questId, out option!);

    public int ChoiceIndex(int questId, IReadOnlyList<TOption> options)
    {
        if (!_chosen.TryGetValue(questId, out var option)) return -1;
        for (var index = 0; index < options.Count; index++)
            if (EqualityComparer<TOption>.Default.Equals(options[index], option)) return index;
        return -1;
    }

    public bool Received(int questId, out TReceipt receipt) => _received.TryGetValue(questId, out receipt!);

    public void Receive(int questId, TReceipt receipt)
    {
        _received[questId] = receipt;
        _chosen.Remove(questId);
    }

    public void Viewed(int questId, bool choosing, bool completed, IReadOnlyList<TOption> options)
    {
        if (!completed) _received.Remove(questId);
        if (!choosing || ChoiceIndex(questId, options) < 0) _chosen.Remove(questId);
    }
}
