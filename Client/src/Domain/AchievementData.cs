using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public enum AchievementTab
{
    Normal = 0,
    Quest = 1,
    War = 2,
    Adventure = 3,
    Challenge = 4,
}

public static class AchievementData
{
    public static readonly AchievementTab[] Tabs =
    {
        AchievementTab.Normal, AchievementTab.Quest, AchievementTab.War,
        AchievementTab.Adventure, AchievementTab.Challenge,
    };

    public readonly record struct Info(
        AchievementTab Tab, int Group, int Points, string Name, string Objective,
        int ItemId, int ItemCount, int TitleId);

    public readonly record struct Title(string Name, int AchievementId, string Bonus);

    private static readonly Dictionary<int, Info> Table = new();
    private static readonly Dictionary<int, Title> Titles = new();
    private static bool _loaded;

    public static string TabName(AchievementTab tab) => tab switch
    {
        AchievementTab.Normal => "Normal",
        AchievementTab.Quest => "Quest",
        AchievementTab.War => "War",
        AchievementTab.Adventure => "Adventure",
        AchievementTab.Challenge => "Challenge",
        _ => $"Tab {(int)tab}",
    };

    public static Info? Get(int achievementId)
    {
        EnsureLoaded();
        return Table.TryGetValue(achievementId, out var info) ? info : null;
    }

    public static string NameOf(int achievementId) =>
        Get(achievementId)?.Name ?? $"Achievement {achievementId}";

    public static int Count
    {
        get { EnsureLoaded(); return Table.Count; }
    }

    public static IEnumerable<int> Ids
    {
        get { EnsureLoaded(); return Table.Keys; }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        using var file = Godot.FileAccess.Open(
            "res://assets/achievements/achievements.json", Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning("[achievements] achievements.json missing — the window will show ids only");
            return;
        }

        if (Json.ParseString(file.GetAsText()).Obj is not Godot.Collections.Dictionary root) return;

        foreach (var key in root.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            if (root[key].Obj is not Godot.Collections.Dictionary row) continue;

            Table[id] = new Info(
                (AchievementTab)Read(row, "tab"),
                Read(row, "group"),
                Read(row, "points"),
                row.ContainsKey("name") ? row["name"].AsString() : string.Empty,
                row.ContainsKey("objective") ? row["objective"].AsString() : string.Empty,
                Read(row, "item"),
                Read(row, "count"),
                Read(row, "title"));
        }

        LoadTitles();

        GD.Print($"[achievements] {Table.Count} definitions");
    }

    public static Title? TitleOf(int titleId)
    {
        EnsureLoaded();
        return Titles.TryGetValue(titleId, out var title) ? title : null;
    }

    private static void LoadTitles()
    {
        using var file = Godot.FileAccess.Open(
            "res://assets/achievements/titles.json", Godot.FileAccess.ModeFlags.Read);
        if (file == null) return;
        if (Json.ParseString(file.GetAsText()).Obj is not Godot.Collections.Dictionary root) return;

        foreach (var key in root.Keys)
        {
            if (!int.TryParse(key.AsString(), out int id)) continue;
            if (root[key].Obj is not Godot.Collections.Dictionary row) continue;

            Titles[id] = new Title(
                row.ContainsKey("name") ? row["name"].AsString() : string.Empty,
                Read(row, "achievement"),
                DescribeBonus(row));
        }
    }

    private static string DescribeBonus(Godot.Collections.Dictionary row)
    {
        if (!row.ContainsKey("bonus")) return string.Empty;
        if (row["bonus"].Obj is not Godot.Collections.Dictionary bonus) return string.Empty;

        var parts = new List<string>();
        foreach (var key in bonus.Keys)
        {
            string name = BonusLabel(key.AsString());
            int value = (int)bonus[key];
            parts.Add(value >= 0 ? $"+{value} {name}" : $"{value} {name}");
        }
        return string.Join(", ", parts);
    }

    private static string BonusLabel(string key) => key switch
    {
        "str" => "力量", "hp" => "体力", "dex" => "敏捷", "int" => "智力", "mp" => "魔力",
        "attack" => "攻击力", "defence" => "防御力",
        "loyalty" => "贡献", "exp" => "经验",
        "fire" => "火焰", "ice" => "寒冰", "light" => "闪电",
        "r_fire" => "火焰抗性", "r_ice" => "寒冰抗性", "r_light" => "闪电抗性",
        "r_magic" => "魔法抗性", "r_curse" => "诅咒抗性", "r_poison" => "剧毒抗性",
        _ => key.StartsWith("ac_") ? WeaponAcLabel(key[3..]) : key,
    };

    private static string WeaponAcLabel(string w) => w switch
    {
        "shortsword" => "短剑防御", "sword" => "剑防御", "axe" => "斧防御",
        "blow" => "钝器防御", "spear" => "枪防御", "jamadar" => "战锤防御",
        "dagger" => "匕首防御", "bow" => "弓防御", "arrow" => "箭防御",
        _ => w + "防御",
    };

    private static int Read(Godot.Collections.Dictionary row, string key) =>
        row.ContainsKey(key) ? (int)row[key] : 0;
}
