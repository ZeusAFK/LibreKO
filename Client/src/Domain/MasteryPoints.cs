using System;

namespace LibreKO.Domain;

public sealed class MasteryPoints
{
    public const int SlotCount = 9;
    public const int PoolSlot = 0;
    public const int FirstTree = 5;
    public const int LastTree = 8;
    public const int MasterTree = 8;
    public const int MinLevel = 10;
    public const int MasterTreeMinLevel = 60;
    public const int MasterTreeMaxPoints = 23;

    private readonly byte[] slots = new byte[SlotCount];

    public int Pool => slots[PoolSlot];

    public int PointsForLevel
    {
        get
        {
            int total = 0;
            foreach (byte v in slots) total += v;
            return total;
        }
    }

    public static bool IsTree(int tree) => tree is >= FirstTree and <= LastTree;

    public int InTree(int tree) => IsTree(tree) ? slots[tree] : 0;

    public static bool ClassHasTree(int classCode, int tree)
    {
        if (!IsTree(tree)) return false;
        int tier = CharacterClassCatalog.Tier(classCode);
        return tree == MasterTree
            ? tier == CharacterClassCatalog.TierMaster
            : tier >= CharacterClassCatalog.TierNovice;
    }

    public static int CapInTree(int classCode, int tree, int level)
    {
        if (!ClassHasTree(classCode, tree)) return 0;
        return tree == MasterTree
            ? Math.Clamp(level - MasterTreeMinLevel, 0, MasterTreeMaxPoints)
            : Math.Max(0, level);
    }

    public bool CanSpend(int classCode, int tree, int level) =>
        Pool > 0 && InTree(tree) < CapInTree(classCode, tree, level);

    public byte[] ToArray() => (byte[])slots.Clone();

    public void Seed(byte[]? slots)
    {
        Array.Clear(this.slots);
        if (slots == null) return;
        Array.Copy(slots, this.slots, Math.Min(slots.Length, SlotCount));
    }

    public void SetPool(int pool) => slots[PoolSlot] = Clamp(pool);

    public bool Spend(int tree)
    {
        if (!IsTree(tree) || Pool < 1 || slots[tree] == byte.MaxValue) return false;
        slots[PoolSlot]--;
        slots[tree]++;
        return true;
    }

    public void ApplyRejection(int tree, int serverValue)
    {
        if (!IsTree(tree)) return;
        slots[tree] = Clamp(serverValue);
        if (slots[PoolSlot] < byte.MaxValue) slots[PoolSlot]++;
    }

    public void ResetTrees(int pool)
    {
        SetPool(pool);
        for (int tree = FirstTree; tree <= LastTree; tree++) slots[tree] = 0;
    }

    public void ApplyTrees(int[] trees, int pool)
    {
        SetPool(pool);
        for (int tree = FirstTree; tree <= LastTree; tree++)
        {
            int index = tree - FirstTree;
            slots[tree] = index < trees.Length ? Clamp(trees[index]) : (byte)0;
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, byte.MaxValue);
}
