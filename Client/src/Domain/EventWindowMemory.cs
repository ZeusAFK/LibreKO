namespace LibreKO.Domain;

public sealed class EventWindowMemory
{
    public const double NewRunEndShiftSeconds = 30;
    public const double EndedGraceSeconds = 5;

    private sealed class Entry
    {
        public double EndsAt;
        public bool Hidden;
        public bool Folded;
    }

    private readonly Dictionary<string, Entry> entries = new();

    public bool Observe(string key, double now, int remainingSeconds)
    {
        double endsAt = now + Math.Max(0, remainingSeconds);
        if (entries.TryGetValue(key, out var entry)
            && now <= entry.EndsAt + EndedGraceSeconds
            && Math.Abs(endsAt - entry.EndsAt) <= NewRunEndShiftSeconds)
        {
            entry.EndsAt = endsAt;
            return false;
        }
        entries[key] = new Entry { EndsAt = endsAt };
        return true;
    }

    public bool Hidden(string key) => entries.TryGetValue(key, out var entry) && entry.Hidden;

    public bool Folded(string key) => entries.TryGetValue(key, out var entry) && entry.Folded;

    public void SetHidden(string key, bool hidden)
    {
        if (entries.TryGetValue(key, out var entry)) entry.Hidden = hidden;
    }

    public void SetFolded(string key, bool folded)
    {
        if (entries.TryGetValue(key, out var entry)) entry.Folded = folded;
    }
}
