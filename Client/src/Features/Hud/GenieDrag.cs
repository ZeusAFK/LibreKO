using Godot;

namespace LibreKO.Features.Hud;

internal static class GenieDrag
{
    internal const string IdKey = "id";
    internal const string SkillSlotKey = "genieSkillSlot";
    internal const string ScrollSlotKey = "genieScrollSlot";
    private const float PreviewSize = 36f;

    internal static Variant Begin(Control source, string key, int slot, int id, Texture2D? icon, bool locked)
    {
        if (id == 0 || locked) return default;
        var preview = new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(PreviewSize, PreviewSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        source.SetDragPreview(preview);
        return new Godot.Collections.Dictionary { { IdKey, id }, { key, slot } };
    }

    internal static bool DragId(Variant data, out int id)
    {
        id = 0;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey(IdKey)) return false;
        id = d[IdKey].AsInt32();
        return id > 0;
    }

    internal static bool TryDragSlot(Variant data, string key, out int slot)
    {
        slot = -1;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey(key)) return false;
        slot = d[key].AsInt32();
        return slot >= 0;
    }
}
