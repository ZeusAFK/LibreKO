namespace LibreKO.Domain;

public static class SkillMsgOut
{
    private static readonly Dictionary<int, string> cast = new();
    private static readonly Dictionary<int, string> effect = new();
    private static bool loaded;

    internal static Action? Loader { get; set; }

    public static string? Cast(int skillId) => Lookup(cast, skillId);

    public static string? Effect(int skillId) => Lookup(effect, skillId);

    internal static void AddCast(int skillId, string text) => cast[skillId] = text;

    internal static void AddEffect(int skillId, string text) => effect[skillId] = text;

    private static string? Lookup(Dictionary<int, string> table, int skillId)
    {
        EnsureLoaded();
        return table.TryGetValue(skillId, out var text) ? text : null;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Loader?.Invoke();
    }
}
