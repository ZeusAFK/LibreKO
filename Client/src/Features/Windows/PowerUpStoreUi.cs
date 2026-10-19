using Godot;

namespace LibreKO.Features.Windows;

internal static class PowerUpStoreUi
{
    internal const string FeaturedName = "Featured";
    internal const float ModalButtonHeight = 42f;
    internal const float ModalTouchButtonHeight = 56f;
    private const float StrikeWidth = 1.5f;
    private const float StrikeHeight = 0.55f;
    private const int ChipFontSize = 11;
    internal static readonly Color TimerChipColor = new("f2c94c");
    private static readonly Color WellFill = new(0.022f, 0.022f, 0.027f, 0.8f);

    internal static PanelContainer Well()
    {
        var well = new PanelContainer();
        var style = UiTheme.Inset();
        style.BgColor = WellFill;
        well.AddThemeStyleboxOverride("panel", style);
        return well;
    }

    internal static TextureRect Gem(int size) => new()
    {
        Texture = UiIcons.Get("system/gem"),
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SelfModulate = UiTheme.Premium,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    internal static HBoxContainer Price(long amount, int fontSize, Color? color = null)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(Gem(fontSize));
        row.AddChild(UiTheme.Text(amount.ToString("n0"), fontSize, color ?? UiTheme.Premium));
        return row;
    }

    internal static HBoxContainer PriceTag(PowerUpStoreEntry entry, int fontSize, Color? color = null)
    {
        if (!entry.Discounted) return Price(entry.Price, fontSize, color);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(Gem(fontSize));
        var was = UiTheme.Text(entry.BasePrice.ToString("n0"), fontSize - 2, UiTheme.TextDim);
        was.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var strike = new ColorRect
        {
            Color = UiTheme.TextDim,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorTop = StrikeHeight,
            AnchorBottom = StrikeHeight,
            OffsetTop = -StrikeWidth / 2,
            OffsetBottom = StrikeWidth / 2,
        };
        was.AddChild(strike);
        row.AddChild(was);
        row.AddChild(UiTheme.Text("→", fontSize - 2, UiTheme.TextDim));
        row.AddChild(UiTheme.Text(entry.Price.ToString("n0"), fontSize, color ?? UiTheme.Premium));
        return row;
    }

    internal static PanelContainer SearchField(string placeholder, int fontSize, float height, out LineEdit edit)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(4);
        box.ContentMarginLeft = 10;
        box.ContentMarginRight = 4;
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = new Color(UiTheme.Gold, 0.9f);

        var field = new PanelContainer { CustomMinimumSize = new Vector2(0, height) };
        field.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        field.AddChild(row);
        row.AddChild(UiIcons.Image("system/search", new Vector2(fontSize + 2, fontSize + 2), UiTheme.TextLo));
        var input = new LineEdit
        {
            PlaceholderText = placeholder,
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        foreach (var state in new[] { "normal", "focus", "read_only" })
            input.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        input.AddThemeFontSizeOverride("font_size", fontSize);
        input.AddThemeColorOverride("font_color", UiTheme.TextHi);
        input.AddThemeColorOverride("font_placeholder_color", new Color(UiTheme.TextLo, 0.6f));
        input.AddThemeColorOverride("caret_color", UiTheme.Gold);
        input.FocusEntered += () => field.AddThemeStyleboxOverride("panel", focus);
        input.FocusExited += () => field.AddThemeStyleboxOverride("panel", box);
        row.AddChild(input);
        edit = input;
        return field;
    }

    internal static PanelContainer Chip(string icon, string text, Color fill, Color ink, out Label label)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        var style = new StyleBoxFlat { BgColor = fill };
        style.SetCornerRadiusAll(9);
        style.ContentMarginLeft = style.ContentMarginRight = 6;
        style.ContentMarginTop = style.ContentMarginBottom = 1;
        chip.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 3);
        chip.AddChild(row);
        row.AddChild(UiIcons.Image(icon, new Vector2(ChipFontSize, ChipFontSize), ink));
        label = UiTheme.Text(text, ChipFontSize, ink);
        label.AddThemeConstantOverride("font_embolden", 1);
        row.AddChild(label);
        return chip;
    }

    internal static PanelContainer ModalBox(float width, out VBoxContainer body)
    {
        var box = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(radius: 4));
        var margin = new MarginContainer();
        UiTheme.Margins(margin, 18, 14, 18, 16);
        box.AddChild(margin);
        body = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        body.AddThemeConstantOverride("separation", 10);
        margin.AddChild(body);
        return box;
    }

    internal static HBoxContainer ModalHeader(VBoxContainer body, string title, Texture2D? icon, Action close)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        body.AddChild(head);
        if (icon != null)
        {
            head.AddChild(new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = UiTheme.GoldVivid,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        var label = UiTheme.Text(title, 15, UiTheme.Gold);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(label);
        var closeButton = UiTheme.IconButton(UiIcons.Get("system/close"), "Close");
        closeButton.CustomMinimumSize = Platform.Pick(new Vector2(26, 26), new Vector2(46, 46));
        closeButton.Pressed += close;
        head.AddChild(closeButton);
        return head;
    }
}
