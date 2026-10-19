using Godot;

namespace LibreKO;

internal static class HudStyle
{
    public static Label Label(int fontSize, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", Colors.White);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 4);
        return l;
    }
}

public partial class StatBar : Control
{
    public enum RibbonKind { None, Upper, Lower, Plain }

    private const float Skew = 14f;
    internal const float FillLighten = 0.34f;
    internal const float FillDarken = 0.38f;
    private const float RibbonBreathe = 0.19f;
    private const float RibbonBreatheSwing = 0.025f;
    private const float LowerCoreLift = 0.08f;
    private const float FlowMargin = 4f;
    private static Shader? glowShader, flowShader;
    private static Shader GlowShader => glowShader ??= GD.Load<Shader>("res://shaders/ribbon_glow.gdshader");
    private static Shader FlowShader => flowShader ??= GD.Load<Shader>("res://shaders/ribbon_flow.gdshader");
    private readonly Color fillColor;
    private Color? fillOverride;
    private const float TrailLighten = 0.55f;
    private const float TrailAlpha = 0.8f;

    private Color Fill => fillOverride ?? fillColor;

    public Color? FillOverride
    {
        get => fillOverride;
        set
        {
            if (fillOverride == value) return;
            fillOverride = value;
            QueueRedraw();
        }
    }
    private readonly Label textLabel;
    private readonly RibbonKind ribbon;
    private readonly Control? glow, flow, border;
    private readonly ShaderMaterial? flowMaterial;
    private float frac;

    public StatBar(Color fill, Vector2 size, RibbonKind ribbon = RibbonKind.None)
    {
        fillColor = fill;
        this.ribbon = ribbon;
        CustomMinimumSize = size;

        if (this.ribbon != RibbonKind.None)
        {
            var glowMaterial = new ShaderMaterial { Shader = GlowShader };
            glowMaterial.SetShaderParameter("breathe", new Vector3(1f - fill.R, 1f - fill.G, 1f - fill.B) * RibbonBreatheSwing);
            flowMaterial = new ShaderMaterial { Shader = FlowShader };
            glow = Layer(glowMaterial, DrawGlow);
            flow = Layer(flowMaterial, DrawFlow);
            border = Layer(null, DrawBorder);
        }

        textLabel = HudStyle.Label(this.ribbon is RibbonKind.None or RibbonKind.Plain ? 11 : 10,
                               HorizontalAlignment.Center);
        textLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.86f, 0.84f, 0.94f));
        textLabel.AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.045f, 0.05f, 0.82f));
        textLabel.AddThemeConstantOverride("outline_size",
            this.ribbon is RibbonKind.None or RibbonKind.Plain ? 2 : 1);
        textLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(textLabel);
        Resized += Refresh;
        Refresh();
    }

    private Control Layer(Material? material, System.Action<Control> draw)
    {
        var layer = new Control { MouseFilter = MouseFilterEnum.Ignore, Material = material };
        layer.Draw += () => draw(layer);
        AddChild(layer);
        return layer;
    }

    private void Refresh()
    {
        QueueRedraw();
        if (glow == null || flow == null || border == null || flowMaterial == null) return;
        bool filled = frac > 0f;
        glow.Visible = filled;
        flow.Visible = filled;
        var quad = FilledQuad(Silhouette());
        flowMaterial.SetShaderParameter("quad_top_left", quad[0]);
        flowMaterial.SetShaderParameter("quad_top_right", quad[1]);
        flowMaterial.SetShaderParameter("quad_bottom_right", quad[2]);
        flowMaterial.SetShaderParameter("quad_bottom_left", quad[3]);
        glow.QueueRedraw();
        flow.QueueRedraw();
        border.QueueRedraw();
    }

    private int cur = -1, max = -1;
    private string? fullText;
    private BarGlide? glide;
    private bool snapNext = true;

    public bool Glide
    {
        get => glide != null;
        set => glide = value ? new BarGlide() : null;
    }

    public void SnapNext() => snapNext = true;

    public void Set(int cur, int max)
    {
        if (cur == this.cur && max == this.max) return;
        this.cur = cur;
        this.max = max;
        fullText = null;
        float frac = max > 0 ? Mathf.Clamp((float)cur / max, 0f, 1f) : 0f;
        textLabel.Text = $"{cur} / {max}";
        if (glide == null || snapNext)
        {
            snapNext = false;
            glide?.Snap(frac);
            this.frac = frac;
            Refresh();
            return;
        }
        glide.Aim(frac);
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (glide == null || !glide.Step(delta))
        {
            if (glide != null) frac = glide.Shown;
            SetProcess(false);
            Refresh();
            return;
        }
        frac = glide.Shown;
        Refresh();
    }

    public void SetFraction(float fraction, string text)
    {
        cur = max = -1;
        fullText = null;
        frac = Mathf.Clamp(fraction, 0f, 1f);
        textLabel.Text = text;
        Refresh();
    }

    public void SetFull(string text = "")
    {
        if (fullText == text) return;
        fullText = text;
        cur = max = -1;
        frac = 1f;
        textLabel.Text = text;
        Refresh();
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        if (w <= 0f || h <= 0f) return;
        if (ribbon != RibbonKind.None)
        {
            DrawRibbon();
            return;
        }
        if (ribbon == RibbonKind.Plain)
        {
            DrawPlain(w, h);
            return;
        }

        Vector2[] outer = { new(Skew, 0), new(w, 0), new(w - Skew, h), new(0, h) };
        DrawColoredPolygon(outer, new Color(0.025f, 0.026f, 0.030f, 0.92f));

        if (frac > 0f)
        {
            float topLeft = Skew + 2f, bottomLeft = 3f;
            float topLimit = w - 3f, bottomLimit = w - Skew - 2f;
            float topRight = Mathf.Lerp(topLeft, topLimit, frac);
            float bottomRight = Mathf.Lerp(bottomLeft, bottomLimit, frac);
            Vector2[] fill =
            {
                new(topLeft, 3), new(topRight, 3),
                new(bottomRight, h - 3), new(bottomLeft, h - 3),
            };
            Color top = Fill.Lightened(FillLighten);
            Color bottom = Fill.Darkened(FillDarken);
            DrawPolygon(fill, new[] { top, top, bottom, bottom });
            if (glide != null && glide.Trail > frac)
            {
                float trailTop = Mathf.Lerp(topLeft, topLimit, glide.Trail);
                float trailBottom = Mathf.Lerp(bottomLeft, bottomLimit, glide.Trail);
                Vector2[] trail =
                {
                    new(topRight, 3), new(trailTop, 3),
                    new(trailBottom, h - 3), new(bottomRight, h - 3),
                };
                DrawColoredPolygon(trail, TrailColour);
            }

            float filledSpan = Mathf.Max(0f, topRight - topLeft);
            for (int i = 0; i < 7; i++)
            {
                float x = topLeft + filledSpan * ((i + 1f) / 8f);
                if (x >= topRight - 3f) break;
                float y = 5f + ((i * 7) % Mathf.Max(2, (int)h - 10));
                DrawCircle(new Vector2(x, y), i % 3 == 0 ? 1.1f : 0.65f,
                    new Color(1f, 1f, 0.9f, i % 3 == 0 ? 0.24f : 0.15f));
            }
        }

        Vector2[] gloss =
        {
            new(Skew + 1f, 2), new(w - 2f, 2),
            new(w - Skew * 0.42f, h * 0.43f), new(Skew * 0.58f, h * 0.43f),
        };
        DrawColoredPolygon(gloss, new Color(1f, 1f, 1f, 0.09f));

        Vector2[] borderPoints = { outer[0], outer[1], outer[2], outer[3], outer[0] };
        DrawPolyline(borderPoints, new Color(0.025f, 0.027f, 0.031f, 0.98f), 3f, true);
        DrawPolyline(borderPoints, new Color(0.49f, 0.41f, 0.23f, 0.90f), 1.25f, true);
    }

    private void DrawPlain(float w, float h)
    {
        DrawRect(new Rect2(0f, 0f, w, h), new Color(0.020f, 0.022f, 0.027f, 0.90f));
        if (Trailing)
            DrawRect(new Rect2(1f, 1f, Mathf.Max(0f, (w - 2f) * glide!.Trail), h - 2f), TrailColour);
        if (frac > 0f)
        {
            var fill = new Rect2(1f, 1f, Mathf.Max(0f, (w - 2f) * frac), h - 2f);
            DrawRect(fill, Fill.Darkened(0.30f));
            DrawRect(new Rect2(fill.Position, new Vector2(fill.Size.X, fill.Size.Y * 0.45f)),
                     Fill.Lightened(0.22f));
        }
        DrawRect(new Rect2(0.5f, 0.5f, w - 1f, h - 1f),
                 new Color(0.62f, 0.56f, 0.38f, 0.55f), false, 1f);
    }

    private static readonly Vector2[] UpperShape = { new(0, 0), new(251, 0), new(273, 23), new(23, 24) };
    private static readonly Vector2 UpperFrame = new(274, 25);
    private static readonly Vector2[] LowerShape = { new(13, 0), new(264, 1), new(245, 16), new(0, 15) };
    private static readonly Vector2 LowerFrame = new(265, 17);

    public bool ShowNumbers
    {
        get => textLabel.Visible;
        set => textLabel.Visible = value;
    }

    private Vector2[] Silhouette() => ribbon switch
    {
        RibbonKind.Upper => Fitted(UpperShape, UpperFrame),
        RibbonKind.Lower => Fitted(LowerShape, LowerFrame),
        _ => new Vector2[] { new(0, 0), new(Size.X, 0), new(Size.X, Size.Y), new(0, Size.Y) },
    };

    private Vector2[] Fitted(Vector2[] shape, Vector2 frame)
    {
        float scale = Size.Y / frame.Y;
        var fitted = new Vector2[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            var point = shape[i];
            float x = point.X * 2f < frame.X ? point.X * scale : Size.X - (frame.X - point.X) * scale;
            fitted[i] = new Vector2(x, point.Y * scale);
        }
        return fitted;
    }

    private Vector2[] FilledQuad(Vector2[] silhouette) => FilledQuad(silhouette, frac);

    private static Vector2[] FilledQuad(Vector2[] silhouette, float fraction) => new[]
    {
        silhouette[0],
        silhouette[0].Lerp(silhouette[1], fraction),
        silhouette[3].Lerp(silhouette[2], fraction),
        silhouette[3],
    };

    private bool Trailing => glide != null && glide.Trail > frac;

    private Color TrailColour => new(Fill.Lightened(TrailLighten), TrailAlpha);

    private Color RibbonEdge() => Fill.Darkened(ribbon == RibbonKind.Lower ? 0.40f : 0.45f);

    private void DrawRibbon()
    {
        var silhouette = Silhouette();
        DrawColoredPolygon(silhouette, new Color(0.035f, 0.038f, 0.040f, 0.90f));
        if (Trailing) DrawColoredPolygon(FilledQuad(silhouette, glide!.Trail), TrailColour);
        if (frac > 0f) DrawColoredPolygon(FilledQuad(silhouette), RibbonEdge());
    }

    private void DrawGlow(Control layer)
    {
        if (frac <= 0f) return;
        var filled = FilledQuad(Silhouette());
        Color edge = RibbonEdge();
        Color core = Fill.Lightened(ribbon == RibbonKind.Upper ? RibbonBreathe : RibbonBreathe + LowerCoreLift);

        Vector2 a = filled[0].Lerp(filled[3], 0.22f);
        Vector2 b = filled[1].Lerp(filled[2], 0.22f);
        Vector2 c = filled[1].Lerp(filled[2], 0.72f);
        Vector2 d = filled[0].Lerp(filled[3], 0.72f);
        layer.DrawPolygon(new[] { a, b, c, d },
            new[] { edge, edge, core, core },
            new[] { Vector2.Zero, Vector2.Zero, Vector2.Right, Vector2.Right });
        Vector2 e = filled[0].Lerp(filled[3], 0.48f);
        Vector2 f = filled[1].Lerp(filled[2], 0.48f);
        Vector2 g = filled[1].Lerp(filled[2], 0.88f);
        Vector2 j = filled[0].Lerp(filled[3], 0.88f);
        layer.DrawPolygon(new[] { e, f, g, j },
            new[] { core, core, edge, edge },
            new[] { Vector2.Right, Vector2.Right, Vector2.Zero, Vector2.Zero });
    }

    private void DrawFlow(Control layer)
    {
        var silhouette = Silhouette();
        var bounds = new Rect2(silhouette[0], Vector2.Zero);
        foreach (var point in silhouette) bounds = bounds.Expand(point);
        layer.DrawRect(bounds.Grow(FlowMargin), Colors.White);
    }

    private void DrawBorder(Control layer)
    {
        var silhouette = Silhouette();
        Vector2[] borderPoints =
        {
            silhouette[0], silhouette[1], silhouette[2], silhouette[3], silhouette[0],
        };
        layer.DrawPolyline(borderPoints, new Color(Fill, 0.13f), 3f, true);
        layer.DrawPolyline(borderPoints, new Color(0.055f, 0.060f, 0.060f, 0.96f), 1.5f, true);
        layer.DrawPolyline(borderPoints, new Color(0.56f, 0.48f, 0.27f, 0.48f), 0.75f, true);
    }
}

public partial class LevelOrb : Control
{
    private const float IconInset = 0.28f;
    private static readonly Color IconTint = new(0.86f, 0.87f, 0.90f);
    private static readonly Color HighlightIconTint = new(0.12f, 0.09f, 0.04f);
    private static readonly Color Face = new(0.115f, 0.120f, 0.125f, 0.97f);
    private static readonly Color HighlightFace = new(0.86f, 0.66f, 0.27f, 0.98f);
    private readonly CenterContainer labels;
    private readonly Label levelLabel;
    private readonly Label exp;
    private TextureRect? icon;
    private bool highlighted;
    private float expFraction;
    private float expShown;
    private bool expPlaced;

    public LevelOrb(float size)
    {
        CustomMinimumSize = new Vector2(size, size);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        labels = center;
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", -2);
        center.AddChild(vb);

        levelLabel = HudStyle.Label(20, HorizontalAlignment.Center);
        levelLabel.AddThemeConstantOverride("outline_size", 2);
        exp = HudStyle.Label(11, HorizontalAlignment.Center);
        exp.AddThemeColorOverride("font_color", new Color(0.83f, 0.82f, 0.78f));
        exp.AddThemeConstantOverride("outline_size", 2);
        vb.AddChild(levelLabel);
        vb.AddChild(exp);
        exp.Visible = Platform.PointerUi;
        Resized += QueueRedraw;
    }

    public Texture2D? Icon
    {
        get => icon?.Texture;
        set
        {
            if (icon == null)
            {
                float inset = Mathf.Min(CustomMinimumSize.X, CustomMinimumSize.Y) * IconInset;
                icon = new TextureRect
                {
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                    SelfModulate = highlighted ? HighlightIconTint : IconTint,
                };
                icon.SetAnchorsPreset(LayoutPreset.FullRect);
                icon.OffsetLeft = icon.OffsetTop = inset;
                icon.OffsetRight = icon.OffsetBottom = -inset;
                AddChild(icon);
            }
            icon.Texture = value;
            labels.Visible = value == null;
        }
    }

    public bool Highlighted
    {
        get => highlighted;
        set
        {
            if (highlighted == value) return;
            highlighted = value;
            if (icon != null) icon.SelfModulate = value ? HighlightIconTint : IconTint;
            QueueRedraw();
        }
    }

    public void Set(int level, double expPercent)
    {
        levelLabel.Text = level.ToString();
        exp.Text = $"{expPercent:0.000}%";
        SetGauge(expPercent);
    }

    public void SetGauge(double percent)
    {
        float fraction = Mathf.Clamp((float)(percent / 100.0), 0f, 1f);
        if (expPlaced && Mathf.IsEqualApprox(fraction, expFraction)) return;
        expFraction = fraction;
        if (!expPlaced || expFraction < expShown) expShown = expFraction;
        expPlaced = true;
        SetProcess(expShown < expFraction);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        expShown = Mathf.MoveToward(expShown, expFraction, BarGlide.GlidePerSecond * (float)delta);
        if (expShown >= expFraction) SetProcess(false);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y);
        if (s <= 0f) return;
        float half = s * 0.5f;
        float r = half - 1f;
        var c = new Vector2(Size.X * 0.5f, Size.Y * 0.5f);
        Vector2[] dia =
        {
            new(c.X, c.Y - r), new(c.X + r, c.Y), new(c.X, c.Y + r), new(c.X - r, c.Y),
        };
        DrawColoredPolygon(dia, highlighted ? HighlightFace : Face);
        Vector2[] outline = { dia[0], dia[1], dia[2], dia[3], dia[0] };
        DrawPolyline(outline, new Color(0.025f, 0.027f, 0.030f, 0.98f), 3f, true);
        DrawPolyline(outline, new Color(0.24f, 0.22f, 0.17f, 0.92f), 1.25f, true);

        float innerR = r - 5f;
        Vector2[] inner =
        {
            new(c.X, c.Y - innerR), new(c.X + innerR, c.Y),
            new(c.X, c.Y + innerR), new(c.X - innerR, c.Y), new(c.X, c.Y - innerR),
        };
        DrawPolyline(inner, new Color(0.25f, 0.27f, 0.28f, 0.78f), 1f, true);

        DrawExpGauge(dia);
    }

    private void DrawExpGauge(Vector2[] diamond)
    {
        Vector2[] path = { diamond[2], diamond[3], diamond[0], diamond[1], diamond[2] };
        float remaining = expShown * 4f;
        var glow = new Color(0.86f, 0.64f, 0.22f, 0.18f);
        var gold = new Color(0.86f, 0.66f, 0.27f, 0.98f);
        var highlight = new Color(1.00f, 0.82f, 0.39f, 0.72f);

        for (int side = 0; side < 4 && remaining > 0f; side++)
        {
            float amount = Mathf.Min(remaining, 1f);
            Vector2 end = path[side].Lerp(path[side + 1], amount);
            DrawLine(path[side], end, glow, 4f, true);
            DrawLine(path[side], end, gold, 1.6f, true);
            DrawLine(path[side], end, highlight, 0.65f, true);
            remaining -= amount;
        }
    }
}

public partial class MapMarker : Control
{
    public MapMarker() => CustomMinimumSize = new Vector2(14, 18);

    public override void _Draw()
    {
        var gold = new Color("e0c25a");
        DrawCircle(new Vector2(7, 6), 6f, gold);
        DrawColoredPolygon(new Vector2[] { new(2, 8), new(12, 8), new(7, 17) }, gold);
        DrawCircle(new Vector2(7, 6), 2.4f, new Color("1b1f26"));
    }
}
