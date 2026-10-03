using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class HudWindow : PanelContainer
{
    public VBoxContainer Body { get; }

    public string Id { get; }

    public bool PluginReplaced { get; }

    public HudLayout Layout { get; }

    private System.Func<Vector2>? _dock;

    public void DockTo(System.Func<Vector2> position) => _dock = position;

    private const ulong CentreSettleMsec = 300;
    private readonly bool _opensCentred;
    private ulong _centreSettlesAt;

    private static readonly System.Collections.Generic.HashSet<string> _centredIds =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static System.Collections.Generic.IReadOnlyCollection<string> CentredIds => _centredIds;

    private static readonly System.Collections.Generic.Dictionary<string, HudWindow> _byId =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static HudWindow? Find(string id) =>
        _byId.TryGetValue(id, out var w) && GodotObject.IsInstanceValid(w) ? w : null;

    private readonly bool _pluginHidden;
    private readonly WindowHost? _host;

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationEnterTree:
                _byId[Id] = this;
                break;
            case NotificationExitTree:
                if (_byId.TryGetValue(Id, out var current) && current == this) _byId.Remove(Id);
                break;
            case NotificationVisibilityChanged:
                if (_pluginHidden && Visible) { Visible = false; break; }
                if (_opensCentred && Visible) _centreSettlesAt = Time.GetTicksMsec() + CentreSettleMsec;
                if (_host == null) break;
                if (Visible) _host.RaiseShown(); else _host.RaiseHidden();
                break;
        }
    }

    private void CloseFromPlugin()
    {
        Visible = false;
        Closed?.Invoke();
        Audio.PlayUi(Sfx.InventoryClose);
    }

    public void SetBackgroundAlpha(float alpha)
    {
        if (PluginReplaced) return;
        AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(alpha: alpha));
    }

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
    private HBoxContainer? _headerBar;
    private Button? _closeButton;
    private Button? _dockPin;

    public void SetHeaderAccent(Color fill, Color border, Color marker)
    {
        if (_header == null) return;
        var sb = UiTheme.WindowHeaderBand();
        sb.BgColor = fill;
        sb.BorderColor = border;
        _header.AddThemeStyleboxOverride("panel", sb);
        _marker?.AddThemeColorOverride("font_color", marker);
    }

    private const float DockPinDockedAlpha = 0.42f;
    private const float DockPinHoverAlpha = 0.30f;
    private const float DockPinFreeAlpha = 0.4f;

    private static StyleBoxFlat PinChip(float alpha)
    {
        var chip = new StyleBoxFlat { BgColor = new Color(0.06f, 0.05f, 0.035f, alpha) };
        chip.SetCornerRadiusAll(3);
        return chip;
    }

    public void ShowDockPin(System.Action redock)
    {
        if (PluginReplaced || _headerBar == null || _dockPin != null) return;
        _dockPin = UiTheme.IconButton(UiIcons.Get("system/pin"), "Dock to the screen edge");
        _dockPin.CustomMinimumSize = HeaderButtonSize;
        _dockPin.AddThemeConstantOverride("icon_max_width", Platform.Pick(12, 22));
        _dockPin.AddThemeColorOverride("icon_hover_color", Colors.White);
        _dockPin.AddThemeColorOverride("icon_pressed_color", Colors.White);
        _dockPin.AddThemeStyleboxOverride("hover", PinChip(DockPinHoverAlpha));
        _dockPin.AddThemeStyleboxOverride("pressed", PinChip(DockPinHoverAlpha));
        _dockPin.Pressed += redock;
        _headerBar.AddChild(_dockPin);
        if (_closeButton != null) _headerBar.MoveChild(_dockPin, _closeButton.GetIndex());
        SetDocked(true);
    }

    public void SetDocked(bool docked)
    {
        if (_dockPin == null) return;
        _dockPin.AddThemeColorOverride("icon_normal_color", docked ? Colors.White : new Color(UiTheme.TextHi, DockPinFreeAlpha));
        _dockPin.AddThemeStyleboxOverride("normal", docked ? PinChip(DockPinDockedAlpha) : new StyleBoxEmpty());
        _dockPin.TooltipText = docked ? "Docked to the screen edge" : "Dock to the screen edge";
    }

    public void SetMinimized(bool minimized)
    {
        if (PluginReplaced || _content == null || Minimized == minimized) return;
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
        bool minimizable = false,
        bool closable = true)
        : this(id, title, (Vector2?)defaultPos, bodyMinWidth, resizable, minimumSize, persistLayout, titleIcon,
            minimizable, closable)
    {
    }

    public HudWindow(
        string id,
        string title,
        int bodyMinWidth = 0,
        bool resizable = false,
        Vector2 minimumSize = default,
        bool persistLayout = true,
        Texture2D? titleIcon = null,
        bool minimizable = false,
        bool closable = true)
        : this(id, title, (Vector2?)null, bodyMinWidth, resizable, minimumSize, persistLayout, titleIcon,
            minimizable, closable)
    {
    }

    private HudWindow(
        string id,
        string title,
        Vector2? defaultPos,
        int bodyMinWidth,
        bool resizable,
        Vector2 minimumSize,
        bool persistLayout,
        Texture2D? titleIcon,
        bool minimizable,
        bool closable)
    {
        Id = id;
        var rule = PluginHost.Ui.RuleFor(id);
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
        _headerBar = bar;

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

        if (closable)
        {
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
            _closeButton = close;
        }

        var content = new MarginContainer();
        _content = content;
        UiTheme.Margins(content, 9, 8, 9, 10);
        root.AddChild(content);
        Body = new VBoxContainer();
        Body.AddThemeConstantOverride("separation", 8);
        if (bodyMinWidth > 0) Body.CustomMinimumSize = new Vector2(bodyMinWidth, 0);
        content.AddChild(Body);

        Control dragHandle = bar;
        if (rule != null)
        {
            if (rule.Hidden)
            {
                _pluginHidden = true;
                Visible = false;
            }
            else if (rule.Replacement != null)
            {
                PluginReplaced = true;
                margin.Visible = false;
                AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
                _host = new WindowHost(id, title, this, CloseFromPlugin);
                var replacement = rule.Replacement(_host);
                AddChild(replacement);
                dragHandle = _host.DragHandle ?? replacement;
            }
            if (!PluginReplaced)
                foreach (var extend in rule.Extenders) extend(Body);
        }

        Layout = HudLayout.Attach(
            this, id, dragHandle, () => _dock?.Invoke() ?? defaultPos ?? CentredSpot(),
            resizable: resizable,
            minimumSize: minimumSize,
            persist: persistLayout);

        if (!resizable) MinimumSizeChanged += QueueFit;
        if (defaultPos == null)
        {
            _opensCentred = true;
            _centredIds.Add(id);
            Resized += RecentreWhileSettling;
        }
    }

    private Vector2 CentredSpot()
    {
        if (!IsInsideTree()) return Position;
        Vector2 min = GetCombinedMinimumSize();
        Vector2 footprint = new Vector2(Mathf.Max(Size.X, min.X), Mathf.Max(Size.Y, min.Y)) * Scale;
        return WindowPlacement.Centre(GetViewportRect().Size, footprint);
    }

    private void RecentreWhileSettling()
    {
        if (Visible && Time.GetTicksMsec() < _centreSettlesAt && IsInstanceValid(Layout))
            Callable.From(Layout.ReapplyDefault).CallDeferred();
    }
}
