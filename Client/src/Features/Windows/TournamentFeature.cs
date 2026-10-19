using Godot;

namespace LibreKO.Features.Windows;

internal sealed class TournamentFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly TournamentSession tournament;
    private readonly ITournamentRequests requests;

    public TournamentFeature(IWorldContext context, TournamentSession tournament, ITournamentRequests requests)
    {
        this.context = context;
        this.tournament = tournament;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private Label roundLbl = null!;
    private Label countLbl = null!;
    private Label stateLbl = null!;
    private Button regBtn = null!;
    private Button unregBtn = null!;
    private bool shown;

    public bool IsOpen => shown;

    private bool registered;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        panel = new HudWindow("tournament", "Arena Tournament") { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Arena Tournament"));

        roundLbl = UiTheme.Text("Round: -", 13, UiTheme.TextHi);
        root.AddChild(roundLbl);
        countLbl = UiTheme.Text("Participants: -", 13, UiTheme.TextHi);
        root.AddChild(countLbl);
        stateLbl = UiTheme.Text("Not registered", 13, UiTheme.TextLo);
        root.AddChild(stateLbl);

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        root.AddChild(hb);
        regBtn = new Button { Text = "Register", FocusMode = Control.FocusModeEnum.None };
        regBtn.Pressed += () => requests.Register();
        hb.AddChild(regBtn);
        unregBtn = new Button { Text = "Unregister", FocusMode = Control.FocusModeEnum.None };
        unregBtn.Pressed += () => requests.Unregister();
        hb.AddChild(unregBtn);

        tournament.TournamentStatusEvent += OnStatus;
        tournament.TournamentRegisterEvent += OnRegister;
    }

    public void Dispose()
    {
        tournament.TournamentStatusEvent -= OnStatus;
        tournament.TournamentRegisterEvent -= OnRegister;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        panel.Visible = true;
        shown = true;
        requests.Status();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void OnStatus(bool registered, int participantCount, int round)
    {
        this.registered = registered;
        roundLbl.Text = $"Round: {round}";
        countLbl.Text = $"Participants: {participantCount}";
        stateLbl.Text = registered ? "Registered" : "Not registered";
        stateLbl.AddThemeColorOverride("font_color", registered ? UiTheme.Gold : UiTheme.TextLo);
        regBtn.Disabled = registered;
        unregBtn.Disabled = !registered;
    }

    private void OnRegister(bool nowRegistered)
    {
        registered = nowRegistered;
        requests.Status();
    }
}
