using Godot;

namespace LibreKO;

public partial class NationSelect : Control
{
    private const float FadeTime = 0.35f;

    private static float HeaderTop => Platform.Pick(36f, 20f);
    private static float HeaderBottom => Platform.Pick(130f, 104f);
    private static float PickAnchor => Platform.Pick(0.54f, 0.44f);
    private static float LoreTop => Platform.Pick(-120f, -190f);
    private static float LoreBottom => Platform.Pick(-44f, -26f);

    private const string ElMoradLore =
        "El Morad is a nation that was formed by King Manes, the one that has sealed up Patos the "
        + "lord of change. El Morad used to be a small kingdom that was located on the west coast "
        + "of the Adonis continent. The kingdom grew to become the biggest nation by taking in all "
        + "the refugees that have run away from the 5 kingdoms that were destroyed by Patos. The "
        + "peace in El Morad didn't last long. They were soon being threatened by Karus in the north.";

    private const string KarusLore =
        "Karus is a new nation that was established in Adonis continent after the chaos caused by "
        + "Patos the God of Change. Orcs, the descendents of Patos, had been outcast by the humans. "
        + "They decided to set up their own nation in the Luferson Castle located on the northern "
        + "part of the Iskanz mountain range.";

    private Label _status = null!;
    private Button _karus = null!;
    private Button _elmorad = null!;
    private ShaderMaterial _split = null!;
    private Tween? _tweenLeft;
    private Tween? _tweenRight;

    public override void _Ready()
    {
        Ui.MenuScale(true);
        var backdrop = Ui.Background(this, "res://assets/backgrounds/nation_select.jpg");
        _split = Shaders.Material("nation_split");
        _split.SetShaderParameter("sat_left", 0f);
        _split.SetShaderParameter("sat_right", 0f);
        (backdrop as BackdropRect)?.SetArtMaterial(_split);

        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(root);

        var header = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        header.SetAnchorsPreset(LayoutPreset.TopWide);
        header.OffsetTop = HeaderTop;
        header.OffsetBottom = HeaderBottom;
        header.AddThemeConstantOverride("separation", 4);
        root.AddChild(header);
        header.AddChild(Ui.Legend("Select Your Nation", 34, UiTheme.GoldBright));
        header.AddChild(Ui.Legend("This choice binds the account and cannot be changed.",
                                  14, UiTheme.TextHi));

        _elmorad = BuildSide(root, "El Morad", ElMoradLore, Nations.ElMorad, left: true);
        _karus = BuildSide(root, "Karus", KarusLore, Nations.Karus, left: false);

        _status = Ui.Legend("", 14, UiTheme.TextHi);
        _status.SetAnchorsPreset(LayoutPreset.BottomWide);
        _status.GrowVertical = GrowDirection.Begin;
        _status.OffsetTop = -30;
        _status.OffsetBottom = -8;
        root.AddChild(_status);

        Net.I.NationResultEvent += OnNationResult;
        Net.I.ErrorEvent += OnError;
    }

    private Button BuildSide(Control root, string name, string lore, int nation, bool left)
    {
        var side = new Control { MouseFilter = MouseFilterEnum.Ignore };
        side.SetAnchorsPreset(LayoutPreset.FullRect);
        side.AnchorLeft = left ? 0f : 0.5f;
        side.AnchorRight = left ? 0.5f : 1f;
        root.AddChild(side);

        var pick = Ui.MenuButton($"Choose {name}", 52, 21);
        if (Platform.PointerUi)
        {
            pick.AddThemeStyleboxOverride("normal", Ui.EdgeFade(new Color(0.02f, 0.02f, 0.03f, 0.42f)));
            pick.AddThemeStyleboxOverride("hover", Ui.EdgeFade(new Color(0.30f, 0.22f, 0.09f, 0.68f)));
            pick.AddThemeStyleboxOverride("pressed", Ui.EdgeFade(new Color(0.05f, 0.04f, 0.03f, 0.60f)));
        }
        else
        {
            var face = new StyleBoxFlat
            {
                BgColor = new Color(0.055f, 0.050f, 0.040f, 0.92f),
                BorderColor = new Color(UiTheme.Gold, 0.75f),
            };
            face.SetBorderWidthAll(2);
            face.SetCornerRadiusAll(6);
            face.SetContentMarginAll(10);
            var down = (StyleBoxFlat)face.Duplicate();
            down.BgColor = new Color(0.30f, 0.22f, 0.09f, 0.95f);
            down.BorderColor = UiTheme.GoldBright;
            pick.AddThemeStyleboxOverride("normal", face);
            pick.AddThemeStyleboxOverride("hover", face);
            pick.AddThemeStyleboxOverride("pressed", down);
            pick.AddThemeStyleboxOverride("disabled", face);
        }
        pick.SetAnchorsPreset(LayoutPreset.CenterTop);
        pick.AnchorTop = PickAnchor;
        pick.AnchorBottom = PickAnchor;
        pick.GrowHorizontal = GrowDirection.Both;
        pick.OffsetLeft = -150;
        pick.OffsetRight = 150;
        pick.OffsetBottom = 52;
        pick.Pressed += () => Choose(nation);
        pick.MouseEntered += () => Highlight(left, 1f);
        pick.MouseExited += () => Highlight(left, 0f);
        side.AddChild(pick);

        var body = new Label
        {
            Text = lore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        body.SetAnchorsPreset(LayoutPreset.BottomWide);
        body.GrowVertical = GrowDirection.Begin;
        body.OffsetLeft = 48;
        body.OffsetRight = -48;
        body.OffsetTop = LoreTop;
        body.OffsetBottom = LoreBottom;
        body.AddThemeFontSizeOverride("font_size", 14);
        body.AddThemeColorOverride("font_color", UiTheme.TextLo);
        body.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        body.AddThemeConstantOverride("outline_size", 5);
        side.AddChild(body);
        return pick;
    }

    private void Highlight(bool left, float target)
    {
        if (_split?.Shader == null) return;

        ref var slot = ref left ? ref _tweenLeft : ref _tweenRight;
        if (slot != null && slot.IsValid()) slot.Kill();
        slot = CreateTween();
        var step = slot.TweenProperty(
            _split, left ? "shader_parameter/sat_left" : "shader_parameter/sat_right",
            target, FadeTime);
        if (step == null)
        {
            slot.Kill();
            slot = null;
            _split.SetShaderParameter(left ? "sat_left" : "sat_right", target);
            return;
        }
        step.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
    }

    internal void HoverForPreview(bool left) => Highlight(left, 1f);

    private void Choose(int nation)
    {
        _karus.Disabled = _elmorad.Disabled = true;
        _status.Text = "Selecting nation…";
        Net.I.SelectNation(nation);
    }

    private void OnNationResult(int nation)
    {
        if (nation == Nations.NotSelected)
        {
            _karus.Disabled = _elmorad.Disabled = false;
            _status.Text = "";
            Notice.Show(this, "Nation selection was rejected. Please try again.");
            return;
        }
        GetTree().ChangeSceneToFile("res://scenes/CharSelect.tscn");
    }

    private void OnError(string e)
    {
        _karus.Disabled = _elmorad.Disabled = false;
        _status.Text = "";
        Notice.Show(this, "Error: " + e);
    }

    public override void _ExitTree()
    {
        Net.I.NationResultEvent -= OnNationResult;
        Net.I.ErrorEvent -= OnError;
    }
}
