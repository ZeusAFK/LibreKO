using System;

namespace LibreKO.Domain;

public sealed class RebirthPick
{
    public const int PointsPerRebirth = 2;
    public const int MaxRebirthLevel = 15;
    public const int StatCount = 5;
    public const byte ResultRefused = 0;
    public const byte ResultAccepted = 1;

    private readonly int[] _picked = new int[StatCount];

    public int PickedAt(int row) => (uint)row < StatCount ? _picked[row] : 0;

    public int Placed
    {
        get
        {
            int total = 0;
            for (int i = 0; i < StatCount; i++) total += _picked[i];
            return total;
        }
    }

    public int Remaining => PointsPerRebirth - Placed;

    public bool Complete => Remaining == 0;

    public bool CanAdd(int row) => (uint)row < StatCount && Remaining > 0;

    public bool CanRemove(int row) => (uint)row < StatCount && _picked[row] > 0;

    public bool Add(int row)
    {
        if (!CanAdd(row)) return false;
        _picked[row]++;
        return true;
    }

    public bool Remove(int row)
    {
        if (!CanRemove(row)) return false;
        _picked[row]--;
        return true;
    }

    public void Clear() => Array.Clear(_picked);

    public static bool Available(int level, int rebirthLevel) =>
        level >= CharacterSheet.MaxLevel && rebirthLevel < MaxRebirthLevel;

    public static bool IsAllocation(ReadOnlySpan<byte> picks)
    {
        if (picks.Length != StatCount) return false;
        int total = 0;
        foreach (byte picked in picks) total += picked;
        return total == PointsPerRebirth;
    }

    public static bool IsResult(int code) => code is ResultRefused or ResultAccepted;

    public byte[] Payload()
    {
        var bytes = new byte[StatCount];
        for (int i = 0; i < StatCount; i++) bytes[i] = (byte)_picked[i];
        return bytes;
    }
}
