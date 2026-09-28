using Godot;

namespace LibreKO;

public partial class Notice : CanvasLayer
{
    private const int PanelWidth = 420;

    private Button? _ok;
    private Button? _cancel;
    private Label _message = null!;
    private System.Action? _onClose;
    private System.Action? _onConfirm;
    private System.Action? _onCancel;

    public static Notice Show(Node parent, string message, string title = "Notice",
                              System.Action? onClose = null)
        => Spawn(parent, message, title, dismissable: true, onClose);

    public static Notice Busy(Node parent, string message, string title = "Please wait")
        => Spawn(parent, message, title, dismissable: false, null);

    public static Notice Confirm(Node parent, string message, string confirmText, string cancelText,
                                 System.Action onConfirm, System.Action? onCancel = null,
                                 string title = "Notice")
    {
        var n = new Notice { Layer = 220, _onConfirm = onConfirm, _onCancel = onCancel };
        parent.AddChild(n);
        n.Build(message, title, dismissable: true, confirmText, cancelText);
        return n;
    }

    private DialogRequest? _request;

    public void SetMessage(string message)
    {
        if (_request != null) _request.SetMessage(message);
        else _message.Text = message;
    }

    public void Close()
    {
        if (!IsInstanceValid(this) || IsQueuedForDeletion()) return;
        QueueFree();
        _onClose?.Invoke();
    }

    private static Notice Spawn(Node parent, string message, string title, bool dismissable,
                                System.Action? onClose)
    {
        var n = new Notice { Layer = 220, _onClose = onClose };
        parent.AddChild(n);
        n.Build(message, title, dismissable);
        return n;
    }

    private void Build(string message, string title, bool dismissable,
                       string? confirmText = null, string? cancelText = null)
    {
        if (PluginHost.Ui.DialogBuilder is { } pluginDialog)
        {
            _request = new DialogRequest(title, message, confirmText ?? "OK",
                confirmText == null ? null : cancelText ?? "Cancel", dismissable, Accept, Decline, Close);
            AddChild(pluginDialog(_request));
            return;
        }

        var blocker = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        blocker.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(blocker);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.055f, 0.065f, 0.96f),
            BorderColor = new Color(UiTheme.Gold, 0.40f),
            ShadowColor = new Color(0, 0, 0, 0.7f),
            ShadowSize = 18,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        panel.AddThemeStyleboxOverride("panel", sb);
        centre.AddChild(panel);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 0);
        panel.AddChild(vb);

        var bar = new PanelContainer();
        var barBox = new StyleBoxFlat { BgColor = new Color(0.13f, 0.13f, 0.15f, 0.98f) };
        barBox.CornerRadiusTopLeft = 4;
        barBox.CornerRadiusTopRight = 4;
        barBox.SetContentMarginAll(9);
        bar.AddThemeStyleboxOverride("panel", barBox);
        vb.AddChild(bar);

        var heading = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 17);
        heading.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        bar.AddChild(heading);

        var body = new MarginContainer();
        foreach (var s in new[] { "left", "right" }) body.AddThemeConstantOverride($"margin_{s}", 20);
        body.AddThemeConstantOverride("margin_top", 22);
        body.AddThemeConstantOverride("margin_bottom", dismissable ? 16 : 22);
        vb.AddChild(body);

        _message = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _message.AddThemeColorOverride("font_color", UiTheme.TextHi);
        body.AddChild(_message);

        if (!dismissable) return;

        var footer = new MarginContainer();
        foreach (var s in new[] { "left", "right", "bottom" }) footer.AddThemeConstantOverride($"margin_{s}", 14);
        vb.AddChild(footer);

        if (confirmText == null)
        {
            _ok = Ui.MenuButton("OK (ENTER)", height: 36, fontSize: 16);
            _ok.Pressed += Close;
            footer.AddChild(_ok);
            return;
        }

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        footer.AddChild(row);

        _ok = Ui.MenuButton(confirmText, height: 36, fontSize: 16);
        _ok.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _ok.Pressed += Accept;
        row.AddChild(_ok);

        _cancel = Ui.MenuButton(cancelText ?? "Cancel", height: 36, fontSize: 16);
        _cancel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _cancel.Pressed += Decline;
        row.AddChild(_cancel);
    }

    private void Accept()
    {
        var confirm = _onConfirm;
        _onConfirm = null;
        _onCancel = null;
        Close();
        confirm?.Invoke();
    }

    private void Decline()
    {
        var cancel = _onCancel;
        _onConfirm = null;
        _onCancel = null;
        Close();
        cancel?.Invoke();
    }

    public override void _Input(InputEvent ev)
    {
        if (_ok == null) return;
        if (ev is not InputEventKey { Pressed: true, Echo: false } k) return;

        if (_cancel != null)
        {
            if (k.Keycode is Key.Enter or Key.KpEnter) { GetViewport().SetInputAsHandled(); Accept(); }
            else if (k.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); Decline(); }
            return;
        }

        if (k.Keycode is not (Key.Enter or Key.KpEnter or Key.Escape or Key.Space)) return;
        GetViewport().SetInputAsHandled();
        Close();
    }
}
