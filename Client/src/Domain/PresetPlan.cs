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
