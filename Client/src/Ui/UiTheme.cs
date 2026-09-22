using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class UiTheme
{
    public static readonly Color Ink        = new("101013");
    public static readonly Color GlassDeep  = new(0.040f, 0.040f, 0.048f, 0.96f);
    public static readonly Color Glass      = new(0.094f, 0.094f, 0.107f, 0.91f);
    public static readonly Color GlassLight = new(0.135f, 0.135f, 0.150f, 0.94f);
    public static readonly Color Header     = new("393940");
    public static readonly Color Edge       = new("5c5b62");
    public static readonly Color EdgeSoft   = new(0.30f, 0.30f, 0.34f, 0.6f);

    public static readonly Color Gold       = new("c8a45a");
    public static readonly Color GoldBright = new("ecd9a6");
    public static readonly Color GoldDark   = new("5a5038");
    public static readonly Color Bronze     = new("8a6d3c");
    public static readonly Color BronzeDark = new("3f3325");

    public static readonly Color TextHi     = new("ece4d2");
    public static readonly Color TextLo     = new("a8a298");
    public static readonly Color TextDim    = new(0.57f, 0.55f, 0.51f, 0.75f);

    public static readonly Color SlotBg     = new(0.066f, 0.066f, 0.082f, 0.96f);
    public static readonly Color SlotBorder = new(0.26f, 0.25f, 0.225f);
    public static readonly Color RowBg      = new(0.105f, 0.103f, 0.116f, 0.84f);
    public static readonly Color RowHover   = new(0.165f, 0.153f, 0.138f, 0.92f);
    public static readonly Color Locked     = new(0.080f, 0.080f, 0.095f, 0.70f);

    public static readonly Color Good       = new("7fd98b");
    public static readonly Color Bad        = new("e0574a");
    public static readonly Color Neutral    = new("e0bb52");
    public static readonly Color Self       = new("eaf4ff");
    public static readonly Color Hp         = new("b5352f");
    public static readonly Color Mp         = new("3a6bbf");
    public static readonly Color Warning    = new("d9a441");

    public const float WindowPanelAlpha = 0.965f;
    public const float TranslucentWindowAlpha = 0.86f;

    public static StyleBoxFlat WindowPanel(int radius = 2, float alpha = WindowPanelAlpha)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.105f, 0.108f, 0.116f, alpha),
            BorderColor = new Color(0.34f, 0.31f, 0.22f, 0.90f),
            ShadowColor = new Color(0, 0, 0, 0.58f),
            ShadowSize = 6,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(0);
        return sb;
    }

    public static StyleBoxFlat Panel(int radius = 5, bool bright = false)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bright ? GlassLight : Glass,
            BorderColor = new Color(Edge, 0.7f),
            ShadowColor = new Color(0, 0, 0, 0.4f),
            ShadowSize = 5,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(11);
        return sb;
    }

    public static StyleBoxFlat HeaderBand(int radius = 3)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = Header,
            BorderColor = new Color(Edge, 0.82f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginLeft = sb.ContentMarginRight = 10;
        sb.ContentMarginTop = sb.ContentMarginBottom = 5;
        return sb;
    }

    public static StyleBoxFlat WindowHeaderBand(int radius = 2)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.40f, 0.315f, 0.165f, 0.98f),
            BorderColor = new Color(0.48f, 0.40f, 0.24f, 0.88f),
        };
        sb.SetBorderWidthAll(1);
        sb.CornerRadiusTopLeft = radius;
        sb.CornerRadiusTopRight = radius;
        sb.CornerRadiusBottomLeft = 0;
        sb.CornerRadiusBottomRight = 0;
        sb.ContentMarginLeft = sb.ContentMarginRight = 8;
        sb.ContentMarginTop = sb.ContentMarginBottom = 3;
        return sb;
    }

    public static StyleBoxFlat Tab(bool selected = false)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = selected ? new Color("34343b") : new Color("25252b"),
            BorderColor = selected ? new Color("62616a") : new Color("3c3c43"),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(2);
        sb.ContentMarginLeft = sb.ContentMarginRight = 12;
        sb.ContentMarginTop = sb.ContentMarginBottom = 5;
        return sb;
    }

    public static StyleBoxFlat TopTab(bool selected, bool hover = false)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = selected ? new Color("1e2028") : hover ? new Color("191a20") : new Color("13141a"),
            BorderColor = Gold,
        };
        sb.BorderWidthTop = selected ? 2 : 0;
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = 3;
        sb.ContentMarginLeft = sb.ContentMarginRight = 10;
        sb.ContentMarginTop = sb.ContentMarginBottom = 6;
        return sb;
    }

    public static Button TopTabButton(string text, int fontSize = 12)
    {
        var b = new Button
        {
            Text = text,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeStyleboxOverride("normal", TopTab(false));
        b.AddThemeStyleboxOverride("hover", TopTab(false, hover: true));
        b.AddThemeStyleboxOverride("pressed", TopTab(true));
        b.AddThemeStyleboxOverride("hover_pressed", TopTab(true));
        b.AddThemeColorOverride("font_color", TextLo);
        b.AddThemeColorOverride("font_hover_color", TextHi);
        b.AddThemeColorOverride("font_pressed_color", GoldBright);
        b.AddThemeColorOverride("font_hover_pressed_color", GoldBright);
        return b;
    }

    public static StyleBoxFlat ListRow(bool selected)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = selected ? new Color("2b2417") : new Color(0.055f, 0.058f, 0.068f, 0.55f),
            BorderColor = new Color(Gold, selected ? 0.95f : 0f),
        };
        sb.BorderWidthLeft = selected ? 3 : 0;
        sb.SetCornerRadiusAll(2);
        sb.ContentMarginLeft = selected ? 7 : 10;
        sb.ContentMarginRight = 8;
        sb.ContentMarginTop = sb.ContentMarginBottom = 5;
        return sb;
    }

    public static CheckBox FlatCheck(string text, bool pressed, int fontSize = 12)
    {
        var c = new CheckBox
        {
            Text = text,
            ButtonPressed = pressed,
            FocusMode = Control.FocusModeEnum.None,
        };
        c.AddThemeFontSizeOverride("font_size", fontSize);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            c.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginRight = 4 });
        c.AddThemeColorOverride("font_color", TextLo);
        c.AddThemeColorOverride("font_hover_color", TextHi);
        c.AddThemeColorOverride("font_pressed_color", GoldBright);
        c.AddThemeColorOverride("font_hover_pressed_color", GoldBright);
        return c;
    }

    public static StyleBoxFlat MeterTrack()
    {
        var sb = new StyleBoxFlat { BgColor = new Color(0.035f, 0.036f, 0.043f, 0.95f) };
        sb.SetCornerRadiusAll(2);
        return sb;
    }

    public static StyleBoxFlat MeterFill(Color tint)
    {
        var sb = new StyleBoxFlat { BgColor = tint };
        sb.SetCornerRadiusAll(2);
        return sb;
    }

    public static StyleBoxFlat Inset(int radius = 3)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.030f, 0.030f, 0.037f, 0.72f),
            BorderColor = new Color(EdgeSoft, 0.62f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(8);
        return sb;
    }

    public static StyleBoxFlat Row(bool selected = false, bool muted = false)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = muted ? Locked : (selected ? RowHover : RowBg),
            BorderColor = selected ? new Color(Gold, 0.72f) : new Color(EdgeSoft, 0.45f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(3);
        sb.ContentMarginLeft = sb.ContentMarginRight = 8;
        sb.ContentMarginTop = sb.ContentMarginBottom = 5;
        return sb;
    }

    public static StyleBoxFlat Chip()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.03f, 0.04f, 0.82f),
            BorderColor = new Color(Edge, 0.55f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(9);
        sb.ContentMarginLeft = sb.ContentMarginRight = 10;
        sb.ContentMarginTop = sb.ContentMarginBottom = 3;
        return sb;
    }

    public static StyleBoxFlat Slot(Color? grade = null, bool hover = false, bool locked = false)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = locked ? Locked : (hover ? new Color(0.105f, 0.100f, 0.090f, 0.98f) : SlotBg),
            BorderColor = locked ? new Color(EdgeSoft, 0.32f) : (hover ? new Color(Gold, 0.92f) : grade ?? SlotBorder),
            ShadowColor = new Color(0, 0, 0, hover ? 0.34f : 0.22f),
            ShadowSize = hover ? 4 : 2,
        };
        sb.SetBorderWidthAll(grade.HasValue || hover ? 2 : 1);
        sb.SetCornerRadiusAll(3);
        return sb;
    }

    public static Button SmallButton(string text, string tooltip)
    {
        var b = new Button
        {
            Text = text,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 26),
        };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeColorOverride("font_color", Gold);
        b.AddThemeColorOverride("font_hover_color", GoldBright);
        b.AddThemeColorOverride("font_pressed_color", GoldDark);
        var normal = new StyleBoxFlat { BgColor = Glass, BorderColor = new Color(Edge, 0.7f) };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(5);
        normal.ContentMarginLeft = normal.ContentMarginRight = 12;
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = GlassLight;
        hover.BorderColor = new Color(Gold, 0.85f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        Audio.HookButton(b);
        return b;
    }

    public static Button ActionButton(string text, string tooltip)
    {
        var b = SmallButton(text, tooltip);
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Ink);
        b.AddThemeColorOverride("font_pressed_color", GoldDark);
        var normal = new StyleBoxFlat { BgColor = Gold, BorderColor = new Color(GoldBright, 0.85f) };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(5);
        normal.ContentMarginLeft = normal.ContentMarginRight = 14;
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = GoldBright;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", normal);
        return b;
    }

    public static Button IconButton(string glyph, string tooltip)
    {
        var b = new Button
        {
            Text = glyph,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(26, 26),
        };
        b.AddThemeFontSizeOverride("font_size", 16);
        b.AddThemeColorOverride("font_color", Gold);
        b.AddThemeColorOverride("font_hover_color", GoldBright);
        b.AddThemeColorOverride("font_pressed_color", GoldDark);
        var normal = new StyleBoxFlat { BgColor = Glass, BorderColor = new Color(Edge, 0.7f) };
        normal.SetBorderWidthAll(1); normal.SetCornerRadiusAll(5);
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = GlassLight; hover.BorderColor = new Color(Gold, 0.85f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        Audio.HookButton(b);
        return b;
    }

    public static Button IconButton(Texture2D? icon, string tooltip)
    {
        var b = IconButton("", tooltip);
        b.Icon = icon;
        b.ExpandIcon = true;
        b.IconAlignment = HorizontalAlignment.Center;
        b.AddThemeConstantOverride("icon_max_width", 15);
        b.AddThemeColorOverride("icon_normal_color", Gold);
        b.AddThemeColorOverride("icon_hover_color", GoldBright);
        b.AddThemeColorOverride("icon_pressed_color", GoldDark);
        b.AddThemeColorOverride("icon_focus_color", GoldBright);
        return b;
    }

    public static Label Text(string text = "", int size = 13, Color? color = null,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = HudStyle.Label(size, align);
        l.Text = text;
        l.AddThemeColorOverride("font_color", color ?? TextLo);
        l.AddThemeConstantOverride("outline_size", 0);
        return l;
    }

    public static Label Heading(int size, string text = "")
    {
        var l = Text("", size, TextHi, HorizontalAlignment.Center);
        l.Text = text;
        return l;
    }

    public static Label SectionTitle(string text)
    {
        var l = Text(text, 13, Gold, HorizontalAlignment.Left);
        l.AddThemeConstantOverride("font_embolden", 1);
        return l;
    }

    public static HBoxContainer SectionTitle(string text, Texture2D? icon)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        var image = new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(16, 16),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = Gold,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(image);
        row.AddChild(SectionTitle(text));
        return row;
    }

    public static PanelContainer Pill(string text, Color? color = null, int size = 11)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Chip());
        var l = Text(text, size, color ?? TextHi, HorizontalAlignment.Center);
        p.AddChild(l);
        return p;
    }

    public static PanelContainer Section()
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Inset());
        return p;
    }

    public static PanelContainer RowPanel(bool selected = false, bool muted = false)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", Row(selected, muted));
        return p;
    }

    public static PanelContainer IconTile(string glyph, string tooltip = "", Vector2? size = null)
    {
        var p = new PanelContainer
        {
            TooltipText = tooltip,
            CustomMinimumSize = size ?? new Vector2(46, 46),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        p.AddThemeStyleboxOverride("panel", Row(false));
        var label = Text(glyph, 20, TextHi, HorizontalAlignment.Center);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        p.AddChild(label);
        return p;
    }

    public static void Margins(MarginContainer margin, int all) => Margins(margin, all, all, all, all);

    public static void Margins(MarginContainer margin, int left, int top, int right, int bottom)
    {
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
    }

    public static void DrawGoldRing(CanvasItem ci, Vector2 center, float radius, float width = 4f)
    {
        ci.DrawArc(center, radius + width * 0.5f, 0, Mathf.Tau, 96, new Color(0, 0, 0, 0.55f), width + 3f, true);
        ci.DrawArc(center, radius, 0, Mathf.Tau, 96, GoldDark, width + 1.5f, true);
        ci.DrawArc(center, radius, 0, Mathf.Tau, 96, Gold, width, true);
        ci.DrawArc(center, radius - width * 0.35f, 0, Mathf.Tau, 96, new Color(GoldBright, 0.85f), width * 0.4f, true);
    }

    public static SpinBox NumberBox(double min, double max, double step, float width, int fontSize = 13)
    {
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            CustomMinimumSize = new Vector2(width, 0),
        };
        var edit = spin.GetLineEdit();
        edit.AddThemeFontSizeOverride("font_size", fontSize);
        edit.FocusExited += spin.Apply;
        return spin;
    }

    public static OptionButton Dropdown(IReadOnlyList<string>? labels = null)
    {
        var option = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        option.AddThemeFontSizeOverride("font_size", 12);
        option.AddThemeColorOverride("font_color", TextHi);
        option.AddThemeColorOverride("font_hover_color", GoldBright);
        option.AddThemeColorOverride("font_pressed_color", Gold);

        var normal = new StyleBoxFlat { BgColor = Glass, BorderColor = new Color(Edge, 0.85f) };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(5);
        normal.ContentMarginLeft = normal.ContentMarginRight = 8;
        normal.ContentMarginTop = normal.ContentMarginBottom = 5;
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = GlassLight;
        hover.BorderColor = new Color(Gold, 0.9f);
        option.AddThemeStyleboxOverride("normal", normal);
        option.AddThemeStyleboxOverride("hover", hover);
        option.AddThemeStyleboxOverride("pressed", hover);
        option.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var menu = option.GetPopup();
        var panel = new StyleBoxFlat { BgColor = GlassDeep, BorderColor = new Color(Edge, 0.85f) };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(3);
        panel.SetContentMarginAll(4);
        var selected = new StyleBoxFlat { BgColor = new Color(GoldDark, 0.55f) };
        selected.SetCornerRadiusAll(2);
        menu.AddThemeStyleboxOverride("panel", panel);
        menu.AddThemeStyleboxOverride("hover", selected);
        menu.AddThemeColorOverride("font_color", TextLo);
        menu.AddThemeColorOverride("font_hover_color", GoldBright);
        menu.AddThemeFontSizeOverride("font_size", 12);

        if (labels == null) return option;
        foreach (string label in labels) option.AddItem(label);
        if (labels.Count > 0) option.Selected = 0;
        return option;
    }

    private static ImageTexture? _dividerTex;

    public static ImageTexture Divider()
    {
        if (_dividerTex != null) return _dividerTex;
        const int w = 64, h = 3;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int x = 0; x < w; x++)
        {
            float t = x / (w - 1f);
            float a = Mathf.Pow(1f - Mathf.Abs(t - 0.5f) * 2f, 0.7f);
            img.SetPixel(x, 0, new Color(Gold, a * 0.35f));
            img.SetPixel(x, 1, new Color(Gold, a));
            img.SetPixel(x, 2, new Color(GoldDark, a * 0.35f));
        }
        _dividerTex = ImageTexture.CreateFromImage(img);
        return _dividerTex;
    }
}
