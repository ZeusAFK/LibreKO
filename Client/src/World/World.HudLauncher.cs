using System;
using Godot;

namespace LibreKO;

public partial class World
{
    private static float LauncherButtonSize => HudPlacement.LauncherButtonSize;
    private const float LauncherGap = 4f;
    private const int LauncherSlots = 7;

    private CanvasLayer _hudLauncherLayer = null!;
    private HBoxContainer _hudLauncher = null!;
    private Control _hudLauncherRoot = null!;
    private Button? _townButton;

    private Control BuildHudLauncher()
    {
        _hudLauncherLayer = new CanvasLayer { Layer = 67 };
        AddChild(_hudLauncherLayer);

        _hudLauncher = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _hudLauncher.AddThemeConstantOverride("separation", (int)LauncherGap);
        _hudLauncherLayer.AddChild(_hudLauncher);
        _hudLauncherRoot = _hudLauncher;

        if (Platform.TouchUi) BuildTownButton();
        else
            _hudLauncher.AddChild(LauncherButton(
                "system/home", "Go to town", () => Net.I.SendGoTown(), town: true));
        _hudLauncher.AddChild(LauncherButton(
            "game/helmet", "Character Info", () => ToggleMainWindow("Character")));
        _hudLauncher.AddChild(LauncherButton(
            "game/main-hand", "Skills", () => ToggleMainWindow("Skills")));
        _hudLauncher.AddChild(LauncherButton(
            "system/bag", "Inventory", () => ToggleMainWindow("Inventory")));
        _hudLauncher.AddChild(LauncherButton(
            "system/pus", "Power-Up Store", OpenPowerUpStore));
        _hudLauncher.AddChild(LauncherButton(
            "system/gift", "Lottery Event", ToggleLottery));
        _hudLauncher.AddChild(LauncherButton(
            "system/users-three", "Party", ToggleParty));

        if (Platform.TouchUi)
        {
            _hudLauncher.AddChild(LauncherButton(
                "system/scroll", "Quest Journal", () => ToggleMainWindow("Quests")));
            _hudLauncher.AddChild(LauncherMenuButton("Settings", () => SettingsPanel.Open(this)));
        }

        int slots = _hudLauncher.GetChildCount();
        _hudLauncher.CustomMinimumSize = new Vector2(
            LauncherButtonSize * slots + LauncherGap * (slots - 1), LauncherButtonSize);

        HudPlacement.Launcher(new Vector2(
            StatusHudPos.X + MpBarPos.X,
            StatusHudPos.Y + MpBarPos.Y + MpBarSize.Y + StatusHudGap))
            .ApplyTo(_hudLauncherRoot);

        return _hudLauncherRoot;
    }

    private void BuildTownButton()
    {
        _townButton = LauncherButton("system/home", "Go to town",
                                     () => Net.I.SendGoTown(), town: true);
        _hudLauncherLayer.AddChild(_townButton);
        HudPlacement.TownButton.ApplyTo(_townButton);
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

        private bool _hover;
        private bool _held;

        public bool Hover { set { if (_hover != value) { _hover = value; QueueRedraw(); } } }

        public bool Held { set { if (_held != value) { _held = value; QueueRedraw(); } } }

        public override void _Draw()
        {
            Vector2 c = Size * 0.5f;
            float r = Mathf.Min(Size.X, Size.Y) * 0.5f;
            DrawCircle(c, r, Halo, true, -1f, true);
            DrawCircle(c, r - 1.5f, _held ? FillHeld : _hover ? FillHover : Fill, true, -1f, true);
            if (_hover || _held)
                DrawArc(c, r - 2f, 0f, Mathf.Tau, 48, Ring, 1f, true);
        }
    }
}
