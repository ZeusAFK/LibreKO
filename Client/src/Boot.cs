using Godot;

namespace LibreKO;

public partial class Boot : Control
{
    public override void _Ready()
    {
        var cli = OS.GetCmdlineUserArgs();
        if (cli.Length >= 2 && cli[0] == "terraincheck")
        {
            TerrainSelfTest(cli[1]);
            return;
        }
        if (cli.Length >= 1 && cli[0] == "modelcheck")
        {
            ModelSelfTest();
            return;
        }

        Config.ApplyVideo();
        GameCursor.Enable();
        if (Platform.BundledContent && !Packs.ContentReady)
        {
            if (GetNodeOrNull<Control>("Center") is { } placeholder) placeholder.Visible = false;
            AddChild(new ContentDownloadScreen());
            return;
        }
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/Login.tscn");
    }

    private void TerrainSelfTest(string stem)
    {
        var hm = KoHeightmap.LoadPng(stem);
        var tex = KoGroundTex.LoadPng(stem);
        var objs = KoObjects.LoadJson(stem);
        bool ok = hm != null && hm.MapSize > 2 && tex != null && tex.TileCount > 0 && objs != null && objs.Names.Length > 0;
        GetTree().Quit(ok ? 0 : 1);
    }

    private void ModelSelfTest()
    {
        bool ok = true;
        ok &= Probe("players/body", "res://assets/characters/upc_el_rf.glb");
        ok &= Probe("players/face", "res://assets/characters/upc_el_rf_face00.glb");
        ok &= Probe("items/weapon", $"res://assets/items/weapon/{FirstIndexStem("res://assets/items/weapon/index.json")}.glb");
        ok &= Probe("mobs", $"res://assets/npcs/{FirstIndexStem("res://assets/npcs/index.json")}.glb");
        GetTree().Quit(ok ? 0 : 1);
    }

    private static bool Probe(string label, string path)
    {
        bool ok = ResourceLoader.Exists(path) && ResourceLoader.Load(path) is PackedScene;
        return ok;
    }

    private static string FirstIndexStem(string indexPath)
    {
        using var f = Godot.FileAccess.Open(indexPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return "_missing_index_";
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } d || d.Count == 0) return "_empty_index_";
        foreach (var k in d.Keys)
        {
            var v = d[k];
            if (v.VariantType == Variant.Type.String) return v.AsString();
            if (v.AsGodotDictionary() is { } o && o.TryGetValue("stem", out var s)) return s.AsString();
            break;
        }
        return "_no_stem_";
    }
}
