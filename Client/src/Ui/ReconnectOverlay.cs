using Godot;

namespace LibreKO;

public partial class ReconnectOverlay : CanvasLayer
{
    private ReconnectDialog dialog = null!;
    private ReconnectSupervisor reconnect = null!;

    public override void _Ready()
    {
        Layer = UiLayers.Reconnect;
        Visible = false;

        var dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.58f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        dialog = new ReconnectDialog();
        dialog.CloseGamePressed += CloseGame;
        AddChild(dialog);

        reconnect = Game.Connection.Reconnect;
        reconnect.ChangedEvent += Refresh;
    }

    public override void _ExitTree()
    {
        reconnect.ChangedEvent -= Refresh;
    }

    public override void _Process(double delta)
    {
        if (Visible && reconnect.State == ReconnectPhase.Visible)
            dialog.SetWait(reconnect.RetryIn);
    }

    private void Refresh()
    {
        switch (reconnect.State)
        {
            case ReconnectPhase.Visible:
                dialog.SetReconnecting(reconnect.Attempt);
                dialog.SetWait(reconnect.RetryIn);
                Visible = true;
                break;
            case ReconnectPhase.Failed:
                dialog.SetFailed(reconnect.Failure);
                Visible = true;
                break;
            default:
                dialog.SetIdle();
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

    private Label headline = null!;
    private Label status = null!;
    private Label hint = null!;
    private ProgressBar attempts = null!;
    private Button close = null!;
    private bool animating;
    private int waitSeconds;
    private double dotsAccum;
    private int dots;

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

        headline = UiTheme.Text("", 14, UiTheme.Warning, HorizontalAlignment.Center);
        headline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(headline);

        status = UiTheme.Text("", 13, UiTheme.TextLo, HorizontalAlignment.Center);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(status);

        hint = UiTheme.Text("", 12, UiTheme.TextDim, HorizontalAlignment.Center);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(hint);

        attempts = new ProgressBar
        {
            MinValue = 0,
            MaxValue = ReconnectSupervisor.MaxAttempts,
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
        attempts.AddThemeStyleboxOverride("background", track);
        attempts.AddThemeStyleboxOverride("fill", fill);
        body.AddChild(attempts);

        close = Ui.MenuButton("Close Game", 38, 16);
        close.Visible = false;
        close.Pressed += () => CloseGamePressed?.Invoke();
        body.AddChild(close);
    }

    public void SetReconnecting(int attempt)
    {
        headline.Text = "The connection to the server was lost.";
        status.Text = $"Reconnecting — attempt {Mathf.Max(1, attempt)} of {ReconnectSupervisor.MaxAttempts}";
        attempts.Value = Mathf.Max(1, attempt);
        attempts.Visible = true;
        hint.Visible = true;
        close.Visible = false;
        animating = true;
        dots = 0;
        dotsAccum = 0;
        ApplyHint();
    }

    public void SetWait(int seconds)
    {
        if (!animating || seconds == waitSeconds) return;
        waitSeconds = seconds;
        ApplyHint();
    }

    public void SetFailed(string reason)
    {
        headline.Text = "Could not reconnect to the server.";
        status.Text = reason.Length > 0 ? reason : "The game has to be closed.";
        hint.Visible = false;
        attempts.Visible = false;
        close.Visible = true;
        animating = false;
    }

    public void SetIdle()
    {
        animating = false;
        close.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!animating || waitSeconds > 0) return;
        dotsAccum += delta;
        if (dotsAccum < DotsInterval) return;
        dotsAccum = 0;
        dots = (dots + 1) % 4;
        ApplyHint();
    }

    private void ApplyHint() =>
        hint.Text = waitSeconds > 0
            ? $"Next attempt in {waitSeconds} s"
            : "Contacting the server" + new string('.', dots);
}
