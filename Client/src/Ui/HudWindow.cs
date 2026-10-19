using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class HudWindow : PanelContainer
{
    public VBoxContainer Body { get; }

    public string Id { get; }

    public bool PluginReplaced { get; }

    public HudLayout Layout { get; }

    private System.Func<Vector2>? dock;

    public void DockTo(System.Func<Vector2> position) => dock = position;

    private const ulong CentreSettleMsec = 300;
    private readonly bool opensCentred;
    private ulong centreSettlesAt;

    private static readonly System.Collections.Generic.HashSet<string> centredIds =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static System.Collections.Generic.IReadOnlyCollection<string> CentredIds => centredIds;

    private static readonly System.Collections.Generic.Dictionary<string, HudWindow> byId =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static HudWindow? Find(string id) =>
        byId.TryGetValue(id, out var w) && GodotObject.IsInstanceValid(w) ? w : null;

    private readonly bool pluginHidden;
    private readonly WindowHost? host;
    private readonly LibreKO.Plugins.WindowFrame? frame;

    public bool PluginFramed => frame != null;

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationEnterTree:
                byId[Id] = this;
                break;
            case NotificationExitTree:
                if (byId.TryGetValue(Id, out var current) && current == this) byId.Remove(Id);
                break;
            case NotificationVisibilityChanged:
                if (pluginHidden && Visible) { Visible = false; break; }
                if (opensCentred && Visible) centreSettlesAt = Time.GetTicksMsec() + CentreSettleMsec;
                if (frame != null)
                {
                    if (Visible) frame.RaiseShown(); else frame.RaiseHidden();
                }
                if (host == null) break;
                if (Visible) host.RaiseShown(); else host.RaiseHidden();
                break;
        }
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
        Audio.PlayUi(Sfx.InventoryClose);
    }

    public void SetBackgroundAlpha(float alpha)
    {
        if (PluginReplaced || frame != null) return;
        AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(alpha: alpha));
    }

    public event System.Action? Closed;

    public void SetCloseVisible(bool visible)
    {
        if (closeButton != null) closeButton.Visible = visible;
    }

    private static Vector2 HeaderButtonSize => Platform.Pick(new Vector2(20, 20), new Vector2(46, 46));

    public event System.Action<bool>? MinimizedChanged;

    public string Title
    {
        set
        {
            titleLbl.Text = value;
            frame?.SetTitle(value);
        }
    }

    public bool Minimized { get; private set; }

    private const int MinimizedTitleWidth = 124;
    private const int MinimizeGlyphFontSize = 17;

    private readonly Label titleLbl;
    private bool fitQueued;
    private MarginContainer? content;
    private Button? minimizeBtn;
    private PanelContainer? header;
    private Label? markerLabel;
    private HBoxContainer? headerBar;
    private Button? closeButton;
    private Button? dockPin;

    public void SetHeaderAccent(Color fill, Color border, Color marker)
    {
        if (header == null) return;
        var sb = UiTheme.WindowHeaderBand();
        sb.BgColor = fill;
        sb.BorderColor = border;
        header.AddThemeStyleboxOverride("panel", sb);
        markerLabel?.AddThemeColorOverride("font_color", marker);
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
        if (PluginReplaced || headerBar == null || dockPin != null) return;
        dockPin = UiTheme.IconButton(UiIcons.Get("system/pin"), "Dock to the screen edge");
        dockPin.CustomMinimumSize = HeaderButtonSize;
        dockPin.AddThemeConstantOverride("icon_max_width", Platform.Pick(12, 22));
        dockPin.AddThemeColorOverride("icon_hover_color", Colors.White);
        dockPin.AddThemeColorOverride("icon_pressed_color", Colors.White);
        dockPin.AddThemeStyleboxOverride("hover", PinChip(DockPinHoverAlpha));
        dockPin.AddThemeStyleboxOverride("pressed", PinChip(DockPinHoverAlpha));
        dockPin.Pressed += redock;
        headerBar.AddChild(dockPin);
        if (closeButton != null) headerBar.MoveChild(dockPin, closeButton.GetIndex());
        SetDocked(true);
    }

    public Button? AddHeaderIcon(string iconId, string tooltip, System.Action pressed)
    {
        if (PluginReplaced || headerBar == null) return null;
        var button = UiTheme.IconButton(UiIcons.Get(iconId), tooltip);
        button.CustomMinimumSize = HeaderButtonSize;
        button.AddThemeConstantOverride("icon_max_width", Platform.Pick(12, 22));
        button.AddThemeColorOverride("icon_hover_color", UiTheme.TextHi);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("hover", PinChip(DockPinHoverAlpha));
        button.AddThemeStyleboxOverride("pressed", PinChip(DockPinHoverAlpha));
        button.Pressed += pressed;
        headerBar.AddChild(button);
        Control? before = (Control?)minimizeBtn ?? closeButton;
        if (before != null) headerBar.MoveChild(button, before.GetIndex());
        return button;
    }

    public void SetDocked(bool docked)
    {
        if (dockPin == null) return;
        dockPin.AddThemeColorOverride("icon_normal_color", docked ? Colors.White : new Color(UiTheme.TextHi, DockPinFreeAlpha));
        dockPin.AddThemeStyleboxOverride("normal", docked ? PinChip(DockPinDockedAlpha) : new StyleBoxEmpty());
        dockPin.TooltipText = docked ? "Docked to the screen edge" : "Dock to the screen edge";
    }

    public void SetMinimized(bool minimized)
    {
        if (PluginReplaced || content == null || Minimized == minimized) return;
        Minimized = minimized;
        content.Visible = !minimized;
        if (minimizeBtn != null)
        {
            minimizeBtn.Text = minimized ? "❒" : "—";
            minimizeBtn.TooltipText = minimized ? "Restore" : "Minimize";
        }

        titleLbl.ClipText = minimized;
        titleLbl.TextOverrunBehavior = minimized
            ? TextServer.OverrunBehavior.TrimEllipsis
            : TextServer.OverrunBehavior.NoTrimming;
        titleLbl.CustomMinimumSize = minimized ? new Vector2(MinimizedTitleWidth, 0) : Vector2.Zero;

        Size = Vector2.Zero;
        ResetSize();
        MinimizedChanged?.Invoke(minimized);
    }

    private void QueueFit()
    {
        if (fitQueued) return;
        fitQueued = true;
        Callable.From(Fit).CallDeferred();
    }

    private void Fit()
    {
        fitQueued = false;
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
        this.header = header;

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
        headerBar = bar;

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
            markerLabel = marker;
        }

        titleLbl = UiTheme.Text(title, 15, new Color("f1ead9"));
        titleLbl.Text = title;
        titleLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLbl.MouseFilter = MouseFilterEnum.Ignore;
        bar.AddChild(titleLbl);

        var closeNormal = new StyleBoxEmpty();
        var closeHover = new StyleBoxFlat { BgColor = new Color(0.06f, 0.05f, 0.035f, 0.34f) };
        closeHover.SetCornerRadiusAll(2);

        if (minimizable)
        {
            minimizeBtn = UiTheme.IconButton("—", "Minimize");
            minimizeBtn.CustomMinimumSize = HeaderButtonSize;
            minimizeBtn.AddThemeFontSizeOverride("font_size", MinimizeGlyphFontSize);
            minimizeBtn.AddThemeColorOverride("font_color", new Color(UiTheme.TextHi, 0.82f));
            minimizeBtn.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
            minimizeBtn.AddThemeStyleboxOverride("normal", closeNormal);
            minimizeBtn.AddThemeStyleboxOverride("hover", closeHover);
            minimizeBtn.AddThemeStyleboxOverride("pressed", closeHover);
            minimizeBtn.Pressed += () => SetMinimized(!Minimized);
            bar.AddChild(minimizeBtn);
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
            close.Pressed += Close;
            bar.AddChild(close);
            closeButton = close;
        }

        var content = new MarginContainer();
        this.content = content;
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
                pluginHidden = true;
                Visible = false;
            }
            else if (rule.Replacement != null)
            {
                PluginReplaced = true;
                margin.Visible = false;
                AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
                host = new WindowHost(id, title, this, Close);
                var replacement = rule.Replacement(host);
                AddChild(replacement);
                dragHandle = host.DragHandle ?? replacement;
            }
            if (!PluginReplaced)
                foreach (var extend in rule.Extenders) extend(Body);
        }
        if (!pluginHidden && !PluginReplaced && PluginHost.Ui.FrameFor(id) is { } frameBuild)
        {
            root.RemoveChild(content);
            margin.Visible = false;
            AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            frame = new LibreKO.Plugins.WindowFrame(id, title, content, closable, Close);
            var chrome = frameBuild(frame);
            AddChild(chrome);
            dragHandle = frame.DragHandle ?? chrome;
        }

        Layout = HudLayout.Attach(
            this, id, dragHandle, () => dock?.Invoke() ?? defaultPos ?? CentredSpot(),
            resizable: resizable,
            minimumSize: minimumSize,
            persist: persistLayout);

        if (!resizable) MinimumSizeChanged += QueueFit;
        if (defaultPos == null)
        {
            opensCentred = true;
            centredIds.Add(id);
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
        if (Visible && Time.GetTicksMsec() < centreSettlesAt && IsInstanceValid(Layout))
            Callable.From(Layout.ReapplyDefault).CallDeferred();
    }
}
