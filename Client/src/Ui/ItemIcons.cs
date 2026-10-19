using Godot;

namespace LibreKO;

public static class ItemIcons
{
    private static readonly Dictionary<int, Texture2D?> iconCache = Shutdown.Track(new Dictionary<int, Texture2D?>());

    public static Texture2D Get(int itemId)
    {
        int icon = ItemData.IconId(itemId);
        if (icon == 0) return Placeholder();
        if (iconCache.TryGetValue(icon, out var cached)) return cached ?? Placeholder();
        string path = $"res://assets/items/icons/{icon}.png";
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex != null) tex = CropToArt(tex);
        iconCache[icon] = tex;
        return tex ?? Placeholder();
    }

    private static Texture2D CropToArt(Texture2D tex)
    {
        Image? img;
        try { img = FxImages.Read(tex); } catch { return tex; }
        if (img == null) return tex;
        if (img.IsCompressed() && img.Decompress() != Error.Ok) return tex;

        var used = img.GetUsedRect();
        int iw = img.GetWidth(), ih = img.GetHeight();
        if (used.Size.X <= 0 || used.Size.Y <= 0) return tex;
        if (used.Size.X >= iw - 2 && used.Size.Y >= ih - 2) return tex;

        // KO art sits flush in the cell's top-left, so the pad has to fit the room EVERY side has.
        int room = Mathf.Min(
            Mathf.Min(used.Position.X, used.Position.Y),
            Mathf.Min(iw - used.End.X, ih - used.End.Y));
        int pad = Mathf.Min(Mathf.RoundToInt(Mathf.Max(used.Size.X, used.Size.Y) * 0.07f), Mathf.Max(0, room));
        int side = Mathf.Min(Mathf.Max(used.Size.X, used.Size.Y) + pad * 2, Mathf.Min(iw, ih));
        var center = used.Position + used.Size / 2;
        var sq = new Rect2I(
            Mathf.Clamp(center.X - side / 2, 0, iw - side),
            Mathf.Clamp(center.Y - side / 2, 0, ih - side),
            side, side);
        if (sq.Size.X <= 0 || sq.Size.Y <= 0) return tex;
        return new AtlasTexture { Atlas = tex, Region = new Rect2(sq.Position, sq.Size), FilterClip = true };
    }

    private static Texture2D? placeholder;

    private static Texture2D Placeholder()
    {
        if (placeholder != null) return placeholder;
        const int s = 64;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var bg = new Color(0.15f, 0.16f, 0.19f);
        var border = new Color(0.40f, 0.42f, 0.48f);
        var fg = new Color(0.58f, 0.60f, 0.66f);
        img.Fill(bg);
        for (int i = 0; i < s; i++)
        {
            img.SetPixel(i, 0, border); img.SetPixel(i, s - 1, border);
            img.SetPixel(0, i, border); img.SetPixel(s - 1, i, border);
        }
        byte[] glyph = { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100 };
        const int gw = 5, gh = 7, scale = 6;
        int ox = (s - gw * scale) / 2, oy = (s - gh * scale) / 2;
        for (int r = 0; r < gh; r++)
            for (int c = 0; c < gw; c++)
                if ((glyph[r] >> (gw - 1 - c) & 1) != 0)
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            img.SetPixel(ox + c * scale + dx, oy + r * scale + dy, fg);
        placeholder = ImageTexture.CreateFromImage(img);
        return placeholder;
    }
}
