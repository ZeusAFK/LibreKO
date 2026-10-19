using Godot;

namespace LibreKO;

public partial class Notice : CanvasLayer
{
    private const int PanelWidth = 420;

    private Button? okButton;
    private Button? cancelButton;
    private Label messageLabel = null!;
    private System.Action? onClose;
    private System.Action? onConfirm;
    private System.Action? onCancel;
    private System.Action<string>? onSubmit;
    private VBoxContainer? column;
    private LineEdit? field;

    public static Notice Show(Node parent, string message, string title = "Notice",
                              System.Action? onClose = null)
        => Spawn(parent, message, title, dismissable: true, onClose);

    public static Notice Waiting(Node parent, string message, string closeText, System.Action onClose, string title = "Notice")
    {
        var n = new Notice { Layer = UiLayers.Dialogs, onClose = onClose };
        parent.AddChild(n);
        n.Build(message, title, dismissable: true, closeText: closeText);
        return n;
    }

    public static Notice Busy(Node parent, string message, string title = "Please wait")
        => Spawn(parent, message, title, dismissable: false, null);

    public static Notice Confirm(Node parent, string message, string confirmText, string cancelText,
                                 System.Action onConfirm, System.Action? onCancel = null,
                                 string title = "Notice")
    {
        var n = new Notice { Layer = UiLayers.Dialogs, onConfirm = onConfirm, onCancel = onCancel };
        parent.AddChild(n);
        n.Build(message, title, dismissable: true, confirmText, cancelText);
        return n;
    }

    public static Notice AskText(Node parent, string message, System.Action<string> onSubmit, string title = "Notice",
                                 string initial = "", string placeholder = "", int maxLength = 0, bool secret = false,
                                 string confirmText = "OK", string cancelText = "Cancel", System.Action? onCancel = null)
    {
        var n = new Notice { Layer = UiLayers.Dialogs, onSubmit = onSubmit, onCancel = onCancel };
        parent.AddChild(n);
        if (PluginHost.Ui.TextInputBuilder is { } build)
        {
            n.AddChild(build(new TextRequest(title, message, initial, placeholder, maxLength, secret,
                confirmText, cancelText, n.Submit, n.Decline)));
            return n;
        }
        n.Build(message, title, dismissable: true, confirmText, cancelText, allowPlugin: false);
        n.field = new LineEdit { Text = initial, PlaceholderText = placeholder, MaxLength = maxLength, Secret = secret };
        n.column!.AddChild(n.field);
        n.field.CallDeferred(Control.MethodName.GrabFocus);
        return n;
    }

    private DialogRequest? request;

    public void SetMessage(string message)
    {
        if (request != null) request.SetMessage(message);
        else messageLabel.Text = message;
    }

    public void Close()
    {
        if (!IsInstanceValid(this) || IsQueuedForDeletion()) return;
        QueueFree();
        onClose?.Invoke();
    }

    private static Notice Spawn(Node parent, string message, string title, bool dismissable,
                                System.Action? onClose)
    {
        var n = new Notice { Layer = UiLayers.Dialogs, onClose = onClose };
        parent.AddChild(n);
        n.Build(message, title, dismissable);
        return n;
    }

    private void Build(string message, string title, bool dismissable,
                       string? confirmText = null, string? cancelText = null, bool allowPlugin = true, string? closeText = null)
    {
        if (allowPlugin && PluginHost.Ui.DialogBuilder is { } pluginDialog)
        {
            request = new DialogRequest(title, message, confirmText ?? closeText ?? "OK",
                confirmText == null ? null : cancelText ?? "Cancel", dismissable, Accept, Decline, Close);
            AddChild(pluginDialog(request));
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

        messageLabel = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        messageLabel.AddThemeColorOverride("font_color", UiTheme.TextHi);
        column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        body.AddChild(column);
        column.AddChild(messageLabel);

        if (!dismissable) return;

        var footer = new MarginContainer();
        foreach (var s in new[] { "left", "right", "bottom" }) footer.AddThemeConstantOverride($"margin_{s}", 14);
        vb.AddChild(footer);

        if (confirmText == null)
        {
            okButton = Ui.MenuButton(closeText ?? "OK (ENTER)", height: 36, fontSize: 16);
            okButton.Pressed += Close;
            footer.AddChild(okButton);
            return;
        }

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        footer.AddChild(row);

        okButton = Ui.MenuButton(confirmText, height: 36, fontSize: 16);
        okButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        okButton.Pressed += Accept;
        row.AddChild(okButton);

        cancelButton = Ui.MenuButton(cancelText ?? "Cancel", height: 36, fontSize: 16);
        cancelButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cancelButton.Pressed += Decline;
        row.AddChild(cancelButton);
    }

    private void Submit(string text)
    {
        var submit = onSubmit;
        onSubmit = null;
        onCancel = null;
        Close();
        submit?.Invoke(text);
    }

    private void Accept()
    {
        if (field != null)
        {
            Submit(field.Text);
            return;
        }
        var confirm = onConfirm;
        onConfirm = null;
        onCancel = null;
        Close();
        confirm?.Invoke();
    }

    private void Decline()
    {
        var cancel = onCancel;
        onConfirm = null;
        onCancel = null;
        onSubmit = null;
        Close();
        cancel?.Invoke();
    }

    private bool TypingElsewhere() =>
        GetViewport().GuiGetFocusOwner() is (LineEdit or TextEdit) and var owner && !IsAncestorOf(owner);

    public override void _Input(InputEvent ev)
    {
        if (okButton == null) return;
        if (ev is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode is Key.Enter or Key.KpEnter or Key.Space && TypingElsewhere()) return;

        if (cancelButton != null)
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
