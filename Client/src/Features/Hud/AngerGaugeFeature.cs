using Godot;

namespace LibreKO.Features.Hud;

internal sealed class AngerGaugeFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly PvpSession pvp;
    private readonly SelfSession self;

    public AngerGaugeFeature(IWorldContext context, PvpSession pvp, SelfSession self)
    {
        this.context = context;
        this.pvp = pvp;
        this.self = self;
    }

    public void Tick(double delta, double nowSec)
    {
        AngerGaugeTick(nowSec);
    }

    private const string SheetPath = "res://assets/ui/anger/anger.png";
    private static readonly Vector2 Default = new(330f, 14f);
    private const float Gap = 6f;

    private Control? angerGauge;
    private TextureRect image = null!;
    private AtlasTexture frame = null!;
    private int shownImage;
    private int fullFrame;
    private double frameAt;

    public void Build()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        var gauge = new Control
        {
            CustomMinimumSize = new Vector2(AngerGauge.FrameWidth, AngerGauge.FrameHeight),
            Size = new Vector2(AngerGauge.FrameWidth, AngerGauge.FrameHeight),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        layer.AddChild(gauge);
        frame = new AtlasTexture { Atlas = ResourceLoader.Load<Texture2D>(SheetPath) };
        image = new TextureRect
        {
            Texture = frame,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        gauge.AddChild(image);
        angerGauge = gauge;
        HudLayout.Attach(gauge, "hud_anger", gauge, Home);

        pvp.PvpAngerEvent += On;
        On(pvp.AngerGauge, pvp.AngerFull);
    }

    public void Dispose() => pvp.PvpAngerEvent -= On;

    private void On(int gauge, bool full)
    {
        shownImage = full ? 0 : AngerGauge.ImageFor(gauge, shownImage);
        if (!full) ShowFrame(AngerGauge.GageFrame(shownImage));
    }

    private void AngerGaugeTick(double now)
    {
        if (angerGauge == null) return;
        bool shown = AngerGauge.ShownIn(context.Zone, self.Sheet.Level);
        if (angerGauge.Visible != shown) angerGauge.Visible = shown;
        if (!shown || !pvp.AngerFull || now < frameAt) return;
        frameAt = now + AngerGauge.FullFrameSeconds;
        ShowFrame(AngerGauge.FullFrame(fullFrame++));
    }

    private void ShowFrame((int X, int Y) at) =>
        frame.Region = new Rect2(at.X, at.Y, AngerGauge.FrameWidth, AngerGauge.FrameHeight);

    private Vector2 Home()
    {
        if (context.Hud.StatusHud == null || !GodotObject.IsInstanceValid(context.Hud.StatusHud) || !context.Hud.StatusHud.IsVisibleInTree()) return Default;
        var status = context.Hud.StatusHud.GetGlobalRect();
        return new Vector2(status.End.X + Gap, status.Position.Y);
    }
}
