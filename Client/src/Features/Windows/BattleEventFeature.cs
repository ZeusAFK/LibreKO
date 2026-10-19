using Godot;

namespace LibreKO.Features.Windows;

internal sealed class BattleEventFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly BattleEventSession battleEvent;
    private readonly SelfSession self;
    private readonly IBattleEventRequests requests;
    private readonly EventPlatesFeature eventPlates;

    public BattleEventFeature(IWorldContext context, BattleEventSession battleEvent, SelfSession self, IBattleEventRequests requests, EventPlatesFeature eventPlates)
    {
        this.context = context;
        this.battleEvent = battleEvent;
        this.self = self;
        this.requests = requests;
        this.eventPlates = eventPlates;
    }

    private CanvasLayer battleeventLayer = null!;

    private PanelContainer battleeventBanner = null!;
    private Label battleeventBannerLbl = null!;
    private int battleeventBannerToken;

    internal WarScoreStrip BattleeventBoard { get; private set; } = null!;
    private Godot.Timer battleeventPoll = null!;

    private PanelContainer battleeventResult = null!;
    private Label battleeventResultLbl = null!;
    private int battleeventResultToken;

    private bool battleeventActive;
    internal int Remaining { get; set; }

    private const string BoardTooltip = "War Zone";

    public void Build()
    {
        BuildUi();
        battleEvent.BattleZoneToggleEvent += OnZoneToggle;
        battleEvent.BattleStatusEvent     += OnStatus;
        battleEvent.BattleResultEvent     += OnResult;
        battleEvent.BattleScoreEvent      += OnScore;

        requests.BattleStatusRequest();
    }

    public void Dispose()
    {
        battleEvent.BattleZoneToggleEvent -= OnZoneToggle;
        battleEvent.BattleStatusEvent     -= OnStatus;
        battleEvent.BattleResultEvent     -= OnResult;
        battleEvent.BattleScoreEvent      -= OnScore;
    }

    internal void BuildUi()
    {
        battleeventLayer = new CanvasLayer { Layer = UiLayers.Announcements };
        context.Root.AddChild(battleeventLayer);

        BuildBanner();
        BuildBoard();
        BuildResult();

        battleeventPoll = new Godot.Timer { WaitTime = 2.0, Autostart = false, OneShot = false };
        battleeventPoll.Timeout += OnPollTick;
        battleeventLayer.AddChild(battleeventPoll);
    }

    private void BuildBanner()
    {
        battleeventBanner = new PanelContainer { Visible = false };
        battleeventBanner.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        battleeventLayer.AddChild(battleeventBanner);

        var m = new MarginContainer();
        UiTheme.Margins(m, 20, 9, 20, 9);
        battleeventBanner.AddChild(m);
        battleeventBannerLbl = UiTheme.Text("", 17, UiTheme.GoldBright, HorizontalAlignment.Center);
        battleeventBannerLbl.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(battleeventBannerLbl);
        var layout = HudLayout.Attach(battleeventBanner, UiKit.BattleBannerLayoutId, battleeventBannerLbl,
            () => eventPlates.PlateSpot(battleeventBanner));
        eventPlates.AddPlate(battleeventBanner, layout, UiKit.BattleBannerLayoutId);
    }

    private void BuildBoard()
    {
        BattleeventBoard = new WarScoreStrip(withStatus: false) { Visible = false, TooltipText = BoardTooltip };
        battleeventLayer.AddChild(BattleeventBoard);
        UpdateTimerLabel();

        var layout = HudLayout.Attach(BattleeventBoard, UiKit.BattleBoardLayoutId, BattleeventBoard,
            () => eventPlates.PlateSpot(BattleeventBoard));
        eventPlates.AddPlate(BattleeventBoard, layout, UiKit.BattleBoardLayoutId);
    }

    private void BuildResult()
    {
        battleeventResult = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            OffsetTop = -70,
            Visible = false,
        };
        battleeventResult.AddThemeStyleboxOverride("panel", UiTheme.Panel(8, true));
        battleeventLayer.AddChild(battleeventResult);

        var m = new MarginContainer();
        UiTheme.Margins(m, 30, 16, 30, 16);
        battleeventResult.AddChild(m);
        battleeventResultLbl = UiTheme.Text("", 24, UiTheme.GoldBright, HorizontalAlignment.Center);
        battleeventResultLbl.AddThemeConstantOverride("outline_size", 6);
        m.AddChild(battleeventResultLbl);
    }

    private void OnZoneToggle(BattleEventNotice openType, int zone)
    {
        if (openType == BattleEventNotice.ZoneClose)
        {
            ShowBanner("The battle zone has closed.");
            StopScoreboard();
        }
        else
        {
            string snow = openType == BattleEventNotice.SnowZoneOpen ? "Snow " : "";
            ShowBanner($"The {snow}Battle Zone is now OPEN!  ({ZoneName(zone)})");
            StartScoreboard(zone, 0);
            requests.BattleStatusRequest();
        }
    }

    private void OnStatus(BattleEventState battleState, int zone, int remaining)
    {
        if (battleState == BattleEventState.None)
        {
            StopScoreboard();
            return;
        }
        StartScoreboard(zone, remaining);
    }

    private void OnResult(BattleEventNotice declareType, int nation)
    {
        bool mine = nation == self.LastEnter.Nation;
        string nationName = Nations.Name(nation);

        if (declareType == BattleEventNotice.DeclareWinner)
        {
            ShowResultPopup(mine ? "VICTORY!" : $"{nationName} won the war.", mine);
            context.Log.Notice($"[War] {nationName} has won the battle!");
        }
        else if (declareType == BattleEventNotice.DeclareLoser)
        {
            if (mine) ShowResultPopup("DEFEAT", false);
        }
        StopScoreboard();
    }

    private void OnScore(MapEventSubOpcode eventType, int karus, int elmo)
    {
        if (!battleeventActive) return;
        BattleeventBoard.SetScores(karus, elmo);
    }

    private void StartScoreboard(int zone, int remaining)
    {
        battleeventActive = true;
        Remaining = remaining;
        BattleeventBoard.TooltipText = ZoneName(zone);
        UpdateTimerLabel();
        BattleeventBoard.Visible = true;
        if (battleeventPoll.IsStopped()) battleeventPoll.Start();
        requests.MapEvent();
    }

    private void StopScoreboard()
    {
        battleeventActive = false;
        BattleeventBoard.Visible = false;
        battleeventPoll.Stop();
    }

    private void OnPollTick()
    {
        if (!battleeventActive) { battleeventPoll.Stop(); return; }
        requests.MapEvent();
        if (Remaining > 0)
        {
            Remaining = Mathf.Max(0, Remaining - 2);
            UpdateTimerLabel();
        }
    }

    internal void UpdateTimerLabel()
    {
        if (Remaining <= 0) { BattleeventBoard.Centre = "--:--"; return; }
        int m = Remaining / 60;
        int s = Remaining % 60;
        BattleeventBoard.Centre = $"{m:00}:{s:00}";
    }

    internal void ShowBanner(string text)
    {
        context.Log.Notice(text);
        battleeventBannerLbl.Text = text;
        battleeventBanner.Visible = true;
        int token = ++battleeventBannerToken;
        context.Root.After(7.0, () =>
        {
            if (battleeventBannerToken == token) battleeventBanner.Visible = false;
        });
    }

    private void ShowResultPopup(string text, bool win)
    {
        battleeventResultLbl.Text = text;
        battleeventResultLbl.AddThemeColorOverride("font_color", win ? UiTheme.GoldBright : UiTheme.Bad);
        battleeventResult.Visible = true;
        int token = ++battleeventResultToken;
        context.Root.After(6.0, () =>
        {
            if (battleeventResultToken == token) battleeventResult.Visible = false;
        });
    }

    internal static string ZoneName(int zone) => zone switch
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
