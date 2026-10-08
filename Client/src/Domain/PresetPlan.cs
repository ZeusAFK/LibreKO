using System;

namespace LibreKO.Domain;

public sealed class PresetPlan
{
    public const int SlotCount = 4;
    public const int TreeCount = MasteryPoints.LastTree - MasteryPoints.FirstTree + 1;

    public int[] Stats { get; } = new int[CharacterSheet.StatCount];
    public int[] Trees { get; } = new int[TreeCount];

    public bool IsEmpty
    {
        get
        {
            foreach (int v in Stats) if (v > 0) return false;
            foreach (int v in Trees) if (v > 0) return false;
            return true;
        }
    }

    public void SetStats(int[]? values) => Copy(values, Stats);

    public void SetTrees(int[]? values) => Copy(values, Trees);

    public int StatValue(int classCode, int row) => StarterStats.BaseForClass(classCode).StatAtRow(row) + Stats[row];

    public int StatBudget(int classCode, int row, int pointPool)
    {
        long other = 0;
        for (int i = 0; i < Stats.Length; i++) if (i != row) other += Stats[i];
        long cap = CharacterSheet.StatMax - StarterStats.BaseForClass(classCode).StatAtRow(row);
        return (int)Math.Max(0, Math.Min(pointPool - other, cap));
    }

    public bool IsRedistributed(int classCode, CharacterSheet sheet)
    {
        var basis = StarterStats.BaseForClass(classCode);
        for (int i = 0; i < Stats.Length; i++) if (sheet.StatAtRow(i) != basis.StatAtRow(i)) return false;
        return true;
    }

    public bool TryStatValues(int classCode, int pointPool, out int[] values, out int remaining)
    {
        values = new int[CharacterSheet.StatCount];
        remaining = pointPool;
        if (pointPool < 0) return false;
        var basis = StarterStats.BaseForClass(classCode);
        for (int i = 0; i < Stats.Length; i++)
        {
            int allocation = Stats[i];
            if (allocation < 0 || allocation > CharacterSheet.StatMax - basis.StatAtRow(i) || allocation > remaining)
                return false;
            values[i] = basis.StatAtRow(i) + allocation;
            remaining -= allocation;
        }
        return true;
    }

    public PresetPlan Clone()
    {
        var copy = new PresetPlan();
        copy.SetStats(Stats);
        copy.SetTrees(Trees);
        return copy;
    }

    private static void Copy(int[]? from, int[] to)
    {
        Array.Clear(to);
        if (from == null) return;
        Array.Copy(from, to, Math.Min(from.Length, to.Length));
    }
}

public readonly record struct PresetStatState(
    int[] Stats, int Points, int MaxHp, int MaxMp, int TotalHit, int MaxWeight);

public readonly record struct PresetSkillState(int[] Trees, int Pool);
