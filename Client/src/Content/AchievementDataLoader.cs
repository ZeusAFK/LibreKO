using Godot;

namespace LibreKO.Content;

internal static class AchievementDataLoader
{
    internal static void Load()
    {
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

            AchievementData.Add(id, new AchievementData.Info(
                (AchievementTab)Read(row, "tab"),
                Read(row, "group"),
                Read(row, "points"),
                row.ContainsKey("name") ? row["name"].AsString() : string.Empty,
                row.ContainsKey("objective") ? row["objective"].AsString() : string.Empty,
                Read(row, "item"),
                Read(row, "count"),
                Read(row, "title")));
        }

        LoadTitles();

        GD.Print($"[achievements] {AchievementData.DefinitionCount} definitions");
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

            AchievementData.AddTitle(id, new AchievementData.Title(
                row.ContainsKey("name") ? row["name"].AsString() : string.Empty,
                Read(row, "achievement"),
                DescribeBonus(row)));
        }
    }

    private static string DescribeBonus(Godot.Collections.Dictionary row)
    {
        if (!row.ContainsKey("bonus")) return string.Empty;
        if (row["bonus"].Obj is not Godot.Collections.Dictionary bonus) return string.Empty;

        var parts = new List<string>();
        foreach (var key in bonus.Keys)
            parts.Add(AchievementData.BonusLine(key.AsString(), (int)bonus[key]));
        return string.Join(", ", parts);
    }

    private static int Read(Godot.Collections.Dictionary row, string key) =>
        row.ContainsKey(key) ? (int)row[key] : 0;
}
