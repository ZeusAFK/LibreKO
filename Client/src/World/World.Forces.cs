using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _forcesLayer = null!;
    private HudWindow _forcesPanel = null!;
    private Label _forcesStateLbl = null!;
    private Label _forcesPointsLbl = null!;
    private Label _forcesRankLbl = null!;
    private StatBar _forcesAngerBar = null!;
    private Button _forcesJoinBtn = null!;
    private Button _forcesLeaveBtn = null!;
    private bool _forcesShown;
    private bool _forcesJoined;

    private static readonly string[] ForcesRankNames =
        { "Recruit", "Soldier", "Veteran", "Officer", "Commander", "Warlord" };

    private void ForcesInit()
    {
        _forcesLayer = new CanvasLayer { Layer = 63 };
        AddChild(_forcesLayer);
        _forcesPanel = new HudWindow("forces", "Nation Force", new Vector2(200, 140)) { Visible = false };
        _forcesPanel.Closed += CloseForces;
        _forcesLayer.AddChild(_forcesPanel);

        var root = _forcesPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Nation Force"));

        _forcesStateLbl = UiTheme.Text("Not enlisted.", 13, UiTheme.TextLo);
        root.AddChild(_forcesStateLbl);

        var rankRow = new PanelContainer();
        rankRow.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var rankHb = new HBoxContainer();
        rankHb.AddThemeConstantOverride("separation", 8);
        rankRow.AddChild(rankHb);
        var rankCap = UiTheme.Text("Rank", 13, UiTheme.TextLo);
        rankCap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rankHb.AddChild(rankCap);
        _forcesRankLbl = UiTheme.Text("—", 13, UiTheme.Gold);
        rankHb.AddChild(_forcesRankLbl);
        root.AddChild(rankRow);

        _forcesPointsLbl = UiTheme.Text("Force Points: 0", 13, UiTheme.TextHi);
        root.AddChild(_forcesPointsLbl);

        root.AddChild(UiTheme.Text("Anger Gauge", 12, UiTheme.TextLo));
        _forcesAngerBar = new StatBar(new Color("c0392b"), new Vector2(240, 16));
        _forcesAngerBar.Set(0, 100);
        root.AddChild(_forcesAngerBar);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(btnRow);

        _forcesJoinBtn = new Button { Text = "Join", FocusMode = Control.FocusModeEnum.None };
        _forcesJoinBtn.Pressed += () => Net.I.SendForcesJoin();
        btnRow.AddChild(_forcesJoinBtn);

        _forcesLeaveBtn = new Button { Text = "Leave", FocusMode = Control.FocusModeEnum.None };
        _forcesLeaveBtn.Pressed += () => Net.I.SendForcesLeave();
        btnRow.AddChild(_forcesLeaveBtn);

        Net.I.ForcesStatusEvent += OnForcesStatus;
    }

    private void ForcesDispose()
    {
        Net.I.ForcesStatusEvent -= OnForcesStatus;
    }

    private void ToggleForces()
    {
        if (_forcesShown) { CloseForces(); return; }
        _forcesPanel.Visible = true;
        _forcesShown = true;
        Net.I.SendForcesStatus();
    }

    private void CloseForces()
    {
        if (!_forcesShown) return;
        _forcesShown = false;
        _forcesPanel.Visible = false;
    }

    private void OnForcesStatus(ForcesStatus s)
    {
        _forcesJoined = s.Joined;
        string rankName = s.Rank < ForcesRankNames.Length ? ForcesRankNames[s.Rank] : $"Rank {s.Rank}";

        _forcesStateLbl.Text = s.Joined ? "Enlisted in your nation's force." : "Not enlisted.";
        _forcesStateLbl.AddThemeColorOverride("font_color", s.Joined ? UiTheme.TextHi : UiTheme.TextLo);
        _forcesRankLbl.Text = s.Joined ? rankName : "—";
        _forcesPointsLbl.Text = $"Force Points: {s.Points:n0}";
        _forcesAngerBar.Set(s.Joined ? s.AngerPct : 0, 100);

        _forcesJoinBtn.Disabled = s.Joined;
        _forcesLeaveBtn.Disabled = !s.Joined;
    }
}
