using Godot;

namespace LibreKO;

public static class Ui
{
    public static Control? Background(Node parent, string resPath,
        Backdrop.Fit fit = Backdrop.Fit.Cover, bool bottomScrim = false)
        => Backdrop.Build(parent, resPath, fit, bottomScrim);

    public static void PreGameBackdrop(Control scene)
    {
        Background(scene, Backdrop.LoginArt, Backdrop.LoginFit);
        if (scene.GetNodeOrNull<Panel>("Panel") is { } p)
            p.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        if (scene.GetNodeOrNull<Control>("Panel/Center") is { } center) AutoScale(center);
        if (scene.GetNodeOrNull<Control>("Panel/Center/VBox") is { } form)
        {
            form.AddThemeConstantOverride("separation", 10);
            FormCard(form);
        }
    }

    public static void FormCard(Control form, Vector2? padding = null)
    {
        Node? host = form.GetParent();
        while (host is Container) host = host.GetParent();
        if (host is not Control hostCtrl) return;

        Node branch = form;
        while (branch.GetParent() != null && branch.GetParent() != host) branch = branch.GetParent()!;

        var card = new FormCardPanel { Target = form, Pad = padding ?? new Vector2(30, 26) };
        hostCtrl.AddChild(card);
        hostCtrl.MoveChild(card, branch.GetIndex());
    }

    public static void AutoScale(Control container, float baseHeight = 1080f,
        float min = 0.6f, float max = 2.0f, Vector2? pivot = null)
        => container.AddChild(new FormScaler
        {
            Target = container, BaseHeight = baseHeight / Platform.MenuScale, Min = min, Max = max,
            Pivot = pivot ?? new Vector2(0.5f, 0.5f),
        });

    public static void MenuScale(bool on)
    {
        Platform.MenuScreens = on;
        Config.ApplyUiScale();
    }

    public static ImageTexture FadeTexture(Color centre, float hold, bool vertical = false)
    {
        const int n = 256;
        var img = Image.CreateEmpty(vertical ? 1 : n, vertical ? n : 1, false, Image.Format.Rgba8);
        for (int i = 0; i < n; i++)
        {
            float t = Mathf.Abs(i / (n - 1f) * 2f - 1f);
            float a = t <= hold ? 1f : 1f - Mathf.SmoothStep(hold, 1f, t);
            var c = new Color(centre, centre.A * a);
            if (vertical) img.SetPixel(0, i, c); else img.SetPixel(i, 0, c);
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static StyleBoxTexture EdgeFade(Color centre, float hold = 0.55f)
        => new()
        {
            Texture = FadeTexture(centre, hold),
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        };

    public static ImageTexture BoxFadeTexture(Color centre, float holdX, float holdY)
    {
        const int n = 128;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        {
            float ty = Mathf.Abs(y / (n - 1f) * 2f - 1f);
            float ay = ty <= holdY ? 1f : 1f - Mathf.SmoothStep(holdY, 1f, ty);
            for (int x = 0; x < n; x++)
            {
                float tx = Mathf.Abs(x / (n - 1f) * 2f - 1f);
                float ax = tx <= holdX ? 1f : 1f - Mathf.SmoothStep(holdX, 1f, tx);
                img.SetPixel(x, y, new Color(centre, centre.A * ax * ay));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static void SoftScrim(Control form, Vector2? padding = null)
    {
        Node? host = form.GetParent();
        while (host is Container) host = host.GetParent();
        if (host is not Control hostCtrl) return;

        Node branch = form;
        while (branch.GetParent() != null && branch.GetParent() != host) branch = branch.GetParent()!;

        var card = new FormCardPanel
        {
            Target = form,
            Pad = padding ?? new Vector2(64, 46),
            Style = new StyleBoxTexture
            {
                Texture = BoxFadeTexture(
                    new Color(0.015f, 0.015f, 0.02f, Platform.Pick(0.74f, 0.90f)),
                    Platform.Pick(0.30f, 0.52f), Platform.Pick(0.55f, 0.68f)),
            },
        };
        hostCtrl.AddChild(card);
        hostCtrl.MoveChild(card, branch.GetIndex());
    }

    public const int TouchButtonHeight = 46;

    public static Button MenuButton(string text, int height = 42, int fontSize = 19)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, Platform.TouchUi ? Mathf.Max(height, TouchButtonHeight) : height),
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color", UiTheme.TextHi);
        b.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        b.AddThemeColorOverride("font_pressed_color", UiTheme.Gold);
        b.AddThemeColorOverride("font_disabled_color", new Color(UiTheme.TextLo, 0.45f));
        b.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        b.AddThemeConstantOverride("outline_size", 4);
        b.AddThemeStyleboxOverride("normal", EdgeFade(new Color(0.02f, 0.02f, 0.03f, 0.80f)));
        b.AddThemeStyleboxOverride("hover", EdgeFade(Platform.Pick(
            new Color(0.30f, 0.22f, 0.09f, 0.92f), new Color(0.02f, 0.02f, 0.03f, 0.80f))));
        b.AddThemeStyleboxOverride("pressed", EdgeFade(new Color(0.05f, 0.04f, 0.03f, 0.85f)));
        b.AddThemeStyleboxOverride("disabled", EdgeFade(new Color(0.02f, 0.02f, 0.03f, 0.35f)));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    public static Button[] ActionGroup(Node parent, params (string Text, System.Action Pressed)[] items)
    {
        Node host = parent;
        if (Platform.TouchUi)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            parent.AddChild(row);
            host = row;
        }

        var made = new Button[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var b = MenuButton(items[i].Text, 36, 16);
            b.Pressed += items[i].Pressed;
            if (Platform.TouchUi) b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            host.AddChild(b);
            made[i] = b;
        }
        return made;
    }

    public static void MarkSelected(Button b, bool selected)
    {
        var fill = selected
            ? EdgeFade(new Color(0.30f, 0.22f, 0.09f, 0.92f))
            : EdgeFade(new Color(0.02f, 0.02f, 0.03f, 0.80f));
        b.AddThemeStyleboxOverride("normal", fill);
        b.AddThemeStyleboxOverride("pressed", fill);
        if (Platform.TouchUi) b.AddThemeStyleboxOverride("hover", fill);
        b.AddThemeColorOverride("font_color", selected ? UiTheme.GoldBright : UiTheme.TextHi);
    }

    public static Label Legend(string text, int fontSize, Color colour)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", colour);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", 5);
        return l;
    }

    public static void StyleField(LineEdit e)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.03f, 0.62f),
            BorderColor = new Color(UiTheme.Gold, 0.32f),
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);
        box.SetContentMarginAll(8);
        var focused = (StyleBoxFlat)box.Duplicate();
        focused.BorderColor = new Color(UiTheme.Gold, 0.85f);
        focused.BgColor = new Color(0.04f, 0.035f, 0.03f, 0.78f);
        e.AddThemeStyleboxOverride("normal", box);
        e.AddThemeStyleboxOverride("focus", focused);
        e.AddThemeStyleboxOverride("read_only", box);
        e.AddThemeColorOverride("font_color", UiTheme.TextHi);
        e.AddThemeColorOverride("font_placeholder_color", new Color(UiTheme.TextLo, 0.5f));
        e.CustomMinimumSize = new Vector2(0, Platform.Pick(34, TouchButtonHeight));
    }
}

public partial class FormCardPanel : Panel
{
    public Control Target = null!;
    public Vector2 Pad = new(30, 26);
    public StyleBox? Style;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (Style != null) { AddThemeStyleboxOverride("panel", Style); return; }
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.040f, 0.038f, 0.044f, 0.84f),
            BorderColor = new Color(UiTheme.Gold, 0.55f),
            ShadowColor = new Color(0, 0, 0, 0.60f),
            ShadowSize = 16,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(6);
        AddThemeStyleboxOverride("panel", sb);
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(Target)) { QueueFree(); return; }
        if (Visible != Target.Visible) Visible = Target.Visible;
        if (!Visible) return;
        var xf = Target.GetGlobalTransform();
        var scale = xf.Scale;
        var pad = Pad * scale;
        var pos = xf.Origin - pad;
        var size = Target.Size * scale + pad * 2f;
        if (GlobalPosition != pos) GlobalPosition = pos;
        if (Size != size) Size = size;
    }
}

public partial class FormScaler : Node
{
    public Control Target = null!;
    public float BaseHeight = 1080f, Min = 0.6f, Max = 2.0f;
    public Vector2 Pivot = new(0.5f, 0.5f);

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(Target)) { QueueFree(); return; }
        var box = Target.Size;
        if (box.X < 1f || box.Y < 1f) return;

        float s = Mathf.Clamp(box.Y / BaseHeight, Min, Max);
        var content = ContentSize();
        if (content.X > 1f) s = Mathf.Min(s, box.X * 0.90f / content.X);
        if (content.Y > 1f) s = Mathf.Min(s, box.Y * 0.90f / content.Y);
        s = Mathf.Max(s, 0.35f);

        Target.PivotOffset = box * Pivot;
        if (!Target.Scale.IsEqualApprox(new Vector2(s, s))) Target.Scale = new Vector2(s, s);
    }

    private Vector2 ContentSize()
    {
        var max = Vector2.Zero;
        foreach (var child in Target.GetChildren())
            if (child is Control c && c.Visible)
                max = new Vector2(Mathf.Max(max.X, c.Size.X), Mathf.Max(max.Y, c.Size.Y));
        return max;
    }
}

public partial class SettingsPanel : CanvasLayer
{
    private static readonly Vector2I[] Resolutions =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440),
    };

    private const int TabWidth = 660;
    private const int TabHeight = 660;
    private const int TouchScreenMargin = 10;

    private OptionButton _mode = null!;
    private OptionButton _language = null!;
    private OptionButton _res = null!;
    private CheckButton _shadows = null!, _ssao = null!, _volFog = null!, _bloom = null!, _clouds = null!;
    private CheckButton _capes = null!;
    private OptionButton _aa = null!;
    private OptionButton _upscale = null!;
    private OptionButton _upscaleQuality = null!;
    private CheckButton _vsync = null!;
    private OptionButton _fps = null!;
    private CheckButton _fxAmbient = null!, _fxNumbers = null!, _fxCombatLog = null!;
    private HSlider _fxDistance = null!;
    private HSlider _uiScale = null!;
    private HSlider _nameScale = null!;
    private HSlider _viewDistance = null!;
    private HSlider _camTurnSpeed = null!, _camEdgeSpeed = null!;
    private HSlider? _moveStick, _lookStick;
    private CheckButton _camEdgePan = null!;
    private CheckButton _sndOn = null!;
    private HSlider _volMaster = null!, _volMusic = null!, _volSfx = null!, _volUi = null!, _volVoice = null!;

    public static void Open(Node parent) => parent.AddChild(new SettingsPanel { Layer = 200 });

    public override void _ExitTree() => Audio.ApplyVolumes();

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        Control host;
        if (Platform.TouchUi)
        {
            var full = new MarginContainer();
            foreach (var s in new[] { "left", "right", "top", "bottom" })
                full.AddThemeConstantOverride($"margin_{s}", TouchScreenMargin);
            host = full;
        }
        else
        {
            host = new CenterContainer();
        }
        host.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(host);

        var panel = new PanelContainer();
        var panelStyle = new StyleBoxFlat { BgColor = new Color(0.11f, 0.11f, 0.13f, 0.98f) };
        panelStyle.SetCornerRadiusAll(8);
        panelStyle.SetBorderWidthAll(1);
        panelStyle.BorderColor = new Color(0.30f, 0.30f, 0.36f);
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        host.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var s in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{s}", 18);
        panel.AddChild(margin);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 12);
        margin.AddChild(outer);

        var title = new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        outer.AddChild(title);

        var tabs = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (Platform.PointerUi)
        {
            var room = GetViewport().GetVisibleRect().Size;
            tabs.CustomMinimumSize = new Vector2(
                Mathf.Min(TabWidth + 40, room.X - 80),
                Mathf.Min(TabHeight + 60, room.Y - 170));
        }
        outer.AddChild(tabs);

        BuildDisplayTab(Tab(tabs, "Display"));
        BuildGraphicsTab(Tab(tabs, "Graphics"));
        BuildEffectsTab(Tab(tabs, "Effects"));
        BuildControlsTab(Tab(tabs, "Controls"));
        BuildKeysTab(Tab(tabs, "Keys"));
        BuildPadTab(Tab(tabs, "Joypad"));
        BuildSoundTab(Tab(tabs, "Sound"));
        BuildPluginsTab(Tab(tabs, "Plugins"));

        var btns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btns.AddThemeConstantOverride("separation", 12);
        outer.AddChild(btns);
        var apply = new Button { Text = "Apply", CustomMinimumSize = ActionButtonSize };
        apply.Pressed += () =>
        {
            Config.SetVideo((Config.VideoMode)_mode.Selected, Resolutions[_res.Selected].X,
                            Resolutions[_res.Selected].Y, _vsync.ButtonPressed);
            Config.SetGraphics(_shadows.ButtonPressed, _ssao.ButtonPressed, _volFog.ButtonPressed,
                               _bloom.ButtonPressed, _clouds.ButtonPressed, _capes.ButtonPressed,
                               (Config.AaMode)_aa.Selected, (Config.UpscaleMode)_upscale.Selected,
                               (Config.UpscaleLevel)_upscaleQuality.Selected, (Config.FpsCap)_fps.Selected);
            Config.SetEffects(_fxAmbient.ButtonPressed, (int)_fxDistance.Value, _fxNumbers.ButtonPressed,
                              _fxCombatLog.ButtonPressed);
            Config.SetControls((float)_camTurnSpeed.Value, _camEdgePan.ButtonPressed, (float)_camEdgeSpeed.Value);
            if (_moveStick != null && _lookStick != null)
                Config.SetStickSensitivity((float)_moveStick.Value, (float)_lookStick.Value);
            Config.SetUiScale((float)_uiScale.Value);
            Config.SetNamePlateScale((float)_nameScale.Value);
            ApplyLanguage();
            Config.SetViewDistance((float)_viewDistance.Value);
            Config.SetAudio(_sndOn.ButtonPressed, Config.AudioMuted, (float)_volMaster.Value,
                            (float)_volMusic.Value, (float)_volSfx.Value,
                            (float)_volUi.Value, (float)_volVoice.Value);
        };
        btns.AddChild(apply);
        var close = new Button { Text = "Close", CustomMinimumSize = ActionButtonSize };
        close.Pressed += QueueFree;
        btns.AddChild(close);
    }

    private static Vector2 ActionButtonSize =>
        Platform.Pick(Vector2.Zero, new Vector2(140, Ui.TouchButtonHeight));

    private static readonly (GameLanguage Language, string Label)[] LanguageChoices =
    {
        (GameLanguage.English, "English"),
        (GameLanguage.Spanish, "Espanol"),
    };

    private void ApplyLanguage()
    {
        var index = Mathf.Clamp(_language.Selected, 0, LanguageChoices.Length - 1);
        var chosen = LanguageChoices[index].Language;
        if (chosen == Config.Language)
            return;

        Config.SetLanguage(chosen);
        Net.I?.SendLanguage(chosen);
    }

    private void BuildDisplayTab(VBoxContainer vb)
    {
        vb.AddChild(Row("Language", _language = new OptionButton()));
        foreach (var (language, label) in LanguageChoices)
            _language.AddItem(label, (int)language);
        _language.Selected = Array.FindIndex(LanguageChoices, c => c.Language == Config.Language);
        if (_language.Selected < 0)
            _language.Selected = 0;

        var windowRow = Row("Window", _mode = new OptionButton());
        windowRow.Visible = Platform.PointerUi;
        vb.AddChild(windowRow);
        _mode.AddItem("Windowed", 0);
        _mode.AddItem("Borderless Fullscreen", 1);
        _mode.AddItem("Fullscreen", 2);
        _mode.Selected = (int)Config.WindowMode;
        _mode.ItemSelected += _ => _res.Disabled = _mode.Selected != 0;

        var resRow = Row("Resolution", _res = new OptionButton());
        resRow.Visible = Platform.PointerUi;
        vb.AddChild(resRow);
        int sel = 0;
        for (int i = 0; i < Resolutions.Length; i++)
        {
            _res.AddItem($"{Resolutions[i].X} x {Resolutions[i].Y}", i);
            if (Resolutions[i].X == Config.WinWidth && Resolutions[i].Y == Config.WinHeight) sel = i;
        }
        _res.Selected = sel;
        _res.Disabled = Config.WindowMode != Config.VideoMode.Windowed;

        _uiScale = new HSlider
        {
            MinValue = Config.UiScaleMin, MaxValue = Config.UiScaleMax, Step = Config.UiScaleStep,
            Value = Config.UiScale, CustomMinimumSize = new Vector2(0, 18),
        };
        var scaleValue = new Label
        {
            Text = UiScaleText(Config.UiScale),
            CustomMinimumSize = new Vector2(52, 0),
        };
        _uiScale.ValueChanged += v => scaleValue.Text = UiScaleText((float)v);
        var scaleRow = Row("UI Size", _uiScale);
        scaleRow.AddChild(scaleValue);
        scaleRow.TooltipText = "Scales every menu, window and HUD element. Larger values suit small or touch screens.";
        vb.AddChild(scaleRow);

        _nameScale = new HSlider
        {
            MinValue = Config.NamePlateScaleMin, MaxValue = Config.NamePlateScaleMax, Step = Config.NamePlateScaleStep,
            Value = Config.NamePlateScale, CustomMinimumSize = new Vector2(0, 18),
        };
        var nameScaleValue = new Label
        {
            Text = UiScaleText(Config.NamePlateScale),
            CustomMinimumSize = new Vector2(52, 0),
        };
        _nameScale.ValueChanged += v => nameScaleValue.Text = UiScaleText((float)v);
        var nameScaleRow = Row("Name Size", _nameScale);
        nameScaleRow.AddChild(nameScaleValue);
        nameScaleRow.TooltipText = "Size of the names over characters, monsters and NPCs.";
        vb.AddChild(nameScaleRow);

        var vsyncRow = Row("V-Sync", _vsync = new CheckButton { ButtonPressed = Config.VSync });
        vsyncRow.Visible = Platform.PointerUi;
        vsyncRow.TooltipText = "Caps the frame rate to your monitor's refresh rate. Turn it off to see "
                               + "what the other graphics settings are actually doing.";
        vb.AddChild(vsyncRow);

        vb.AddChild(Row("FPS Limit", _fps = new OptionButton()));
        foreach (var (label, id) in new[] { ("Unlimited", 0), ("30", 1), ("60", 2), ("120", 3), ("144", 4), ("240", 5) })
            _fps.AddItem(label, id);
        _fps.Selected = (int)Config.FpsLimit;
    }

    private void BuildGraphicsTab(VBoxContainer vb)
    {
        _viewDistance = new HSlider
        {
            MinValue = Config.ViewDistanceMin, MaxValue = Config.ViewDistanceMax, Step = 0.05,
            Value = Config.ViewDistance, CustomMinimumSize = new Vector2(0, 18),
        };
        var viewValue = new Label
        {
            Text = ViewDistanceText(Config.ViewDistance),
            CustomMinimumSize = new Vector2(52, 0),
        };
        _viewDistance.ValueChanged += v => viewValue.Text = ViewDistanceText((float)v);
        var viewRow = Row("View Distance", _viewDistance);
        viewRow.AddChild(viewValue);
        viewRow.TooltipText = "How far terrain, objects and characters draw. Lower values run faster on phones.";
        vb.AddChild(viewRow);

        vb.AddChild(Row("Shadows", _shadows = new CheckButton { ButtonPressed = Config.Shadows }));
        vb.AddChild(Row("Ambient Occlusion", _ssao = new CheckButton { ButtonPressed = Config.Ssao }));
        vb.AddChild(Row("Volumetric Fog", _volFog = new CheckButton { ButtonPressed = Config.VolumetricFog }));
        vb.AddChild(Row("Bloom", _bloom = new CheckButton { ButtonPressed = Config.Bloom }));
        vb.AddChild(Row("Clouds", _clouds = new CheckButton { ButtonPressed = Config.Clouds }));
        var capeRow = Row("Clan Capes", _capes = new CheckButton { ButtonPressed = Config.Capes });
        capeRow.TooltipText = "Simulated cloth on every player in range. Costs a lot of frame time.";
        vb.AddChild(capeRow);
        vb.AddChild(Row("Anti-Aliasing", _aa = new OptionButton()));
        foreach (var (label, id) in new[] { ("Off", 0), ("FXAA", 1), ("MSAA 2x", 2), ("MSAA 4x", 3) })
            _aa.AddItem(label, id);
        _aa.Selected = (int)Config.AntiAlias;

        var upscaleRow = Row("Upscaling", _upscale = new OptionButton());
        foreach (var (label, id) in new[] { ("Off", 0), ("FSR 1.0", 1), ("FSR 2.2", 2) })
            _upscale.AddItem(label, id);
        _upscale.Selected = (int)Config.Upscale;
        _upscale.Disabled = !Config.UpscaleSupported;
        upscaleRow.TooltipText = Config.UpscaleSupported
            ? "Renders the world below screen resolution and upscales it. The interface stays sharp. "
              + "FSR 2.2 is sharper but can smear fast-moving effects."
            : "Not available on this renderer.";
        vb.AddChild(upscaleRow);

        var qualityRow = Row("Upscale Quality", _upscaleQuality = new OptionButton());
        foreach (var (label, id) in new[]
                 {
                     ("Ultra Quality", 0), ("Quality", 1), ("Balanced", 2),
                     ("Performance", 3), ("Ultra Performance", 4),
                 })
            _upscaleQuality.AddItem(label, id);
        _upscaleQuality.Selected = (int)Config.UpscaleQuality;
        var internalRes = new Label { CustomMinimumSize = new Vector2(120, 0) };
        void ShowInternalRes()
        {
            var target = GetWindow().Size;
            float s = Config.ScaleFor((Config.UpscaleLevel)_upscaleQuality.Selected);
            internalRes.Text = _upscaleQuality.Disabled
                ? "—"
                : $"{target.X * s:0} x {target.Y * s:0}";
        }
        _upscaleQuality.ItemSelected += _ => ShowInternalRes();
        _upscale.ItemSelected += i =>
        {
            _upscaleQuality.Disabled = i == (long)Config.UpscaleMode.Off || !Config.UpscaleSupported;
            ShowInternalRes();
        };
        _upscaleQuality.Disabled = Config.Upscale == Config.UpscaleMode.Off || !Config.UpscaleSupported;
        ShowInternalRes();
        qualityRow.AddChild(internalRes);
        qualityRow.TooltipText = "How far below screen resolution the world is drawn before upscaling.";
        vb.AddChild(qualityRow);
    }

    private void BuildEffectsTab(VBoxContainer vb)
    {
        vb.AddChild(Row("World Effects", _fxAmbient = new CheckButton { ButtonPressed = Config.FxAmbient }));
        _fxDistance = new HSlider
        {
            MinValue = 40, MaxValue = 400, Step = 10, Value = Config.FxDistance,
            CustomMinimumSize = new Vector2(0, 18),
        };
        var dist = new Label { Text = $"{Config.FxDistance} m", CustomMinimumSize = new Vector2(52, 0) };
        _fxDistance.ValueChanged += v => dist.Text = $"{(int)v} m";
        var distRow = Row("Effect Distance", _fxDistance);
        distRow.AddChild(dist);
        vb.AddChild(distRow);
        vb.AddChild(Row("Damage Numbers", _fxNumbers = new CheckButton { ButtonPressed = Config.DamageNumbers }));
        vb.AddChild(Row("Combat Log", _fxCombatLog = new CheckButton { ButtonPressed = Config.CombatLog }));
    }

    private void BuildControlsTab(VBoxContainer vb)
    {
        _camTurnSpeed = new HSlider
        {
            MinValue = Config.CamTurnSpeedMin, MaxValue = Config.CamTurnSpeedMax, Step = 10,
            Value = Config.CamTurnSpeed, CustomMinimumSize = new Vector2(0, 18),
        };
        var turnValue = new Label { Text = TurnSpeedText(Config.CamTurnSpeed), CustomMinimumSize = new Vector2(52, 0) };
        _camTurnSpeed.ValueChanged += v => turnValue.Text = TurnSpeedText((float)v);
        var turnRow = Row("Camera Turn Speed", _camTurnSpeed);
        turnRow.AddChild(turnValue);
        turnRow.TooltipText = "How fast the middle-mouse click spins the camera a half turn.";
        turnRow.Visible = Platform.PointerUi;
        vb.AddChild(turnRow);

        var edgeRow = Row("Screen Edge Panning", _camEdgePan = new CheckButton { ButtonPressed = Config.CamEdgePan });
        edgeRow.TooltipText = "In fullscreen, hold the cursor against the left or right border to turn the camera.";
        edgeRow.Visible = Platform.PointerUi;
        vb.AddChild(edgeRow);

        _camEdgeSpeed = new HSlider
        {
            MinValue = Config.CamEdgePanSpeedMin, MaxValue = Config.CamEdgePanSpeedMax, Step = 1,
            Value = Config.CamEdgePanSpeed, CustomMinimumSize = new Vector2(0, 18),
        };
        var edgeValue = new Label { Text = TurnSpeedText(Config.CamEdgePanSpeed), CustomMinimumSize = new Vector2(52, 0) };
        _camEdgeSpeed.ValueChanged += v => edgeValue.Text = TurnSpeedText((float)v);
        var edgeSpeedRow = Row("Edge Pan Speed", _camEdgeSpeed);
        edgeSpeedRow.AddChild(edgeValue);
        edgeSpeedRow.TooltipText = $"The original client turns at {Config.CamEdgePanSpeedRetail:0} °/s.";
        edgeSpeedRow.Visible = Platform.PointerUi;
        vb.AddChild(edgeSpeedRow);

        if (Platform.PointerUi) return;
        vb.AddChild(StickRow("Move Stick", out _moveStick, Config.MoveStickSensitivity,
                             "How far the left stick pushes before the character runs flat out."));
        vb.AddChild(StickRow("Look Stick", out _lookStick, Config.LookStickSensitivity,
                             "How fast the right stick swings the camera."));
    }

    private static HBoxContainer StickRow(string label, out HSlider slider, float value, string hint)
    {
        slider = new HSlider
        {
            MinValue = Config.StickSensitivityMin,
            MaxValue = Config.StickSensitivityMax,
            Step = Config.StickSensitivityStep,
            Value = value,
            CustomMinimumSize = new Vector2(0, 18),
        };
        var readout = new Label { Text = $"{value * 100f:F0}%", CustomMinimumSize = new Vector2(52, 0) };
        var bound = slider;
        slider.ValueChanged += v => readout.Text = $"{v * 100f:F0}%";
        var row = Row(label, bound);
        row.AddChild(readout);
        row.TooltipText = hint;
        return row;
    }

    private static string TurnSpeedText(float degreesPerSecond) => $"{degreesPerSecond:0} °/s";

    private void BuildSoundTab(VBoxContainer vb)
    {
        vb.AddChild(Row("Enabled", _sndOn = new CheckButton { ButtonPressed = Config.AudioEnabled }));
        vb.AddChild(VolumeRow("Master", Audio.BusMaster, out _volMaster, Config.MasterVolume));
        vb.AddChild(VolumeRow("Music", Audio.BusMusic, out _volMusic, Config.MusicVolume));
        vb.AddChild(VolumeRow("Sound effects", Audio.BusSfx, out _volSfx, Config.SfxVolume));
        vb.AddChild(VolumeRow("Interface sounds", Audio.BusUi, out _volUi, Config.UiVolume));
        vb.AddChild(VolumeRow("Voice", Audio.BusVoice, out _volVoice, Config.VoiceVolume));
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventJoypadButton { Pressed: true } pad)
        {
            if (CapturePadButton(pad)) GetViewport().SetInputAsHandled();
            return;
        }
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (CaptureBindKey(key)) { GetViewport().SetInputAsHandled(); return; }
        if (key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled();
        QueueFree();
    }

    private static VBoxContainer Tab(TabContainer tabs, string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var s in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{s}", 10);
        scroll.AddChild(margin);
        var vb = new VBoxContainer { CustomMinimumSize = new Vector2(Platform.Pick(TabWidth, 0), 0) };
        vb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vb.AddThemeConstantOverride("separation", 12);
        margin.AddChild(vb);
        return vb;
    }

    private static HBoxContainer VolumeRow(string label, string bus, out HSlider slider, float value)
    {
        slider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.05, Value = value,
            CustomMinimumSize = new Vector2(0, 18),
        };
        var pct = new Label { Text = $"{value * 100:0}%", CustomMinimumSize = new Vector2(52, 0) };
        slider.ValueChanged += v =>
        {
            pct.Text = $"{v * 100:0}%";
            Audio.PreviewVolume(bus, (float)v);
        };
        var row = Row(label, slider);
        row.AddChild(pct);
        return row;
    }

    private static string UiScaleText(float scale) => $"{scale * 100f:F0}%";

    private static string ViewDistanceText(float scale) => $"{scale * 100f:F0}%";

    private static HBoxContainer Row(string label, Control control)
    {
        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 10);
        hb.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hb.AddChild(control);
        return hb;
    }
}
