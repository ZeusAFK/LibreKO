
using Godot;

namespace LibreKO;

public static class TouchControls
{
    public const float StickSize = 196f;
    public const float TipSize = 80f;
    public const float StickMargin = 44f;

    public const float LookStickSize = 140f;
    public const float LookTipSize = 58f;
    public const float Orbit = 143f;
    public const float ActionSize = 70f;
    public const float ThumbInset = 168f;
    public const int ActionSlots = 4;
    public const int ActionPages = 3;
    public const float ClusterMargin = 44f;
    public const float PageButtonSize = 70f;
    public const float PageButtonAngle = 318f;
    public const float InteractButtonSize = 70f;
    public const float InteractButtonOrbit = 1.78f;
    public const float TargetButtonSize = 70f;
    public const float TargetButtonAngle = 242f;
    public const float LootButtonAngle = 230f;
    public const float NpcButtonAngle = 210f;
    public const float AnvilButtonAngle = 270f;
    public const float TeleportButtonAngle = 250f;
    public const float UserButtonAngle = 190f;
    public const float MarketButtonAngle = 170f;
    public const float MarketSetupButtonAngle = 150f;
    public const float PrimarySize = 78f;
    public const float PrimaryAngle = 136f;
    public const float PrimaryOrbit = 194f;
    public const float DotSize = 12f;
    public const float DotGap = 8f;
    public const float PotionSize = 66f;
    public const float PotionOrbit = 2.02f;
    public const float TapSlop = 14f;
    public const float IconInset = 0.94f;
    public const string CircleShaderPath = "res://shaders/circle_icon.gdshader";
    public const string IconChildName = "SlotIcon";
    public const string PageIconId = "system/refresh";
    public const string StopGlyphName = "StopGlyph";
    public const string CooldownName = "Cooldown";

    private static readonly float[] SlotAngles = { 216f, 165f, 104f, 46f };

    private static readonly float[] PotionAngles = { 172f, 205f };

    public static bool Available => Platform.TouchUi;

    public static float ReservedBottom => Px(StickSize + StickMargin);

    private static float Px(float design) => design * Platform.TouchPixel;

    private static Vector2 Px(Vector2 design) => design * Platform.TouchPixel;

    private readonly record struct Orb(float Angle, float Radius, float Size);

    public static VirtualJoystick BuildStick(Node parent, Action? onTap = null)
    {
        var stick = Stick("MoveJoystick", StickSize, TipSize,
                          KeyBinds.TouchLeft, KeyBinds.TouchRight,
                          KeyBinds.TouchUp, KeyBinds.TouchDown);
        parent.AddChild(stick);
        float edge = Px(ThumbInset - StickSize * 0.5f);
        HudAnchor.Pin(stick, HudAnchor.Spot.BottomLeft,
            new Vector2(edge, edge + HudPlacement.BottomInset),
            new Vector2(Px(StickSize), Px(StickSize)));
        if (onTap != null) stick.Tapped += onTap;
        return stick;
    }

    private static VirtualJoystick Stick(string name, float size, float tip,
                                         string left, string right, string up, string down)
    {
        var stick = new VirtualJoystick
        {
            Name = name,
            JoystickMode = VirtualJoystick.JoystickModeEnum.Fixed,
            VisibilityMode = VirtualJoystick.VisibilityModeEnum.Always,
            JoystickSize = Px(size),
            TipSize = Px(tip),
            ActionLeft = left,
            ActionRight = right,
            ActionUp = up,
            ActionDown = down,
        };
        StyleStick(stick);
        var arrows = new StickArrows
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = Vector2.One * Px(size),
        };
        stick.AddChild(arrows);
        return stick;
    }

    public static TouchCameraZone BuildCameraZone(Node parent, Action<Vector2> orbit, Action tapTarget)
    {
        var zone = new TouchCameraZone
        {
            Name = "TouchCameraZone",
            Orbit = orbit,
            TapTarget = tapTarget,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        zone.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(zone);
        return zone;
    }

    public static TouchActionBar BuildActions(Node parent, Action onAttack, Action<int> onSlot,
                                              Action<int> onPage, Func<int, Texture2D?> iconFor,
                                              Action onTapTarget,
                                              Action<int, int, int>? onDrop = null,
                                              Action? onNpcInteract = null,
                                              Action? onLootOpen = null,
                                              Action? onAnvilOpen = null,
                                              Action? onTeleportOpen = null,
                                              Action? onUserOpen = null,
                                              Action? onMarketBrowse = null,
                                              Action? onMarketSetup = null)
    {
        var orbs = new Orb[ActionSlots + 11 + PotionAngles.Length];
        orbs[0] = new Orb(0f, 0f, LookStickSize);
        for (int i = 0; i < ActionSlots; i++)
            orbs[i + 1] = new Orb(SlotAngles[i], Orbit, ActionSize);
        orbs[ActionSlots + 1] = new Orb(PageButtonAngle, Orbit, PageButtonSize);
        orbs[ActionSlots + 2] = new Orb(PrimaryAngle, PrimaryOrbit, PrimarySize);
        orbs[ActionSlots + 3] = new Orb(TargetButtonAngle, Orbit * InteractButtonOrbit, TargetButtonSize);
        orbs[ActionSlots + 4] = new Orb(LootButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        orbs[ActionSlots + 5] = new Orb(NpcButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        orbs[ActionSlots + 6] = new Orb(AnvilButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        orbs[ActionSlots + 7] = new Orb(TeleportButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        orbs[ActionSlots + 8] = new Orb(UserButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        orbs[ActionSlots + 9] = new Orb(MarketButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        orbs[ActionSlots + 10] = new Orb(MarketSetupButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        for (int i = 0; i < PotionAngles.Length; i++)
            orbs[ActionSlots + 11 + i] =
                new Orb(PotionAngles[i], Orbit * PotionOrbit, PotionSize);

        Vector2 min = Vector2.Inf, max = -Vector2.Inf;
        foreach (var orb in orbs)
        {
            var centre = Radial(orb.Angle, orb.Radius);
            float half = orb.Size * 0.5f;
            min = min.Min(centre - Vector2.One * half);
            max = max.Max(centre + Vector2.One * half);
        }

        var cluster = new Control
        {
            Name = "TouchActions",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(cluster);
        HudAnchor.Pin(cluster, HudAnchor.Spot.BottomRight,
            new Vector2(Px(ThumbInset - max.X), Px(ThumbInset - max.Y + 95f) + HudPlacement.BottomInset),
            new Vector2(Px(max.X - min.X), Px(max.Y - min.Y)));

        var hub = -Px(min);

        var guide = new OrbitRing
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = hub - Vector2.One * Px(Orbit),
            Size = Vector2.One * Px(Orbit * 2f),
        };
        cluster.AddChild(guide);

        var look = Stick("LookJoystick", LookStickSize, LookTipSize,
                         KeyBinds.TouchLookLeft, KeyBinds.TouchLookRight,
                         KeyBinds.TouchLookUp, KeyBinds.TouchLookDown);
        look.Position = hub - Vector2.One * Px(LookStickSize * 0.5f);
        look.Size = Vector2.One * Px(LookStickSize);
        cluster.AddChild(look);

        var slots = new Godot.Button[ActionSlots];
        for (int i = 0; i < ActionSlots; i++)
        {
            int slot = i;
            var button = SlotButton((slot + 1).ToString(), Px(ActionSize), UiTheme.Gold);
            button.SlotInPage = slot;
            button.OnDrop = onDrop;
            Place(button, hub, SlotAngles[i], Orbit, ActionSize);
            button.Pressed += () => onSlot(slot);
            AddSlotIcon(button, Px(ActionSize));
            AddOuterRing(button);
            AddCooldown(button, Px(ActionSize));
            cluster.AddChild(button);
            slots[i] = button;
        }

        var attack = Button("", Px(PrimarySize), UiTheme.Bad);
        var attackGlyph = Glyph("game/main-hand", Px(PrimarySize), 0.27f,
                                new Color(0.97f, 0.92f, 0.88f));
        attack.AddChild(attackGlyph);
        attack.AddChild(new StopGlyph
        {
            Name = StopGlyphName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        });
        attack.GetNode<Control>(StopGlyphName).SetAnchorsPreset(Control.LayoutPreset.FullRect);
        attack.TooltipText = "Auto attack";
        AddOuterRing(attack);
        Place(attack, hub, PrimaryAngle, PrimaryOrbit, PrimarySize);
        attack.Pressed += onAttack;
        cluster.AddChild(attack);

        var pageButton = Button("", Px(PageButtonSize), UiTheme.Bronze);
        pageButton.AddChild(Glyph(PageIconId, Px(PageButtonSize), 0.28f,
                                  new Color(UiTheme.GoldBright, 0.90f)));
        pageButton.TooltipText = "Next skill page";
        Place(pageButton, hub, PageButtonAngle, Orbit, PageButtonSize);
        AddOuterRing(pageButton);
        pageButton.Pressed += () => onPage(1);
        cluster.AddChild(pageButton);

        var targetButton = Button("Z", Px(TargetButtonSize), UiTheme.Gold);
        targetButton.TooltipText = "Target nearest hostile";
        Place(targetButton, hub, TargetButtonAngle, Orbit * InteractButtonOrbit, TargetButtonSize);
        AddOuterRing(targetButton);
        targetButton.Pressed += () => onTapTarget?.Invoke();
        cluster.AddChild(targetButton);

        var lootButton = Button("KUTU", Px(InteractButtonSize), UiTheme.Bronze);
        lootButton.TooltipText = "Open nearest loot box";
        Place(lootButton, hub, LootButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(lootButton);
        lootButton.Pressed += () => onLootOpen?.Invoke();
        lootButton.Visible = false;
        cluster.AddChild(lootButton);

        var npcButton = Button("NPC", Px(InteractButtonSize), UiTheme.Gold);
        npcButton.TooltipText = "Talk to nearest NPC";
        Place(npcButton, hub, NpcButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(npcButton);
        npcButton.Pressed += () => onNpcInteract?.Invoke();
        npcButton.Visible = false;
        cluster.AddChild(npcButton);

        var anvilButton = Button("ANVIL", Px(InteractButtonSize), UiTheme.Gold);
        anvilButton.TooltipText = "Use nearest Magic Anvil";
        Place(anvilButton, hub, AnvilButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(anvilButton);
        anvilButton.Pressed += () => onAnvilOpen?.Invoke();
        anvilButton.Visible = false;
        cluster.AddChild(anvilButton);

        var teleportButton = Button("TP", Px(InteractButtonSize), UiTheme.Bronze);
        teleportButton.TooltipText = "Use nearest Warp Gate";
        Place(teleportButton, hub, TeleportButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(teleportButton);
        teleportButton.Pressed += () => onTeleportOpen?.Invoke();
        teleportButton.Visible = false;
        cluster.AddChild(teleportButton);

        var userButton = Button("USER", Px(InteractButtonSize), UiTheme.Gold);
        userButton.TooltipText = "Open nearest player's information";
        Place(userButton, hub, UserButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(userButton);
        userButton.Pressed += () => onUserOpen?.Invoke();
        userButton.Visible = false;
        cluster.AddChild(userButton);

        var marketButton = Button("PAZAR", Px(InteractButtonSize), UiTheme.Bronze);
        marketButton.TooltipText = "Open nearest player's merchant";
        Place(marketButton, hub, MarketButtonAngle, Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(marketButton);
        marketButton.Pressed += () => onMarketBrowse?.Invoke();
        marketButton.Visible = false;
        cluster.AddChild(marketButton);

        var marketSetupButton = Button("KUR", Px(InteractButtonSize), UiTheme.Gold);
        marketSetupButton.TooltipText = "Set up a merchant";
        Place(marketSetupButton, hub, MarketSetupButtonAngle,
              Orbit * InteractButtonOrbit, InteractButtonSize);
        AddOuterRing(marketSetupButton);
        marketSetupButton.Pressed += () => onMarketSetup?.Invoke();
        cluster.AddChild(marketSetupButton);

        var bar = new TouchActionBar(cluster, slots, iconFor, BuildPageIndicator(parent),
                                    attack, attackGlyph, hub, npcButton, lootButton,
                                    anvilButton, teleportButton, userButton, marketButton);
        bar.Refresh();
        return bar;
    }

    private static PageIndicator BuildPageIndicator(Node parent)
    {
        var strip = new PageIndicator { MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(strip);
        HudAnchor.Pin(strip, HudAnchor.Spot.BottomRight,
            new Vector2(HudPlacement.TouchEdge, HudPlacement.PageStripBottom),
            strip.Extent());
        return strip;
    }

    internal static CooldownWipe AddCooldown(Control button, float size)
    {
        var wipe = new CooldownWipe
        {
            Name = CooldownName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        wipe.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(wipe);
        return wipe;
    }

    private static void AddOuterRing(Control button)
    {
        var ring = new OrbitRing { MouseFilter = Control.MouseFilterEnum.Ignore };
        ring.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        float grow = button.Size.X * 0.085f;
        ring.OffsetLeft = ring.OffsetTop = -grow;
        ring.OffsetRight = ring.OffsetBottom = grow;
        button.AddChild(ring);
        button.MoveChild(ring, 0);
    }

    internal static void PlacePotion(Control orb, Vector2 hub, int index)
    {
        orb.Size = Vector2.One * Px(PotionSize);
        orb.CustomMinimumSize = orb.Size;
        Place(orb, hub, PotionAngles[index % PotionAngles.Length],
              Orbit * PotionOrbit, PotionSize);
    }

    private static void Place(Control control, Vector2 hub, float angle, float radius, float size)
    {
        control.Position = hub + Radial(angle, Px(radius)) - Vector2.One * Px(size * 0.5f);
    }

    private static Vector2 Radial(float degrees, float radius)
    {
        float a = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * radius;
    }

    private static TextureRect Glyph(string iconId, float size, float inset, Color tint)
    {
        var glyph = new TextureRect
        {
            Texture = UiIcons.Get(iconId),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = tint,
        };
        glyph.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        float pad = size * inset;
        glyph.OffsetLeft = glyph.OffsetTop = pad;
        glyph.OffsetRight = glyph.OffsetBottom = -pad;
        return glyph;
    }

    private static void StyleStick(VirtualJoystick stick)
    {
        stick.AddThemeStyleboxOverride("normal_joystick", Disc(
            new Color(0f, 0f, 0f, 0f), new Color(0.92f, 0.95f, 0.99f, 0.26f), 2f));
        stick.AddThemeStyleboxOverride("pressed_joystick", Disc(
            new Color(0.85f, 0.89f, 0.95f, 0.05f), new Color(0.96f, 0.98f, 1f, 0.40f), 2f));
        stick.AddThemeStyleboxOverride("normal_tip", Disc(
            new Color(0.90f, 0.92f, 0.95f, 0.52f), new Color(0.92f, 0.95f, 0.99f, 0.40f), 2f));
        stick.AddThemeStyleboxOverride("pressed_tip", Disc(
            new Color(0.99f, 1f, 1f, 0.96f), new Color(UiTheme.Gold, 0.85f), 3f));
    }

    private static StyleBoxFlat Disc(Color fill, Color border, float width)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetCornerRadiusAll(4096);
        box.SetBorderWidthAll(Mathf.RoundToInt(width));
        return box;
    }

    private static ShaderMaterial? _circleMask;

    private static ShaderMaterial CircleMask()
    {
        if (_circleMask != null) return _circleMask;
        _circleMask = new ShaderMaterial { Shader = GD.Load<Shader>(CircleShaderPath) };
        return _circleMask;
    }

    private static void AddSlotIcon(Godot.Button button, float size)
    {
        float inner = size * IconInset;
        var icon = new TextureRect
        {
            Name = IconChildName,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = Vector2.One * ((size - inner) * 0.5f),
            Size = Vector2.One * inner,
            Material = CircleMask(),
        };
        button.AddChild(icon);
    }

    private static Button Button(string text, float size, Color accent) =>
        Style(new TouchTapButton(), text, size, accent);

    private static TouchSlotButton SlotButton(string text, float size, Color accent) =>
        Style(new TouchSlotButton(), text, size, accent);

    private static T Style<T>(T button, string text, float size, Color accent) where T : Godot.Button
    {
        button.Text = text;
        button.CustomMinimumSize = new Vector2(size, size);
        button.Size = new Vector2(size, size);
        button.FocusMode = Control.FocusModeEnum.None;
        button.ActionMode = BaseButton.ActionModeEnum.Press;

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.030f, 0.033f, 0.040f, 0.90f),
            BorderColor = new Color(accent, 0.85f),
            ShadowColor = new Color(0f, 0f, 0f, 0.60f),
            ShadowSize = (int)(size * 0.07f),
        };
        normal.SetCornerRadiusAll((int)size);
        normal.SetBorderWidthAll(Mathf.Max(2, (int)(size * 0.032f)));
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(UiTheme.Gold, 0.30f);
        pressed.BorderColor = UiTheme.GoldBright;

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", normal);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeColorOverride("font_color", new Color(UiTheme.TextLo, 0.55f));
        button.AddThemeFontSizeOverride("font_size", (int)(size * 0.24f));
        return button;
    }
}

public partial class StopGlyph : Control
{
    private static readonly Color Fill = new(0.96f, 0.90f, 0.88f, 0.92f);

    public override void _Draw()
    {
        float side = Mathf.Min(Size.X, Size.Y) * 0.38f;
        var origin = (Size - Vector2.One * side) * 0.5f;
        DrawRect(new Rect2(origin, Vector2.One * side), Fill);
    }
}

public partial class TouchTapButton : Godot.Button
{
    private int _finger = -1;

    public override void _GuiInput(InputEvent ev)
    {
        if (!Platform.TouchUi)
        {
            base._GuiInput(ev);
            return;
        }

        if (ev is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                if (_finger < 0)
                {
                    _finger = touch.Index;
                    if (!Disabled) EmitSignal(BaseButton.SignalName.Pressed);
                }
            }
            else if (touch.Index == _finger)
            {
                _finger = -1;
            }
            AcceptEvent();
            return;
        }

        if (ev is InputEventMouseButton or InputEventMouseMotion)
        {
            AcceptEvent();
            return;
        }

        base._GuiInput(ev);
    }
}

public partial class TouchSlotButton : TouchTapButton
{
    public int SlotInPage;
    public Action<int, int, int>? OnDrop;

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("id");

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        int from = d.ContainsKey("barFrom") ? d["barFrom"].AsInt32() : -1;
        OnDrop?.Invoke(SlotInPage, d["id"].AsInt32(), from);
    }
}

public partial class CooldownWipe : Control
{
    private const int Segments = 64;
    private static readonly Color Shade = new(0.02f, 0.02f, 0.03f, 0.62f);

    private float _remain;

    public float Remain
    {
        set
        {
            float clamped = Mathf.Clamp(value, 0f, 1f);
            if (Mathf.Abs(clamped - _remain) < 0.004f) return;
            _remain = clamped;
            bool on = clamped > 0.001f;
            if (Visible != on) Visible = on;
            if (on) QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_remain <= 0.001f) return;
        var centre = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f;
        int steps = Mathf.Max(2, Mathf.CeilToInt(Segments * _remain));

        var fan = new Vector2[steps + 2];
        fan[0] = centre;
        float start = -Mathf.Pi * 0.5f;
        float sweep = Mathf.Tau * _remain;
        for (int i = 0; i <= steps; i++)
        {
            float a = start + sweep * i / steps;
            fan[i + 1] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        DrawColoredPolygon(fan, Shade);
    }
}

public partial class CloseGlyph : Control
{
    public Color Tint = new(0.94f, 0.90f, 0.86f);

    private bool _hot;

    public override void _Ready()
    {
        MouseEntered += () => { _hot = true; QueueRedraw(); };
        MouseExited += () => { _hot = false; QueueRedraw(); };
    }

    public override void _Draw()
    {
        var centre = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.5f;
        DrawCircle(centre, r, new Color(0.02f, 0.02f, 0.03f, _hot ? 0.86f : 0.66f), true, -1f, true);
        DrawArc(centre, r - 1f, 0f, Mathf.Tau, 40,
                new Color(UiTheme.Gold, _hot ? 0.85f : 0.45f), 1.4f, true);

        float k = r * 0.40f;
        var tint = _hot ? UiTheme.GoldBright : Tint;
        DrawLine(centre + new Vector2(-k, -k), centre + new Vector2(k, k), tint, 2.1f, true);
        DrawLine(centre + new Vector2(k, -k), centre + new Vector2(-k, k), tint, 2.1f, true);
    }
}

public partial class OrbitRing : Control
{
    private static readonly Color Line = new(0.86f, 0.89f, 0.94f, 0.20f);

    public override void _Draw()
    {
        var centre = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.5f - 1f;
        if (r <= 0f) return;
        DrawArc(centre, r, 0f, Mathf.Tau, 96, Line, 1.5f, true);
    }
}

public partial class PageIndicator : Control
{
    private const int FontSize = 17;

    private readonly Label _label;
    private readonly HBoxContainer _dots = new();

    public PageIndicator()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)TouchControls.DotGap);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(row);

        _label = UiTheme.Text("Page 1", FontSize, UiTheme.TextHi);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_label);

        _dots.AddThemeConstantOverride("separation", (int)TouchControls.DotGap);
        _dots.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(_dots);

        for (int i = 0; i < TouchControls.ActionPages; i++)
            _dots.AddChild(new PageDot
            {
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = Vector2.One * TouchControls.DotSize,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            });
    }

    public Vector2 Extent()
    {
        float dots = TouchControls.ActionPages * (TouchControls.DotSize + TouchControls.DotGap);
        float label = _label.GetMinimumSize().X + TouchControls.DotGap;
        return new Vector2(label + dots, Mathf.Max(FontSize * 1.6f, TouchControls.DotSize));
    }

    public void Show(int page)
    {
        _label.Text = $"Page {page + 1}";
        for (int i = 0; i < _dots.GetChildCount(); i++)
            if (_dots.GetChild(i) is PageDot dot) dot.Active = i == page;
    }
}

public partial class PageDot : Control
{
    private bool _active;

    public bool Active
    {
        set
        {
            if (_active == value) return;
            _active = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var centre = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.5f;
        DrawCircle(centre, r, new Color(0f, 0f, 0f, 0.50f), true, -1f, true);
        DrawCircle(centre, r - 1.5f,
                   _active ? UiTheme.GoldBright : new Color(UiTheme.TextLo, 0.42f), true, -1f, true);
    }
}

public sealed class TouchActionBar
{
    private readonly Godot.Button[] _slots;
    private readonly Func<int, Texture2D?> _iconFor;
    private readonly PageIndicator _indicator;
    private readonly Godot.Button _attack;
    private readonly Control _attackGlyph;
    private readonly Godot.Button _npcButton;
    private readonly Godot.Button _lootButton;
    private readonly Godot.Button _anvilButton;
    private readonly Godot.Button _teleportButton;
    private readonly Godot.Button _userButton;
    private readonly Godot.Button _marketButton;
    private readonly Vector2 _hub;
    private int _attackState = -1;

    public Control Root { get; }

    internal TouchActionBar(Control root, Godot.Button[] slots, Func<int, Texture2D?> iconFor,
                            PageIndicator indicator, Godot.Button attack, Control attackGlyph,
                            Vector2 hub, Godot.Button npcButton, Godot.Button lootButton,
                            Godot.Button anvilButton, Godot.Button teleportButton,
                            Godot.Button userButton, Godot.Button marketButton)
    {
        Root = root;
        _slots = slots;
        _iconFor = iconFor;
        _indicator = indicator;
        _attack = attack;
        _attackGlyph = attackGlyph;
        _npcButton = npcButton;
        _lootButton = lootButton;
        _anvilButton = anvilButton;
        _teleportButton = teleportButton;
        _userButton = userButton;
        _marketButton = marketButton;
        _hub = hub;
        SetInteractionVisibility(false, false, false, false, false, false);
        SetAutoAttack(false, false);
    }

    internal Godot.Button AutoAttackButtonForPreview => _attack;

    internal Godot.Button SlotButtonForPreview(int index) => _slots[index];

    public void AttachPotion(Control orb, int index)
    {
        Root.AddChild(orb);
        TouchControls.PlacePotion(orb, _hub, index);
    }

    public void SetInteractionVisibility(bool npcNearby, bool lootNearby,
                                         bool anvilNearby, bool teleportNearby,
                                         bool userNearby, bool marketNearby)
    {
        _npcButton.Visible = npcNearby;
        _lootButton.Visible = lootNearby;
        _anvilButton.Visible = anvilNearby;
        _teleportButton.Visible = teleportNearby;
        _userButton.Visible = userNearby;
        _marketButton.Visible = marketNearby;
    }

    public void SetAutoAttack(bool hasTarget, bool attacking)
    {
        int state = !hasTarget ? 0 : attacking ? 2 : 1;
        if (state == _attackState) return;
        _attackState = state;
        _attack.Modulate = new Color(1f, 1f, 1f, state == 0 ? 0.45f : 1f);
        _attackGlyph.Visible = state != 2;
        if (_attack.GetNodeOrNull<Control>(TouchControls.StopGlyphName) is { } stop)
            stop.Visible = state == 2;
        _attack.TooltipText = state switch
        {
            0 => "Auto attack (no target)",
            1 => "Start auto attack",
            _ => "Stop auto attack",
        };
    }

    public void SetCooldown(int slot, float remain)
    {
        if (slot < 0 || slot >= _slots.Length) return;
        if (_slots[slot].GetNodeOrNull<CooldownWipe>(TouchControls.CooldownName) is { } wipe)
            wipe.Remain = remain;
    }

    public void Refresh(int page = 0)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            var texture = _iconFor(i);
            if (_slots[i].GetNodeOrNull<TextureRect>(TouchControls.IconChildName) is { } holder)
                holder.Texture = texture;
            _slots[i].Text = texture == null ? (i + 1).ToString() : "";
        }
        int pages = TouchControls.ActionPages;
        _indicator.Show(((page % pages) + pages) % pages);
    }
}

public partial class TouchCameraZone : Control
{
    public Action<Vector2>? Orbit;
    public Action? TapTarget;

    private bool _dragging;
    private float _travelled;
    private int _finger = -1;

    public override void _GuiInput(InputEvent ev)
    {
        if (Platform.TouchUi)
        {
            TouchInput(ev);
            return;
        }

        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                _dragging = true;
                _travelled = 0f;
            }
            else
            {
                _dragging = false;
            }
            AcceptEvent();
            return;
        }

        if (ev is InputEventMouseMotion motion && _dragging)
        {
            _travelled += motion.Relative.Length();
            Orbit?.Invoke(motion.Relative);
            AcceptEvent();
        }
    }

    private void TouchInput(InputEvent ev)
    {
        if (ev is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                if (_finger >= 0) return;
                _finger = touch.Index;
                _travelled = 0f;
            }
            else if (touch.Index == _finger)
            {
                _finger = -1;
            }
            AcceptEvent();
            return;
        }

        if (ev is InputEventScreenDrag drag && drag.Index == _finger)
        {
            _travelled += drag.Relative.Length();
            Orbit?.Invoke(drag.Relative);
            AcceptEvent();
        }
    }
}
