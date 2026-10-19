using Godot;
using LibreKO.Features.PluginBridge;

namespace LibreKO.Features.Hud;

internal sealed partial class MapFeature : IFeature, IHudTickFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly QuestBook questBook;
    private readonly IBattleEventRequests battleEventRequests;
    private readonly ScenerySystem scenery;
    private readonly BuffSystem buff;
    private readonly CameraSystem camera;
    private readonly PluginBridgeFeature pluginBridge;

    public MapFeature(IWorldContext context, QuestBook questBook, IBattleEventRequests battleEventRequests, ScenerySystem scenery, BuffSystem buff, CameraSystem camera, PluginBridgeFeature pluginBridge)
    {
        this.context = context;
        this.questBook = questBook;
        this.battleEventRequests = battleEventRequests;
        this.scenery = scenery;
        this.buff = buff;
        this.camera = camera;
        this.pluginBridge = pluginBridge;
    }
    private const double RefreshInterval = 1.0 / 12.0;

    private double refreshAccum;

    public void HudTick(double delta, double nowSec) => Tick(delta);

    public void Build()
    {
        BuildMini();
        BuildFull();
    }

    bool IEscapable.CloseOnEscape()
    {
        if (FullOpen) ToggleFull();
        else return false;
        return true;
    }

    public void Dispose()
    {
    }

    internal MiniMap MiniMap { get; set; } = null!;
    internal List<MiniMap.Blip> BlipScratch { get; } = new();
    private readonly List<Action<List<MiniMap.Blip>>> miniSources = new();

    internal void AddMiniSource(Action<List<MiniMap.Blip>> source) => miniSources.Add(source);

    private const float QuestTargetBlipRadius = 4.5f;

    internal void Tick(double delta)
    {
        refreshAccum += delta;
        if (refreshAccum < RefreshInterval) return;
        refreshAccum = 0;
        UpdateMini();
        UpdateFull();
    }

    private void BuildMini()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, LibreKO.Plugins.HudPart.MiniMap);

        MiniMap = new MiniMap { MouseFilter = Control.MouseFilterEnum.Stop };
        layer.AddChild(MiniMap);
        HudPlacement.MiniMap.ApplyTo(MiniMap);

        string stem = scenery.Terrain != null ? scenery.Terrain.ZoneStem : ZoneCatalog.Stem(context.Zone) ?? "";
        MiniMap.SetZone(stem, ZoneCatalog.Name(context.Zone));
    }

    internal void ToggleMini()
    {
        if (context.Hud.PluginPart(HudPart.MiniMap) is { } themed)
        {
            themed.Visible = !themed.Visible;
            return;
        }
        if (MiniMap != null) MiniMap.Visible = !MiniMap.Visible;
    }

    internal void UpdateMini()
    {
        if (MiniMap == null) return;
        BlipScratch.Clear();
        if (!buff.BlindHidesOthers(context.Now)) AddEntityBlips();
        foreach (var source in miniSources) source(BlipScratch);
        float heading = Coord.MapHeading(Mathf.Sin(camera.CamYaw), -Mathf.Cos(camera.CamYaw));
        MiniMap.UpdateView(context.Self.MapXZ.X, context.Self.MapXZ.Z, heading, BlipScratch);
        pluginBridge.NotifyMap(heading);
    }

    private void AddEntityBlips()
    {
        foreach (var kv in context.Entities.Views)
        {
            var e = kv.Value;
            if (e.Body == null) continue;
            var gp = e.Body.GlobalPosition;
            var (kx, kz) = Coord.ToMap(gp.X, gp.Z);
            Color col;
            float rad;
            if (e.IsNpc && e.Attackable) { col = UiTheme.Bad; rad = 2.6f; }
            else if (e.IsNpc) { col = UiTheme.Neutral; rad = 3f; }
            else { col = UiTheme.Good; rad = 3f; }
            if (e.Dead) col = col.Darkened(0.5f);
            BlipScratch.Add(new MiniMap.Blip(kx, kz, col, rad, false));
            if (e.IsNpc && !e.Dead && questBook.TargetNpcs.Contains(e.NpcId))
                BlipScratch.Add(new MiniMap.Blip(kx, kz, UiTheme.GoldBright, QuestTargetBlipRadius, true));
            if (kv.Key == context.Selection.SelectedId)
                BlipScratch.Add(new MiniMap.Blip(kx, kz, UiTheme.TextHi, 5.5f, true));
        }
    }

    internal CanvasLayer FullLayer { get; set; } = null!;
    private bool fullShown;

    internal bool FullOpen => fullShown;

    private FullView fullView = null!;
    private Label fullTitle = null!;

    private void BuildFull()
    {
        FullLayer = new CanvasLayer { Layer = UiLayers.FloatingPanels, Visible = false };
        context.Root.AddChild(FullLayer);

        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.82f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true }) ToggleFull(); };
        FullLayer.AddChild(dim);

        fullView = new FullView { MouseFilter = Control.MouseFilterEnum.Ignore };
        fullView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        FullLayer.AddChild(fullView);

        fullTitle = UiTheme.Heading(28, "Map");
        fullTitle.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        fullTitle.OffsetTop = 14; fullTitle.OffsetBottom = 56;
        fullTitle.AddThemeConstantOverride("outline_size", 6);
        fullTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        FullLayer.AddChild(fullTitle);

        var close = UiTheme.IconButton("✕", "Close map (M)");
        close.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        close.OffsetLeft = -52; close.OffsetTop = 16; close.OffsetRight = -18; close.OffsetBottom = 50;
        close.Pressed += ToggleFull;
        FullLayer.AddChild(close);

        var hint = UiTheme.Text("M or Esc to close", 13, UiTheme.TextDim, HorizontalAlignment.Center);
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hint.OffsetTop = -34; hint.OffsetBottom = -12;
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        FullLayer.AddChild(hint);
    }

    internal void ToggleFull()
    {
        fullShown = !fullShown;
        FullLayer.Visible = fullShown;
        if (fullShown)
        {
            fullTitle.Text = ZoneCatalog.Name(context.Zone);
            fullView.SetMap(MiniMap?.MapTexture, MiniMap?.MapExtent ?? 0f);
            UpdateFull();
            if (context.Zone == NereidMonuments.Zone) battleEventRequests.MapEvent();
        }
    }

    private void UpdateFull()
    {
        if (!fullShown) return;
        float heading = Coord.MapHeading(Mathf.Sin(camera.CamYaw), -Mathf.Cos(camera.CamYaw));
        fullView.SetView(context.Self.MapXZ.X, context.Self.MapXZ.Z, heading, BlipScratch);
    }

    private sealed partial class FullView : Control
    {
        private Texture2D? tex;
        private float extent;
        private float mapX, mapZ, heading;
        private readonly List<MiniMap.Blip> blips = new();
        private readonly DiscBatch discs = new();
        private static readonly Color BlipShadow = new(0, 0, 0, 0.7f);
        private const float BlipRimMargin = 1.5f;

        public void SetMap(Texture2D? tex, float extent) { this.tex = tex; this.extent = extent; QueueRedraw(); }

        public void SetView(float mapX, float mapZ, float heading, IReadOnlyList<MiniMap.Blip> currentBlips)
        {
            this.mapX = mapX; this.mapZ = mapZ; this.heading = heading;
            blips.Clear();
            blips.AddRange(currentBlips);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var vp = Size;
            float side = Mathf.Min(vp.X, vp.Y) - 56f;
            if (side <= 0f) return;
            var origin = (vp - new Vector2(side, side)) * 0.5f;
            var rect = new Rect2(origin, new Vector2(side, side));

            if (tex != null)
                DrawTextureRect(tex, rect, false);
            else
                DrawRect(rect, new Color(0.06f, 0.07f, 0.09f));
            DrawRect(rect, new Color(UiTheme.Gold, 0.85f), false, 2.5f);

            if (extent <= 0f) return;
            float scale = side / extent;
            Vector2 ToPx(float x, float z) => origin + new Vector2(x * scale, side - z * scale);

            foreach (var b in blips)
            {
                var p = ToPx(b.X, b.Z);
                float r = (b.Radius + 1.5f) * 1.4f;
                if (b.Hollow)
                {
                    discs.Flush(GetCanvasItem());
                    DrawArc(p, r + BlipRimMargin, 0, Mathf.Tau, 18, b.Color, 2.5f, true);
                }
                else
                {
                    discs.Add(p, r + BlipRimMargin, BlipShadow);
                    discs.Add(p, r, b.Color);
                }
            }
            discs.Flush(GetCanvasItem());

            var pp = ToPx(mapX, mapZ);
            float a = Mathf.DegToRad(heading);
            Vector2 Rot(Vector2 v) => new(v.X * Mathf.Cos(a) - v.Y * Mathf.Sin(a), v.X * Mathf.Sin(a) + v.Y * Mathf.Cos(a));
            var tip = pp + Rot(new Vector2(0, -14));
            var bl = pp + Rot(new Vector2(-9, 9));
            var br = pp + Rot(new Vector2(9, 9));
            var notch = pp + Rot(new Vector2(0, 4));
            DrawColoredPolygon(new[] { tip, bl, notch }, UiTheme.Self);
            DrawColoredPolygon(new[] { tip, notch, br }, UiTheme.Self.Darkened(0.18f));
            DrawPolyline(new[] { tip, bl, notch, br, tip }, new Color(0, 0, 0, 0.85f), 2f, true);
        }
    }
}
