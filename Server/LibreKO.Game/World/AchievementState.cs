using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public enum AchievementProgressState : byte
{
    InProgress = 0,
    Achieved = 4,
    Claimed = 5,
}

public sealed class AchievementState
{
    private const int BytesPerEntry = 7;
    private const byte LegacyClaimable = 1;
    private const byte LegacyClaimed = 2;

    public readonly record struct Entry(int Progress, AchievementProgressState State);

    private readonly Dictionary<int, Entry> _entries = new();

    public IReadOnlyDictionary<int, Entry> Entries => _entries;

    public Entry Of(int achievementId) =>
        _entries.TryGetValue(achievementId, out var entry) ? entry : new Entry(0, AchievementProgressState.InProgress);

    public bool IsClaimed(int achievementId) => Of(achievementId).State == AchievementProgressState.Claimed;

    public bool IsReached(int achievementId) => Of(achievementId).State != AchievementProgressState.InProgress;

    public void MarkClaimed(int achievementId)
    {
        var entry = Of(achievementId);
        _entries[achievementId] = entry with { State = AchievementProgressState.Claimed };
    }

    public bool Advance(AchievementData definition, int amount)
    {
        if (amount <= 0) return false;
        return Reach(definition, Of(definition.Id).Progress + amount);
    }

    public bool Reach(AchievementData definition, long value)
    {
        if (definition.Target <= 0) return false;

        var entry = Of(definition.Id);
        if (entry.State != AchievementProgressState.InProgress) return false;

        var progress = (int)Math.Min(value, definition.Target);
        if (progress <= entry.Progress) return false;

        var state = progress >= definition.Target
            ? AchievementProgressState.Achieved
            : AchievementProgressState.InProgress;
        _entries[definition.Id] = new Entry(progress, state);
        return state == AchievementProgressState.Achieved;
    }

    public byte[] Serialize()
    {
        if (_entries.Count == 0) return [];

        var data = new byte[2 + _entries.Count * BytesPerEntry];
        BitConverter.TryWriteBytes(data.AsSpan(0), (short)_entries.Count);

        var offset = 2;
        foreach (var (achievementId, entry) in _entries)
        {
            BitConverter.TryWriteBytes(data.AsSpan(offset), (short)achievementId);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 2), entry.Progress);
            data[offset + 6] = (byte)entry.State;
            offset += BytesPerEntry;
        }

        return data;
    }

    private static AchievementProgressState StateFrom(byte stored) => stored switch
    {
        LegacyClaimable => AchievementProgressState.Achieved,
        LegacyClaimed => AchievementProgressState.Claimed,
        _ => (AchievementProgressState)stored,
    };

    public void Load(byte[]? data)
    {
        _entries.Clear();
        if (data == null || data.Length < 2) return;

        var count = BitConverter.ToInt16(data, 0);
        var offset = 2;
        for (var index = 0; index < count && offset + BytesPerEntry <= data.Length; index++)
        {
            var achievementId = BitConverter.ToInt16(data, offset);
            var progress = BitConverter.ToInt32(data, offset + 2);
            _entries[achievementId] = new Entry(progress, StateFrom(data[offset + 6]));
            offset += BytesPerEntry;
        }
    }
}
