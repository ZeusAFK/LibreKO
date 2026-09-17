using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class QuestText
{
    private static readonly Dictionary<int, string> _menu = new();
    private static readonly Dictionary<int, string> _talk = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        using var f = Godot.FileAccess.Open("res://assets/quests/quest_text.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[quest] missing quest_text.json (run tools/bake_quest_text.py)"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        LoadInto(d, "menu", _menu);
        LoadInto(d, "talk", _talk);
    }

    private static void LoadInto(Godot.Collections.Dictionary root, string key, Dictionary<int, string> into)
    {
        if (!root.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Dictionary) return;
        var map = v.AsGodotDictionary();
        foreach (var k in map.Keys)
            if (int.TryParse(k.AsString(), out int id))
                into[id] = map[k].AsString();
    }

    public static string Menu(int id)
    {
        EnsureLoaded();
        return id >= 0 && _menu.TryGetValue(id, out var s) ? s : "";
    }

    public static string Talk(int id, string selfName)
    {
        EnsureLoaded();
        if (id < 0 || !_talk.TryGetValue(id, out var s) || s.Length == 0) return "";
        return Substitute(s, selfName);
    }

    private static string Substitute(string s, string selfName)
    {
        if (s.Contains("<selfname>") && selfName.Length > 0)
            s = s.Replace("<selfname>", selfName);
        return s;
    }
}
