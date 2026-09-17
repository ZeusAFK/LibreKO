using System;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class ReconnectOverlay : CanvasLayer
{
    private const int OverlayLayer = 125;

    private ReconnectDialog _dialog = null!;

    public override void _Ready()
    {
        Layer = OverlayLayer;
        Visible = false;

        var dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.58f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        _dialog = new ReconnectDialog();
        _dialog.CloseGamePressed += CloseGame;
        AddChild(_dialog);

        Net.I.ReconnectChangedEvent += Refresh;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Net.I)) Net.I.ReconnectChangedEvent -= Refresh;
    }

    public override void _Process(double delta)
    {
        if (Visible && Net.I.ReconnectState == Net.ReconnectPhase.Visible)
            _dialog.SetWait(Net.I.ReconnectRetryIn);
    }

    private void Refresh()
    {
        switch (Net.I.ReconnectState)
        {
            case Net.ReconnectPhase.Visible:
                _dialog.SetReconnecting(Net.I.ReconnectAttempt);
                _dialog.SetWait(Net.I.ReconnectRetryIn);
                Visible = true;
                break;
            case Net.ReconnectPhase.Failed:
                _dialog.SetFailed(Net.I.ReconnectFailure);
                Visible = true;
                break;
            default:
                _dialog.SetIdle();
                Visible = false;
                break;
        }
    }

    private void CloseGame() => _ = Diag.Guard("reconnect-exit", () => Shutdown.Begin(this, 260));
}

public partial class ReconnectDialog : PanelContainer
{
    private const int BodyWidth = 380;
    private const double DotsInterval = 0.4;

    public event Action? CloseGamePressed;

    private Label _headline = null!;
    private Label _status = null!;
    private Label _hint = null!;
    private ProgressBar _attempts = null!;
    private Button _close = null!;
    private bool _animating;
    private int _waitSeconds;
    private double _dotsAccum;
    private int _dots;

    public override void _Ready()
    {
        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0.5f; AnchorBottom = 0.5f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        AddThemeStyleboxOverride("panel", UiTheme.WindowPanel(4));

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        AddChild(col);

        var header = new PanelContainer();
        header.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(4));
        header.AddChild(UiTheme.Text("Connection Lost", 18, UiTheme.GoldBright, HorizontalAlignment.Center));
        col.AddChild(header);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 26, 22, 26, 20);
        col.AddChild(margin);

        var body = new VBoxContainer { CustomMinimumSize = new Vector2(BodyWidth, 0) };
        body.AddThemeConstantOverride("separation", 12);
        margin.AddChild(body);

        _headline = UiTheme.Text("", 14, UiTheme.Warning, HorizontalAlignment.Center);
        _headline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_headline);

        _status = UiTheme.Text("", 13, UiTheme.TextLo, HorizontalAlignment.Center);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_status);

        _hint = UiTheme.Text("", 12, UiTheme.TextDim, HorizontalAlignment.Center);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_hint);

        _attempts = new ProgressBar
        {
            MinValue = 0,
            MaxValue = Net.MaxReconnectAttempts,
            Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 10),
        };
        var track = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.03f, 0.85f),
            BorderColor = new Color(UiTheme.Gold, 0.40f),
        };
        track.SetBorderWidthAll(1);
        track.SetCornerRadiusAll(3);
        var fill = new StyleBoxFlat { BgColor = UiTheme.Gold };
        fill.SetCornerRadiusAll(2);
        _attempts.AddThemeStyleboxOverride("background", track);
        _attempts.AddThemeStyleboxOverride("fill", fill);
        body.AddChild(_attempts);

        _close = Ui.MenuButton("Close Game", 38, 16);
        _close.Visible = false;
        _close.Pressed += () => CloseGamePressed?.Invoke();
        body.AddChild(_close);
    }

    public void SetReconnecting(int attempt)
    {
        _headline.Text = "The connection to the server was lost.";
        _status.Text = $"Reconnecting — attempt {Mathf.Max(1, attempt)} of {Net.MaxReconnectAttempts}";
        _attempts.Value = Mathf.Max(1, attempt);
        _attempts.Visible = true;
        _hint.Visible = true;
        _close.Visible = false;
        _animating = true;
        _dots = 0;
        _dotsAccum = 0;
        ApplyHint();
    }

    public void SetWait(int seconds)
    {
        if (!_animating || seconds == _waitSeconds) return;
        _waitSeconds = seconds;
        ApplyHint();
    }

    public void SetFailed(string reason)
    {
        _headline.Text = "Could not reconnect to the server.";
        _status.Text = reason.Length > 0 ? reason : "The game has to be closed.";
        _hint.Visible = false;
        _attempts.Visible = false;
        _close.Visible = true;
        _animating = false;
    }

    public void SetIdle()
    {
        _animating = false;
        _close.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_animating || _waitSeconds > 0) return;
        _dotsAccum += delta;
        if (_dotsAccum < DotsInterval) return;
        _dotsAccum = 0;
        _dots = (_dots + 1) % 4;
        ApplyHint();
    }

    private void ApplyHint() =>
        _hint.Text = _waitSeconds > 0
            ? $"Next attempt in {_waitSeconds} s"
            : "Contacting the server" + new string('.', _dots);
}
