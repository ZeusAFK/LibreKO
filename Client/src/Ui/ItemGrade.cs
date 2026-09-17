using Godot;
using LibreKO.Domain;

namespace LibreKO;

public static class ItemGrade
{
    public static int ColorIndex(int rarity) => rarity switch
    {
        ItemData.Rarity.Unique => Config.RarityNameUnique,
        ItemData.Rarity.Upgrade => Config.RarityNameUpgrade,
        ItemData.Rarity.Reverse => Config.RarityNameReverse,
        ItemData.Rarity.ReverseUnique => Config.RarityNameReverseUnique,
        _ => Config.RarityNameRegular,
    };

    public static Color Tint(int itemId) =>
        Config.TooltipColor(ColorIndex(ItemData.ExtFor(itemId)?.MagicOrRare ?? -1));
}

public sealed partial class UpgradeBadge : Label
{
    private const int FontSize = 11;

    public UpgradeBadge()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        SizeFlagsHorizontal = SizeFlagsVertical = SizeFlags.Fill;
        AddThemeFontSizeOverride("font_size", FontSize);
        AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        AddThemeConstantOverride("outline_size", 3);
        AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.92f));
        AddThemeConstantOverride("shadow_offset_x", 1);
        AddThemeConstantOverride("shadow_offset_y", 1);
        AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginTop = 1 });
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Set(int itemId)
    {
        int plus = ItemData.UpgradeLevel(itemId);
        if (plus <= 0) { Clear(); return; }
        Text = $"+{plus}";
        AddThemeColorOverride("font_color", ItemGrade.Tint(itemId));
        Visible = true;
    }

    public void Clear()
    {
        Text = "";
        Visible = false;
    }

    public static UpgradeBadge Attach(Control parent)
    {
        var badge = new UpgradeBadge();
        parent.AddChild(badge);
        return badge;
    }

    public static UpgradeBadge? Show(Control parent, int itemId)
    {
        if (ItemData.UpgradeLevel(itemId) <= 0) return null;
        var badge = Attach(parent);
        badge.Set(itemId);
        return badge;
    }
}
