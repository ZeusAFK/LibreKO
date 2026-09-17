using Godot;

namespace LibreKO;

public static class HudTheme
{
    private static Theme? _shared;
    public static Theme Shared => _shared ??= Build();

    private const float ScrollBarWidth = 11f;
    private const float ScrollGrabberInset = 2f;

    private static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 14 };

        foreach (var type in new[] { "PanelContainer", "Panel", "PopupPanel" })
            t.SetStylebox("panel", type, UiTheme.Panel(6));

        t.SetColor("font_color", "Label", UiTheme.TextLo);
        t.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.6f));
        t.SetConstant("shadow_offset_x", "Label", 1);
        t.SetConstant("shadow_offset_y", "Label", 1);

        StyleBoxFlat Btn(Color bg, Color border, int bw = 1)
        {
            var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            sb.SetBorderWidthAll(bw);
            sb.SetCornerRadiusAll(5);
            sb.ContentMarginLeft = sb.ContentMarginRight = 10;
            sb.ContentMarginTop = sb.ContentMarginBottom = 5;
            return sb;
        }
        t.SetStylebox("normal", "Button", Btn(UiTheme.Glass, new Color(UiTheme.Edge, 0.85f)));
        t.SetStylebox("hover", "Button", Btn(UiTheme.GlassLight, new Color(UiTheme.Gold, 0.9f)));
        t.SetStylebox("pressed", "Button", Btn(new Color(0.05f, 0.05f, 0.06f, 0.97f), new Color(UiTheme.GoldDark, 0.95f)));
        t.SetStylebox("disabled", "Button", Btn(new Color(0.10f, 0.10f, 0.115f, 0.5f), new Color(0.3f, 0.29f, 0.25f, 0.35f)));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", UiTheme.Gold);
        t.SetColor("font_hover_color", "Button", UiTheme.GoldBright);
        t.SetColor("font_pressed_color", "Button", UiTheme.GoldDark);
        t.SetColor("font_disabled_color", "Button", new Color(UiTheme.TextLo, 0.45f));

        t.SetColor("font_color", "CheckButton", UiTheme.TextLo);
        t.SetColor("font_color", "CheckBox", UiTheme.TextLo);

        var sep = new StyleBoxTexture { Texture = UiTheme.Divider() };
        sep.SetContentMarginAll(0);
        t.SetStylebox("separator", "HSeparator", sep);
        t.SetConstant("separation", "HSeparator", 6);

        var le = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.9f), BorderColor = new Color(UiTheme.Edge, 0.7f) };
        le.SetBorderWidthAll(1); le.SetCornerRadiusAll(4); le.SetContentMarginAll(6);
        var leFocus = (StyleBoxFlat)le.Duplicate(); leFocus.BorderColor = new Color(UiTheme.Gold, 0.9f);
        t.SetStylebox("normal", "LineEdit", le);
        t.SetStylebox("focus", "LineEdit", leFocus);
        t.SetColor("font_color", "LineEdit", UiTheme.TextHi);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(UiTheme.TextLo, 0.6f));
        t.SetColor("caret_color", "LineEdit", UiTheme.Gold);

        StyleBoxFlat Grab(Color c)
        {
            var sb = new StyleBoxFlat { BgColor = c };
            sb.SetCornerRadiusAll(4);
            sb.SetContentMarginAll(ScrollGrabberInset);
            return sb;
        }
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f) };
        track.SetCornerRadiusAll(4);
        track.SetContentMarginAll(ScrollBarWidth / 2f);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", bar, track);
            t.SetStylebox("grabber", bar, Grab(new Color(UiTheme.Edge, 0.8f)));
            t.SetStylebox("grabber_highlight", bar, Grab(new Color(UiTheme.Gold, 0.85f)));
            t.SetStylebox("grabber_pressed", bar, Grab(new Color(UiTheme.GoldBright, 0.9f)));
        }

        var sliderTrack = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.4f) };
        sliderTrack.SetCornerRadiusAll(3);
        sliderTrack.ContentMarginTop = sliderTrack.ContentMarginBottom = 3;
        t.SetStylebox("slider", "HSlider", sliderTrack);
        var sliderFill = new StyleBoxFlat { BgColor = new Color(UiTheme.GoldDark, 0.9f) };
        sliderFill.SetCornerRadiusAll(3);
        t.SetStylebox("grabber_area", "HSlider", sliderFill);
        t.SetStylebox("grabber_area_highlight", "HSlider", sliderFill);

        var pbBg = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f), BorderColor = new Color(UiTheme.Edge, 0.6f) };
        pbBg.SetBorderWidthAll(1); pbBg.SetCornerRadiusAll(3);
        var pbFill = new StyleBoxFlat { BgColor = new Color(UiTheme.Gold, 0.85f) };
        pbFill.SetCornerRadiusAll(3);
        t.SetStylebox("background", "ProgressBar", pbBg);
        t.SetStylebox("fill", "ProgressBar", pbFill);
        t.SetColor("font_color", "ProgressBar", UiTheme.TextHi);

        return t;
    }
}
