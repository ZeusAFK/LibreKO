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
    private const float RibbonBreathe = 0.19f;
    private const float RibbonBreatheSwing = 0.025f;
    private const float LowerCoreLift = 0.08f;
    private const float FlowMargin = 4f;
    private static Shader? _glowShader, _flowShader;
    private static Shader GlowShader => _glowShader ??= GD.Load<Shader>("res://shaders/ribbon_glow.gdshader");
    private static Shader FlowShader => _flowShader ??= GD.Load<Shader>("res://shaders/ribbon_flow.gdshader");
    private readonly Color _fill;
    private readonly Label _text;
    private readonly RibbonKind _ribbon;
    private readonly Control? _glow, _flow, _border;
    private readonly ShaderMaterial? _flowMaterial;
    private float _frac;

    public StatBar(Color fill, Vector2 size, RibbonKind ribbon = RibbonKind.None)
    {
        _fill = fill;
        _ribbon = ribbon;
        CustomMinimumSize = size;

        if (_ribbon != RibbonKind.None)
        {
            var glowMaterial = new ShaderMaterial { Shader = GlowShader };
            glowMaterial.SetShaderParameter("breathe", new Vector3(1f - fill.R, 1f - fill.G, 1f - fill.B) * RibbonBreatheSwing);
            _flowMaterial = new ShaderMaterial { Shader = FlowShader };
            _glow = Layer(glowMaterial, DrawGlow);
            _flow = Layer(_flowMaterial, DrawFlow);
            _border = Layer(null, DrawBorder);
        }

        _text = HudStyle.Label(_ribbon is RibbonKind.None or RibbonKind.Plain ? 11 : 10,
                               HorizontalAlignment.Center);
        _text.AddThemeColorOverride("font_color", new Color(0.85f, 0.86f, 0.84f, 0.94f));
        _text.AddThemeColorOverride("font_outline_color", new Color(0.04f, 0.045f, 0.05f, 0.82f));
        _text.AddThemeConstantOverride("outline_size",
            _ribbon is RibbonKind.None or RibbonKind.Plain ? 2 : 1);
        _text.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_text);
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
        if (_glow == null || _flow == null || _border == null || _flowMaterial == null) return;
        bool filled = _frac > 0f;
        _glow.Visible = filled;
        _flow.Visible = filled;
        var quad = FilledQuad(Silhouette());
        _flowMaterial.SetShaderParameter("quad_top_left", quad[0]);
        _flowMaterial.SetShaderParameter("quad_top_right", quad[1]);
        _flowMaterial.SetShaderParameter("quad_bottom_right", quad[2]);
        _flowMaterial.SetShaderParameter("quad_bottom_left", quad[3]);
        _glow.QueueRedraw();
        _flow.QueueRedraw();
        _border.QueueRedraw();
    }

    private int _cur = -1, _max = -1;
    private string? _fullText;

    public void Set(int cur, int max)
    {
        if (cur == _cur && max == _max) return;
        _cur = cur;
        _max = max;
        _fullText = null;
        _frac = max > 0 ? Mathf.Clamp((float)cur / max, 0f, 1f) : 0f;
        _text.Text = $"{cur} / {max}";
        Refresh();
    }

    public void SetFraction(float fraction, string text)
    {
        _cur = _max = -1;
        _fullText = null;
        _frac = Mathf.Clamp(fraction, 0f, 1f);
        _text.Text = text;
        Refresh();
    }

    public void SetFull(string text = "")
    {
        if (_fullText == text) return;
        _fullText = text;
        _cur = _max = -1;
        _frac = 1f;
        _text.Text = text;
        Refresh();
    }

    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        if (w <= 0f || h <= 0f) return;
        if (_ribbon != RibbonKind.None)
        {
            DrawRibbon();
            return;
        }
        if (_ribbon == RibbonKind.Plain)
        {
            DrawPlain(w, h);
            return;
        }

        Vector2[] outer = { new(Skew, 0), new(w, 0), new(w - Skew, h), new(0, h) };
        DrawColoredPolygon(outer, new Color(0.025f, 0.026f, 0.030f, 0.92f));

        if (_frac > 0f)
        {
            float topLeft = Skew + 2f, bottomLeft = 3f;
            float topLimit = w - 3f, bottomLimit = w - Skew - 2f;
            float topRight = Mathf.Lerp(topLeft, topLimit, _frac);
            float bottomRight = Mathf.Lerp(bottomLeft, bottomLimit, _frac);
            Vector2[] fill =
            {
                new(topLeft, 3), new(topRight, 3),
                new(bottomRight, h - 3), new(bottomLeft, h - 3),
            };
            Color top = _fill.Lightened(0.34f);
            Color bottom = _fill.Darkened(0.38f);
            DrawPolygon(fill, new[] { top, top, bottom, bottom });

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

        Vector2[] border = { outer[0], outer[1], outer[2], outer[3], outer[0] };
        DrawPolyline(border, new Color(0.025f, 0.027f, 0.031f, 0.98f), 3f, true);
        DrawPolyline(border, new Color(0.49f, 0.41f, 0.23f, 0.90f), 1.25f, true);
    }

    private void DrawPlain(float w, float h)
    {
        DrawRect(new Rect2(0f, 0f, w, h), new Color(0.020f, 0.022f, 0.027f, 0.90f));
        if (_frac > 0f)
        {
            var fill = new Rect2(1f, 1f, Mathf.Max(0f, (w - 2f) * _frac), h - 2f);
            DrawRect(fill, _fill.Darkened(0.30f));
            DrawRect(new Rect2(fill.Position, new Vector2(fill.Size.X, fill.Size.Y * 0.45f)),
                     _fill.Lightened(0.22f));
        }
        DrawRect(new Rect2(0.5f, 0.5f, w - 1f, h - 1f),
                 new Color(0.62f, 0.56f, 0.38f, 0.55f), false, 1f);
    }

    private Vector2[] Silhouette() => _ribbon switch
    {
        RibbonKind.Upper => new Vector2[] { new(0, 0), new(251, 0), new(273, 23), new(23, 24) },
        RibbonKind.Lower => new Vector2[] { new(13, 0), new(264, 1), new(245, 16), new(0, 15) },
        _ => new Vector2[] { new(0, 0), new(Size.X, 0), new(Size.X, Size.Y), new(0, Size.Y) },
    };

    private Vector2[] FilledQuad(Vector2[] silhouette) => new[]
    {
        silhouette[0],
        silhouette[0].Lerp(silhouette[1], _frac),
        silhouette[3].Lerp(silhouette[2], _frac),
        silhouette[3],
    };

    private Color RibbonEdge() => _fill.Darkened(_ribbon == RibbonKind.Lower ? 0.40f : 0.45f);

    private void DrawRibbon()
    {
        var silhouette = Silhouette();
        DrawColoredPolygon(silhouette, new Color(0.035f, 0.038f, 0.040f, 0.90f));
        if (_frac > 0f) DrawColoredPolygon(FilledQuad(silhouette), RibbonEdge());
    }

    private void DrawGlow(Control layer)
    {
        if (_frac <= 0f) return;
        var filled = FilledQuad(Silhouette());
        Color edge = RibbonEdge();
        Color core = _fill.Lightened(_ribbon == RibbonKind.Upper ? RibbonBreathe : RibbonBreathe + LowerCoreLift);

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
        Vector2[] border =
        {
            silhouette[0], silhouette[1], silhouette[2], silhouette[3], silhouette[0],
        };
        layer.DrawPolyline(border, new Color(_fill, 0.13f), 3f, true);
        layer.DrawPolyline(border, new Color(0.055f, 0.060f, 0.060f, 0.96f), 1.5f, true);
        layer.DrawPolyline(border, new Color(0.56f, 0.48f, 0.27f, 0.48f), 0.75f, true);
    }
}

public partial class LevelOrb : Control
{
    private readonly Label _level;
    private readonly Label _exp;
    private float _expFraction;

    public LevelOrb(float size)
    {
        CustomMinimumSize = new Vector2(size, size);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", -2);
        center.AddChild(vb);

        _level = HudStyle.Label(20, HorizontalAlignment.Center);
        _level.AddThemeConstantOverride("outline_size", 2);
        _exp = HudStyle.Label(11, HorizontalAlignment.Center);
        _exp.AddThemeColorOverride("font_color", new Color(0.83f, 0.82f, 0.78f));
        _exp.AddThemeConstantOverride("outline_size", 2);
        vb.AddChild(_level);
        vb.AddChild(_exp);
        _exp.Visible = Platform.PointerUi;
        Resized += QueueRedraw;
    }

    public void Set(int level, double expPercent)
    {
        _expFraction = Mathf.Clamp((float)(expPercent / 100.0), 0f, 1f);
        _level.Text = level.ToString();
        _exp.Text = $"{expPercent:0.000}%";
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
        DrawColoredPolygon(dia, new Color(0.115f, 0.120f, 0.125f, 0.97f));
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
        float remaining = _expFraction * 4f;
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
