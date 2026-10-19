namespace LibreKO.Session;

public sealed class VanguardSession
{
    public event Action<VanguardReply>? VanguardEvent;

    internal void RaiseVanguard(VanguardReply vanguardReply) => VanguardEvent?.Invoke(vanguardReply);

    public bool IsVanguard { get; private set; }

    private readonly List<string> wantedNames = new();

    public IReadOnlyList<string> WantedNames => wantedNames;

    public int WantedCount { get; private set; }

    private readonly List<VanguardMark> vanguardMarks = new();

    public IReadOnlyList<VanguardMark> VanguardMarks => vanguardMarks;

    internal void Reset()
    {
        IsVanguard = false;
        wantedNames.Clear();
        vanguardMarks.Clear();
    }

    internal void ApplyAppointed(bool appointed) => IsVanguard = appointed;

    internal void ApplyWantedList(IReadOnlyList<string> names)
    {
        wantedNames.Clear();
        wantedNames.AddRange(names);
        WantedCount = names.Count;
    }

    internal void ApplyPositions(IEnumerable<string> names, IEnumerable<VanguardMark> marks)
    {
        wantedNames.Clear();
        wantedNames.AddRange(names);
        vanguardMarks.Clear();
        vanguardMarks.AddRange(marks);
    }

    public bool ForgetWanted(string name) =>
        wantedNames.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 0;
}
