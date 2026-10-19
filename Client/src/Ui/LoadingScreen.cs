using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class LoadingScreen : CanvasLayer
{
    private ProgressBar bar = null!;
    private Label statusLabel = null!;
    private Label detailLabel = null!;
    private Label percent = null!;
    private readonly LoadingGlide glide = new();
    private const float PercentWidth = 56f;

    public override void _Ready()
    {
        Layer = UiLayers.Loading;

        Ui.Background(this, Backdrop.LoadingArt, Backdrop.Fit.Cover, bottomScrim: true);

        var host = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);
        Ui.AutoScale(host, baseHeight: 1080f / Platform.TouchMenuScale, min: 0.7f,
                     max: Platform.Pick(2.0f, Platform.MenuScaleMax), pivot: new Vector2(0.5f, 1f));

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AnchorLeft = 0.5f; box.AnchorRight = 0.5f; box.AnchorTop = 1f; box.AnchorBottom = 1f;
        box.OffsetLeft = -340; box.OffsetRight = 340; box.OffsetTop = -104; box.OffsetBottom = -44;
        box.AddThemeConstantOverride("separation", 8);
        host.AddChild(box);

        statusLabel = Line("Loading…", 19, UiTheme.TextHi, 5);
        box.AddChild(statusLabel);

        var barRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        barRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(barRow);
        bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 16),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var barBg = new StyleBoxFlat { BgColor = new Color(0.02f, 0.02f, 0.03f, 0.85f), BorderColor = new Color(UiTheme.Gold, 0.45f) };
        barBg.SetBorderWidthAll(1);
        barBg.SetCornerRadiusAll(3);
        var barFill = new StyleBoxFlat { BgColor = UiTheme.Gold };
        barFill.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("background", barBg);
        bar.AddThemeStyleboxOverride("fill", barFill);
        barRow.AddChild(bar);
        percent = Line("0 %", 15, UiTheme.GoldBright, 4);
        percent.HorizontalAlignment = HorizontalAlignment.Right;
        percent.CustomMinimumSize = new Vector2(PercentWidth, 0);
        barRow.AddChild(percent);

        detailLabel = Line("", 12, new Color(UiTheme.TextLo, 0.94f), 4);
        box.AddChild(detailLabel);
    }

    public void Set(string status, double progress, string detail = "")
    {
        if (statusLabel != null) statusLabel.Text = status;
        if (detailLabel != null) detailLabel.Text = detail;
        glide.Set(progress, FrameClock.Now());
        ShowProgress();
    }

    public override void _Process(double delta) => ShowProgress();

    private void ShowProgress()
    {
        if (bar == null) return;
        double shown = glide.At(FrameClock.Now());
        bar.Value = shown;
        percent.Text = $"{LoadingGlide.Percent(shown)} %";
    }

    private static Label Line(string text, int size, Color color, int outline)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("outline_size", outline);
        return l;
    }
}
