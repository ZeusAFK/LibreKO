using Godot;

namespace LibreKO;

public partial class World : Node3D
{
    private const int ExpBarLayerIndex = 65;
    private const double ExpBarStatPoll = 0.5;
    private const int FpsGood = 50;
    private const int FpsFair = 30;
    private const int PingGood = 90;
    private const int PingFair = 180;
    private const float StripGap = 10f;
    private const float LevelTextScale = 0.80f;

    private CanvasLayer? _expBarLayer;
    private Control? _expBarRoot;
    private ColorRect? _expBarTrack;
    private ColorRect? _expBarFill;
    private ColorRect? _expBarGleam;
    private Label? _expBarLevel;
    private Label? _expBarLabel;
    private Label? _expBarStats;
    private Label? _expBarClock;
    private double _expBarStatsAt;

    private void ExpBarInit()
    {
        _expBarLayer = new CanvasLayer { Layer = ExpBarLayerIndex };
        AddChild(_expBarLayer);
        PluginHudSeam(_expBarLayer, HudPart.ExpBar);

        _expBarRoot = new Control
        {
            Name = "ExpStrip",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -HudPlacement.BottomInset,
        };
        _expBarLayer.AddChild(_expBarRoot);

        var track = new ColorRect
        {
            Name = "ExpTrack",
            Color = new Color(0.020f, 0.022f, 0.028f, 0.88f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -HudPlacement.ExpBarHeight,
        };
        _expBarRoot!.AddChild(track);
        _expBarTrack = track;

        _expBarFill = new ColorRect
        {
            Name = "ExpFill",
            Color = new Color(UiTheme.Gold, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        track.AddChild(_expBarFill);

        _expBarGleam = new ColorRect
        {
            Name = "ExpGleam",
            Color = UiTheme.GoldBright,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetBottom = 2f,
        };
        track.AddChild(_expBarGleam);

        var row = new HBoxContainer
        {
            Name = "ExpStripText",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = HudPlacement.TouchEdge,
            OffsetBottom = HudPlacement.StripTextHeight,
        };
        row.AddThemeConstantOverride("separation", (int)StripGap);
        _expBarRoot.AddChild(row);

        _expBarLevel = StripText(row, UiTheme.TextHi, LevelTextScale);
        StripText(row, UiTheme.TextLo).Text = "EXP";
        _expBarLabel = StripText(row, UiTheme.GoldBright);
        _expBarClock = StripText(row, UiTheme.TextHi);
        _expBarStats = StripText(row, UiTheme.Good);

        UpdateExpBar();
    }

    private static Label StripText(Control parent, Color color, float scale = 0.62f)
    {
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size",
            (int)(HudPlacement.StripTextHeight * scale));
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        parent.AddChild(label);
        return label;
    }

    private static Label StripLabel(Control parent, HorizontalAlignment align)
    {
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = align,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        label.AddThemeFontSizeOverride("font_size", (int)(HudPlacement.ExpBarHeight * 0.58f));
        label.AddThemeColorOverride("font_color", UiTheme.TextHi);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        parent.AddChild(label);
        return label;
    }

    private void UpdateExpBar()
    {
        if (_expBarFill == null || _expBarLabel == null) return;
        float ratio = Mathf.Clamp((float)(Sheet.ExpPercent / 100.0), 0f, 1f);
        _expBarFill.AnchorRight = ratio;
        if (_expBarGleam != null) _expBarGleam.AnchorRight = ratio;
        _expBarLabel.Text = $"{Sheet.ExpPercent:0.0}%";
        if (_expBarLevel != null) _expBarLevel.Text = $"Lv {Sheet.Level}";
    }

    private void ExpBarStatsTick(double now)
    {
        if (_expBarStats == null || now < _expBarStatsAt) return;
        _expBarStatsAt = now + ExpBarStatPoll;

        int fps = (int)Engine.GetFramesPerSecond();
        int ping = Net.I is { } net ? net.PingMs : -1;
        _expBarStats.Text = ping < 0 ? $"{fps} fps" : $"{fps} fps  {ping} ms";
        _expBarStats.AddThemeColorOverride("font_color",
            Grade(fps >= FpsGood && (ping < 0 || ping <= PingGood),
                  fps >= FpsFair && (ping < 0 || ping <= PingFair)));
    }

    private static Color Grade(bool good, bool fair) =>
        good ? UiTheme.Good : fair ? UiTheme.Warning : UiTheme.Bad;

    private void SetExpBarClock(string text)
    {
        if (_expBarClock != null) _expBarClock.Text = text;
    }

    private void ExpBarDispose()
    {
        _expBarLayer?.QueueFree();
        _expBarLayer = null;
        _expBarRoot = null;
        _expBarTrack = null;
        _expBarLevel = null;
        _expBarFill = null;
        _expBarGleam = null;
        _expBarLabel = null;
        _expBarStats = null;
        _expBarClock = null;
    }
}
