using Godot;

namespace LibreKO.Features.Windows;

internal static class AdminPanelUi
{
    internal const float LabelWidth = 58;
    internal const float SpinWidth = 110;
    internal const float ColumnGap = 32;
    internal const float ControlHeight = 30;
    internal const float TabWidth = 640;

    internal static HBoxContainer Heading(string text, string icon, int size = 15)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        row.AddChild(UiIcons.Image(icon, new Vector2(size + 3, size + 3), UiTheme.GoldVivid));
        var label = UiTheme.Text(text, size, UiTheme.GoldVivid);
        label.AddThemeFontOverride("font", UiTheme.Strong);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        return row;
    }

    internal static Label FieldLabel(string text, float width = LabelWidth)
    {
        var label = UiTheme.Text(text, 15, UiTheme.TextHi);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    internal static SpinBox Spin(int min, int max, float width) =>
        UiTheme.NumberBox(min, max, 1, width, 14);

    internal static Button Button(string text, float minWidth = 0)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(minWidth, ControlHeight),
        };
        button.AddThemeFontSizeOverride("font_size", 15);
        return button;
    }

    internal static string ZoneName(int zoneId)
    {
        foreach (var zone in ZoneCatalog.All)
            if (zone.Id == zoneId) return zone.Name;
        return $"zone {zoneId}";
    }
}
