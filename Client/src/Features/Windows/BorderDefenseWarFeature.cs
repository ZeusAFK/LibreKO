using Godot;

namespace LibreKO.Features.Windows;

internal sealed class BorderDefenseWarFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly BifrostSession bifrost;
    private readonly RankSession rank;
    private readonly SelfSession self;
    private readonly IRankRequests rankRequests;
    private readonly EventPlatesFeature eventPlates;

    public BorderDefenseWarFeature(IWorldContext context, BifrostSession bifrost, RankSession rank, SelfSession self, IRankRequests rankRequests, EventPlatesFeature eventPlates)
    {
        this.context = context;
        this.bifrost = bifrost;
        this.rank = rank;
        this.self = self;
        this.rankRequests = rankRequests;
        this.eventPlates = eventPlates;
    }

    internal CanvasLayer HudLayer { get; set; } = null!;
    internal WarScoreStrip BdwScoreBanner { get; private set; } = null!;
    internal Godot.Timer RefreshTimer { get; set; } = null!;

    private const int BdwVictorySound = 340119;
    private const int BdwDefeatSound = 340118;
    private const string BdwCentre = "VS";
    private const string BdwAltarStands = "Altar of Manes: Active in Center";
    private const string BdwCarrierFx = "ft_chr_point_fxqallll";
    private const string BdwCarrierFxNode = "bdw_carrier_fx";
    private const string JuraidStatus = "Clear the rooms and slay Devabird";
    private Node3D? bdwCarrierFx;

    private static bool IsWarRoomZone(int zone) => zone is TempleZones.BorderDefenseWar or TempleZones.JuraidMountain;

    public void Build()
    {
        HudLayer = new CanvasLayer { Layer = UiLayers.EventBanners };
        context.Root.AddChild(HudLayer);

        BuildBdwScoreBanner();

        RefreshTimer = new Godot.Timer { WaitTime = 1.0, Autostart = false, OneShot = false };
        RefreshTimer.Timeout += RefreshBdwHud;
        HudLayer.AddChild(RefreshTimer);

        bifrost.TempleScreenScoreEvent += OnBdwScoresReceived;
        bifrost.AltarFlagEvent += OnBdwAltarFlagReceived;
        bifrost.AltarTimerEvent += OnBdwAltarTimerReceived;
        bifrost.TempleEventFinishEvent += OnBdwFinishReceived;
        rank.BorderWarRankEvent += OnWarRanking;

        if (!IsWarRoomZone(context.Zone))
        {
            bifrost.BorderWar.Reset();
            return;
        }
        BdwScoreBanner.RankingShown = context.Zone == TempleZones.BorderDefenseWar;
        BdwScoreBanner.Visible = true;
        RefreshBdwHud();
        RefreshBdwCarrierFx();
        if (bifrost.BorderWar.Finished && bifrost.BorderWar.HomeSecondsLeft(DateTime.UtcNow) > 0)
            ShowWarResult();
    }

    public void Dispose()
    {
        bifrost.TempleScreenScoreEvent -= OnBdwScoresReceived;
        bifrost.AltarFlagEvent -= OnBdwAltarFlagReceived;
        bifrost.AltarTimerEvent -= OnBdwAltarTimerReceived;
        bifrost.TempleEventFinishEvent -= OnBdwFinishReceived;
        rank.BorderWarRankEvent -= OnWarRanking;

        RefreshTimer?.Stop();
        if (HudLayer != null && GodotObject.IsInstanceValid(HudLayer))
            HudLayer.QueueFree();
    }

    internal void BuildBdwScoreBanner()
    {
        BdwScoreBanner = new WarScoreStrip(withStatus: true)
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Centre = BdwCentre,
        };
        BdwScoreBanner.RankingPressed += ToggleWarRanking;
        HudLayer.AddChild(BdwScoreBanner);
        eventPlates.AddPlate(BdwScoreBanner);
    }

    private void OnBdwScoresReceived(int karus, int elmo) => RefreshBdwHud();

    private void OnBdwAltarFlagReceived(string playerName, byte nation)
    {
        RefreshBdwHud();
        RefreshBdwCarrierFx();
    }

    private void OnBdwAltarTimerReceived(int seconds)
    {
        RefreshBdwHud();
        RefreshBdwCarrierFx();
    }

    private void RefreshBdwCarrierFx()
    {
        string carrier = context.Zone == TempleZones.BorderDefenseWar ? bifrost.BorderWar.Carrier : "";
        Node3D? body = carrier.Length == 0 ? null : BodyNamed(carrier);
        if (bdwCarrierFx != null && GodotObject.IsInstanceValid(bdwCarrierFx))
        {
            if (body != null && bdwCarrierFx.GetParent() == body) return;
            Fx.Free(bdwCarrierFx);
        }
        bdwCarrierFx = null;
        if (body == null) return;
        bdwCarrierFx = Fx.Spawn(BdwCarrierFx, body, Vector3.Zero, oneShot: false);
        if (bdwCarrierFx != null) bdwCarrierFx.Name = BdwCarrierFxNode;
    }

    private Node3D? BodyNamed(string name)
    {
        if (string.Equals(name, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase)) return context.Self.Body;
        foreach (var ent in context.Entities.All())
            if (!ent.IsNpc && string.Equals(ent.Name, name, StringComparison.OrdinalIgnoreCase)) return context.Entities.BodyOf(ent.Id);
        return null;
    }

    private void OnBdwFinishReceived(int eventId, int winnerNation, uint seconds)
    {
        bool won = winnerNation != 0 && winnerNation == self.LastEnter.Nation;
        Audio.PlayUi(won ? BdwVictorySound : BdwDefeatSound);
        context.Log.Notice(BdwResultLine(winnerNation));
        RefreshBdwHud();
        RefreshBdwCarrierFx();
        if (IsWarRoomZone(context.Zone)) ShowWarResult();
    }

    private string BdwResultLine(int winnerNation) =>
        winnerNation == 0 ? "The battle ended in a draw."
        : winnerNation == self.LastEnter.Nation ? "Your nation has won the battle."
        : "Your nation has lost the battle.";

    private void RefreshBdwHud()
    {
        if (!IsWarRoomZone(context.Zone) || !GodotObject.IsInstanceValid(BdwScoreBanner))
        {
            RefreshTimer.Stop();
            return;
        }

        var war = bifrost.BorderWar;
        var now = DateTime.UtcNow;
        BdwScoreBanner.SetScores(war.KarusScore, war.ElmoradScore);
        int end = war.EndSecondsLeft(now);
        BdwScoreBanner.SetClock(end > 0 ? $"End  {ClockText(end)}" : "");
        bool counting = end > 0;
        RefreshWarResult(now);
        if (war.Winner is { } winner)
        {
            int home = war.HomeSecondsLeft(now);
            var colour = winner == 0 ? UiTheme.TextHi : winner == self.LastEnter.Nation ? UiTheme.Good : UiTheme.Bad;
            BdwScoreBanner.SetStatus($"{BdwResultLine(winner)}  {home / 60}:{home % 60:00}", colour);
            counting |= home > 0;
        }
        else if (context.Zone == TempleZones.JuraidMountain)
        {
            BdwScoreBanner.SetStatus(JuraidStatus, UiTheme.TextHi);
        }
        else if (war.Carrier.Length > 0)
        {
            ShowBdwCarrier(war.Carrier, war.CarrierNation);
        }
        else if (war.AltarSecondsLeft(now) is > 0 and var altar)
        {
            BdwScoreBanner.SetStatus($"Altar respawning in {altar}s", UiTheme.GoldBright);
            counting = true;
        }
        else
        {
            BdwScoreBanner.SetStatus(BdwAltarStands, UiTheme.Good);
        }

        if (!counting) RefreshTimer.Stop();
        else if (RefreshTimer.IsStopped()) RefreshTimer.Start();
    }

    internal void ShowBdwCarrier(string carrier, byte nation)
    {
        bool karus = nation == Nations.Karus;
        BdwScoreBanner.SetStatus($"Carrier: {carrier} ({(karus ? "Karus" : "El Morad")})",
            karus ? WarScoreStrip.KarusColour : WarScoreStrip.ElmoradColour);
    }

    private const string WarResultId = "war_result";
    private const string WarRankingId = "war_ranking";
    private const string BdwTitle = "Border Defense War";
    private const string JuraidTitle = "Juraid Mountain";
    private const int WarResultWidth = 320;
    private const int WarRankingWidth = 520;
    private const int WarRankingNameWidth = 110;
    private const int WarRankingPlaceWidth = 22;
    private static readonly Color WarBonusColour = new("ff6a6a");

    internal HudWindow? WarResult { get; private set; }
    private Label warOutcome = null!;
    private Label warResultScores = null!;
    private Label warResultHome = null!;
    internal HudWindow? WarRanking { get; private set; }
    private VBoxContainer warRankKarus = null!;
    private VBoxContainer warRankElmorad = null!;
    private RichTextLabel warRankExp = null!;

    private string WarTitle => context.Zone == TempleZones.JuraidMountain ? JuraidTitle : BdwTitle;

    internal static string ClockText(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    private void ShowWarResult()
    {
        if (WarResult == null || !GodotObject.IsInstanceValid(WarResult)) BuildWarResult();
        WarResult!.Visible = true;
        RefreshWarResult(DateTime.UtcNow);
        if (RefreshTimer.IsStopped()) RefreshTimer.Start();
    }

    internal void BuildWarResult()
    {
        WarResult = new HudWindow(WarResultId, WarTitle, bodyMinWidth: WarResultWidth, persistLayout: false) { Visible = false };
        WarResult.Closed += () => WarResult.Visible = false;
        HudLayer.AddChild(WarResult);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 8);
        WarResult.Body.AddChild(col);
        warOutcome = UiTheme.Text("", 26, UiTheme.TextHi, HorizontalAlignment.Center);
        warOutcome.AddThemeConstantOverride("outline_size", 3);
        col.AddChild(warOutcome);
        warResultScores = UiTheme.Text("", 15, UiTheme.TextHi, HorizontalAlignment.Center);
        col.AddChild(warResultScores);
        col.AddChild(UiTheme.Rule());
        warResultHome = UiTheme.Text("", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        col.AddChild(warResultHome);
    }

    internal void RefreshWarResult(DateTime now)
    {
        if (WarResult == null || !GodotObject.IsInstanceValid(WarResult) || !WarResult.Visible) return;
        var war = bifrost.BorderWar;
        if (war.Winner is not { } winner)
        {
            WarResult.Visible = false;
            return;
        }
        var outcome = BorderWarState.OutcomeFor(winner, self.LastEnter.Nation);
        warOutcome.Text = outcome.ToString();
        warOutcome.AddThemeColorOverride("font_color", outcome switch
        {
            WarOutcome.Victory => UiTheme.Good,
            WarOutcome.Defeat => UiTheme.Bad,
            _ => UiTheme.TextHi,
        });
        warResultScores.Text = $"Karus  {war.KarusScore}   :   {war.ElmoradScore}  El Morad";
        int home = war.HomeSecondsLeft(now);
        warResultHome.Text = home > 0 ? $"Rewards and return home in {ClockText(home)}" : "Returning home";
    }

    private void ToggleWarRanking()
    {
        if (WarRanking != null && GodotObject.IsInstanceValid(WarRanking) && WarRanking.Visible)
        {
            WarRanking.Visible = false;
            return;
        }
        if (WarRanking == null || !GodotObject.IsInstanceValid(WarRanking)) BuildWarRanking();
        WarRanking!.Visible = true;
        rankRequests.RankRequest(RankType.BorderDefenseWar);
    }

    internal void BuildWarRanking()
    {
        WarRanking = new HudWindow(WarRankingId, "Ranking", bodyMinWidth: WarRankingWidth) { Visible = false };
        WarRanking.Closed += () => WarRanking.Visible = false;
        HudLayer.AddChild(WarRanking);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 8);
        WarRanking.Body.AddChild(columns);
        warRankKarus = WarRankColumn(columns, "Karus", WarScoreStrip.KarusColour);
        warRankElmorad = WarRankColumn(columns, "El Morad", WarScoreStrip.ElmoradColour);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 8);
        WarRanking.Body.AddChild(foot);
        warRankExp = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        warRankExp.AddThemeFontSizeOverride("normal_font_size", 13);
        foot.AddChild(warRankExp);
        var refresh = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refresh.Pressed += () => rankRequests.RankRequest(RankType.BorderDefenseWar);
        foot.AddChild(refresh);
    }

    private static VBoxContainer WarRankColumn(HBoxContainer parent, string nation, Color colour)
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        parent.AddChild(section);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        section.AddChild(col);
        col.AddChild(UiTheme.Text(nation, 14, colour, HorizontalAlignment.Center));
        col.AddChild(UiTheme.Rule());
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        col.AddChild(rows);
        return rows;
    }

    internal void OnWarRanking(BorderWarRanking ranking)
    {
        if (WarRanking == null || !GodotObject.IsInstanceValid(WarRanking)) return;
        FillWarRankColumn(warRankKarus, ranking.Karus);
        FillWarRankColumn(warRankElmorad, ranking.Elmorad);
        string text = $"Victory reward:  {ranking.VictoryExp:N0} EXP";
        if (ranking.BonusExp > 0) text += $" [color=#{WarBonusColour.ToHtml(false)}](+{ranking.BonusExp:N0})[/color]";
        warRankExp.Text = text;
    }

    private static void FillWarRankColumn(VBoxContainer rows, IReadOnlyList<BorderWarRankRow> entries)
    {
        foreach (var child in rows.GetChildren()) child.QueueFree();
        if (entries.Count == 0)
        {
            rows.AddChild(UiTheme.Text("No one yet", 12, UiTheme.TextLo, HorizontalAlignment.Center));
            return;
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            rows.AddChild(row);
            var place = UiTheme.Text($"{i + 1}.", 12, UiTheme.GoldBright);
            place.CustomMinimumSize = new Vector2(WarRankingPlaceWidth, 0);
            row.AddChild(place);
            var name = UiTheme.Text(entry.Name, 12, UiTheme.TextHi);
            name.CustomMinimumSize = new Vector2(WarRankingNameWidth, 0);
            name.ClipText = true;
            row.AddChild(name);
            var clan = UiTheme.Text(entry.ClanName, 11, UiTheme.TextLo);
            clan.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            clan.ClipText = true;
            row.AddChild(clan);
            row.AddChild(UiTheme.Text($"{entry.Points}", 12, UiTheme.Gold, HorizontalAlignment.Right));
        }
    }
}
