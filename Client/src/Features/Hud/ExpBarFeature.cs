using Godot;

namespace LibreKO.Features.Hud;

internal sealed class ExpBarFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly ExpSealSession seal;
    private readonly SelfSession self;
    private readonly ScenerySystem scenery;

    public ExpBarFeature(IWorldContext context, ExpSealSession expSeal, SelfSession self, ScenerySystem scenery)
    {
        this.context = context;
        seal = expSeal;
        this.self = self;
        this.scenery = scenery;
    }
    private const double StatPoll = 0.5;
    private const int FpsGood = 50;
    private const int FpsFair = 30;
    private const int PingGood = 90;
    private const int PingFair = 180;
    private const float StripGap = 10f;
    private const float LevelTextScale = 0.80f;

    private CanvasLayer? layer;

    internal Control? Root { get; private set; }

    private ColorRect? track;
    private ColorRect? fill;
    private ColorRect? gleam;
    private Label? level;
    private Label? label;
    internal Label? Stats { get; private set; }
    private Label? clock;
    private double statsAt;
    private Tween? tween;
    private float shown = -1f;

    private static readonly Color SealedColour = new(0.62f, 0.45f, 0.95f, 0.92f);

    public void Build()
    {
        scenery.ClockChanged += SetClock;
        layer = new CanvasLayer { Layer = UiLayers.HudTrackers };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, HudPart.ExpBar);

        Root = new Control
        {
            Name = "ExpStrip",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -HudPlacement.BottomInset,
        };
        layer.AddChild(Root);

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
        Root!.AddChild(track);
        this.track = track;

        fill = new ColorRect
        {
            Name = "ExpFill",
            Color = new Color(UiTheme.Gold, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
        };
        track.AddChild(fill);

        gleam = new ColorRect
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
        track.AddChild(gleam);

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
        Root.AddChild(row);

        level = StripText(row, UiTheme.TextHi, LevelTextScale);
        StripText(row, UiTheme.TextLo).Text = "EXP";
        label = StripText(row, UiTheme.GoldBright);
        clock = StripText(row, UiTheme.TextHi);
        Stats = StripText(row, UiTheme.Good);

        Update();
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

    internal void Update()
    {
        if (fill == null || label == null) return;
        bool sealing = seal.On;
        double percent = sealing ? ExpSeal.Percent(seal.SealedExperience) : self.Sheet.ExpPercent;
        float ratio = Mathf.Clamp((float)(percent / 100.0), 0f, 1f);
        fill.Color = sealing ? SealedColour : new Color(UiTheme.Gold, 0.92f);
        Glide(ratio);
        label.Text = sealing ? $"EXP:{percent:0.00}%" : $"{self.Sheet.ExpPercent:0.0}%";
        if (level != null) level.Text = $"Lv {self.Sheet.Level}";
    }

    private void Glide(float ratio)
    {
        tween?.Kill();
        bool snap = shown < 0f || ratio < shown;
        shown = ratio;
        if (snap)
        {
            fill!.AnchorRight = ratio;
            if (gleam != null) gleam.AnchorRight = ratio;
            return;
        }
        double seconds = (ratio - fill!.AnchorRight) / BarGlide.GlidePerSecond;
        tween = context.Root.CreateTween().SetParallel();
        tween.TweenProperty(fill, "anchor_right", ratio, seconds);
        if (gleam != null) tween.TweenProperty(gleam, "anchor_right", ratio, seconds);
    }

    internal void StatsTick(double now)
    {
        if (Stats == null || now < statsAt) return;
        statsAt = now + StatPoll;

        int fps = (int)Engine.GetFramesPerSecond();
        int ping = Game.Connection is { } connection ? connection.PingMs : -1;
        Stats.Text = ping < 0 ? $"{fps} fps" : $"{fps} fps  {ping} ms";
        UiTheme.ApplyFontColor(Stats,
            Grade(fps >= FpsGood && (ping < 0 || ping <= PingGood),
                  fps >= FpsFair && (ping < 0 || ping <= PingFair)));
    }

    internal static Color Grade(bool good, bool fair) =>
        good ? UiTheme.Good : fair ? UiTheme.Warning : UiTheme.Bad;

    internal void SetClock(string text)
    {
        if (clock != null) clock.Text = text;
    }

    public void Dispose()
    {
        scenery.ClockChanged -= SetClock;
        layer?.QueueFree();
        layer = null;
        Root = null;
        track = null;
        level = null;
        fill = null;
        gleam = null;
        label = null;
        Stats = null;
        clock = null;
        tween = null;
        shown = -1f;
    }

    public void LateTick(double delta, double nowSec)
    {
        StatsTick(nowSec);
    }
}
