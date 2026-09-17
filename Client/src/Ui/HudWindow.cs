using Godot;

namespace LibreKO;

public partial class HudWindow : PanelContainer
{
    public VBoxContainer Body { get; }

    public event System.Action? Closed;

    private static Vector2 HeaderButtonSize => Platform.Pick(new Vector2(20, 20), new Vector2(46, 46));

    public event System.Action<bool>? MinimizedChanged;

    public string Title { set => _titleLbl.Text = value; }

    public bool Minimized { get; private set; }

    private const int MinimizedTitleWidth = 124;
    private const int MinimizeGlyphFontSize = 17;

    private readonly Label _titleLbl;
    private bool _fitQueued;
    private MarginContainer? _content;
    private Button? _minimizeBtn;
    private PanelContainer? _header;
    private Label? _marker;

    public void SetHeaderAccent(Color fill, Color border, Color marker)
    {
        if (_header == null) return;
        var sb = UiTheme.WindowHeaderBand();
        sb.BgColor = fill;
        sb.BorderColor = border;
        _header.AddThemeStyleboxOverride("panel", sb);
        _marker?.AddThemeColorOverride("font_color", marker);
    }

    public void SetMinimized(bool minimized)
    {
        if (_content == null || Minimized == minimized) return;
        Minimized = minimized;
        _content.Visible = !minimized;
        if (_minimizeBtn != null)
        {
            _minimizeBtn.Text = minimized ? "❒" : "—";
            _minimizeBtn.TooltipText = minimized ? "Restore" : "Minimize";
        }

        _titleLbl.ClipText = minimized;
        _titleLbl.TextOverrunBehavior = minimized
            ? TextServer.OverrunBehavior.TrimEllipsis
            : TextServer.OverrunBehavior.NoTrimming;
        _titleLbl.CustomMinimumSize = minimized ? new Vector2(MinimizedTitleWidth, 0) : Vector2.Zero;

        Size = Vector2.Zero;
        ResetSize();
        MinimizedChanged?.Invoke(minimized);
    }

    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Callable.From(Fit).CallDeferred();
    }

    private void Fit()
    {
        _fitQueued = false;
        if (IsInsideTree()) ResetSize();
    }

    public HudWindow(
        string id,
        string title,
        Vector2 defaultPos,
        int bodyMinWidth = 0,
        bool resizable = false,
        Vector2 minimumSize = default,
        bool persistLayout = true,
        Texture2D? titleIcon = null,
        bool minimizable = false)
    {
        AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        GrowHorizontal = GrowDirection.End;
        GrowVertical = GrowDirection.End;

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 0);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        margin.AddChild(root);

        var header = new PanelContainer { ClipContents = true };
        header.AddThemeStyleboxOverride("panel", UiTheme.WindowHeaderBand());
        root.AddChild(header);
        _header = header;

        var facets = new Control { MouseFilter = MouseFilterEnum.Ignore };
        header.AddChild(facets);
        facets.AddChild(new ColorRect
        {
            Color = new Color(0.78f, 0.65f, 0.36f, 0.075f),
            Position = new Vector2(112, -30),
            Size = new Vector2(42, 86),
            RotationDegrees = -35,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        facets.AddChild(new ColorRect
        {
            Color = new Color(0.12f, 0.09f, 0.045f, 0.10f),
            Position = new Vector2(154, -30),
            Size = new Vector2(25, 86),
            RotationDegrees = -35,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var bar = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(0, 20),
        };
        bar.AddThemeConstantOverride("separation", 6);
        header.AddChild(bar);

        if (titleIcon != null)
        {
            var icon = new TextureRect
            {
                Texture = titleIcon,
                CustomMinimumSize = new Vector2(17, 17),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = UiTheme.GoldBright,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bar.AddChild(icon);
        }
        else
        {
            var marker = UiTheme.Text("◆", 11, UiTheme.GoldBright, HorizontalAlignment.Center);
            marker.CustomMinimumSize = new Vector2(16, 20);
            marker.MouseFilter = MouseFilterEnum.Ignore;
            bar.AddChild(marker);
            _marker = marker;
        }

        _titleLbl = UiTheme.Text(title, 15, new Color("f1ead9"));
        _titleLbl.Text = title;
        _titleLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _titleLbl.MouseFilter = MouseFilterEnum.Ignore;
        bar.AddChild(_titleLbl);

        var closeNormal = new StyleBoxEmpty();
        var closeHover = new StyleBoxFlat { BgColor = new Color(0.06f, 0.05f, 0.035f, 0.34f) };
        closeHover.SetCornerRadiusAll(2);

        if (minimizable)
        {
            _minimizeBtn = UiTheme.IconButton("—", "Minimize");
            _minimizeBtn.CustomMinimumSize = HeaderButtonSize;
            _minimizeBtn.AddThemeFontSizeOverride("font_size", MinimizeGlyphFontSize);
            _minimizeBtn.AddThemeColorOverride("font_color", new Color(UiTheme.TextHi, 0.82f));
            _minimizeBtn.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
            _minimizeBtn.AddThemeStyleboxOverride("normal", closeNormal);
            _minimizeBtn.AddThemeStyleboxOverride("hover", closeHover);
            _minimizeBtn.AddThemeStyleboxOverride("pressed", closeHover);
            _minimizeBtn.Pressed += () => SetMinimized(!Minimized);
            bar.AddChild(_minimizeBtn);
        }

        var close = UiTheme.IconButton(UiIcons.Get("system/close"), "Close");
        close.CustomMinimumSize = HeaderButtonSize;
        close.AddThemeConstantOverride("icon_max_width", Platform.Pick(12, 22));
        close.AddThemeColorOverride("icon_normal_color", new Color(UiTheme.TextHi, 0.82f));
        close.AddThemeColorOverride("icon_hover_color", UiTheme.TextHi);
        close.AddThemeStyleboxOverride("normal", closeNormal);
        close.AddThemeStyleboxOverride("hover", closeHover);
        close.AddThemeStyleboxOverride("pressed", closeHover);
        close.Pressed += () => { Visible = false; Closed?.Invoke(); Audio.PlayUi(Sfx.InventoryClose); };
        bar.AddChild(close);

        var content = new MarginContainer();
        _content = content;
        UiTheme.Margins(content, 9, 8, 9, 10);
        root.AddChild(content);
        Body = new VBoxContainer();
        Body.AddThemeConstantOverride("separation", 8);
        if (bodyMinWidth > 0) Body.CustomMinimumSize = new Vector2(bodyMinWidth, 0);
        content.AddChild(Body);

        HudLayout.Attach(
            this, id, bar, () => defaultPos,
            resizable: resizable,
            minimumSize: minimumSize,
            persist: persistLayout);

        if (!resizable) MinimumSizeChanged += QueueFit;
    }
}
