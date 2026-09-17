using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal sealed class FloaterSystem
{
    private enum Kind { Damage, Wound, Cure, RegenHp, RegenMp, Exp, Gold, Item, Notice }

    private sealed class Floater
    {
        public Control Node = null!;
        public int AnchorId;
        public float AnchorY;
        public Vector3 Anchor;
        public Vector2 Base;
        public Vector2 Offset;
        public Rect2 Screen;
        public double Start;
        public float Life;
        public float Rise;
        public float Drift;
    }

    private const int MaxLive = 40;
    private const float CombatLift = 0.55f;
    private const float RewardChest = 0.68f;
    private const float RowGap = 3f;
    private const float CullDistance = 55f;
    private const int IconSize = 24;

    private readonly IWorldContext _ctx;
    private readonly List<Floater> _live = new();
    private CanvasLayer _layer = null!;
    private Control _host = null!;

    internal FloaterSystem(IWorldContext ctx) => _ctx = ctx;

    internal void Build()
    {
        _layer = new CanvasLayer { Layer = 59 };
        _ctx.Root.AddChild(_layer);

        _host = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_host);
    }

    internal void Dispose()
    {
        _live.Clear();
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
    }

    internal void Damage(int victimId, int amount)
    {
        if (!Config.DamageNumbers) return;
        Spawn(Kind.Damage, victimId, $"{amount:n0}", null);
    }

    internal void Wound(int amount)
    {
        if (!Config.DamageNumbers) return;
        Spawn(Kind.Wound, _ctx.SelfCharId, $"-{amount:n0}", null);
    }

    internal void Cure(int charId, int amount)
    {
        if (!Config.DamageNumbers) return;
        Spawn(Kind.Cure, charId, $"+{amount:n0}", null);
    }

    internal void RegenHp(int amount)
    {
        if (!Config.DamageNumbers) return;
        Spawn(Kind.RegenHp, _ctx.SelfCharId, $"+{amount:n0} HP", null);
    }

    internal void RegenMp(int amount)
    {
        if (!Config.DamageNumbers) return;
        Spawn(Kind.RegenMp, _ctx.SelfCharId, $"+{amount:n0} MP", null);
    }

    internal void Exp(long amount) => Spawn(Kind.Exp, _ctx.SelfCharId, $"+{amount:n0} EXP", null);

    internal void Gold(long amount) => Spawn(Kind.Gold, _ctx.SelfCharId, $"+{amount:n0} Gold", null);

    internal void Item(int itemId, int count) => Spawn(
        Kind.Item, _ctx.SelfCharId,
        $"{ItemData.DisplayName(itemId)} x{Mathf.Max(1, count)}", ItemData.Icon(itemId));

    internal void Notice(string text) => Spawn(Kind.Notice, _ctx.SelfCharId, text, null);

    private static (Color Tint, int Font, float Life, bool Overhead, float Jitter) Look(Kind kind) => kind switch
    {
        Kind.Damage  => (new Color("f5f3ec"), 26, 0.95f, true,  11f),
        Kind.Wound   => (new Color("ff5647"), 28, 1.05f, true,  11f),
        Kind.Cure    => (new Color("5bffa2"), 24, 1.05f, true,  10f),
        Kind.RegenHp => (new Color("74d79b"), 18, 0.95f, false, 6f),
        Kind.RegenMp => (new Color("5cb2ff"), 18, 0.95f, false, 6f),
        Kind.Exp     => (new Color("ffd451"), 19, 1.45f, false, 5f),
        Kind.Gold    => (new Color("ffc03a"), 19, 1.55f, false, 5f),
        Kind.Notice  => (new Color("ff9a6a"), 20, 2.10f, true,  4f),
        _            => (new Color("ffe488"), 18, 1.95f, false, 4f),
    };

    private void Spawn(Kind kind, int anchorId, string text, Texture2D? icon)
    {
        if (_host == null || !GodotObject.IsInstanceValid(_host)) return;
        if (_ctx.BodyOf(anchorId) is not { } body) return;

        var (tint, font, life, overhead, jitter) = Look(kind);
        float head = _ctx.HeadHeight(anchorId);
        float band = overhead ? head + CombatLift : head * RewardChest;
        var anchor = body.GlobalPosition + new Vector3(0, band, 0);
        var cam = Camera();

        float distance = cam != null ? cam.GlobalPosition.DistanceTo(anchor) : 0f;
        if (distance > CullDistance) return;

        float ui = Mathf.Clamp(ViewportSize().Y / 1080f, 0.8f, 2f);
        float near = Mathf.Clamp(1.15f - distance * 0.022f, 0.62f, 1f);
        int fontSize = Mathf.Max(9, Mathf.RoundToInt(font * ui * near));

        var node = BuildNode(text, icon, tint, fontSize, out var size);
        _host.AddChild(node);

        var screen = Project(cam, anchor, ViewportSize() * new Vector2(0.5f, 0.42f), out bool onScreen);
        var centre = screen + new Vector2((float)GD.RandRange(-jitter, jitter), 0f);
        var rect = new Rect2(centre - size * 0.5f, size);
        for (int row = 0; row < 8 && Collides(rect); row++)
            rect.Position += new Vector2(0, size.Y + RowGap);

        var f = new Floater
        {
            Node = node,
            AnchorId = anchorId,
            AnchorY = band,
            Anchor = anchor,
            Base = screen,
            Offset = rect.Position + size * 0.5f - screen,
            Screen = rect,
            Start = _ctx.Now,
            Life = life,
            Rise = ViewportSize().Y * (overhead ? 0.055f : 0.075f),
            Drift = (float)GD.RandRange(-1.0, 1.0) * jitter,
        };
        node.Position = rect.Position;
        node.Visible = onScreen;
        _live.Add(f);

        while (_live.Count > MaxLive)
        {
            if (GodotObject.IsInstanceValid(_live[0].Node)) _live[0].Node.QueueFree();
            _live.RemoveAt(0);
        }
    }

    internal void Tick(double now)
    {
        var cam = Camera();
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var f = _live[i];
            if (!GodotObject.IsInstanceValid(f.Node)) { _live.RemoveAt(i); continue; }

            float t = (float)((now - f.Start) / f.Life);
            if (t >= 1f) { f.Node.QueueFree(); _live.RemoveAt(i); continue; }

            if (_ctx.BodyOf(f.AnchorId) is { } body)
                f.Anchor = body.GlobalPosition + new Vector3(0, f.AnchorY, 0);

            var screen = Project(cam, f.Anchor, f.Base, out bool onScreen);
            float glide = Mathf.Pow(t, 0.6f);
            float pop = t < 0.18f ? Pop(t / 0.18f) : 1f;
            float alpha = t < 0.09f ? t / 0.09f : t > 0.70f ? 1f - (t - 0.70f) / 0.30f : 1f;

            var centre = screen + f.Offset + new Vector2(f.Drift * glide, -f.Rise * glide);
            f.Node.Position = centre - f.Node.Size * 0.5f;
            f.Node.Scale = new Vector2(pop, pop);
            f.Node.Modulate = new Color(1f, 1f, 1f, alpha);
            f.Node.Visible = onScreen;
            f.Screen = new Rect2(f.Node.Position, f.Node.Size);
        }
    }

    private Control BuildNode(string text, Texture2D? icon, Color tint, int fontSize, out Vector2 size)
    {
        int outline = Mathf.Max(4, Mathf.RoundToInt(fontSize * 0.38f));
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", tint);
        label.AddThemeColorOverride("font_outline_color", Ink(tint));
        label.AddThemeConstantOverride("outline_size", outline);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.45f));
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", Mathf.Max(1, Mathf.RoundToInt(fontSize * 0.09f)));

        var measured = _host.GetThemeDefaultFont()
            .GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        float iconEdge = icon != null ? Mathf.Round(IconSize * fontSize / 18f) : 0f;
        float gutter = icon != null ? iconEdge + 5f : 0f;
        float pad = outline * 0.5f + 3f;
        size = new Vector2(
            Mathf.Ceil(measured.X + gutter + pad * 2f),
            Mathf.Ceil(Mathf.Max(measured.Y, iconEdge) + pad));

        var node = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = size };
        node.PivotOffset = size * 0.5f;
        if (icon != null)
        {
            var badge = new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            badge.Size = new Vector2(iconEdge, iconEdge);
            badge.Position = new Vector2(pad, (size.Y - iconEdge) * 0.5f);
            node.AddChild(badge);
        }
        label.Position = new Vector2(gutter, 0f);
        label.Size = new Vector2(size.X - gutter, size.Y);
        node.AddChild(label);
        return node;
    }

    private bool Collides(Rect2 probe)
    {
        var padded = probe.Grow(1f);
        foreach (var f in _live)
            if (padded.Intersects(f.Screen)) return true;
        return false;
    }

    private Camera3D? Camera()
    {
        var cam = _ctx.Root.GetViewport()?.GetCamera3D();
        return cam != null && GodotObject.IsInstanceValid(cam) ? cam : null;
    }

    private Vector2 ViewportSize()
    {
        var size = _host != null && GodotObject.IsInstanceValid(_host) ? _host.Size : Vector2.Zero;
        return size.Y > 1f ? size : new Vector2(1920f, 1080f);
    }

    private static Vector2 Project(Camera3D? cam, Vector3 world, Vector2 fallback, out bool onScreen)
    {
        if (cam == null) { onScreen = true; return fallback; }
        onScreen = !cam.IsPositionBehind(world);
        return cam.UnprojectPosition(world);
    }

    private static Color Ink(Color tint) => new(tint.R * 0.10f, tint.G * 0.10f, tint.B * 0.12f, 0.94f);

    private static float Pop(float x)
    {
        const float c1 = 2.2f;
        float k = x - 1f;
        return 0.62f + 0.38f * (1f + (c1 + 1f) * k * k * k + c1 * k * k);
    }
}
