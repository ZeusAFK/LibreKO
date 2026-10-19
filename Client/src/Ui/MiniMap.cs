using System;
using System.Collections.Generic;
using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public partial class MiniMap : Control
{
    public readonly record struct Blip(float X, float Z, Color Color, float Radius, bool Hollow, bool Diamond = false);

    public const float SquareSize = 220f;
    public const float TouchSquareSize = 214f;
    public const float HeaderHeight = 38f;
    private const float HeaderCell = 34f;

    public static float MapSize => Platform.TouchUi ? TouchSquareSize : SquareSize;

    public static float HeaderSize => Platform.TouchUi ? HeaderHeight : 0f;

    public static float TotalHeight => MapSize + HeaderSize;

    public static bool Collapsed { get; private set; }

    public static event System.Action? CollapsedChanged;

    public static float PanelHeight => Collapsed ? HeaderSize : TotalHeight;
    private const float MapInset = 0f;
    private const float Diameter = SquareSize - MapInset * 2f;
    private static readonly float[] ZoomRadii = { 64f, 100f, 150f, 220f, 320f, 460f };
    private int zoomIndex = 2;

    private ColorRect disc = null!;
    private ShaderMaterial discMat = null!;
    private Overlay overlay = null!;
    private Label zoneLabel = null!;
    private Label coordLabel = null!;
    private Control coordChip = null!;
    private Button zoomIn = null!;
    private Button zoomOut = null!;
    private MapChevron? chevron;
    private bool collapsed;

    private ImageTexture? mapTex;
    private float worldExtent;
    private bool hasMap;

    public Texture2D? MapTexture => mapTex;
    public float MapExtent => worldExtent;
    public string MapSource { get; private set; } = "relief";

    private float mapX, mapZ, headingDeg;
    private readonly List<Blip> blips = new();

    public MiniMap()
    {
        CustomMinimumSize = new Vector2(MapSize, TotalHeight);
        Build();
    }

    private void Build()
    {
        if (Platform.TouchUi) BuildFrame();
        disc = new ColorRect
        {
            Position = new Vector2(MapInset, HeaderSize + MapInset),
            Size = new Vector2(MapSize, MapSize),
            Color = Colors.White,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        discMat = new ShaderMaterial { Shader = BuildShader() };
        discMat.SetShaderParameter("center", new Vector2(0.5f, 0.5f));
        discMat.SetShaderParameter("span", 0.3f);
        discMat.SetShaderParameter("rot", 0f);
        discMat.SetShaderParameter("oob", new Color(0.04f, 0.05f, 0.07f));
        disc.Material = discMat;
        AddChild(disc);

        overlay = new Overlay(this)
        {
            Position = disc.Position,
            Size = disc.Size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(overlay);

        zoneLabel = UiTheme.Heading(Platform.TouchUi ? 16 : 13, "—");
        zoneLabel.Position = Platform.TouchUi
            ? new Vector2(HeaderCell + 8f, 0f)
            : new Vector2(6f, 8f);
        zoneLabel.Size = new Vector2(MapSize - (Platform.TouchUi ? HeaderCell * 3f + 16f : 12f),
                                      Platform.TouchUi ? HeaderHeight : 20f);
        if (Platform.TouchUi) zoneLabel.VerticalAlignment = VerticalAlignment.Center;
        zoneLabel.AddThemeConstantOverride("outline_size", 3);
        zoneLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        zoneLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(zoneLabel);

        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        coordChip = chip;
        chip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        chip.Position = new Vector2(8, TotalHeight - 29f);
        chip.CustomMinimumSize = new Vector2(98, 0);
        AddChild(chip);
        coordLabel = UiTheme.Text("0, 0", 11, UiTheme.TextLo, HorizontalAlignment.Center);
        coordLabel.AddThemeColorOverride("font_color", UiTheme.TextLo);
        chip.AddChild(coordLabel);

        var zin = UiTheme.IconButton("+", "Zoom in");
        var zout = UiTheme.IconButton("−", "Zoom out");
        if (Platform.TouchUi)
            foreach (var button in new[] { zin, zout })
                foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                    button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        float cell = Platform.TouchUi ? HeaderCell : 24f;
        zin.CustomMinimumSize = new Vector2(cell, cell);
        zout.CustomMinimumSize = new Vector2(cell, cell);
        if (Platform.TouchUi)
        {
            float cellTop = (HeaderHeight - cell) * 0.5f;
            zin.Position = new Vector2(MapSize - cell * 2f - 6f, cellTop);
            zout.Position = new Vector2(MapSize - cell - 2f, cellTop);
        }
        else
        {
            zin.Position = new Vector2(SquareSize - 58f, 7f);
            zout.Position = new Vector2(SquareSize - 31f, 7f);
        }
        zin.Pressed += () => Zoom(-1);
        zout.Pressed += () => Zoom(1);
        AddChild(zin);
        AddChild(zout);
        zoomIn = zin;
        zoomOut = zout;

        if (Platform.TouchUi) BuildCollapseToggle();
        BuildNpcList();
    }

    private void BuildFrame()
    {
        var frame = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        var outer = new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.014f, 0.019f, 0.55f),
            BorderColor = new Color(UiTheme.Edge, 0.40f),
        };
        outer.SetCornerRadiusAll(6);
        outer.SetBorderWidthAll(1);
        frame.AddThemeStyleboxOverride("panel", outer);
        AddChild(frame);

        var header = new Panel
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Position = Vector2.Zero,
            Size = new Vector2(MapSize, HeaderHeight),
        };
        var plate = new StyleBoxFlat
        {
            BgColor = new Color(0.030f, 0.034f, 0.042f, 0.90f),
            BorderColor = new Color(UiTheme.Edge, 0.34f),
        };
        plate.SetCornerRadiusAll(6);
        plate.CornerRadiusBottomLeft = 0;
        plate.CornerRadiusBottomRight = 0;
        plate.BorderWidthBottom = 1;
        header.AddThemeStyleboxOverride("panel", plate);
        AddChild(header);
    }

    private void BuildCollapseToggle()
    {
        var button = new Button
        {
            TooltipText = "Show/hide map",
            FocusMode = FocusModeEnum.None,
            Position = new Vector2(2f, (HeaderHeight - HeaderCell) * 0.5f),
            Size = Vector2.One * HeaderCell,
            CustomMinimumSize = Vector2.One * HeaderCell,
        };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());

        chevron = new MapChevron { MouseFilter = MouseFilterEnum.Ignore };
        chevron.SetAnchorsPreset(LayoutPreset.FullRect);
        button.AddChild(chevron);
        button.Pressed += () => SetCollapsed(!collapsed);
        AddChild(button);
    }

    private void SetCollapsed(bool on)
    {
        collapsed = on;
        Collapsed = on;
        disc.Visible = !on;
        overlay.Visible = !on;
        coordChip.Visible = !on;
        zoomIn.Visible = !on;
        zoomOut.Visible = !on;
        if (chevron != null) chevron.PointsDown = on;
        CustomMinimumSize = new Vector2(MapSize, on ? HeaderSize : TotalHeight);
        Size = CustomMinimumSize;
        CollapsedChanged?.Invoke();
    }

    private static Shader BuildShader() => Shaders.Get("minimap_disc");

    public void SetZone(string stem, string displayName)
    {
        zoneLabel.Text = displayName;
        var hm = Heightmap.LoadPng(stem);
        if (hm == null)
        {
            hasMap = false;
            mapTex = null;
            MapSource = "none";
            discMat.SetShaderParameter("map_tex", new Variant());
            return;
        }
        worldExtent = hm.WorldExtent;
        string baked = BakedMapPath(stem);
        mapTex = LoadBakedMap(baked);
        MapSource = mapTex != null ? baked : "relief";
        mapTex ??= BuildReliefTexture(hm, LoadWaterRects(stem, hm.CellSize));
        discMat.SetShaderParameter("map_tex", mapTex);
        hasMap = true;
        ApplyZoom();
    }

    public float WorldExtent => worldExtent;

    internal static ImageTexture? LoadBaked(string stem) => LoadBakedMap(BakedMapPath(stem));

    private static string BakedMapPath(string stem) =>
        $"res://assets/terrain/{stem}/minimap.webp";

    private static ImageTexture? LoadBakedMap(string path)
    {
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var img = new Image();
        if (img.LoadWebpFromBuffer(f.GetBuffer((long)f.GetLength())) != Error.Ok) return null;
        return ImageTexture.CreateFromImage(img);
    }

    public void UpdateView(float mapX, float mapZ, float headingDeg, IReadOnlyList<Blip> currentBlips)
    {
        this.mapX = mapX; this.mapZ = mapZ; this.headingDeg = headingDeg;
        blips.Clear();
        blips.AddRange(currentBlips);

        if (hasMap && worldExtent > 0f)
        {
            var center = new Vector2(mapX / worldExtent, 1f - mapZ / worldExtent);
            discMat.SetShaderParameter("center", center);
        }
        coordLabel.Text = $"{mapX:0}, {mapZ:0}";
        overlay.QueueRedraw();
    }

    private void Zoom(int dir)
    {
        zoomIndex = Mathf.Clamp(zoomIndex + dir, 0, ZoomRadii.Length - 1);
        ApplyZoom();
        overlay.QueueRedraw();
    }

    private float ViewRadiusWorld => ZoomRadii[zoomIndex];

    private void ApplyZoom()
    {
        if (!hasMap || worldExtent <= 0f) return;
        float span = 2f * ViewRadiusWorld / worldExtent;
        discMat.SetShaderParameter("span", span);
    }

    private float PxPerWorld => Diameter * 0.5f / ViewRadiusWorld;

    private readonly record struct WaterRect(float X0, float Z0, float X1, float Z1, float Level);

    private static List<WaterRect> LoadWaterRects(string stem, float cell)
    {
        var rects = new List<WaterRect>();
        using var f = FileAccess.Open($"res://assets/terrain/{stem}/water.json", FileAccess.ModeFlags.Read);
        if (f == null) return rects;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Array) return rects;
        foreach (var item in parsed.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var o = item.AsGodotDictionary();
            if (!o.TryGetValue("verts", out var vv) || vv.VariantType != Variant.Type.Array) continue;
            float level = o.TryGetValue("level_y", out var lv) ? lv.AsSingle() : 0f;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var v in vv.AsGodotArray())
            {
                var arr = v.AsGodotArray();
                if (arr.Count < 3) continue;
                float x = arr[0].AsSingle(), z = arr[2].AsSingle();
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
            }
            if (maxX > minX)
                rects.Add(new WaterRect(minX / cell, minZ / cell, maxX / cell, maxZ / cell, level));
        }
        return rects;
    }

    private static readonly (float T, Color C)[] Ramp =
    {
        (0.00f, new Color("38492f")),
        (0.18f, new Color("4a5f34")),
        (0.40f, new Color("6c7444")),
        (0.62f, new Color("8a7d57")),
        (0.80f, new Color("9c9385")),
        (1.00f, new Color("d2cec2")),
    };

    private static Color RampColor(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        for (int i = 1; i < Ramp.Length; i++)
            if (t <= Ramp[i].T)
            {
                var (t0, c0) = Ramp[i - 1];
                var (t1, c1) = Ramp[i];
                return c0.Lerp(c1, (t - t0) / Mathf.Max(0.0001f, t1 - t0));
            }
        return Ramp[^1].C;
    }

    private static ImageTexture BuildReliefTexture(Heightmap hm, List<WaterRect> water)
    {
        int n = hm.MapSize;
        int dim = Mathf.Min(n, 512);
        var buf = new byte[dim * dim * 4];

        float range = Mathf.Max(0.001f, hm.MaxHeight - hm.MinHeight);
        var light = new Vector3(-0.5f, 1.0f, 0.6f).Normalized();
        const float exaggerate = 2.6f;
        float cell = hm.CellSize;

        var waterDeep = new Color("1d3852");
        var waterShallow = new Color("2f6f86");

        for (int py = 0; py < dim; py++)
        {
            int zc = (int)MathF.Round((1f - (float)py / (dim - 1)) * (n - 1));
            for (int px = 0; px < dim; px++)
            {
                int xc = (int)MathF.Round((float)px / (dim - 1) * (n - 1));

                float h = H(hm, xc, zc);
                float hL = H(hm, xc - 1, zc), hR = H(hm, xc + 1, zc);
                float hD = H(hm, xc, zc - 1), hU = H(hm, xc, zc + 1);
                var nrm = new Vector3(-(hR - hL) / (2f * cell) * exaggerate, 1f,
                                      -(hU - hD) / (2f * cell) * exaggerate).Normalized();
                float shade = Mathf.Clamp(nrm.Dot(light), 0f, 1f);

                float hn = (h - hm.MinHeight) / range;
                Color baseCol = RampColor(hn);

                if (InWater(water, xc, zc, h))
                {
                    float d = Mathf.Clamp(1f - hn * 1.4f, 0f, 1f);
                    baseCol = waterShallow.Lerp(waterDeep, d);
                }

                float lightAmt = 0.42f + 0.78f * shade;
                var col = new Color(
                    Mathf.Clamp(baseCol.R * lightAmt, 0f, 1f),
                    Mathf.Clamp(baseCol.G * lightAmt, 0f, 1f),
                    Mathf.Clamp(baseCol.B * lightAmt, 0f, 1f));

                int o = (py * dim + px) * 4;
                buf[o]     = (byte)(col.R * 255f);
                buf[o + 1] = (byte)(col.G * 255f);
                buf[o + 2] = (byte)(col.B * 255f);
                buf[o + 3] = 255;
            }
        }

        var img = Image.CreateFromData(dim, dim, false, Image.Format.Rgba8, buf);
        return ImageTexture.CreateFromImage(img);
    }

    private static float H(Heightmap hm, int x, int z)
    {
        int n = hm.MapSize;
        x = Mathf.Clamp(x, 0, n - 1);
        z = Mathf.Clamp(z, 0, n - 1);
        return hm.Heights[z * n + x];
    }

    private static bool InWater(List<WaterRect> water, int xc, int zc, float height)
    {
        foreach (var w in water)
            if (xc >= w.X0 - 0.5f && xc <= w.X1 + 0.5f && zc >= w.Z0 - 0.5f && zc <= w.Z1 + 0.5f
                && height <= w.Level + 1.5f)
                return true;
        return false;
    }

    private void DrawOverlay(Control c)
    {
        var size = c.Size;
        var ctr = size * 0.5f;
        float radius = Diameter * 0.5f;

        if (!hasMap)
        {
            var noMap = "No map data";
            var font = c.GetThemeDefaultFont();
            c.DrawString(font, ctr + new Vector2(-44, 4), noMap, HorizontalAlignment.Left, -1, 13,
                UiTheme.TextLo);
        }
        else
        {
            DrawBlips(c, ctr, radius);
            DrawPlayer(c, ctr);
        }
    }

    private void DrawPlayer(Control c, Vector2 ctr)
    {
        float a = Mathf.DegToRad(headingDeg);
        Vector2 Rot(Vector2 v) => new(v.X * Mathf.Cos(a) - v.Y * Mathf.Sin(a),
                                      v.X * Mathf.Sin(a) + v.Y * Mathf.Cos(a));
        var tip = ctr + Rot(new Vector2(0, -9f));
        var bl = ctr + Rot(new Vector2(-6f, 6f));
        var br = ctr + Rot(new Vector2(6f, 6f));
        var notch = ctr + Rot(new Vector2(0, 2.5f));
        c.DrawColoredPolygon(new[] { tip, bl, notch }, UiTheme.Self);
        c.DrawColoredPolygon(new[] { tip, notch, br }, UiTheme.Self.Darkened(0.18f));
        c.DrawPolyline(new[] { tip, bl, notch, br, tip }, new Color(0, 0, 0, 0.75f), 1.4f, true);
    }

    private sealed partial class Overlay : Control
    {
        private readonly MiniMap map;
        public Overlay(MiniMap map) => this.map = map;
        public override void _Draw() => map.DrawOverlay(this);
    }
}
