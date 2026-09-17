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

    public const int NameCap = 60;
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

    public static void Match(
        string query, bool tradeableOnly, List<string> names, List<List<ItemSearchHit>> hits)
    {
        names.Clear();
        hits.Clear();
        if (query.Length == 0) return;

        var byName = new Dictionary<string, List<ItemSearchHit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in ItemData.All())
        {
            if (def.Name.Length == 0) continue;
            if (!def.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (tradeableOnly && !IsTradeable(def)) continue;
            Add(byName, new ItemSearchHit(def, null));
            foreach (var ext in VariantExts(def))
                Add(byName, new ItemSearchHit(def, ext));
        }

        foreach (var ext in ItemData.AllExts())
        {
            if (ext.Linked <= 0 || ext.Name.Length == 0) continue;
            if (!ext.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var def = ItemData.Get(ext.Linked);
            if (def == null || !ItemData.ExtAppliesTo(def, ext)) continue;
            if (tradeableOnly && !IsTradeable(def)) continue;
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
