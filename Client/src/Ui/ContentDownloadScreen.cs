using System.Threading;
using Godot;

namespace LibreKO;

public partial class ContentDownloadScreen : Control
{
    private static readonly Color Ink = new("05070c");
    private static readonly Color Gold = new("c9a227");
    private static readonly Color Amber = new("e8b562");
    private static readonly Color Muted = new("9faabc");
    private static readonly Color Bright = new("f4f7fb");
    private static readonly Color PanelTop = new(0.043f, 0.071f, 0.125f, 0.90f);
    private static readonly Color PanelBottom = new(0.027f, 0.047f, 0.090f, 0.95f);
    private static readonly Color PanelEdge = new(0.502f, 0.416f, 0.220f, 0.40f);
    private static readonly Color TrackColor = new(0.071f, 0.106f, 0.169f, 0.70f);
    private static readonly Color BarTop = new("f0c173");
    private static readonly Color BarBottom = new("d08f32");
    private static readonly Color Divider = new(0.180f, 0.290f, 0.420f, 0.85f);

    private const string Art = "res://assets/backgrounds/launcher.png";
    private static float PanelHeight => Platform.Pick(168f, 146f);
    private static float PanelMargin => Platform.Pick(22f, 16f);
    private const float BarHeight = 9f;
    private const long IdleGraceMs = 4000;

    private Label _percent = null!;
    private Label _detail = null!;
    private Label _transfer = null!;
    private Label _eta = null!;
    private ColorRect _fill = null!;
    private Control _track = null!;
    private Button _action = null!;

    private ContentInstall _install = null!;
    private CancellationTokenSource _cancel = null!;
    private ContentStage _shownStage = ContentStage.Idle;
    private bool _actionTaken;
    private bool _leaving;

    public override void _Ready()
    {
        Ui.MenuScale(true);
        TopLevel = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildBackdrop();
        BuildPanel();

        GD.Print($"[content] download screen up {Size.X:0}x{Size.Y:0} — patch host {Config.PatchUrl}"
                 + $" app build {Build.ApkBuild}");
        _install = new ContentInstall(Config.PatchUrl);
        SetProcess(true);

        DisplayServer.ScreenSetKeepOn(true);
        _cancel = new CancellationTokenSource();
        _ = _install.RunAsync(_cancel.Token);
    }

    public override void _ExitTree()
    {
        _cancel?.Cancel();
        DisplayServer.ScreenSetKeepOn(false);
    }

    private void BuildBackdrop()
    {
        var bg = new ColorRect { Color = Ink, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        if (ResourceLoader.Exists(Art))
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>(Art),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            art.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(art);
        }

        var scrim = new ColorRect
        {
            Color = new Color(0.02f, 0.03f, 0.05f, 0.28f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scrim);
    }

    private void BuildPanel()
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var skin = new StyleBoxFlat
        {
            BgColor = PanelBottom,
            BorderColor = PanelEdge,
            ShadowColor = new Color(0f, 0f, 0f, 0.55f),
            ShadowSize = 10,
        };
        skin.SetCornerRadiusAll(6);
        skin.SetBorderWidthAll(1);
        skin.SetContentMarginAll(0);
        panel.AddThemeStyleboxOverride("panel", skin);
        panel.AnchorLeft = 0f; panel.AnchorRight = 1f;
        panel.AnchorTop = 1f; panel.AnchorBottom = 1f;
        panel.OffsetLeft = PanelMargin; panel.OffsetRight = -PanelMargin;
        panel.OffsetTop = -(PanelHeight + PanelMargin); panel.OffsetBottom = -PanelMargin;
        AddChild(panel);

        var sheen = new ColorRect
        {
            Color = PanelTop,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorBottom = 0.5f,
        };
        panel.AddChild(sheen);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 26);
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 34; row.OffsetRight = -34;
        row.OffsetTop = 20; row.OffsetBottom = -20;
        panel.AddChild(row);

        row.AddChild(BuildPercent());
        row.AddChild(new ColorRect
        {
            Color = Divider,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = SizeFlags.Fill,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        row.AddChild(BuildStatus());
        row.AddChild(BuildAction());
    }

    private Control BuildPercent()
    {
        var box = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 6);
        _percent = Text("0", Platform.Pick(62, 54), Bright);
        _percent.CustomMinimumSize = new Vector2(Platform.Pick(150, 128), 0);
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        box.AddChild(_percent);
        var pct = Text("%", 22, Amber);
        pct.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        box.AddChild(pct);
        return box;
    }

    private Control BuildStatus()
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        col.AddThemeConstantOverride("separation", 9);

        _detail = Text("Preparing", 19, Amber);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_detail);

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 18);
        _transfer = Text("", 15, Muted);
        line.AddChild(_transfer);
        _eta = Text("", 15, Muted);
        line.AddChild(_eta);
        col.AddChild(line);

        _track = new Control { CustomMinimumSize = new Vector2(0, BarHeight) };
        _track.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _track.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(_track);

        var back = new ColorRect { Color = TrackColor, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(LayoutPreset.FullRect);
        _track.AddChild(back);

        _fill = new ColorRect
        {
            Color = BarTop,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorTop = 0f, AnchorBottom = 1f, AnchorLeft = 0f, AnchorRight = 0f,
        };
        _track.AddChild(_fill);

        var gloss = new ColorRect
        {
            Color = new Color(BarBottom, 0.55f),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorTop = 0.5f, AnchorBottom = 1f, AnchorLeft = 0f, AnchorRight = 1f,
        };
        _fill.AddChild(gloss);
        return col;
    }

    private Control BuildAction()
    {
        _action = new Button
        {
            Text = "",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(Platform.Pick(240, 260), Platform.Pick(76, 84)),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Visible = false,
        };
        var face = new StyleBoxFlat { BgColor = new Color(Gold, 0.22f), BorderColor = new Color("e8ce8a") };
        face.SetCornerRadiusAll(4);
        face.SetBorderWidthAll(1);
        var hover = (StyleBoxFlat)face.Duplicate();
        hover.BgColor = new Color(Gold, 0.38f);
        _action.AddThemeStyleboxOverride("normal", face);
        _action.AddThemeStyleboxOverride("hover", hover);
        _action.AddThemeStyleboxOverride("pressed", hover);
        _action.AddThemeColorOverride("font_color", Bright);
        _action.AddThemeFontSizeOverride("font_size", 22);
        _action.Pressed += OnAction;
        return _action;
    }

    private string ActionLabel(ContentInstall.Snapshot s) => s.Stage switch
    {
        ContentStage.Failed => "Retry",
        ContentStage.AppOutdated => _install.AppUpdateUrl.Length > 0 ? "Get the new app" : "Check again",
        ContentStage.NeedsConsent => $"Download {Bytes(s.Total - s.Done)}",
        _ => "",
    };

    private void OnAction()
    {
        var stage = _install.Read().Stage;
        _actionTaken = true;
        _action.Visible = false;

        if (stage == ContentStage.AppOutdated)
        {
            if (_install.AppUpdateUrl.Length > 0) OS.ShellOpen(_install.AppUpdateUrl);
            else Restart();
            return;
        }
        if (stage == ContentStage.NeedsConsent)
        {
            _install.GiveConsent();
            return;
        }
        Restart();
    }

    private void Restart()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        _install = new ContentInstall(Config.PatchUrl);
        _ = _install.RunAsync(_cancel.Token);
    }

    public override void _Process(double delta)
    {
        var box = GetViewportRect().Size;
        if (Size != box) Size = box;

        var s = _install.Read();
        float frac = s.Total > 0 ? Mathf.Clamp((float)s.Done / s.Total, 0f, 1f) : 0f;

        bool moving = s.IdleMs < IdleGraceMs;
        bool flowing = moving && s.BytesPerSecond > 1;

        _percent.Text = ((int)(frac * 100f)).ToString();
        _fill.AnchorRight = frac;
        _detail.Text = s.Stage == ContentStage.Downloading && !moving
            ? "Waiting for the server…"
            : s.Detail.Length > 0 ? s.Detail : "Preparing";

        _transfer.Text = s.Total > 0
            ? $"{Bytes(s.Done)} of {Bytes(s.Total)}" + (flowing ? $"   {Bytes((long)s.BytesPerSecond)}/s" : "")
            : "";
        _eta.Text = flowing && s.Stage == ContentStage.Downloading && s.Total > s.Done
            ? $"about {Eta((s.Total - s.Done) / s.BytesPerSecond)} left"
            : "";

        if (s.Stage != _shownStage)
        {
            _shownStage = s.Stage;
            _actionTaken = false;
        }

        string label = ActionLabel(s);
        bool offer = label.Length > 0 && !_actionTaken;
        if (offer && _action.Text != label) _action.Text = label;
        if (_action.Visible != offer) _action.Visible = offer;

        if (s.Stage == ContentStage.Done && !_leaving)
        {
            _leaving = true;
            DisplayServer.ScreenSetKeepOn(false);
            Packs.MountDownloaded();
            GD.Print("[content] install complete — entering login");
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/Login.tscn");
        }
    }

    private static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    private static string Bytes(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.00} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
        : $"{bytes / 1024.0:0} KB";

    private static string Eta(double seconds) => seconds >= 3600
        ? $"{seconds / 3600:0} h {(seconds % 3600) / 60:0} min"
        : seconds >= 60 ? $"{seconds / 60:0} min" : $"{seconds:0} sec";
}
