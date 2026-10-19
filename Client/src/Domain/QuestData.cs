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

    public readonly record struct ClassReward(int HelperClass, ItemStack[] Give);

    public readonly record struct Facts(
        int Level, int Class, int Nation, int Zone, int Exp, int Exchange, Kind Kind);

    internal readonly struct Info
    {
        public readonly int Talk;
        public readonly int Level, Class, Nation, Zone, Exp, Exchange;
        public readonly KillGroup[] Groups;
        public readonly ItemStack[] Give, Need;
        public readonly Dictionary<int, int[]> Npcs;
        public readonly int[] Classes;
        public readonly ClassReward[] GiveByClass;
        public Info(int talk, int level, int cls, int nation, int zone,
            int exp, int exchange, ItemStack[] give, ItemStack[] need,
            KillGroup[] groups, Dictionary<int, int[]> npcs,
            int[] classes, ClassReward[] giveByClass)
        {
            Talk = talk;
            Level = level; Class = cls; Nation = nation; Zone = zone;
            Exp = exp; Exchange = exchange; Give = give; Need = need;
            Groups = groups; Npcs = npcs;
            Classes = classes; GiveByClass = giveByClass;
        }

        public Kind Kind => Groups.Length > 0 ? Kind.Hunt : Exchange != 0 ? Kind.Delivery : Kind.Story;
    }

    internal readonly record struct MenuEntry(
        int HelperIndex, int QuestId, int Level, int Exp, int Class, int Nation, int Zone,
        int QuestType, int RequiredQuest, int Group);

    private static readonly Dictionary<int, Info> quests = new();
    private static readonly Dictionary<int, List<MenuEntry>> npcMenu = new();
    private static bool loaded;

    private static void EnsureLoaded()
    {
        if (!Config.LegacyQuestFallback)
        {
            quests.Clear();
            npcMenu.Clear();
            loaded = false;
            return;
        }
        if (loaded) return;
        loaded = true;
        Loader?.Invoke();
    }

    internal static Action? Loader { get; set; }

    internal static void AddQuest(int questId, Info info) => quests[questId] = info;

    internal static void SetNpcMenu(int npcId, List<MenuEntry> entries) => npcMenu[npcId] = entries;

    public static string Objective(int questId, string selfName)
    {
        EnsureLoaded();
        if (!quests.TryGetValue(questId, out var info)) return "";
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
        return quests.TryGetValue(questId, out var info) ? info.Groups : System.Array.Empty<KillGroup>();
    }

    public static Facts Get(int questId)
    {
        EnsureLoaded();
        if (!quests.TryGetValue(questId, out var i))
            return new Facts(0, 0, 0, 0, 0, 0, Kind.Story);
        return new Facts(i.Level, i.Class, i.Nation, i.Zone, i.Exp, i.Exchange, i.Kind);
    }

    public static ItemStack[] Rewards(int questId, int classId)
    {
        EnsureLoaded();
        return quests.TryGetValue(questId, out var i)
            ? RewardsForClass(i.GiveByClass, i.Give, classId)
            : System.Array.Empty<ItemStack>();
    }

    public static ItemStack[] RewardsForClass(ClassReward[] byClass, ItemStack[] fallback, int classId)
    {
        foreach (var reward in byClass)
            if (MatchesHelperClass(classId, reward.HelperClass))
                return reward.Give;
        return fallback;
    }

    public static bool OfferedToClass(int[] helperClasses, int primaryClass, int classId)
    {
        if (helperClasses.Length == 0) return MatchesHelperClass(classId, primaryClass);
        foreach (int helperClass in helperClasses)
            if (MatchesHelperClass(classId, helperClass))
                return true;
        return false;
    }

    public static ItemStack[] HandIns(int questId)
    {
        EnsureLoaded();
        return quests.TryGetValue(questId, out var i) ? i.Need : System.Array.Empty<ItemStack>();
    }

    public static int[] NpcsForState(int questId, int state)
    {
        EnsureLoaded();
        return quests.TryGetValue(questId, out var info) && info.Npcs.TryGetValue(state, out var npcs)
            ? npcs
            : System.Array.Empty<int>();
    }

    public static List<int> Startable(System.Func<int, int> stateOf, int level, int classId, int nation)
    {
        EnsureLoaded();
        var result = new List<int>();
        foreach (var (questId, info) in quests)
        {
            if (stateOf(questId) != 0) continue;
            if (!info.Npcs.TryGetValue(0, out var npcs) || npcs.Length == 0) continue;
            if (info.Level > 0 && level < info.Level) continue;
            if (!OfferedToClass(info.Classes, info.Class, classId)) continue;
            if (info.Nation is not (0 or 3) && info.Nation != nation) continue;
            result.Add(questId);
        }
        return result;
    }

    public static bool NpcForState(int questId, int state, int npcId)
    {
        EnsureLoaded();
        if (!quests.TryGetValue(questId, out var info)
            || !info.Npcs.TryGetValue(state, out var npcs)) return false;
        return System.Array.IndexOf(npcs, npcId) >= 0;
    }

    // Ordered by Quest_Helper index — the order retail lists them in.
    public static List<Offer> OffersAtNpc(
        int npcId, System.Func<int, int> stateOf, int level, int classId, int nation, int zone)
    {
        EnsureLoaded();
        var offers = new List<Offer>();
        if (!npcMenu.TryGetValue(npcId, out var entries)) return offers;

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
        foreach (var pair in quests)
            if (pair.Value.Npcs.TryGetValue(0, out var npcs)
                && System.Array.IndexOf(npcs, npcId) >= 0)
                result.Add(pair.Key);
        return result.ToArray();
    }
}
