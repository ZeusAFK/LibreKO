using System;

namespace LibreKO.Domain;

public sealed class RebirthPick
{
    public const int PointsPerRebirth = 2;
    public const int MaxRebirthLevel = 15;
    public const int StatCount = 5;

    private readonly int[] picked = new int[StatCount];

    public int Capacity { get; private set; } = PointsPerRebirth;

    public int PickedAt(int row) => (uint)row < StatCount ? picked[row] : 0;

    public int Placed
    {
        get
        {
            int total = 0;
            for (int i = 0; i < StatCount; i++) total += picked[i];
            return total;
        }
    }

    public int Remaining => Capacity - Placed;

    public bool Complete => Remaining == 0;

    public bool CanAdd(int row) => (uint)row < StatCount && Remaining > 0;

    public bool CanRemove(int row) => (uint)row < StatCount && picked[row] > 0;

    public bool Add(int row)
    {
        if (!CanAdd(row)) return false;
        picked[row]++;
        return true;
    }

    public bool Remove(int row)
    {
        if (!CanRemove(row)) return false;
        picked[row]--;
        return true;
    }

    public void Clear() => Reset(PointsPerRebirth);

    public void Reset(int capacity)
    {
        Array.Clear(picked);
        Capacity = Math.Max(0, capacity);
    }

    public static bool Available(int level, int rebirthLevel) =>
        level >= CharacterSheet.MaxLevel && rebirthLevel < MaxRebirthLevel;

    public static bool IsAllocation(ReadOnlySpan<byte> picks)
    {
        if (picks.Length != StatCount) return false;
        int total = 0;
        foreach (byte pick in picks) total += pick;
        return total == PointsPerRebirth;
    }

    public byte[] Payload()
    {
        var bytes = new byte[StatCount];
        for (int i = 0; i < StatCount; i++) bytes[i] = (byte)picked[i];
        return bytes;
    }
}
