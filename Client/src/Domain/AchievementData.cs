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
    private static bool loaded;

    public static string TabName(AchievementTab tab) => tab switch
    {
        AchievementTab.Normal => "Normal",
        AchievementTab.Quest => "Quest",
        AchievementTab.War => "War",
        AchievementTab.Adventure => "Adventure",
        AchievementTab.Challenge => "Challenge",
        _ => $"Tab {(int)tab}",
    };

    public const int AllGroups = -1;
    public const int GroupsPerTab = 10;
    private const int CategoryTextBase = 37000;
    private const int CategoryTextCount = 50;
    private const int CategoryTextFallback = 37050;

    public static AchievementTab TabOfGroup(int group) => (AchievementTab)(group / GroupsPerTab);

    public static List<int> CategoriesOf(AchievementTab tab)
    {
        EnsureLoaded();
        var groups = new SortedSet<int>();
        foreach (var info in Table.Values)
            if (info.Group >= 0 && TabOfGroup(info.Group) == tab) groups.Add(info.Group);
        return new List<int>(groups);
    }

    public static string CategoryName(int group)
    {
        int textId = group is >= 0 and < CategoryTextCount ? CategoryTextBase + group : CategoryTextFallback;
        return ItemData.Text(textId, $"Group {group}");
    }

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

    internal static Action? Loader { get; set; }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Loader?.Invoke();
    }

    internal static void Add(int achievementId, Info info) => Table[achievementId] = info;

    internal static void AddTitle(int titleId, Title title) => Titles[titleId] = title;

    internal static int DefinitionCount => Table.Count;

    public static Title? TitleOf(int titleId)
    {
        EnsureLoaded();
        return Titles.TryGetValue(titleId, out var title) ? title : null;
    }

    private const int BonusTextFirst = 37201;

    private static readonly (string Key, string Fallback)[] Bonuses =
    {
        ("str", "Strength: %d"), ("hp", "Health: %d"), ("dex", "Dexterity: %d"),
        ("int", "Intelligence: %d"), ("mp", "Magic Power: %d"), ("attack", "Attack: %d"),
        ("defence", "Defense: %d"), ("loyalty", "Contribution: %d (Restrictions apply)"),
        ("exp", "Base EXP for Monster Kills +%d%%"),
        ("ac_shortsword", "Dagger Defense: %d"), ("ac_jamadar", "Jamadar Defense: %d"),
        ("ac_sword", "Sword Defense: %d"), ("ac_blow", "Club Defense: %d"), ("ac_axe", "Axe Defense: %d"),
        ("ac_spear", "Spear Defense: %d"), ("ac_arrow", "Arrow Defense: %d"),
        ("fire", "Flame Bonus: %d"), ("ice", "Ice Bonus: %d"), ("light", "Electric Shock Bonus: %d"),
        ("r_fire", "Flame Resistence: %d"), ("r_ice", "Ice Resistence: %d"),
        ("r_light", "Electric Shock Resistence: %d"), ("r_magic", "Magic Resistence: %d"),
        ("r_curse", "Spell Resistence: %d"), ("r_poison", "Poison Resistence: %d"),
    };

    internal static string BonusLine(string key, int value)
    {
        int index = System.Array.FindIndex(Bonuses, bonus => bonus.Key == key);
        if (index < 0) return $"{key}: {value}";
        return ItemData.Text(BonusTextFirst + index, Bonuses[index].Fallback)
            .Replace("%d", value.ToString())
            .Replace("%%", "%");
    }
}
