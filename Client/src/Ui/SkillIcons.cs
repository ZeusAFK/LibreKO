using Godot;

namespace LibreKO;

public static class SkillIcons
{
    private static readonly Dictionary<int, Texture2D?> iconCache = Shutdown.Track(new Dictionary<int, Texture2D?>());
    private static Texture2D? enigma;
    private static bool enigmaTried;

    public static Texture2D? Enigma()
    {
        if (enigmaTried) return enigma;
        enigmaTried = true;
        const string path = "res://assets/skills/icons/enigma.png";
        enigma = Godot.ResourceLoader.Exists(path) ? Godot.ResourceLoader.Load<Texture2D>(path) : null;
        return enigma;
    }

    public static Texture2D? Get(int skillId)
    {
        if (iconCache.TryGetValue(skillId, out var cached)) return cached;
        string path = $"res://assets/skills/icons/{skillId}.png";
        Texture2D? tex = Godot.ResourceLoader.Exists(path) ? Godot.ResourceLoader.Load<Texture2D>(path) : null;
        iconCache[skillId] = tex;
        return tex;
    }
}
