using Godot;

namespace LibreKO.Features.Hud;

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
        public Kind Kind;
        public long Amount;
        public string Prefix = "";
        public string Suffix = "";
        public Label? Text;
    }

    private const int MaxLive = 40;
    private const float CombatLift = 0.55f;
    private const float RewardChest = 0.68f;
    private const float RowGap = 3f;
    private const float CullDistance = 55f;
    private const int IconSize = 24;
    private const float UiReferenceHeight = 1080f;
    private const float UiScaleMin = 0.8f;
    private const float UiScaleMax = 2f;
    private const float NearScaleMin = 0.62f;
    private const float NearScaleMax = 1f;
    private const int MinFontPx = 9;
    private const int MinOutlinePx = 4;
    private const float OutlineRatio = 0.38f;

    private readonly IWorldContext context;
    private readonly List<Floater> live = new();
    private CanvasLayer layer = null!;
    private Control host = null!;

    internal FloaterSystem(IWorldContext context) => this.context = context;

    internal void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.WorldText };
        context.Root.AddChild(layer);

        host = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(host);
    }

    internal void WarmText()
    {
        if (host == null || !GodotObject.IsInstanceValid(host)) return;
        int level = GlyphWarmer.LevelOf(host);
        var font = host.GetThemeDefaultFont();
        int shadowOutline = host.GetThemeConstant("shadow_outline_size", "Label");
        float ui = UiScaleFor(host.GetViewportRect().Size.Y);
        int smallest = int.MaxValue, largest = 0;
        foreach (var kind in System.Enum.GetValues<Kind>())
        {
            int baseFont = Look(kind).Font;
            smallest = Mathf.Min(smallest, FontPxFor(baseFont, ui, NearScaleMin));
            largest = Mathf.Max(largest, FontPxFor(baseFont, ui, NearScaleMax));
        }
        for (int size = smallest; size <= largest; size++)
        {
            GlyphWarmer.Render(font, size, 0, GlyphWarmer.AsciiRange, level);
            GlyphWarmer.Render(font, size, OutlineFor(size), GlyphWarmer.AsciiRange, level);
            if (shadowOutline > 0) GlyphWarmer.Render(font, size, shadowOutline, GlyphWarmer.AsciiRange, level);
        }
    }

    private static float UiScaleFor(float height) => Mathf.Clamp(height / UiReferenceHeight, UiScaleMin, UiScaleMax);

    private static int FontPxFor(int baseFont, float ui, float near) => Mathf.Max(MinFontPx, Mathf.RoundToInt(baseFont * ui * near));

    private static int OutlineFor(int fontSize) => Mathf.Max(MinOutlinePx, Mathf.RoundToInt(fontSize * OutlineRatio));

    internal void Dispose()
    {
        live.Clear();
        if (layer != null && GodotObject.IsInstanceValid(layer)) layer.QueueFree();
    }

    internal void Damage(int victimId, int amount)
    {
        if (!Config.DamageNumbers) return;
        SpawnAmount(Kind.Damage, victimId, amount, "", "");
    }

    internal void Wound(int amount)
    {
        if (!Config.DamageNumbers) return;
        SpawnAmount(Kind.Wound, context.Self.CharId, amount, "-", "");
    }

    internal void Cure(int charId, int amount)
    {
        if (!Config.DamageNumbers) return;
        SpawnAmount(Kind.Cure, charId, amount, "+", "");
    }

    internal void RegenHp(int amount)
    {
        if (!Config.DamageNumbers) return;
        SpawnAmount(Kind.RegenHp, context.Self.CharId, amount, "+", " HP");
    }

    internal void RegenMp(int amount)
    {
        if (!Config.DamageNumbers) return;
        SpawnAmount(Kind.RegenMp, context.Self.CharId, amount, "+", " MP");
    }

    internal void Exp(long amount) => SpawnAmount(Kind.Exp, context.Self.CharId, amount, "+", " EXP");

    internal void Gold(long amount) => SpawnAmount(Kind.Gold, context.Self.CharId, amount, "+", " Gold");

    private void SpawnAmount(Kind kind, int anchorId, long amount, string prefix, string suffix)
    {
        double now = context.Now;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var liveFloater = live[i];
            if (liveFloater.Kind != kind || liveFloater.AnchorId != anchorId || liveFloater.Text == null || !FloaterNumbers.Merges(liveFloater.Start, now)) continue;
            if (!GodotObject.IsInstanceValid(liveFloater.Text)) break;
            liveFloater.Amount += amount;
            liveFloater.Text.Text = prefix + FloaterNumbers.Format(liveFloater.Amount) + suffix;
            Refit(liveFloater);
            return;
        }
        if (Spawn(kind, anchorId, prefix + FloaterNumbers.Format(amount) + suffix, null) is { } spawned)
        {
            spawned.Kind = kind;
            spawned.Amount = amount;
            spawned.Prefix = prefix;
            spawned.Suffix = suffix;
        }
    }

    internal void Item(int itemId, int count) => Spawn(
        Kind.Item, context.Self.CharId,
        $"{ItemData.DisplayName(itemId)} x{Mathf.Max(1, count)}", ItemIcons.Get(itemId));

    internal void Notice(string text) => Spawn(Kind.Notice, context.Self.CharId, text, null);

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

    private Floater? Spawn(Kind kind, int anchorId, string text, Texture2D? icon)
    {
        if (host == null || !GodotObject.IsInstanceValid(host)) return null;
        if (context.Entities.BodyOf(anchorId) is not { } body) return null;

        var (tint, font, life, overhead, jitter) = Look(kind);
        float head = context.Entities.HeadHeight(anchorId);
        float band = overhead ? head + CombatLift : head * RewardChest;
        var anchor = body.GlobalPosition + new Vector3(0, band, 0);
        var cam = Camera();

        float distance = cam != null ? cam.GlobalPosition.DistanceTo(anchor) : 0f;
        if (distance > CullDistance) return null;

        float ui = UiScaleFor(ViewportSize().Y);
        float near = Mathf.Clamp(1.15f - distance * 0.022f, NearScaleMin, NearScaleMax);
        int fontSize = FontPxFor(font, ui, near);

        var node = BuildNode(text, icon, tint, fontSize, out var size);
        host.AddChild(node);

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
            Start = context.Now,
            Life = life,
            Rise = ViewportSize().Y * (overhead ? 0.055f : 0.075f),
            Drift = (float)GD.RandRange(-1.0, 1.0) * jitter,
            Kind = kind,
            Text = node.GetChildCount() > 0 ? node.GetChild<Control>(node.GetChildCount() - 1) as Label : null,
        };
        node.Position = rect.Position;
        node.Visible = onScreen;
        live.Add(f);

        while (live.Count > MaxLive)
        {
            if (GodotObject.IsInstanceValid(live[0].Node)) live[0].Node.QueueFree();
            live.RemoveAt(0);
        }
        return f;
    }

    internal void Tick(double now)
    {
        var cam = Camera();
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var f = live[i];
            if (!GodotObject.IsInstanceValid(f.Node)) { live.RemoveAt(i); continue; }

            float t = Mathf.Max(0f, (float)((now - f.Start) / f.Life));
            if (t >= 1f) { f.Node.QueueFree(); live.RemoveAt(i); continue; }

            if (context.Entities.BodyOf(f.AnchorId) is { } body)
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
        int outline = OutlineFor(fontSize);
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

        var measured = host.GetThemeDefaultFont()
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

    private void Refit(Floater floater)
    {
        if (floater.Text == null || !GodotObject.IsInstanceValid(floater.Node)) return;
        int fontSize = floater.Text.GetThemeFontSize("font_size");
        float pad = OutlineFor(fontSize) * 0.5f + 3f;
        var measured = host.GetThemeDefaultFont().GetStringSize(floater.Text.Text, HorizontalAlignment.Left, -1, fontSize);
        float width = Mathf.Ceil(measured.X + pad * 2f);
        if (width <= floater.Node.Size.X) return;
        floater.Node.Size = new Vector2(width, floater.Node.Size.Y);
        floater.Node.PivotOffset = floater.Node.Size * 0.5f;
        floater.Text.Size = new Vector2(width, floater.Text.Size.Y);
    }

    private bool Collides(Rect2 probe)
    {
        var padded = probe.Grow(1f);
        foreach (var f in live)
            if (padded.Intersects(f.Screen)) return true;
        return false;
    }

    private Camera3D? Camera()
    {
        var cam = context.Root.GetViewport()?.GetCamera3D();
        return cam != null && GodotObject.IsInstanceValid(cam) ? cam : null;
    }

    private Vector2 ViewportSize()
    {
        var size = host != null && GodotObject.IsInstanceValid(host) ? host.Size : Vector2.Zero;
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
