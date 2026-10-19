using Godot;
using static LibreKO.Domain.QuestData;

namespace LibreKO.Content;

internal static class QuestDataLoader
{
    internal static void Load()
    {
        using var f = Godot.FileAccess.Open("res://assets/quests/quests.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[quest] missing quests.json (run tools/bake_quests.py)"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var root = parsed.AsGodotDictionary();

        if (root.TryGetValue("quests", out var qv) && qv.VariantType == Variant.Type.Dictionary)
        {
            var rawQuests = qv.AsGodotDictionary();
            foreach (var k in rawQuests.Keys)
            {
                if (!int.TryParse(k.AsString(), out int id)) continue;
                var q = rawQuests[k].AsGodotDictionary();
                int talk = q.TryGetValue("talk", out var tlk) ? tlk.AsInt32() : 0;
                AddQuest(id, new Info(
                    talk,
                    Int(q, "level"), Int(q, "class"), Int(q, "nation"), Int(q, "zone"),
                    Int(q, "exp"), Int(q, "exchange"),
                    ParseStacks(q, "give"), ParseStacks(q, "need"),
                    ParseGroups(q), ParseNpcs(q),
                    ParseClasses(q), ParseClassRewards(q)));
            }
        }

        LoadNpcMenu(root);
    }

    private static void LoadNpcMenu(Godot.Collections.Dictionary root)
    {
        if (!root.TryGetValue("npcMenu", out var mv) || mv.VariantType != Variant.Type.Dictionary) return;
        var byNpc = mv.AsGodotDictionary();
        foreach (var key in byNpc.Keys)
        {
            if (!int.TryParse(key.AsString(), out int npcId)) continue;
            var arr = byNpc[key].AsGodotArray();
            var entries = new List<MenuEntry>(arr.Count);
            foreach (var item in arr)
            {
                var e = item.AsGodotDictionary();
                entries.Add(new MenuEntry(
                    Int(e, "h"), Int(e, "q"), Int(e, "level"), Int(e, "exp"),
                    Int(e, "class"), Int(e, "nation"), Int(e, "zone"),
                    Int(e, "type"), Int(e, "req"), Int(e, "grp")));
            }
            if (entries.Count > 0) SetNpcMenu(npcId, entries);
        }
    }

    private static ItemStack[] ParseStacks(Godot.Collections.Dictionary q, string key)
    {
        if (!q.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array)
            return System.Array.Empty<ItemStack>();
        var arr = v.AsGodotArray();
        var stacks = new List<ItemStack>(arr.Count);
        foreach (var entry in arr)
        {
            if (entry.VariantType != Variant.Type.Array) continue;
            var pair = entry.AsGodotArray();
            if (pair.Count < 2) continue;
            stacks.Add(new ItemStack(pair[0].AsInt32(), pair[1].AsInt32()));
        }
        return stacks.ToArray();
    }

    private static int Int(Godot.Collections.Dictionary q, string key) =>
        q.TryGetValue(key, out var v) ? v.AsInt32() : 0;

    private static int[] ParseClasses(Godot.Collections.Dictionary q)
    {
        if (!q.TryGetValue("classes", out var v) || v.VariantType != Variant.Type.Array)
            return System.Array.Empty<int>();
        var arr = v.AsGodotArray();
        var classes = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++) classes[i] = arr[i].AsInt32();
        return classes;
    }

    private static ClassReward[] ParseClassRewards(Godot.Collections.Dictionary q)
    {
        if (!q.TryGetValue("giveByClass", out var v) || v.VariantType != Variant.Type.Dictionary)
            return System.Array.Empty<ClassReward>();
        var result = new List<ClassReward>();
        var byClass = v.AsGodotDictionary();
        foreach (var key in byClass.Keys)
        {
            if (!int.TryParse(key.AsString(), out int helperClass)) continue;
            var holder = new Godot.Collections.Dictionary { ["give"] = byClass[key] };
            result.Add(new ClassReward(helperClass, ParseStacks(holder, "give")));
        }
        result.Sort((a, b) => a.HelperClass.CompareTo(b.HelperClass));
        return result.ToArray();
    }

    private static Dictionary<int, int[]> ParseNpcs(Godot.Collections.Dictionary q)
    {
        var result = new Dictionary<int, int[]>();
        if (!q.TryGetValue("npcs", out var nv) || nv.VariantType != Variant.Type.Dictionary)
            return result;
        foreach (var pair in nv.AsGodotDictionary())
        {
            if (!int.TryParse(pair.Key.AsString(), out int state)
                || pair.Value.VariantType != Variant.Type.Array) continue;
            var ids = new List<int>();
            foreach (var id in pair.Value.AsGodotArray()) ids.Add(id.AsInt32());
            result[state] = ids.ToArray();
        }
        return result;
    }

    private static KillGroup[] ParseGroups(Godot.Collections.Dictionary q)
    {
        if (!q.TryGetValue("groups", out var gv) || gv.VariantType != Variant.Type.Array)
            return System.Array.Empty<KillGroup>();
        var arr = gv.AsGodotArray();
        var groups = new List<KillGroup>(arr.Count);
        foreach (var item in arr)
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var g = item.AsGodotDictionary();
            int count = g.TryGetValue("count", out var cv) ? cv.AsInt32() : 0;
            var npcs = new List<int>();
            if (g.TryGetValue("npcs", out var nv) && nv.VariantType == Variant.Type.Array)
                foreach (var n in nv.AsGodotArray()) npcs.Add(n.AsInt32());
            if (npcs.Count > 0 && count > 0) groups.Add(new KillGroup(npcs.ToArray(), count));
        }
        return groups.ToArray();
    }
}
