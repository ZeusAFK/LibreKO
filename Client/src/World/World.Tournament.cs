using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _tournamentLayer = null!;
    private HudWindow _tournamentPanel = null!;
    private Label _tournamentRoundLbl = null!;
    private Label _tournamentCountLbl = null!;
    private Label _tournamentStateLbl = null!;
    private Button _tournamentRegBtn = null!;
    private Button _tournamentUnregBtn = null!;
    private bool _tournamentShown;
    private bool _tournamentRegistered;

    private void TournamentInit()
    {
        _tournamentLayer = new CanvasLayer { Layer = 74 };
        AddChild(_tournamentLayer);
        _tournamentPanel = new HudWindow("tournament", Localization.Loc.Tr("Arena Tournament"), new Vector2(200, 130)) { Visible = false };
        _tournamentPanel.Closed += CloseTournament;
        _tournamentLayer.AddChild(_tournamentPanel);

        var root = _tournamentPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Arena Tournament")));

        _tournamentRoundLbl = UiTheme.Text(Localization.Loc.Tr("Round: -"), 13, UiTheme.TextHi);
        root.AddChild(_tournamentRoundLbl);
        _tournamentCountLbl = UiTheme.Text(Localization.Loc.Tr("Participants: -"), 13, UiTheme.TextHi);
        root.AddChild(_tournamentCountLbl);
        _tournamentStateLbl = UiTheme.Text(Localization.Loc.Tr("Not registered"), 13, UiTheme.TextLo);
        root.AddChild(_tournamentStateLbl);

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        root.AddChild(hb);
        _tournamentRegBtn = new Button { Text = Localization.Loc.Tr("Register"), FocusMode = Control.FocusModeEnum.None };
        _tournamentRegBtn.Pressed += () => Net.I.SendTournamentRegister();
        hb.AddChild(_tournamentRegBtn);
        _tournamentUnregBtn = new Button { Text = Localization.Loc.Tr("Unregister"), FocusMode = Control.FocusModeEnum.None };
        _tournamentUnregBtn.Pressed += () => Net.I.SendTournamentUnregister();
        hb.AddChild(_tournamentUnregBtn);

        Net.I.TournamentStatusEvent += OnTournamentStatus;
        Net.I.TournamentRegisterEvent += OnTournamentRegister;
    }

    private void TournamentDispose()
    {
        Net.I.TournamentStatusEvent -= OnTournamentStatus;
        Net.I.TournamentRegisterEvent -= OnTournamentRegister;
    }

    private void ToggleTournament()
    {
        if (_tournamentShown) { CloseTournament(); return; }
        _tournamentPanel.Visible = true;
        _tournamentShown = true;
        Net.I.SendTournamentStatus();
    }

    private void CloseTournament()
    {
        if (!_tournamentShown) return;
        _tournamentShown = false;
        _tournamentPanel.Visible = false;
    }

    private void OnTournamentStatus(bool registered, int participantCount, int round)
    {
        _tournamentRegistered = registered;
        _tournamentRoundLbl.Text = $"{Localization.Loc.Tr("Round")}: {round}";
        _tournamentCountLbl.Text = $"{Localization.Loc.Tr("Participants")}: {participantCount}";
        _tournamentStateLbl.Text = registered ? Localization.Loc.Tr("Registered") : Localization.Loc.Tr("Not registered");
        _tournamentStateLbl.AddThemeColorOverride("font_color", registered ? UiTheme.Gold : UiTheme.TextLo);
        _tournamentRegBtn.Disabled = registered;
        _tournamentUnregBtn.Disabled = !registered;
    }

    private void OnTournamentRegister(bool nowRegistered)
    {
        _tournamentRegistered = nowRegistered;
        Net.I.SendTournamentStatus();
    }
}
