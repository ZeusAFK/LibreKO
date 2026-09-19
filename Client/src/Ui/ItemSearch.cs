using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO;

public readonly struct ItemSearchHit
{
    public readonly ItemData.Item Def;
    public readonly ItemData.Ext? Ext;
    public readonly int Id;
    public readonly string Family;
    public readonly int Plus;

    public ItemSearchHit(ItemData.Item def, ItemData.Ext? ext)
    {
        Def = def;
        Ext = ext;
        Id = ext == null ? def.Id : ItemData.VariantId(def, ext);
        Family = ItemData.SplitUpgrade(ItemData.DisplayName(Id), out Plus);
    }

    public string VariantLabel
    {
        get
        {
            string name = Ext?.Name.Trim() ?? "";
            if (name.Length == 0) return "";
            name = ItemData.SplitUpgrade(name, out _);
            return name.Length > 0 && !Family.Contains(name, StringComparison.OrdinalIgnoreCase) ? name : "";
        }
    }
}

public static class ItemSearch
{
    public const string TabBasic = "Basic";
    public const string TabProperty = "Property";
    public const string TabReverse = "Reverse";
    public static readonly string[] Tabs = { TabBasic, TabProperty, TabReverse };

    public const int NameCap = 150;
    public const int ResultCap = 100;

    private const int NoTradeIdLast = 999_999_999;

    public static bool IsTradeable(ItemData.Item def) =>
        (def.Id < ItemData.NoTradeIdFirst || def.Id > NoTradeIdLast)
        && def.Race != ItemData.QuestItemRace
        && def.Bound == 0;

    public static string TabOf(ItemSearchHit hit)
    {
        if (ItemData.IsReverse(hit.Def, hit.Ext)) return TabReverse;
        var ext = hit.Ext;
        if (ext == null || ext.Linked > 0) return TabBasic;
        string family = ext.Name.Trim();
        return family.Length == 0 || family == ItemData.PlainUpgrade ? TabBasic : TabProperty;
    }

    public static IEnumerable<ItemData.Ext> VariantExts(ItemData.Item def)
    {
        var own = new List<ItemData.Ext>();
        var generic = new List<ItemData.Ext>();
        foreach (var ext in ItemData.ExtsForCat(def.Cat))
        {
            if (!ItemData.ExtAppliesTo(def, ext)) continue;
            if (ext.Linked == def.Id) own.Add(ext);
            else if (ext.Linked == 0) generic.Add(ext);
        }
        return own.Count > 0 ? own : generic;
    }

    public static bool MatchesFilters(
        ItemData.Item def, ItemData.Ext? ext, int catFilter, int classFilter, int gradeFilter)
    {
        if (classFilter > 0)
        {
            if (def.Class != 0 && def.Class != classFilter) return false;
        }

        if (catFilter > 0)
        {
            bool matchCat = catFilter switch
            {
                1 => def.Slot >= 0 && def.Slot <= 4 && def.Countable == 0 && (def.Damage > 0 || def.Ac > 0),
                2 => def.Slot >= 5 && def.Slot <= 9,
                3 => def.Slot is 10 or 11 or 12 or 14,
                4 => def.Countable == 1 && (def.Name.Contains("Scroll", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Potion", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Water", StringComparison.OrdinalIgnoreCase)
                    || def.Id.ToString().StartsWith("800") || def.Id.ToString().StartsWith("3890") || def.Id.ToString().StartsWith("3791")),
                5 => def.Id is 379021000 or 379025000 or 700002000
                    || def.Name.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Trina", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Tears", StringComparison.OrdinalIgnoreCase),
                6 => def.Name.Contains("Gem", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Fragment", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                    || def.Name.Contains("Monster Stone", StringComparison.OrdinalIgnoreCase),
                7 => def.Race == ItemData.QuestItemRace || def.Id.ToString().StartsWith("900"),
                _ => true,
            };
            if (!matchCat) return false;
        }

        if (gradeFilter > 0)
        {
            int mor = ext?.MagicOrRare ?? def.Grade;
            bool matchGrade = gradeFilter switch
            {
                1 => mor is 0 or 1,
                2 => mor == 3,
                3 => mor == 4,
                4 => mor is 5 or ItemData.Rarity.Unique or ItemData.Rarity.ReverseUnique,
                5 => mor is 6 or ItemData.Rarity.Upgrade or ItemData.Rarity.Reverse,
                _ => true,
            };
            if (!matchGrade) return false;
        }

        return true;
    }

    public static void Match(
        string query, bool tradeableOnly, List<string> names, List<List<ItemSearchHit>> hits,
        int catFilter = 0, int classFilter = 0, int gradeFilter = 0)
    {
        names.Clear();
        hits.Clear();
        bool hasFilter = catFilter > 0 || classFilter > 0 || gradeFilter > 0;
        if (query.Length == 0 && !hasFilter) return;

        var byName = new Dictionary<string, List<ItemSearchHit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in ItemData.All())
        {
            if (def.Name.Length == 0) continue;
            if (query.Length > 0 && !def.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (tradeableOnly && !IsTradeable(def)) continue;
            if (!MatchesFilters(def, null, catFilter, classFilter, gradeFilter)) continue;
            Add(byName, new ItemSearchHit(def, null));
            foreach (var ext in VariantExts(def))
            {
                if (MatchesFilters(def, ext, catFilter, classFilter, gradeFilter))
                    Add(byName, new ItemSearchHit(def, ext));
            }
        }

        foreach (var ext in ItemData.AllExts())
        {
            if (ext.Linked <= 0 || ext.Name.Length == 0) continue;
            if (query.Length > 0 && !ext.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var def = ItemData.Get(ext.Linked);
            if (def == null || !ItemData.ExtAppliesTo(def, ext)) continue;
            if (tradeableOnly && !IsTradeable(def)) continue;
            if (!MatchesFilters(def, ext, catFilter, classFilter, gradeFilter)) continue;
            Add(byName, new ItemSearchHit(def, ext));
        }

        names.AddRange(byName.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names) hits.Add(byName[name]);
    }

    private static void Add(Dictionary<string, List<ItemSearchHit>> byName, ItemSearchHit hit)
    {
        if (hit.Family.Length == 0) return;
        if (hit.Plus > ItemData.MaxPlus(hit.Def, hit.Ext)) return;
        if (!byName.TryGetValue(hit.Family, out var list))
        {
            if (byName.Count >= NameCap) return;
            byName[hit.Family] = list = new List<ItemSearchHit>();
        }
        list.Add(hit);
    }
}
