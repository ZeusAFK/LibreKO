using Godot;

namespace LibreKO;

public static class HudToolButton
{
    public const int FontSize = 11;
    public const int ToolMargin = 4;
    private const float Width = 24f;
    private const float IconSize = 13f;
    private static readonly Color Text = new("#d4d5d7");
    private static readonly Color TextPressed = new("#f0c879");
    private static readonly Color EdgePressed = new("#c7984b");

    public static Button Create(string text, string? icon, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(Width, 0),
            ExpandIcon = false,
        };
        if (icon != null)
        {
            button.Icon = UiIcons.Get(icon);
            button.IconAlignment = HorizontalAlignment.Center;
            button.AddThemeConstantOverride("icon_max_width", (int)IconSize);
            button.AddThemeColorOverride("icon_normal_color", UiTheme.TextLo);
            button.AddThemeColorOverride("icon_hover_color", UiTheme.TextHi);
            button.AddThemeColorOverride("icon_pressed_color", UiTheme.GoldBright);
        }
        Style(button, ToolMargin);
        return button;
    }

    public static void Style(Button button, int sideMargin)
    {
        button.FocusMode = Control.FocusModeEnum.None;
        button.AddThemeFontSizeOverride("font_size", FontSize);
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", TextPressed);
        button.AddThemeColorOverride("font_hover_pressed_color", TextPressed);
        button.AddThemeStyleboxOverride("normal", BarStyle(
            new Color(0.025f, 0.028f, 0.034f, 0.88f), new Color(0.34f, 0.37f, 0.41f, 0.65f), sideMargin));
        button.AddThemeStyleboxOverride("hover", BarStyle(
            new Color(0.055f, 0.060f, 0.070f, 0.94f), new Color(0.68f, 0.71f, 0.75f, 0.85f), sideMargin));
        button.AddThemeStyleboxOverride("pressed", BarStyle(
            new Color(0.095f, 0.082f, 0.050f, 0.98f), EdgePressed, sideMargin));
        button.AddThemeStyleboxOverride("hover_pressed", BarStyle(
            new Color(0.095f, 0.082f, 0.050f, 0.98f), EdgePressed, sideMargin));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    public static void ShowLocked(Button button, bool locked)
    {
        button.Icon = UiIcons.Get(locked ? "system/lock" : "system/unlock");
        button.TooltipText = locked ? "Unlock position and size" : "Lock position and size";
        button.AddThemeColorOverride("icon_normal_color", locked ? UiTheme.GoldBright : UiTheme.TextLo);
    }

    private static StyleBoxFlat BarStyle(Color background, Color edge, int sideMargin)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = edge };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(5);
        style.ContentMarginLeft = style.ContentMarginRight = sideMargin;
        style.ContentMarginTop = style.ContentMarginBottom = 3;
        return style;
    }
}
