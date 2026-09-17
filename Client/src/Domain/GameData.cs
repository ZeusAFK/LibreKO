using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public partial class GameData : Node
{
    public static GameData I { get; private set; } = null!;

    private readonly Dictionary<int, string> _npcNames = new();
    private readonly Dictionary<int, string> _mobNames = new();

    public override void _Ready()
    {
        I = this;
        LoadNames("res://data/tables/NPC_us.res", _npcNames);
        LoadNames("res://data/tables/mob_us.res", _mobNames);
    }

    public string NpcName(int npcId, bool isMonster)
    {
        var map = isMonster ? _mobNames : _npcNames;
        if (map.TryGetValue(npcId, out var n) && n.Length > 0)
            return n;
        return (isMonster ? "Mob #" : "NPC #") + npcId;
    }

    private static void LoadNames(string path, Dictionary<int, string> into)
    {
        if (!ResourceLoader.Exists(path)) return;
        var table = ResourceLoader.Load<KoTable>(path);
        if (table == null) return;
        foreach (var cell in table.Rows)
        {
            var row = cell.AsGodotArray();
            if (row.Count < 2) continue;
            into[(int)row[0].AsInt64()] = row[1].AsString();
        }
    }
}
