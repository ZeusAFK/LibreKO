using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _battleeventLayer = null!;

    private PanelContainer _battleeventBanner = null!;
    private Label _battleeventBannerLbl = null!;
    private int _battleeventBannerToken;

    private PanelContainer _battleeventBoard = null!;
    private Label _battleeventZoneLbl = null!;
    private Label _battleeventKarusLbl = null!;
    private Label _battleeventElmoLbl = null!;
    private Label _battleeventTimerLbl = null!;
    private Godot.Timer _battleeventPoll = null!;

    private PanelContainer _battleeventResult = null!;
    private Label _battleeventResultLbl = null!;
    private int _battleeventResultToken;

    private bool _battleeventActive;
    private int _battleeventRemaining;

    private static readonly Color BattleKarusCol = new("d05a4a");
    private static readonly Color BattleElmoCol  = new("4a82d0");

    private void BattleEventInit()
    {
        BuildBattleEventUi();
        Net.I.BattleZoneToggleEvent += OnBattleZoneToggle;
        Net.I.BattleStatusEvent     += OnBattleStatus;
        Net.I.BattleResultEvent     += OnBattleResult;
        Net.I.BattleScoreEvent      += OnBattleScore;

        Net.I.SendBattleStatusRequest();
    }

    private void BattleEventDispose()
    {
        Net.I.BattleZoneToggleEvent -= OnBattleZoneToggle;
        Net.I.BattleStatusEvent     -= OnBattleStatus;
        Net.I.BattleResultEvent     -= OnBattleResult;
        Net.I.BattleScoreEvent      -= OnBattleScore;
    }

    private void BuildBattleEventUi()
    {
        _battleeventLayer = new CanvasLayer { Layer = 68 };
        AddChild(_battleeventLayer);

        BuildBattleEventBanner();
        BuildBattleEventBoard();
        BuildBattleEventResult();

        _battleeventPoll = new Godot.Timer { WaitTime = 2.0, Autostart = false, OneShot = false };
        _battleeventPoll.Timeout += OnBattlePollTick;
        _battleeventLayer.AddChild(_battleeventPoll);
    }

    private void BuildBattleEventBanner()
    {
        _battleeventBanner = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 140,
            Visible = false,
        };
        _battleeventBanner.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _battleeventLayer.AddChild(_battleeventBanner);

        var m = new MarginContainer();
        UiTheme.Margins(m, 20, 9, 20, 9);
        _battleeventBanner.AddChild(m);
        _battleeventBannerLbl = UiTheme.Text("", 17, UiTheme.GoldBright, HorizontalAlignment.Center);
        _battleeventBannerLbl.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(_battleeventBannerLbl);
        HudLayout.Attach(_battleeventBanner, "hud_battle_banner", _battleeventBannerLbl,
            () => _battleeventBanner.Position);
    }

    private void BuildBattleEventBoard()
    {
        _battleeventBoard = new PanelContainer
        {
            AnchorLeft = 0, AnchorRight = 0, AnchorTop = 0, AnchorBottom = 0,
            OffsetLeft = 14, OffsetTop = 120,
            Visible = false,
        };
        _battleeventBoard.AddThemeStyleboxOverride("panel", UiTheme.Panel(6));
        _battleeventLayer.AddChild(_battleeventBoard);

        var m = new MarginContainer();
        UiTheme.Margins(m, 12, 8, 12, 8);
        _battleeventBoard.AddChild(m);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(176, 0) };
        col.AddThemeConstantOverride("separation", 3);
        m.AddChild(col);

        _battleeventZoneLbl = UiTheme.SectionTitle("War Zone");
        col.AddChild(_battleeventZoneLbl);
        col.AddChild(new HSeparator());

        _battleeventKarusLbl = ScoreRow(col, "Karus", BattleKarusCol);
        _battleeventElmoLbl  = ScoreRow(col, "El Morad", BattleElmoCol);

        col.AddChild(new HSeparator());
        _battleeventTimerLbl = UiTheme.Text("--:--", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        col.AddChild(_battleeventTimerLbl);

        HudLayout.Attach(_battleeventBoard, "hud_battle_score", _battleeventZoneLbl,
            () => _battleeventBoard.Position);
    }

    private static Label ScoreRow(VBoxContainer parent, string name, Color col)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var nameLbl = UiTheme.Text(name, 13, col);
        nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLbl);
        var valLbl = UiTheme.Text("0", 14, UiTheme.TextHi, HorizontalAlignment.Right);
        valLbl.CustomMinimumSize = new Vector2(48, 0);
        row.AddChild(valLbl);
        parent.AddChild(row);
        return valLbl;
    }

    private void BuildBattleEventResult()
    {
        _battleeventResult = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            OffsetTop = -70,
            Visible = false,
        };
        _battleeventResult.AddThemeStyleboxOverride("panel", UiTheme.Panel(8, true));
        _battleeventLayer.AddChild(_battleeventResult);

        var m = new MarginContainer();
        UiTheme.Margins(m, 30, 16, 30, 16);
        _battleeventResult.AddChild(m);
        _battleeventResultLbl = UiTheme.Text("", 24, UiTheme.GoldBright, HorizontalAlignment.Center);
        _battleeventResultLbl.AddThemeConstantOverride("outline_size", 6);
        m.AddChild(_battleeventResultLbl);
    }

    private void OnBattleZoneToggle(int openType, int zone)
    {
        if (openType == Net.BattleZoneClose)
        {
            ShowBattleBanner("The battle zone has closed.");
            StopBattleScoreboard();
        }
        else
        {
            string snow = openType == Net.BattleZoneSnowOpen ? "Snow " : "";
            ShowBattleBanner($"The {snow}Battle Zone is now OPEN!  ({BattleZoneName(zone)})");
            StartBattleScoreboard(zone, 0);
            Net.I.SendBattleStatusRequest();
        }
    }

    private void OnBattleStatus(int battleState, int zone, int remaining)
    {
        if (battleState == Net.BattleStateNone)
        {
            StopBattleScoreboard();
            return;
        }
        StartBattleScoreboard(zone, remaining);
    }

    private void OnBattleResult(int declareType, int nation)
    {
        bool mine = nation == Net.I.LastEnter.Nation;
        string nationName = Nations.Name(nation);

        if (declareType == Net.BattleDeclareWinner)
        {
            ShowBattleResultPopup(mine ? "VICTORY!" : $"{nationName} won the war.", mine);
            Chat.Info($"[War] {nationName} has won the battle!");
        }
        else if (declareType == Net.BattleDeclareLoser)
        {
            if (mine) ShowBattleResultPopup("DEFEAT", false);
        }
        StopBattleScoreboard();
    }

    private void OnBattleScore(int eventType, int karus, int elmo)
    {
        if (!_battleeventActive) return;
        _battleeventKarusLbl.Text = karus.ToString();
        _battleeventElmoLbl.Text  = elmo.ToString();
    }

    private void StartBattleScoreboard(int zone, int remaining)
    {
        _battleeventActive = true;
        _battleeventRemaining = remaining;
        _battleeventZoneLbl.Text = BattleZoneName(zone);
        UpdateBattleTimerLabel();
        _battleeventBoard.Visible = true;
        if (_battleeventPoll.IsStopped()) _battleeventPoll.Start();
        Net.I.SendMapEvent();
    }

    private void StopBattleScoreboard()
    {
        _battleeventActive = false;
        _battleeventBoard.Visible = false;
        _battleeventPoll.Stop();
    }

    private void OnBattlePollTick()
    {
        if (!_battleeventActive) { _battleeventPoll.Stop(); return; }
        Net.I.SendMapEvent();
        if (_battleeventRemaining > 0)
        {
            _battleeventRemaining = Mathf.Max(0, _battleeventRemaining - 2);
            UpdateBattleTimerLabel();
        }
    }

    private void UpdateBattleTimerLabel()
    {
        if (_battleeventRemaining <= 0) { _battleeventTimerLbl.Text = "--:--"; return; }
        int m = _battleeventRemaining / 60;
        int s = _battleeventRemaining % 60;
        _battleeventTimerLbl.Text = $"{m:00}:{s:00}";
    }

    private void ShowBattleBanner(string text)
    {
        Chat.Info(text);
        _battleeventBannerLbl.Text = text;
        _battleeventBanner.Visible = true;
        int token = ++_battleeventBannerToken;
        GetTree().CreateTimer(7.0).Timeout += () =>
        {
            if (_battleeventBannerToken == token) _battleeventBanner.Visible = false;
        };
    }

    private void ShowBattleResultPopup(string text, bool win)
    {
        _battleeventResultLbl.Text = text;
        _battleeventResultLbl.AddThemeColorOverride("font_color", win ? UiTheme.GoldBright : UiTheme.Bad);
        _battleeventResult.Visible = true;
        int token = ++_battleeventResultToken;
        GetTree().CreateTimer(6.0).Timeout += () =>
        {
            if (_battleeventResultToken == token) _battleeventResult.Visible = false;
        };
    }

    private static string BattleZoneName(int zone) => zone switch
    {
        61 => "Napies Gorge",
        62 => "Alseids Prairie",
        63 => "Nieds Triangle",
        64 => "Nereid's Island",
        65 => "Zipang",
        66 => "Oreads",
        69 => "Snow Battle",
        _  => "Battle Zone",
    };
}
