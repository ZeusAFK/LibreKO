using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class HudLauncherFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly StatusHudFeature statusHud;
    private readonly Action goToTown;
    private readonly Action toggleParty;
    private readonly Action toggleGenie;
    private readonly IClientSettingsRequests settingsRequests;

    public HudLauncherFeature(IWorldContext context, StatusHudFeature statusHud, Action goToTown, Action toggleParty, Action toggleGenie,
        IClientSettingsRequests settingsRequests)
    {
        this.context = context;
        this.statusHud = statusHud;
        this.goToTown = goToTown;
        this.toggleParty = toggleParty;
        this.toggleGenie = toggleGenie;
        this.settingsRequests = settingsRequests;
    }

    public void Dispose() => statusHud.LauncherAnchorChanged -= Place;

    private static float LauncherButtonSize => HudPlacement.HudLauncherButtonSize;

    private const float LauncherGap = 4f;

    private CanvasLayer layer = null!;

    internal HBoxContainer Root { get; private set; } = null!;

    internal Button? TownButton { get; private set; }

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.HudLauncher };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, HudPart.Launcher);

        Root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        Root.AddThemeConstantOverride("separation", (int)LauncherGap);
        layer.AddChild(Root);

        if (Platform.TouchUi) BuildTownButton();
        else
            Root.AddChild(LauncherButton(
                "system/home", "Go to town", goToTown, town: true));
        Root.AddChild(LauncherButton(
            "game/helmet", "Character Info", () => context.MainWindows.Toggle("Character")));
        Root.AddChild(LauncherButton(
            "game/main-hand", "Skills", () => context.MainWindows.Toggle("Skills")));
        Root.AddChild(LauncherButton(
            "system/bag", "Inventory", () => context.MainWindows.Toggle("Inventory")));
        Root.AddChild(LauncherButton(
            "system/users-three", "Party", toggleParty));
        if (Platform.TouchUi)
            Root.AddChild(LauncherButton(
                "system/sparkle", "Genie", toggleGenie));

        if (!Platform.TouchUi)
            Root.AddChild(LauncherButton(
                "system/help", "Quest Journal", () => context.MainWindows.Toggle("Quests")));

        if (Platform.TouchUi)
        {
            Root.AddChild(LauncherButton(
                "system/scroll", "Quest Journal", () => context.MainWindows.Toggle("Quests")));
            Root.AddChild(LauncherMenuButton("Settings", () => SettingsPanel.Open(context.Root, settingsRequests)));
        }

        int slots = Root.GetChildCount();
        Root.CustomMinimumSize = new Vector2(
            LauncherButtonSize * slots + LauncherGap * (slots - 1), LauncherButtonSize);

        statusHud.LauncherAnchorChanged += Place;
        Place();
    }

    internal void Place()
    {
        if (Root == null || !GodotObject.IsInstanceValid(Root)) return;
        HudPlacement.Launcher(statusHud.LauncherAnchor).ApplyTo(Root);
    }

    private void BuildTownButton()
    {
        TownButton = LauncherButton("system/home", "Go to town",
                                     goToTown, town: true);
        layer.AddChild(TownButton);
        HudPlacement.TownButton.ApplyTo(TownButton);
    }

    private static Button LauncherButton(
        string iconId,
        string tooltip,
        Action action,
        bool town = false)
    {
        var button = new Button
        {
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(LauncherButtonSize, LauncherButtonSize),
        };
        var flat = new StyleBoxEmpty();
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            button.AddThemeStyleboxOverride(state, flat);

        LauncherDisc? disc = null;
        if (Platform.PointerUi)
        {
            disc = new LauncherDisc { MouseFilter = Control.MouseFilterEnum.Ignore };
            disc.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            button.AddChild(disc);
        }

        Color normalIcon = Platform.TouchUi
            ? new Color(1f, 1f, 1f, 0.96f)
            : town
                ? new Color(0.82f, 0.72f, 0.43f, 0.90f)
                : new Color(0.79f, 0.80f, 0.78f, 0.88f);
        var hoverIcon = new Color(0.94f, 0.95f, 0.96f);

        // Button draws an expand_icon a few px off-centre; a full-rect child centres it exactly.
        var glyph = new TextureRect
        {
            Texture = UiIcons.Get(iconId),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = normalIcon,
        };
        glyph.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        float inset = LauncherButtonSize * (town ? HudPlacement.LauncherGlyphInset : 0.19f);
        glyph.OffsetLeft = glyph.OffsetTop = inset;
        glyph.OffsetRight = glyph.OffsetBottom = -inset;

        button.AddChild(glyph);
        button.MouseEntered += () => { if (disc != null) disc.Hover = true; glyph.Modulate = hoverIcon; };
        button.MouseExited += () => { if (disc != null) disc.Hover = false; glyph.Modulate = normalIcon; };
        button.ButtonDown += () => { if (disc != null) disc.Held = true; };
        button.ButtonUp += () => { if (disc != null) disc.Held = false; };

        button.Pressed += action;
        return button;
    }

    private Button LauncherMenuButton(string tooltip, Action action)
    {
        var button = new Button
        {
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(LauncherButtonSize, LauncherButtonSize),
        };
        var flat = new StyleBoxEmpty();
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            button.AddThemeStyleboxOverride(state, flat);

        if (Platform.PointerUi)
        {
            var disc = new LauncherDisc { MouseFilter = Control.MouseFilterEnum.Ignore };
            disc.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            button.AddChild(disc);
        }

        var bars = new LauncherMenuGlyph { MouseFilter = Control.MouseFilterEnum.Ignore };
        bars.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(bars);
        button.Pressed += action;
        return button;
    }

    private sealed partial class LauncherMenuGlyph : Control
    {
        private static readonly Color Line = new(0.86f, 0.87f, 0.86f, 0.92f);

        public override void _Draw()
        {
            float w = Size.X * 0.52f;
            float x = (Size.X - w) * 0.5f;
            float thickness = Mathf.Max(2f, Size.Y * 0.075f);
            float gap = Size.Y * 0.155f;
            float y = Size.Y * 0.5f - gap;
            for (int i = 0; i < 3; i++)
                DrawLine(new Vector2(x, y + gap * i), new Vector2(x + w, y + gap * i),
                         Line, thickness, true);
        }
    }

    // A translucent StyleBoxFlat rounded to a circle seams down the middle (AA halves blend twice).
    private sealed partial class LauncherDisc : Control
    {
        private static readonly Color Fill = new(0.012f, 0.014f, 0.018f, 0.46f);
        private static readonly Color FillHover = new(0.055f, 0.060f, 0.068f, 0.86f);
        private static readonly Color FillHeld = new(0.025f, 0.028f, 0.034f, 0.94f);
        private static readonly Color Ring = new(0.62f, 0.66f, 0.70f, 0.58f);
        private static readonly Color Halo = new(0f, 0f, 0f, 0.20f);

        private bool hover;
        private bool held;

        public bool Hover { set { if (hover != value) { hover = value; QueueRedraw(); } } }

        public bool Held { set { if (held != value) { held = value; QueueRedraw(); } } }

        public override void _Draw()
        {
            Vector2 c = Size * 0.5f;
            float r = Mathf.Min(Size.X, Size.Y) * 0.5f;
            DrawCircle(c, r, Halo, true, -1f, true);
            DrawCircle(c, r - 1.5f, held ? FillHeld : hover ? FillHover : Fill, true, -1f, true);
            if (hover || held)
                DrawArc(c, r - 2f, 0f, Mathf.Tau, 48, Ring, 1f, true);
        }
    }
}
