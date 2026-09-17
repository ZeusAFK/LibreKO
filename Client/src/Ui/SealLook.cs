using Godot;
using LibreKO.Domain;

namespace LibreKO;

internal static class SealLook
{
    private static readonly Color SealedTint = new(0.50f, 0.62f, 1.00f);
    private static readonly Color BoundBorder = new(0.88f, 0.92f, 0.97f, 0.95f);

    private static readonly StyleBoxFlat BoundBox = UiTheme.Slot(BoundBorder);

    public static void Apply(ItemFlag state, TextureRect icon, PanelContainer cell, StyleBox normal)
    {
        icon.SelfModulate = state == ItemFlag.Sealed ? SealedTint : Colors.White;
        cell.AddThemeStyleboxOverride("panel", state == ItemFlag.Bound ? BoundBox : normal);
    }
}
