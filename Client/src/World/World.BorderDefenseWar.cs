using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _bdwHudLayer = null!;
    private PanelContainer _bdwScoreBanner = null!;
    private Label _bdwKarusScoreLbl = null!;
    private Label _bdwElmoScoreLbl = null!;
    private Label _bdwAltarStatusLbl = null!;
    private Godot.Timer _bdwAltarTimer = null!;

    private int _bdwKarusScore;
    private int _bdwElmoScore;
    private int _bdwAltarSeconds;
    private string _bdwAltarCarrier = string.Empty;
    private int _bdwReturnSeconds;
    private string? _bdwResult;

    private const byte BdwZone = 84;
    private const int BdwVictorySound = 340119;
    private const int BdwDefeatSound = 340118;

    private void BorderDefenseWarInit()
    {
        _bdwHudLayer = new CanvasLayer { Layer = 95 };
        AddChild(_bdwHudLayer);

        BuildBdwScoreBanner();

        _bdwAltarTimer = new Godot.Timer { WaitTime = 1.0, Autostart = false, OneShot = false };
        _bdwAltarTimer.Timeout += OnBdwAltarTick;
        _bdwHudLayer.AddChild(_bdwAltarTimer);

        Net.I.TempleScreenScoreEvent += OnBdwScoresReceived;
        Net.I.AltarFlagEvent += OnBdwAltarFlagReceived;
        Net.I.AltarTimerEvent += OnBdwAltarTimerReceived;
        Net.I.TempleEventFinishEvent += OnBdwFinishReceived;

        UpdateBdwHudVisibility();
    }

    private void BorderDefenseWarDispose()
    {
        Net.I.TempleScreenScoreEvent -= OnBdwScoresReceived;
        Net.I.AltarFlagEvent -= OnBdwAltarFlagReceived;
        Net.I.AltarTimerEvent -= OnBdwAltarTimerReceived;
        Net.I.TempleEventFinishEvent -= OnBdwFinishReceived;

        _bdwAltarTimer?.Stop();
        if (_bdwHudLayer != null && IsInstanceValid(_bdwHudLayer))
            _bdwHudLayer.QueueFree();
    }

    private void BuildBdwScoreBanner()
    {
        _bdwScoreBanner = new PanelContainer
        {
            CustomMinimumSize = new Vector2(340, 60),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };

        _bdwScoreBanner.AnchorLeft = 0.5f;
        _bdwScoreBanner.AnchorRight = 0.5f;
        _bdwScoreBanner.AnchorTop = 0f;
        _bdwScoreBanner.AnchorBottom = 0f;
        _bdwScoreBanner.OffsetLeft = -170f;
        _bdwScoreBanner.OffsetRight = 170f;
        _bdwScoreBanner.OffsetTop = 15f;
        _bdwScoreBanner.OffsetBottom = 75f;

        var panelBox = UiTheme.Panel(6, true);
        panelBox.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.90f);
        panelBox.BorderColor = UiTheme.Gold;
        _bdwScoreBanner.AddThemeStyleboxOverride("panel", panelBox);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        _bdwScoreBanner.AddChild(margin);

        var vbox = new VBoxContainer();
        margin.AddChild(vbox);

        var scoreRow = new HBoxContainer();
        scoreRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(scoreRow);

        _bdwKarusScoreLbl = UiTheme.Text($"{Localization.Loc.Tr("KARUS")}: 0", 14, new Color("ff4d4d"), HorizontalAlignment.Left);
        _bdwKarusScoreLbl.AddThemeConstantOverride("outline_size", 2);
        _bdwKarusScoreLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scoreRow.AddChild(_bdwKarusScoreLbl);

        var vsLbl = UiTheme.Text("VS", 12, UiTheme.GoldBright, HorizontalAlignment.Center);
        vsLbl.AddThemeConstantOverride("outline_size", 2);
        scoreRow.AddChild(vsLbl);

        _bdwElmoScoreLbl = UiTheme.Text($"0 :{Localization.Loc.Tr("EL MORAD")}", 14, new Color("4da6ff"), HorizontalAlignment.Right);
        _bdwElmoScoreLbl.AddThemeConstantOverride("outline_size", 2);
        _bdwElmoScoreLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scoreRow.AddChild(_bdwElmoScoreLbl);

        _bdwAltarStatusLbl = UiTheme.Text(Localization.Loc.Tr("Altar of Manes: Active in Center"), 11, UiTheme.TextHi, HorizontalAlignment.Center);
        _bdwAltarStatusLbl.AddThemeConstantOverride("outline_size", 1);
        vbox.AddChild(_bdwAltarStatusLbl);

        _bdwHudLayer.AddChild(_bdwScoreBanner);
    }

    private void UpdateBdwHudVisibility()
    {
        if (_bdwScoreBanner != null && IsInstanceValid(_bdwScoreBanner))
        {
            _bdwScoreBanner.Visible = _zone == BdwZone;
        }
    }

    private void OnBdwScoresReceived(int karus, int elmo)
    {
        _bdwKarusScore = karus;
        _bdwElmoScore = elmo;

        if (_bdwKarusScoreLbl != null && IsInstanceValid(_bdwKarusScoreLbl))
            _bdwKarusScoreLbl.Text = $"{Localization.Loc.Tr("KARUS")}: {karus}";

        if (_bdwElmoScoreLbl != null && IsInstanceValid(_bdwElmoScoreLbl))
            _bdwElmoScoreLbl.Text = $"{elmo} :{Localization.Loc.Tr("EL MORAD")}";

        UpdateBdwHudVisibility();
    }

    private void OnBdwAltarFlagReceived(string playerName, byte nation)
    {
        _bdwAltarCarrier = playerName;
        _bdwAltarTimer.Stop();

        string nationStr = nation == 1 ? Localization.Loc.Tr("Karus") : Localization.Loc.Tr("El Morad");
        Color nationCol = nation == 1 ? new Color("ff4d4d") : new Color("4da6ff");

        if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
        {
            _bdwAltarStatusLbl.Text = $"{Localization.Loc.Tr("Carrier")}: {playerName} ({nationStr})";
            _bdwAltarStatusLbl.AddThemeColorOverride("font_color", nationCol);
        }

        UpdateBdwHudVisibility();
    }

    private void OnBdwFinishReceived(int eventId, int winnerNation, uint seconds)
    {
        bool won = winnerNation != 0 && winnerNation == Net.I.LastEnter.Nation;
        string result = won ? Localization.Loc.Tr("Your nation has won the battle.") : Localization.Loc.Tr("Your nation has lost the battle.");
        Audio.PlayUi(won ? BdwVictorySound : BdwDefeatSound);
        CombatNotice(result);
        if (_zone != BdwZone) return;

        _bdwResult = result;
        _bdwReturnSeconds = (int)seconds;
        _bdwAltarCarrier = string.Empty;
        if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
        {
            _bdwAltarStatusLbl.AddThemeColorOverride("font_color", won ? UiTheme.Good : UiTheme.Bad);
            ShowBdwReturnCountdown();
        }
        if (_bdwReturnSeconds > 0) _bdwAltarTimer.Start();
        UpdateBdwHudVisibility();
    }

    private void ShowBdwReturnCountdown() =>
        _bdwAltarStatusLbl.Text = $"{_bdwResult}  {_bdwReturnSeconds / 60}:{_bdwReturnSeconds % 60:00}";

    private void OnBdwAltarTimerReceived(int seconds)
    {
        if (_bdwResult != null) return;
        _bdwAltarSeconds = seconds;
        _bdwAltarCarrier = string.Empty;

        if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
        {
            _bdwAltarStatusLbl.Text = $"{Localization.Loc.Tr("Altar respawning in")} {seconds}s";
            _bdwAltarStatusLbl.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        }

        if (seconds > 0)
        {
            _bdwAltarTimer.Start();
        }
        else
        {
            _bdwAltarTimer.Stop();
            if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
            {
                _bdwAltarStatusLbl.Text = Localization.Loc.Tr("Altar of Manes: Active in Center");
                _bdwAltarStatusLbl.AddThemeColorOverride("font_color", UiTheme.Good);
            }
        }

        UpdateBdwHudVisibility();
    }

    private void OnBdwAltarTick()
    {
        if (_bdwResult != null)
        {
            if (_bdwReturnSeconds > 0) _bdwReturnSeconds--;
            else _bdwAltarTimer.Stop();
            if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
                ShowBdwReturnCountdown();
            return;
        }
        if (_bdwAltarSeconds > 0)
        {
            _bdwAltarSeconds--;
            if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
            {
                _bdwAltarStatusLbl.Text = $"{Localization.Loc.Tr("Altar respawning in")} {_bdwAltarSeconds}s";
            }
        }
        else
        {
            _bdwAltarTimer.Stop();
            if (_bdwAltarStatusLbl != null && IsInstanceValid(_bdwAltarStatusLbl))
            {
                _bdwAltarStatusLbl.Text = Localization.Loc.Tr("Altar of Manes: Active in Center");
                _bdwAltarStatusLbl.AddThemeColorOverride("font_color", UiTheme.Good);
            }
        }
    }
}
