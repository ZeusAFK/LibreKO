using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class UiIcons
{
    private const string Root = "res://assets/ui/icons/";
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    private static readonly Dictionary<int, string> EquipmentSlots = new()
    {
        [0] = "game/earrings",
        [1] = "game/helmet",
        [2] = "game/earrings",
        [3] = "game/necklace",
        [4] = "game/chest",
        [6] = "game/main-hand",
        [7] = "game/belt",
        [8] = "game/off-hand",
        [9] = "game/ring",
        [10] = "game/legs",
        [11] = "game/ring",
        [12] = "game/gloves",
        [13] = "game/boots",
    };

    private const string EnvelopeSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"#fff\">" +
        "<path d=\"M224,48H32a8,8,0,0,0-8,8V192a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V56A8,8,0,0,0,224,48ZM203.43,64,128,133.15,52.57,64ZM216,192H40V74.19l82.59,75.71a8,8,0,0,0,10.82,0L216,74.19V192Z\"/>" +
        "</svg>";

    public static Texture2D? Get(string id)
    {
        if (Cache.TryGetValue(id, out Texture2D? cached)) return cached;
        string path = $"{Root}{id}.svg";
        Texture2D? texture = ResourceLoader.Exists(path)
            ? ResourceLoader.Load<Texture2D>(path)
            : null;

        if (texture == null && (id == "system/mail" || id == "system/mailbox"))
        {
            try
            {
                var img = Godot.Image.CreateEmpty(256, 256, false, Godot.Image.Format.Rgba8);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(EnvelopeSvg);
                if (img.LoadSvgFromBuffer(bytes) == Error.Ok)
                {
                    texture = ImageTexture.CreateFromImage(img);
                }
            }
            catch
            {
                // Fallback to scroll if SVG rasterization fails
                texture = Get("system/scroll");
            }
        }

        Cache[id] = texture;
        return texture;
    }

    public static Texture2D? Equipment(int absoluteSlot) =>
        EquipmentSlots.TryGetValue(absoluteSlot, out string? id) ? Get(id) : null;

    public static TextureRect Image(
        string id,
        Vector2 size,
        Color? color = null,
        string tooltip = "")
    {
        return new TextureRect
        {
            Texture = Get(id),
            CustomMinimumSize = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = string.IsNullOrEmpty(tooltip)
                ? Control.MouseFilterEnum.Ignore
                : Control.MouseFilterEnum.Stop,
            SelfModulate = color ?? Colors.White,
            TooltipText = tooltip,
        };
    }
}
