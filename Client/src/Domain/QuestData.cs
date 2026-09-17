using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class QuestData
{
    public readonly struct KillGroup
    {
        public readonly int[] Npcs;
        public readonly int Count;
        public KillGroup(int[] npcs, int count) { Npcs = npcs; Count = count; }
    }

    public readonly struct Offer
    {
        public readonly int QuestId;
        public readonly int HelperIndex;
        public readonly int State;
        public Offer(int questId, int helperIndex, int state)
        { QuestId = questId; HelperIndex = helperIndex; State = state; }
    }

    public enum Kind { Story, Hunt, Delivery }

    public readonly record struct ItemStack(int ItemId, int Count);

    public const int CoinItemId = 900000000;
    public const int ExpItemId = 900001000;
    public const int LadderPointItemId = 900003000;
    public const int JobChangeItemId = 900006000;

    public static bool IsVirtualReward(int itemId) =>
        itemId is CoinItemId or ExpItemId or LadderPointItemId or JobChangeItemId;

    private const int KurianFamily = 5;
    private const int QuestStateActive = 1;
    private const int QuestStateFinished = 2;
    private const int QuestStateRunning = 3;
    private const int QuestTypeStartedIsEnough = 5;

    public readonly record struct Facts(
        int Level, int Class, int Nation, int Zone, int Exp, int Exchange, Kind Kind);

    private readonly struct Info
    {
        public readonly int Talk;
        public readonly int Level, Class, Nation, Zone, Exp, Exchange;
        public readonly KillGroup[] Groups;
        public readonly ItemStack[] Give, Need;
        public readonly Dictionary<int, int[]> Npcs;
        public readonly Dictionary<int, Dictionary<int, int>> Helpers;
        public Info(int talk, int level, int cls, int nation, int zone,
            int exp, int exchange, ItemStack[] give, ItemStack[] need,
            KillGroup[] groups, Dictionary<int, int[]> npcs, Dictionary<int, Dictionary<int, int>> helpers)
        {
            Talk = talk;
            Level = level; Class = cls; Nation = nation; Zone = zone;
            Exp = exp; Exchange = exchange; Give = give; Need = need;
            Groups = groups; Npcs = npcs; Helpers = helpers;
        }

        public Kind Kind => Groups.Length > 0 ? Kind.Hunt : Exchange != 0 ? Kind.Delivery : Kind.Story;
    }

    private readonly record struct MenuEntry(
        int HelperIndex, int QuestId, int Level, int Exp, int Class, int Nation, int Zone,
        int QuestType, int RequiredQuest, int Group);

    private static readonly Dictionary<int, Info> _quests = new();
    private static readonly Dictionary<int, List<MenuEntry>> _npcMenu = new();
    private static readonly HashSet<int> _questItems = new();
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (!Config.LegacyQuestFallback)
        {
            _quests.Clear();
            _npcMenu.Clear();
            _questItems.Clear();
            _loaded = false;
            return;
        }
        if (_loaded) return;
        _loaded = true;
        using var f = Godot.FileAccess.Open("res://assets/quests/quests.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[quest] missing quests.json (run tools/bake_quests.py)"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var root = parsed.AsGodotDictionary();

        if (root.TryGetValue("quests", out var qv) && qv.VariantType == Variant.Type.Dictionary)
        {
            var quests = qv.AsGodotDictionary();
            foreach (var k in quests.Keys)
            {
                if (!int.TryParse(k.AsString(), out int id)) continue;
                var q = quests[k].AsGodotDictionary();
                int talk = q.TryGetValue("talk", out var tlk) ? tlk.AsInt32() : 0;
                _quests[id] = new Info(
                    talk,
                    Int(q, "level"), Int(q, "class"), Int(q, "nation"), Int(q, "zone"),
                    Int(q, "exp"), Int(q, "exchange"),
                    ParseStacks(q, "give"), ParseStacks(q, "need"),
                    ParseGroups(q), ParseNpcs(q), ParseHelpers(q));
            }
        }

        if (root.TryGetValue("questItems", out var qi) && qi.VariantType == Variant.Type.Array)
            foreach (var id in qi.AsGodotArray()) _questItems.Add(id.AsInt32());

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
            if (entries.Count > 0) _npcMenu[npcId] = entries;
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

    private static Dictionary<int, Dictionary<int, int>> ParseHelpers(Godot.Collections.Dictionary q)
    {
        var result = new Dictionary<int, Dictionary<int, int>>();
        if (!q.TryGetValue("helpers", out var hv) || hv.VariantType != Variant.Type.Dictionary)
            return result;
        foreach (var pair in hv.AsGodotDictionary())
        {
            if (!int.TryParse(pair.Key.AsString(), out int state)
                || pair.Value.VariantType != Variant.Type.Dictionary) continue;
            var byNpc = new Dictionary<int, int>();
            foreach (var np in pair.Value.AsGodotDictionary())
                if (int.TryParse(np.Key.AsString(), out int npcId))
                    byNpc[npcId] = np.Value.AsInt32();
            result[state] = byNpc;
        }
        return result;
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

    public static string Objective(int questId, string selfName)
    {
        EnsureLoaded();
        if (!_quests.TryGetValue(questId, out var info)) return "";
        if (info.Talk > 0)
            return QuestText.Talk(info.Talk, selfName).Replace('|', '\n').Trim();
        return "";
    }

    public static string Name(int questId, string selfName)
    {
        string obj = Objective(questId, selfName);
        if (obj.Length > 0)
        {
            int nl = obj.IndexOf('\n');
            string first = (nl >= 0 ? obj.Substring(0, nl) : obj).Trim();
            if (first.Length > 44) first = first.Substring(0, 43).TrimEnd() + "…";
            if (first.Length > 0) return first;
        }
        return $"Quest #{questId}";
    }

    public static KillGroup[] Groups(int questId)
    {
        EnsureLoaded();
        return _quests.TryGetValue(questId, out var info) ? info.Groups : System.Array.Empty<KillGroup>();
    }

    public static Facts Get(int questId)
    {
        EnsureLoaded();
        if (!_quests.TryGetValue(questId, out var i))
            return new Facts(0, 0, 0, 0, 0, 0, Kind.Story);
        return new Facts(i.Level, i.Class, i.Nation, i.Zone, i.Exp, i.Exchange, i.Kind);
    }

    public static ItemStack[] Rewards(int questId)
    {
        EnsureLoaded();
        return _quests.TryGetValue(questId, out var i) ? i.Give : System.Array.Empty<ItemStack>();
    }

    public static ItemStack[] HandIns(int questId)
    {
        EnsureLoaded();
        return _quests.TryGetValue(questId, out var i) ? i.Need : System.Array.Empty<ItemStack>();
    }

    public static bool IsQuestItem(int itemId)
    {
        EnsureLoaded();
        return _questItems.Contains(itemId) || _questItems.Contains(itemId / 1000 * 1000);
    }

    public static int[] NpcsForState(int questId, int state)
    {
        EnsureLoaded();
        return _quests.TryGetValue(questId, out var info) && info.Npcs.TryGetValue(state, out var npcs)
            ? npcs
            : System.Array.Empty<int>();
    }

    public static int HelperForState(int questId, int state)
    {
        EnsureLoaded();
        if (!_quests.TryGetValue(questId, out var info)
            || !info.Helpers.TryGetValue(state, out var byNpc)) return -1;
        foreach (var pair in byNpc) return pair.Value;
        return -1;
    }

    public static List<int> Startable(System.Func<int, int> stateOf, int level, int classId, int nation)
    {
        EnsureLoaded();
        var result = new List<int>();
        foreach (var (questId, info) in _quests)
        {
            if (stateOf(questId) != 0) continue;
            if (!info.Npcs.TryGetValue(0, out var npcs) || npcs.Length == 0) continue;
            if (info.Level > 0 && level < info.Level) continue;
            if (!MatchesHelperClass(classId, info.Class)) continue;
            if (info.Nation is not (0 or 3) && info.Nation != nation) continue;
            result.Add(questId);
        }
        return result;
    }

    public static bool NpcForState(int questId, int state, int npcId)
    {
        EnsureLoaded();
        if (!_quests.TryGetValue(questId, out var info)
            || !info.Npcs.TryGetValue(state, out var npcs)) return false;
        return System.Array.IndexOf(npcs, npcId) >= 0;
    }

    // Ordered by Quest_Helper index — the order retail lists them in.
    public static List<Offer> OffersAtNpc(
        int npcId, System.Func<int, int> stateOf, int level, int classId, int nation, int zone)
    {
        EnsureLoaded();
        var offers = new List<Offer>();
        if (!_npcMenu.TryGetValue(npcId, out var entries)) return offers;

        foreach (var e in entries)
        {
            if (e.Level > 0 && level < e.Level) continue;
            if (!MatchesHelperClass(classId, e.Class)) continue;
            if (e.Nation is not (0 or 3) && e.Nation != nation) continue;
            if (e.Zone != 0 && zone != 0 && e.Zone != zone) continue;

            int state = stateOf(e.QuestId);
            if (state == QuestStateFinished) continue;

            if (e.RequiredQuest != 0)
            {
                int required = stateOf(e.RequiredQuest);
                bool met = e.QuestType == QuestTypeStartedIsEnough
                    ? required >= QuestStateActive
                    : required == QuestStateFinished;
                if (!met) continue;
            }

            if (e.Group != 0 && GroupBusyElsewhere(entries, e, stateOf)) continue;

            offers.Add(new Offer(e.QuestId, e.HelperIndex, state));
        }
        return offers;
    }

    private static bool GroupBusyElsewhere(
        List<MenuEntry> entries, MenuEntry entry, System.Func<int, int> stateOf)
    {
        foreach (var other in entries)
        {
            if (other.Group != entry.Group || other.QuestId == entry.QuestId) continue;
            int state = stateOf(other.QuestId);
            if (state is QuestStateActive or QuestStateRunning) return true;
        }
        return false;
    }

    private static bool MatchesHelperClass(int classId, int helperClass) =>
        CharacterClassCatalog.Family(classId) == KurianFamily || helperClass switch
    {
        0 or 5 => true,
        1 or 2 or 3 or 4 => CharacterClassCatalog.Family(classId) == helperClass,
        _ => classId % 100 == helperClass,
    };

    public static int[] StartingQuestsForNpc(int npcId)
    {
        EnsureLoaded();
        var result = new List<int>();
        foreach (var pair in _quests)
            if (pair.Value.Npcs.TryGetValue(0, out var npcs)
                && System.Array.IndexOf(npcs, npcId) >= 0)
                result.Add(pair.Key);
        return result.ToArray();
    }
}
