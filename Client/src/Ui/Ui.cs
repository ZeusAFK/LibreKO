using Godot;

namespace LibreKO;

public static class Ui
{
    public static Control? Background(Node parent, string resPath,
        Backdrop.Fit fit = Backdrop.Fit.Cover, bool bottomScrim = false)
        => Backdrop.Build(parent, resPath, fit, bottomScrim);

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
    private bool queued;

    public override void _Ready()
    {
        if (!IsInstanceValid(Target)) { QueueFree(); return; }
        Target.Resized += QueueArrange;
        Target.ChildEnteredTree += Watch;
        foreach (var child in Target.GetChildren()) Watch(child);
        QueueArrange();
    }

    private void Watch(Node child)
    {
        if (child is not Control control) return;
        control.Resized += QueueArrange;
        control.VisibilityChanged += QueueArrange;
    }

    private void QueueArrange()
    {
        if (queued) return;
        queued = true;
        Callable.From(Arrange).CallDeferred();
    }

    private void Arrange()
    {
        queued = false;
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
    private static readonly Vector2I[] CommonResolutions =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160),
    };

    private static readonly (QualityLevel Level, string Label)[] LevelChoices =
    {
        (QualityLevel.Off, "Off"), (QualityLevel.Low, "Low"), (QualityLevel.Medium, "Medium"), (QualityLevel.High, "High"),
    };

    private static readonly (CospreKinds Kind, string Label)[] CospreChoices =
    {
        (CospreKinds.Wings, "Wings"), (CospreKinds.Fairies, "Fairies"), (CospreKinds.Talismans, "Talismans"),
        (CospreKinds.Emblems, "Emblems"), (CospreKinds.PathosGlow, "Pathos Glow"),
    };

    private readonly System.Collections.Generic.List<Vector2I> resolutions = new();
    private readonly System.Collections.Generic.Dictionary<CospreKinds, CheckButton> cospreShown = new();
    private OptionButton screenPicker = null!;
    private OptionButton presetPicker = null!;
    private OptionButton shadowLevel = null!, ssaoLevel = null!;
    private OptionButton renderer = null!;
    private CheckButton showTitles = null!;
    private CheckButton questNotices = null!;
    private CheckButton tradeBlock = null!;
    private Button applyButton = null!;
    private bool dirty;
    private bool presetApplying;

    private const int TabWidth = 660;
    private const int TabHeight = 660;
    private const int TouchScreenMargin = 10;

    private OptionButton modePicker = null!;
    private OptionButton languagePicker = null!;
    private OptionButton res = null!;
    private CheckButton volFog = null!, bloom = null!, clouds = null!;
    private OptionButton crowdMode = null!;
    private OptionButton itemShine = null!;
    private CheckButton capes = null!;
    private OptionButton antiAliasing = null!;
    private OptionButton upscale = null!;
    private OptionButton upscaleQuality = null!;
    private CheckButton vsync = null!;
    private OptionButton fps = null!;
    private CheckButton fxAmbient = null!, fxNumbers = null!, combatLog = null!;
    private HSlider fxDistance = null!;
    private HSlider uiScale = null!;
    private HSlider nameScale = null!;
    private HSlider hotbarScale = null!;
    private HSlider hotbarGap = null!;
    private HSlider viewDistance = null!;
    private HSlider brightness = null!, contrast = null!, nightBrightness = null!, fog = null!;
    private HSlider camTurnSpeed = null!, camEdgeSpeed = null!;
    private HSlider? moveStick, lookStick;
    private CheckButton camEdgePan = null!;
    private CheckButton cameraShake = null!;
    private CheckButton sndOn = null!;
    private CheckButton sndOthers = null!;
    private HSlider volMaster = null!, volMusic = null!, volSfx = null!, volUi = null!, volVoice = null!;

    public static void Open(Node parent, IClientSettingsRequests? settingsRequests) =>
        parent.AddChild(new SettingsPanel { Layer = UiLayers.Settings, SettingsRequests = settingsRequests });

    public IClientSettingsRequests? SettingsRequests { get; init; }

    public int InitialTab { get; init; }

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
        tabs.CurrentTab = Mathf.Clamp(InitialTab, 0, tabs.GetTabCount() - 1);

        var btns = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btns.AddThemeConstantOverride("separation", 12);
        outer.AddChild(btns);
        var ok = new Button { Text = "OK", CustomMinimumSize = ActionButtonSize };
        ok.Pressed += () =>
        {
            if (dirty) ApplyAll();
            QueueFree();
        };
        btns.AddChild(ok);
        applyButton = new Button { Text = "Apply", CustomMinimumSize = ActionButtonSize, Disabled = true };
        applyButton.Pressed += ApplyAll;
        btns.AddChild(applyButton);
        var cancel = new Button { Text = "Cancel", CustomMinimumSize = ActionButtonSize };
        cancel.Pressed += QueueFree;
        btns.AddChild(cancel);
        foreach (Node page in tabs.GetChildren())
            TrackDirty(page);
    }

    private void TrackDirty(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            switch (child)
            {
                case CheckButton check: check.Toggled += _ => MarkDirty(); break;
                case OptionButton option: option.ItemSelected += _ => MarkDirty(); break;
                case Godot.Range range: range.ValueChanged += _ => MarkDirty(); break;
            }
            TrackDirty(child);
        }
    }

    private void MarkDirty()
    {
        dirty = true;
        applyButton.Disabled = false;
    }

    private void ApplyAll()
    {
        {
            Config.SetScreen(screenPicker.GetSelectedId());
            var resolution = resolutions.Count > 0 ? resolutions[Mathf.Clamp(res.Selected, 0, resolutions.Count - 1)]
                : new Vector2I(Config.WinWidth, Config.WinHeight);
            Config.SetVideo((Config.VideoMode)modePicker.Selected, resolution.X, resolution.Y, vsync.ButtonPressed);
            Config.SetGraphics(LevelAt(shadowLevel), LevelAt(ssaoLevel), volFog.ButtonPressed,
                               bloom.ButtonPressed, clouds.ButtonPressed, capes.ButtonPressed,
                               (Config.AaMode)antiAliasing.Selected, (Config.UpscaleMode)upscale.Selected,
                               (Config.UpscaleLevel)upscaleQuality.Selected, (Config.FpsCap)fps.Selected);
            Config.SetShowTitles(showTitles.ButtonPressed);
            Config.SetQuestNotices(questNotices.ButtonPressed);
            Config.SetTradeRequestsBlocked(tradeBlock.ButtonPressed);
            var hidden = CospreKinds.None;
            foreach (var (kind, check) in cospreShown)
                if (!check.ButtonPressed) hidden |= kind;
            Config.SetHiddenCospre(hidden);
            Config.SetEffects(fxAmbient.ButtonPressed, (int)fxDistance.Value, fxNumbers.ButtonPressed,
                              combatLog.ButtonPressed);
            Config.SetCrowdMode((LibreKO.Domain.CrowdMode)crowdMode.Selected);
            Config.SetShineStyle((LibreKO.Domain.ShineStyle)itemShine.Selected);
            Config.SetControls((float)camTurnSpeed.Value, camEdgePan.ButtonPressed, (float)camEdgeSpeed.Value);
            Config.SetCameraShake(cameraShake.ButtonPressed);
            if (moveStick != null && lookStick != null)
                Config.SetStickSensitivity((float)moveStick.Value, (float)lookStick.Value);
            Config.SetUiScale((float)uiScale.Value);
            Config.SetNamePlateScale((float)nameScale.Value);
            Config.SetHotbarLook((float)hotbarScale.Value, (int)hotbarGap.Value);
            ApplyLanguage();
            Config.SetViewDistance((float)viewDistance.Value);
            Config.SetLighting((int)brightness.Value, (int)contrast.Value, (int)nightBrightness.Value, (int)fog.Value);
            Config.SetOtherPlayersSounds(sndOthers.ButtonPressed);
            Config.SetAudio(sndOn.ButtonPressed, Config.AudioMuted, (float)volMaster.Value,
                            (float)volMusic.Value, (float)volSfx.Value,
                            (float)volUi.Value, (float)volVoice.Value);
            var driver = (Config.RenderDriver)renderer.Selected;
            if (Config.SetRenderer(driver) || !Config.RendererRunning(driver))
                Notice.Confirm(this, "The renderer changes after a restart. Restart now?", "Restart", "Later",
                    RestartClient, title: "Renderer");
        }
        dirty = false;
        applyButton.Disabled = true;
    }

    private void RestartClient()
    {
        OS.SetRestartOnExit(true, OS.GetCmdlineArgs());
        GetTree().Quit();
    }

    private static QualityLevel LevelAt(OptionButton option) =>
        LevelChoices[Mathf.Clamp(option.Selected, 0, LevelChoices.Length - 1)].Level;

    private static OptionButton LevelPicker(QualityLevel current)
    {
        var option = new OptionButton();
        for (int i = 0; i < LevelChoices.Length; i++)
            option.AddItem(LevelChoices[i].Label, i);
        option.Selected = System.Array.FindIndex(LevelChoices, c => c.Level == current);
        return option;
    }

    private static Button RestoreButton(VBoxContainer vb, System.Action restore)
    {
        var button = new Button
        {
            Text = "Restore defaults",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
        };
        button.Pressed += restore;
        vb.AddChild(new HSeparator());
        vb.AddChild(button);
        return button;
    }

    private GraphicsChoice CurrentGraphics() => new(
        LevelAt(shadowLevel), LevelAt(ssaoLevel), volFog.ButtonPressed, bloom.ButtonPressed, clouds.ButtonPressed,
        antiAliasing.Selected, (float)viewDistance.Value);

    private void ApplyPreset(GraphicsPreset preset)
    {
        if (preset == GraphicsPreset.Custom) return;
        var choice = GraphicsPresets.Of(preset);
        presetApplying = true;
        shadowLevel.Selected = System.Array.FindIndex(LevelChoices, c => c.Level == choice.Shadows);
        ssaoLevel.Selected = System.Array.FindIndex(LevelChoices, c => c.Level == choice.Ssao);
        volFog.ButtonPressed = choice.VolumetricFog;
        bloom.ButtonPressed = choice.Bloom;
        clouds.ButtonPressed = choice.Clouds;
        antiAliasing.Selected = choice.AntiAlias;
        viewDistance.Value = choice.ViewDistance;
        presetApplying = false;
        MarkDirty();
    }

    private void RefreshPreset()
    {
        if (presetApplying) return;
        presetPicker.Selected = (int)GraphicsPresets.Match(CurrentGraphics());
    }

    private void FillResolutions(int screen)
    {
        var native = DisplayServer.ScreenGetSize(screen >= 0 ? screen : DisplayServer.WindowGetCurrentScreen());
        resolutions.Clear();
        foreach (var size in CommonResolutions)
            if (size.X <= native.X && size.Y <= native.Y) resolutions.Add(size);
        if (!resolutions.Contains(native)) resolutions.Add(native);
        res.Clear();
        int selected = resolutions.Count - 1;
        for (int i = 0; i < resolutions.Count; i++)
        {
            res.AddItem($"{resolutions[i].X} x {resolutions[i].Y}{(resolutions[i] == native ? "  (native)" : "")}", i);
            if (resolutions[i].X == Config.WinWidth && resolutions[i].Y == Config.WinHeight) selected = i;
        }
        res.Selected = selected;
    }

    private static Vector2 ActionButtonSize =>
        Platform.Pick(Vector2.Zero, new Vector2(140, Ui.TouchButtonHeight));

    private static readonly (GameLanguage Language, string Label)[] LanguageChoices =
    {
        (GameLanguage.English, "English"),
        (GameLanguage.Spanish, "Espanol"),
        (GameLanguage.Chinese, "中文"),
    };

    private void ApplyLanguage()
    {
        var index = Mathf.Clamp(languagePicker.Selected, 0, LanguageChoices.Length - 1);
        var chosen = LanguageChoices[index].Language;
        if (chosen == Config.Language)
            return;

        Config.SetLanguage(chosen);
        SettingsRequests?.Language(chosen);
    }

    private void BuildDisplayTab(VBoxContainer vb)
    {
        vb.AddChild(Row("Language", languagePicker = new OptionButton()));
        foreach (var (language, label) in LanguageChoices)
            languagePicker.AddItem(label, (int)language);
        languagePicker.Selected = Array.FindIndex(LanguageChoices, c => c.Language == Config.Language);
        if (languagePicker.Selected < 0)
            languagePicker.Selected = 0;

        var screenRow = Row("Monitor", screenPicker = new OptionButton());
        screenRow.Visible = Platform.PointerUi;
        vb.AddChild(screenRow);
        screenPicker.AddItem("Current", Config.CurrentScreen);
        for (int s = 0; s < DisplayServer.GetScreenCount(); s++)
        {
            var size = DisplayServer.ScreenGetSize(s);
            screenPicker.AddItem($"Monitor {s + 1}  ({size.X} x {size.Y})", s);
        }
        screenPicker.Selected = Mathf.Max(0, screenPicker.GetItemIndex(Config.Screen));
        screenPicker.ItemSelected += _ => FillResolutions(screenPicker.GetSelectedId());

        var windowRow = Row("Window", modePicker = new OptionButton());
        windowRow.Visible = Platform.PointerUi;
        vb.AddChild(windowRow);
        modePicker.AddItem("Windowed", 0);
        modePicker.AddItem("Borderless Fullscreen", 1);
        modePicker.AddItem("Fullscreen", 2);
        modePicker.Selected = (int)Config.WindowMode;
        modePicker.ItemSelected += _ => res.Disabled = modePicker.Selected != 0;

        var resRow = Row("Resolution", res = new OptionButton());
        resRow.Visible = Platform.PointerUi;
        vb.AddChild(resRow);
        FillResolutions(Config.Screen);
        res.Disabled = Config.WindowMode != Config.VideoMode.Windowed;

        uiScale = new HSlider
        {
            MinValue = Config.UiScaleMin, MaxValue = Config.UiScaleMax, Step = Config.UiScaleStep,
            Value = Config.EffectiveUiScale, CustomMinimumSize = new Vector2(0, 18),
        };
        var scaleValue = new Label
        {
            Text = UiScaleText(Config.EffectiveUiScale),
            CustomMinimumSize = new Vector2(52, 0),
        };
        uiScale.ValueChanged += v => scaleValue.Text = UiScaleText((float)v);
        var scaleRow = Row("UI Size", uiScale);
        scaleRow.AddChild(scaleValue);
        scaleRow.TooltipText = "Scales every menu, window and HUD element. Larger values suit small or touch screens.";
        vb.AddChild(scaleRow);

        nameScale = new HSlider
        {
            MinValue = Config.NamePlateScaleMin, MaxValue = Config.NamePlateScaleMax, Step = Config.NamePlateScaleStep,
            Value = Config.NamePlateScale, CustomMinimumSize = new Vector2(0, 18),
        };
        var nameScaleValue = new Label
        {
            Text = UiScaleText(Config.NamePlateScale),
            CustomMinimumSize = new Vector2(52, 0),
        };
        nameScale.ValueChanged += v => nameScaleValue.Text = UiScaleText((float)v);
        var nameScaleRow = Row("Name Size", nameScale);
        nameScaleRow.AddChild(nameScaleValue);
        nameScaleRow.TooltipText = "Size of the names over characters, monsters and NPCs.";
        vb.AddChild(nameScaleRow);

        hotbarScale = new HSlider
        {
            MinValue = HotbarLayout.ScaleMin, MaxValue = HotbarLayout.ScaleMax, Step = HotbarLayout.ScaleStep,
            Value = Config.HotbarScale, CustomMinimumSize = new Vector2(0, 18),
        };
        var hotbarScaleValue = new Label { Text = UiScaleText(Config.HotbarScale), CustomMinimumSize = new Vector2(52, 0) };
        hotbarScale.ValueChanged += v => hotbarScaleValue.Text = UiScaleText((float)v);
        var hotbarScaleRow = Row("Skill Bar Size", hotbarScale);
        hotbarScaleRow.AddChild(hotbarScaleValue);
        hotbarScaleRow.TooltipText = "Size of the skill bars.";
        vb.AddChild(hotbarScaleRow);

        hotbarGap = new HSlider
        {
            MinValue = HotbarLayout.GapMin, MaxValue = HotbarLayout.GapMax, Step = 1,
            Value = Config.HotbarGap, CustomMinimumSize = new Vector2(0, 18),
        };
        var hotbarGapValue = new Label { Text = $"{Config.HotbarGap} px", CustomMinimumSize = new Vector2(52, 0) };
        hotbarGap.ValueChanged += v => hotbarGapValue.Text = $"{(int)v} px";
        var hotbarGapRow = Row("Skill Bar Gap", hotbarGap);
        hotbarGapRow.AddChild(hotbarGapValue);
        hotbarGapRow.TooltipText = "Space between the slots of the skill bars.";
        vb.AddChild(hotbarGapRow);

        var resetLayout = new Button { Text = "Reset window positions", FocusMode = Control.FocusModeEnum.None };
        resetLayout.Pressed += () => Notice.Confirm(this, "Move every window back to its default place?", "Reset", "Cancel",
            HudLayout.ResetAll, title: "Windows");
        var resetRow = Row("Windows", resetLayout);
        resetRow.TooltipText = "Forgets every moved or resized window and puts it back where it starts.";
        vb.AddChild(resetRow);

        var vsyncRow = Row("V-Sync", vsync = new CheckButton { ButtonPressed = Config.VSync });
        vsyncRow.Visible = Platform.PointerUi;
        vsyncRow.TooltipText = "Caps the frame rate to your monitor's refresh rate. Turn it off to see "
                               + "what the other graphics settings are actually doing.";
        vb.AddChild(vsyncRow);

        vb.AddChild(Row("FPS Limit", fps = new OptionButton()));
        foreach (var (label, id) in new[] { ("Unlimited", 0), ("30", 1), ("60", 2), ("120", 3), ("144", 4), ("240", 5) })
            fps.AddItem(label, id);
        fps.Selected = (int)Config.FpsLimit;

        var titlesRow = Row("Show Titles", showTitles = new CheckButton { ButtonPressed = Config.ShowTitles });
        titlesRow.TooltipText = "Achievement titles under the names of characters.";
        vb.AddChild(titlesRow);

        var questRow = Row("Quest Notifications", questNotices = new CheckButton { ButtonPressed = Config.QuestNotices });
        questRow.TooltipText = "The quest progress notice in the middle of the screen when you kill or collect for a quest.";
        vb.AddChild(questRow);

        var combatLogRow = Row("Combat Log", combatLog = new CheckButton { ButtonPressed = Config.CombatLog });
        combatLogRow.TooltipText = "The combat log window with damage, skills, loot and system notices.";
        vb.AddChild(combatLogRow);

        var tradeRow = Row("Refuse Trade Requests", tradeBlock = new CheckButton { ButtonPressed = Config.TradeRequestsBlocked });
        tradeRow.TooltipText = "Other players' trade requests are declined. The Block_Trade_Request and Allow_Trade_Request commands switch it too.";
        vb.AddChild(tradeRow);

        RestoreButton(vb, () =>
        {
            screenPicker.Selected = 0;
            modePicker.Selected = (int)Config.DefaultOf<Config.VideoMode>(nameof(Config.WindowMode), "video", "mode");
            uiScale.Value = Config.DefaultOf<float>(nameof(Config.UiScale), "video", "ui_scale");
            nameScale.Value = Config.DefaultOf<float>(nameof(Config.NamePlateScale), "video", "name_plate_scale");
            hotbarScale.Value = Config.DefaultOf<float>(nameof(Config.HotbarScale), "hotbar", "scale");
            hotbarGap.Value = Config.DefaultOf<int>(nameof(Config.HotbarGap), "hotbar", "gap");
            vsync.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.VSync), "video", "vsync");
            fps.Selected = (int)Config.DefaultOf<Config.FpsCap>(nameof(Config.FpsLimit), "graphics", "fps_limit");
            showTitles.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.ShowTitles), "hud", "show_titles");
            questNotices.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.QuestNotices), "hud", "quest_notices");
            combatLog.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.CombatLog), "hud", "combat_log");
            tradeBlock.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.TradeRequestsBlocked), "user_option", "trade_state");
        });
    }

    private void BuildGraphicsTab(VBoxContainer vb)
    {
        var presetRow = Row("Quality", presetPicker = new OptionButton());
        foreach (var preset in GraphicsPresets.Named)
            presetPicker.AddItem(preset.ToString(), (int)preset);
        presetPicker.AddItem("Custom", (int)GraphicsPreset.Custom);
        presetPicker.ItemSelected += index => ApplyPreset((GraphicsPreset)presetPicker.GetItemId((int)index));
        presetRow.TooltipText = "Sets shadows, occlusion, fog, bloom, clouds, anti-aliasing and view distance together.";
        vb.AddChild(presetRow);
        viewDistance = new HSlider
        {
            MinValue = Config.ViewDistanceMin, MaxValue = Config.ViewDistanceMax, Step = 0.05,
            Value = Config.ViewDistance, CustomMinimumSize = new Vector2(0, 18),
        };
        var viewValue = new Label
        {
            Text = ViewDistanceText(Config.ViewDistance),
            CustomMinimumSize = new Vector2(52, 0),
        };
        viewDistance.ValueChanged += v => viewValue.Text = ViewDistanceText((float)v);
        var viewRow = Row("View Distance", viewDistance);
        viewRow.AddChild(viewValue);
        viewRow.TooltipText = "How far terrain, objects and characters draw. Lower values run faster on phones.";
        vb.AddChild(viewRow);

        brightness = LightRow(vb, "Brightness", LightSliders.Brightness, Config.Brightness);
        contrast = LightRow(vb, "Contrast", LightSliders.Contrast, Config.Contrast);
        nightBrightness = LightRow(vb, "Night Brightness", LightSliders.NightBrightness, Config.NightBrightness);
        fog = LightRow(vb, "Fog", LightSliders.Fog, Config.Fog);

        vb.AddChild(Row("Shadows", shadowLevel = LevelPicker(Config.ShadowQuality)));
        vb.AddChild(Row("Ambient Occlusion", ssaoLevel = LevelPicker(Config.SsaoQuality)));
        vb.AddChild(Row("Volumetric Fog", volFog = new CheckButton { ButtonPressed = Config.VolumetricFog }));
        vb.AddChild(Row("Bloom", bloom = new CheckButton { ButtonPressed = Config.Bloom }));
        var crowdRow = Row("Crowd Mode", crowdMode = new OptionButton());
        foreach (LibreKO.Domain.CrowdMode mode in System.Enum.GetValues<LibreKO.Domain.CrowdMode>())
            crowdMode.AddItem(LibreKO.Domain.CrowdModes.Label(mode), (int)mode);
        crowdMode.Selected = (int)Config.Crowd;
        crowdRow.TooltipText = "How many effects other players' skills and gathering may show in a crowd. "
            + "Your own, your target's and your party's effects always show.";
        vb.AddChild(crowdRow);
        vb.AddChild(Row("Clouds", clouds = new CheckButton { ButtonPressed = Config.Clouds }));
        var capeRow = Row("Clan Capes", capes = new CheckButton { ButtonPressed = Config.Capes });
        capeRow.TooltipText = "Simulated cloth on every player in range. Costs a lot of frame time.";
        vb.AddChild(capeRow);
        vb.AddChild(Row("Anti-Aliasing", antiAliasing = new OptionButton()));
        foreach (var (label, id) in new[] { ("Off", 0), ("FXAA", 1), ("MSAA 2x", 2), ("MSAA 4x", 3) })
            antiAliasing.AddItem(label, id);
        antiAliasing.Selected = (int)Config.AntiAlias;

        var upscaleRow = Row("Upscaling", upscale = new OptionButton());
        foreach (var (label, id) in new[] { ("Off", 0), ("FSR 1.0", 1), ("FSR 2.2", 2) })
            upscale.AddItem(label, id);
        upscale.Selected = (int)Config.Upscale;
        upscale.Disabled = !Config.UpscaleSupported;
        upscaleRow.TooltipText = Config.UpscaleSupported
            ? "Renders the world below screen resolution and upscales it. The interface stays sharp. "
              + "FSR 2.2 is sharper but can smear fast-moving effects."
            : "Not available on this renderer.";
        vb.AddChild(upscaleRow);

        var qualityRow = Row("Upscale Quality", upscaleQuality = new OptionButton());
        foreach (var (label, id) in new[]
                 {
                     ("Ultra Quality", 0), ("Quality", 1), ("Balanced", 2),
                     ("Performance", 3), ("Ultra Performance", 4),
                 })
            upscaleQuality.AddItem(label, id);
        upscaleQuality.Selected = (int)Config.UpscaleQuality;
        var internalRes = new Label { CustomMinimumSize = new Vector2(120, 0) };
        void ShowInternalRes()
        {
            var target = GetWindow().Size;
            float s = Config.ScaleFor((Config.UpscaleLevel)upscaleQuality.Selected);
            internalRes.Text = upscaleQuality.Disabled
                ? "—"
                : $"{target.X * s:0} x {target.Y * s:0}";
        }
        upscaleQuality.ItemSelected += _ => ShowInternalRes();
        upscale.ItemSelected += i =>
        {
            upscaleQuality.Disabled = i == (long)Config.UpscaleMode.Off || !Config.UpscaleSupported;
            ShowInternalRes();
        };
        upscaleQuality.Disabled = Config.Upscale == Config.UpscaleMode.Off || !Config.UpscaleSupported;
        ShowInternalRes();
        qualityRow.AddChild(internalRes);
        qualityRow.TooltipText = "How far below screen resolution the world is drawn before upscaling.";
        vb.AddChild(qualityRow);

        var rendererRow = Row("Renderer", renderer = new OptionButton());
        renderer.AddItem("Direct3D 12", (int)Config.RenderDriver.Direct3D12);
        renderer.AddItem("Vulkan", (int)Config.RenderDriver.Vulkan);
        renderer.Selected = (int)Config.Renderer;
        rendererRow.TooltipText = $"The graphics API. Changing it needs a restart. Running now: {Config.RunningDriver}.";
        rendererRow.Visible = OS.GetName() == "Windows";
        vb.AddChild(rendererRow);

        presetPicker.Selected = (int)GraphicsPresets.Match(CurrentGraphics());
        shadowLevel.ItemSelected += _ => RefreshPreset();
        ssaoLevel.ItemSelected += _ => RefreshPreset();
        volFog.Toggled += _ => RefreshPreset();
        bloom.Toggled += _ => RefreshPreset();
        clouds.Toggled += _ => RefreshPreset();
        antiAliasing.ItemSelected += _ => RefreshPreset();
        viewDistance.ValueChanged += _ => RefreshPreset();

        RestoreButton(vb, () =>
        {
            ApplyPreset(GraphicsPreset.High);
            crowdMode.Selected = (int)Config.DefaultOf<LibreKO.Domain.CrowdMode>(nameof(Config.Crowd), "graphics", "crowd_mode");
            capes.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.Capes), "graphics", "capes");
            upscale.Selected = (int)Config.DefaultOf<Config.UpscaleMode>(nameof(Config.Upscale), "graphics", "upscale");
            upscaleQuality.Selected = (int)Config.DefaultOf<Config.UpscaleLevel>(nameof(Config.UpscaleQuality), "graphics", "upscale_quality");
            brightness.Value = LightRange.Neutral;
            contrast.Value = LightRange.Neutral;
            nightBrightness.Value = LightRange.Neutral;
            fog.Value = LightRange.Neutral;
            renderer.Selected = (int)Config.RenderDriver.Direct3D12;
        });
    }

    private void BuildEffectsTab(VBoxContainer vb)
    {
        vb.AddChild(Row("World Effects", fxAmbient = new CheckButton { ButtonPressed = Config.FxAmbient }));
        fxDistance = new HSlider
        {
            MinValue = 40, MaxValue = 400, Step = 10, Value = Config.FxDistance,
            CustomMinimumSize = new Vector2(0, 18),
        };
        var dist = new Label { Text = $"{Config.FxDistance} m", CustomMinimumSize = new Vector2(52, 0) };
        fxDistance.ValueChanged += v => dist.Text = $"{(int)v} m";
        var distRow = Row("Effect Distance", fxDistance);
        distRow.AddChild(dist);
        vb.AddChild(distRow);
        vb.AddChild(Row("Damage Numbers", fxNumbers = new CheckButton { ButtonPressed = Config.DamageNumbers }));
        var shineRow = Row("Item Shine", itemShine = new OptionButton());
        foreach (LibreKO.Domain.ShineStyle style in System.Enum.GetValues<LibreKO.Domain.ShineStyle>())
            itemShine.AddItem(LibreKO.Domain.ShineStyles.Label(style), (int)style);
        itemShine.Selected = (int)Config.Shine;
        shineRow.TooltipText = "How upgraded gear glows: our gold glow, or the classic look that brightens the item's own colours.";
        vb.AddChild(shineRow);

        vb.AddChild(new HSeparator());
        vb.AddChild(new Label { Text = "Other players" });
        foreach (var (kind, label) in CospreChoices)
        {
            var shown = new CheckButton { ButtonPressed = (Config.HiddenCospre & kind) == 0 };
            cospreShown[kind] = shown;
            var row = Row($"Show {label}", shown);
            row.TooltipText = "Hides this kind of costume piece on other players to save frame time; yours always shows.";
            vb.AddChild(row);
        }

        RestoreButton(vb, () =>
        {
            fxAmbient.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.FxAmbient), "effects", "ambient");
            fxDistance.Value = Config.DefaultOf<int>(nameof(Config.FxDistance), "effects", "distance");
            fxNumbers.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.DamageNumbers), "effects", "damage_numbers");
            itemShine.Selected = (int)Config.DefaultOf<LibreKO.Domain.ShineStyle>(nameof(Config.Shine), "effects", "item_shine");
            foreach (var check in cospreShown.Values) check.ButtonPressed = true;
        });
    }

    private void BuildControlsTab(VBoxContainer vb)
    {
        camTurnSpeed = new HSlider
        {
            MinValue = Config.CamTurnSpeedMin, MaxValue = Config.CamTurnSpeedMax, Step = 10,
            Value = Config.CamTurnSpeed, CustomMinimumSize = new Vector2(0, 18),
        };
        var turnValue = new Label { Text = TurnSpeedText(Config.CamTurnSpeed), CustomMinimumSize = new Vector2(52, 0) };
        camTurnSpeed.ValueChanged += v => turnValue.Text = TurnSpeedText((float)v);
        var turnRow = Row("Camera Turn Speed", camTurnSpeed);
        turnRow.AddChild(turnValue);
        turnRow.TooltipText = "How fast the middle-mouse click spins the camera a half turn.";
        turnRow.Visible = Platform.PointerUi;
        vb.AddChild(turnRow);

        var shakeRow = Row("Camera Shake", cameraShake = new CheckButton { ButtonPressed = Config.CameraShake });
        shakeRow.TooltipText = "Let gates, rolling stones and rockslides shake the camera.";
        vb.AddChild(shakeRow);

        var edgeRow = Row("Screen Edge Panning", camEdgePan = new CheckButton { ButtonPressed = Config.CamEdgePan });
        edgeRow.TooltipText = "In fullscreen, hold the cursor against the left or right border to turn the camera.";
        edgeRow.Visible = Platform.PointerUi;
        vb.AddChild(edgeRow);

        camEdgeSpeed = new HSlider
        {
            MinValue = Config.CamEdgePanSpeedMin, MaxValue = Config.CamEdgePanSpeedMax, Step = 1,
            Value = Config.CamEdgePanSpeed, CustomMinimumSize = new Vector2(0, 18),
        };
        var edgeValue = new Label { Text = TurnSpeedText(Config.CamEdgePanSpeed), CustomMinimumSize = new Vector2(52, 0) };
        camEdgeSpeed.ValueChanged += v => edgeValue.Text = TurnSpeedText((float)v);
        var edgeSpeedRow = Row("Edge Pan Speed", camEdgeSpeed);
        edgeSpeedRow.AddChild(edgeValue);
        edgeSpeedRow.TooltipText = $"The original client turns at {Config.CamEdgePanSpeedRetail:0} °/s.";
        edgeSpeedRow.Visible = Platform.PointerUi;
        vb.AddChild(edgeSpeedRow);

        if (!Platform.PointerUi)
        {
            vb.AddChild(StickRow("Move Stick", out moveStick, Config.MoveStickSensitivity,
                                 "How far the left stick pushes before the character runs flat out."));
            vb.AddChild(StickRow("Look Stick", out lookStick, Config.LookStickSensitivity,
                                 "How fast the right stick swings the camera."));
        }

        RestoreButton(vb, () =>
        {
            camTurnSpeed.Value = Config.DefaultOf<float>(nameof(Config.CamTurnSpeed), "controls", "cam_turn_speed");
            cameraShake.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.CameraShake), "controls", "camera_shake");
            camEdgePan.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.CamEdgePan), "controls", "cam_edge_pan");
            camEdgeSpeed.Value = Config.DefaultOf<float>(nameof(Config.CamEdgePanSpeed), "controls", "cam_edge_pan_speed");
            if (moveStick != null) moveStick.Value = Config.DefaultOf<float>(nameof(Config.MoveStickSensitivity), "controls", "move_stick");
            if (lookStick != null) lookStick.Value = Config.DefaultOf<float>(nameof(Config.LookStickSensitivity), "controls", "look_stick");
        });
    }

    private static HSlider LightRow(VBoxContainer vb, string label, LightRange range, int value)
    {
        var slider = new HSlider
        {
            MinValue = range.Min, MaxValue = range.Max, Step = LightRange.Step,
            Value = value, CustomMinimumSize = new Vector2(0, 18),
        };
        var shown = new Label { Text = $"{value} %", CustomMinimumSize = new Vector2(52, 0) };
        slider.ValueChanged += v => shown.Text = $"{(int)v} %";
        var row = Row(label, slider);
        row.AddChild(shown);
        vb.AddChild(row);
        return slider;
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
        vb.AddChild(Row("Enabled", sndOn = new CheckButton { ButtonPressed = Config.AudioEnabled }));
        vb.AddChild(Row("Other players' sounds", sndOthers = new CheckButton { ButtonPressed = Config.OtherPlayersSounds }));
        vb.AddChild(VolumeRow("Master", Audio.BusMaster, out volMaster, Config.MasterVolume));
        vb.AddChild(VolumeRow("Music", Audio.BusMusic, out volMusic, Config.MusicVolume));
        vb.AddChild(VolumeRow("Sound effects", Audio.BusSfx, out volSfx, Config.SfxVolume));
        vb.AddChild(VolumeRow("Interface sounds", Audio.BusUi, out volUi, Config.UiVolume));
        vb.AddChild(VolumeRow("Voice", Audio.BusVoice, out volVoice, Config.VoiceVolume));

        RestoreButton(vb, () =>
        {
            sndOn.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.AudioEnabled), "audio", "enabled");
            sndOthers.ButtonPressed = Config.DefaultOf<bool>(nameof(Config.OtherPlayersSounds), "audio", "other_players");
            volMaster.Value = Config.DefaultOf<float>(nameof(Config.MasterVolume), "audio", "master");
            volMusic.Value = Config.DefaultOf<float>(nameof(Config.MusicVolume), "audio", "music");
            volSfx.Value = Config.DefaultOf<float>(nameof(Config.SfxVolume), "audio", "sfx");
            volUi.Value = Config.DefaultOf<float>(nameof(Config.UiVolume), "audio", "ui");
            volVoice.Value = Config.DefaultOf<float>(nameof(Config.VoiceVolume), "audio", "voice");
        });
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventJoypadButton { Pressed: true } pad)
        {
            if (CapturePadButton(pad)) GetViewport().SetInputAsHandled();
            return;
        }
        if (ev is InputEventKey { Pressed: false } released)
        {
            if (CaptureModifierRelease(released)) GetViewport().SetInputAsHandled();
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
