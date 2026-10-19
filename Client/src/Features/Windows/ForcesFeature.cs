using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ForcesFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ForcesSession forces;
    private readonly IForcesRequests requests;

    public ForcesFeature(IWorldContext context, ForcesSession forces, IForcesRequests requests)
    {
        this.context = context;
        this.forces = forces;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private Label stateLbl = null!;
    private Label pointsLbl = null!;
    private Label rankLbl = null!;
    private StatBar angerBar = null!;
    private Button joinBtn = null!;
    private Button leaveBtn = null!;
    private bool shown;

    public bool IsOpen => shown;

    private bool joined;

    private static readonly string[] RankNames =
    { "Recruit", "Soldier", "Veteran", "Officer", "Commander", "Warlord" };

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(layer);
        panel = new HudWindow("forces", "Nation Force") { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Nation Force"));

        stateLbl = UiTheme.Text("Not enlisted.", 13, UiTheme.TextLo);
        root.AddChild(stateLbl);

        var rankRow = new PanelContainer();
        rankRow.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var rankHb = new HBoxContainer();
        rankHb.AddThemeConstantOverride("separation", 8);
        rankRow.AddChild(rankHb);
        var rankCap = UiTheme.Text("Rank", 13, UiTheme.TextLo);
        rankCap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rankHb.AddChild(rankCap);
        rankLbl = UiTheme.Text("—", 13, UiTheme.Gold);
        rankHb.AddChild(rankLbl);
        root.AddChild(rankRow);

        pointsLbl = UiTheme.Text("Force Points: 0", 13, UiTheme.TextHi);
        root.AddChild(pointsLbl);

        root.AddChild(UiTheme.Text("Anger Gauge", 12, UiTheme.TextLo));
        angerBar = new StatBar(new Color("c0392b"), new Vector2(240, 16));
        angerBar.Set(0, 100);
        root.AddChild(angerBar);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(btnRow);

        joinBtn = new Button { Text = "Join", FocusMode = Control.FocusModeEnum.None };
        joinBtn.Pressed += () => requests.Join();
        btnRow.AddChild(joinBtn);

        leaveBtn = new Button { Text = "Leave", FocusMode = Control.FocusModeEnum.None };
        leaveBtn.Pressed += () => requests.Leave();
        btnRow.AddChild(leaveBtn);

        forces.ForcesStatusEvent += OnStatus;
    }

    public void Dispose()
    {
        forces.ForcesStatusEvent -= OnStatus;
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

    private void OnStatus(ForcesStatus s)
    {
        joined = s.Joined;
        string rankName = s.Rank < RankNames.Length ? RankNames[s.Rank] : $"Rank {s.Rank}";

        stateLbl.Text = s.Joined ? "Enlisted in your nation's force." : "Not enlisted.";
        stateLbl.AddThemeColorOverride("font_color", s.Joined ? UiTheme.TextHi : UiTheme.TextLo);
        rankLbl.Text = s.Joined ? rankName : "—";
        pointsLbl.Text = $"Force Points: {s.Points:n0}";
        angerBar.Set(s.Joined ? s.AngerPct : 0, 100);

        joinBtn.Disabled = s.Joined;
        leaveBtn.Disabled = !s.Joined;
    }
}
