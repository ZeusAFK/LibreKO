using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _innLayer = null!;
    private HudWindow _innPanel = null!;
    private Label _innStatusLabel = null!;
    private Label _innRestLabel = null!;
    private bool _innShown;

    private bool _innSaved;
    private int _innSavedZone;

    private Godot.Timer _innRestTimer = null!;
    private int _innRestTicksLeft;

    private void InnInit()
    {
        _innLayer = new CanvasLayer { Layer = 74 };
        AddChild(_innLayer);

        _innPanel = new HudWindow("inn", "Inn", new Vector2(200, 150)) { Visible = false };
        _innPanel.Closed += CloseInn;
        _innLayer.AddChild(_innPanel);

        var root = _innPanel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiTheme.SectionTitle("Inn"));

        root.AddChild(UiTheme.Text(
            "Bind this town as your recall home, or rest to recover HP and MP.",
            12, UiTheme.TextLo));

        _innStatusLabel = UiTheme.Text("Checking…", 13, UiTheme.TextHi);
        root.AddChild(_innStatusLabel);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(btnRow);

        var setBtn = new Button { Text = "Set as Home", FocusMode = Control.FocusModeEnum.None };
        setBtn.Pressed += () => Net.I.SendInnSetHome();
        btnRow.AddChild(setBtn);

        var restBtn = new Button { Text = "Rest", FocusMode = Control.FocusModeEnum.None };
        restBtn.Pressed += () => Net.I.SendInnRest();
        btnRow.AddChild(restBtn);

        _innRestLabel = UiTheme.Text("", 12, UiTheme.Gold);
        root.AddChild(_innRestLabel);

        _innRestTimer = new Godot.Timer { OneShot = false, WaitTime = 0.5 };
        _innRestTimer.Timeout += InnRestTick;
        AddChild(_innRestTimer);

        Net.I.InnStatusEvent  += OnInnStatus;
        Net.I.InnSetHomeEvent += OnInnSetHome;
        Net.I.InnRestEvent    += OnInnRest;
    }

    private void InnDispose()
    {
        Net.I.InnStatusEvent  -= OnInnStatus;
        Net.I.InnSetHomeEvent -= OnInnSetHome;
        Net.I.InnRestEvent    -= OnInnRest;
    }

    private void ToggleInn()
    {
        if (_innShown) { CloseInn(); return; }
        _innPanel.Visible = true;
        _innShown = true;
        Net.I.SendInnStatus();
    }

    private void CloseInn()
    {
        if (!_innShown) return;
        _innShown = false;
        _innPanel.Visible = false;
    }

    private void OnInnStatus(bool saved, int zone)
    {
        _innSaved = saved;
        _innSavedZone = zone;
        InnRefreshStatusLabel();
    }

    private void OnInnSetHome(bool ok, int zone)
    {
        if (ok)
        {
            _innSaved = true;
            _innSavedZone = zone;
        }
        InnRefreshStatusLabel();
    }

    private void OnInnRest(bool ok)
    {
        if (!ok) return;
        _innRestTicksLeft = 12;
        _innRestLabel.Text = "Resting…";
        if (_innRestTimer.IsStopped()) _innRestTimer.Start();
    }

    private void InnRestTick()
    {
        if (_innRestTicksLeft <= 0)
        {
            _innRestTimer.Stop();
            _innRestLabel.Text = "Rested.";
            return;
        }
        _innRestTicksLeft--;

        if (Vitals.RestHp() > 0) _hpBar?.Set(Vitals.Hp, Vitals.MaxHp);
        if (Vitals.RestMp() > 0) _mpBar?.Set(Vitals.Mp, Vitals.MaxMp);

        if (_innRestTicksLeft == 0)
        {
            _innRestTimer.Stop();
            _innRestLabel.Text = "Rested.";
        }
    }

    private void InnRefreshStatusLabel()
    {
        if (_innStatusLabel == null) return;
        int hereZone = Net.I.LastEnter.Zone;
        if (_innSaved)
        {
            bool here = _innSavedZone == hereZone;
            _innStatusLabel.Text = here
                ? $"This town (zone {_innSavedZone}) is your inn home."
                : $"Your inn home is zone {_innSavedZone}.";
            _innStatusLabel.AddThemeColorOverride("font_color", here ? UiTheme.Gold : UiTheme.TextHi);
        }
        else
        {
            _innStatusLabel.Text = "No inn home set.";
            _innStatusLabel.AddThemeColorOverride("font_color", UiTheme.TextLo);
        }
    }
}
