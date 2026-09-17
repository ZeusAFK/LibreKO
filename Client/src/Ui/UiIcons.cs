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

    public static Texture2D? Get(string id)
    {
        if (Cache.TryGetValue(id, out Texture2D? cached)) return cached;
        string path = $"{Root}{id}.svg";
        Texture2D? texture = ResourceLoader.Exists(path)
            ? ResourceLoader.Load<Texture2D>(path)
            : null;
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
