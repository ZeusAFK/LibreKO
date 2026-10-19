using System.Collections.Generic;

namespace LibreKO.Domain;

public enum LootGroup { Coins, Equipment, Quest, Other }

public enum LootReason { Taken, AlwaysRule, NeverRule, GroupOff, GradeOff, TooCheap }

public sealed class LootFilter
{
    public static readonly int[] FilteredGrades =
    {
        ItemData.Rarity.Regular, ItemData.Rarity.Magic, ItemData.Rarity.Rare,
        ItemData.Rarity.Craft, ItemData.Rarity.Unique, ItemData.Rarity.Upgrade,
    };

    public const int MaxRules = 200;

    public bool Enabled;
    public int MinSellPrice;
    public readonly HashSet<LootGroup> Groups = new() { LootGroup.Coins, LootGroup.Equipment, LootGroup.Quest, LootGroup.Other };
    public readonly HashSet<int> Grades = new(FilteredGrades);
    public readonly HashSet<int> Always = new();
    public readonly HashSet<int> Never = new();

    public bool Takes(int itemId, LootGroup group, int rarity, int sellPrice) =>
        !Enabled || Verdict(itemId, group, rarity, sellPrice) is LootReason.Taken or LootReason.AlwaysRule;

    public LootReason Verdict(int itemId, LootGroup group, int rarity, int sellPrice)
    {
        if (Never.Contains(itemId)) return LootReason.NeverRule;
        if (Always.Contains(itemId)) return LootReason.AlwaysRule;
        return ByGroups(group, rarity, sellPrice);
    }

    public LootReason ByGroups(LootGroup group, int rarity, int sellPrice)
    {
        if (!Groups.Contains(group)) return LootReason.GroupOff;
        if (group == LootGroup.Coins) return LootReason.Taken;
        if (group == LootGroup.Equipment && System.Array.IndexOf(FilteredGrades, rarity) >= 0 && !Grades.Contains(rarity))
            return LootReason.GradeOff;
        return group == LootGroup.Quest || sellPrice >= MinSellPrice ? LootReason.Taken : LootReason.TooCheap;
    }

    public bool Flip(int itemId, LootGroup group, int rarity, int sellPrice)
    {
        bool byGroups = ByGroups(group, rarity, sellPrice) == LootReason.Taken;
        bool taken = Verdict(itemId, group, rarity, sellPrice) is LootReason.Taken or LootReason.AlwaysRule;
        if (taken && Always.Contains(itemId) && !byGroups) { SetRule(itemId, null); return true; }
        if (!taken && Never.Contains(itemId) && byGroups) { SetRule(itemId, null); return true; }
        if (!Always.Contains(itemId) && !Never.Contains(itemId) && Always.Count + Never.Count >= MaxRules) return false;
        SetRule(itemId, !taken);
        return true;
    }

    public static LootGroup GroupOf(int itemId, int coinId, ItemData.Item? item)
    {
        if (itemId == coinId) return LootGroup.Coins;
        if (item == null) return LootGroup.Other;
        if (item.Race == ItemData.Race.Quest) return LootGroup.Quest;
        return item.Slot > 0 ? LootGroup.Equipment : LootGroup.Other;
    }

    public void SetRule(int itemId, bool? take)
    {
        Always.Remove(itemId);
        Never.Remove(itemId);
        if (take == true) Always.Add(itemId);
        else if (take == false) Never.Add(itemId);
    }
}
