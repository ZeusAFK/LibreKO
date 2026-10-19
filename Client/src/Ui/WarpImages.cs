using Godot;

namespace LibreKO;

public static class WarpImages
{
    private static readonly Dictionary<string, Texture2D?> images = Shutdown.Track(new Dictionary<string, Texture2D?>());

    public static Texture2D? Get(string stem)
    {
        if (stem.Length == 0) return null;
        if (images.TryGetValue(stem, out var cached)) return cached;
        var tex = ResourceLoader.Load<Texture2D>($"res://assets/ui/warp/{stem}.png");
        images[stem] = tex;
        return tex;
    }
}
