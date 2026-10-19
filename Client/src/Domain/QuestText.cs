namespace LibreKO.Domain;

public static class QuestText
{
    private static readonly Dictionary<int, string> menu = new();
    private static readonly Dictionary<int, string> talk = new();
    private static bool loaded;

    internal static Action? Loader { get; set; }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Loader?.Invoke();
    }

    internal static void AddMenu(int id, string text) => menu[id] = text;

    internal static void AddTalk(int id, string text) => talk[id] = text;

    public static string Menu(int id)
    {
        EnsureLoaded();
        return id >= 0 && menu.TryGetValue(id, out var s) ? s : "";
    }

    public static string Talk(int id, string selfName)
    {
        EnsureLoaded();
        if (id < 0 || !talk.TryGetValue(id, out var s) || s.Length == 0) return "";
        return Substitute(s, selfName);
    }

    private static string Substitute(string s, string selfName)
    {
        if (s.Contains("<selfname>") && selfName.Length > 0)
            s = s.Replace("<selfname>", selfName);
        return s;
    }
}
