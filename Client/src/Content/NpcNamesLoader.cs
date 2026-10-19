using Godot;

namespace LibreKO.Content;

internal static class NpcNamesLoader
{
    private const string NpcTable = "res://data/tables/npc_names.res";
    private const string MonsterTable = "res://data/tables/monster_names.res";

    internal static void Load()
    {
        LoadNames(NpcTable, NpcNames.AddNpc);
        LoadNames(MonsterTable, NpcNames.AddMonster);
    }

    private static void LoadNames(string path, Action<int, string> add)
    {
        if (!ResourceLoader.Exists(path)) return;
        var table = ResourceLoader.Load<BakedTable>(path);
        if (table == null) return;
        foreach (var cell in table.Rows)
        {
            var row = cell.AsGodotArray();
            if (row.Count < 2) continue;
            add((int)row[0].AsInt64(), row[1].AsString());
        }
    }
}
