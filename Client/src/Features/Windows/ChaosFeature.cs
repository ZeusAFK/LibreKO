using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class ChaosFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly BifrostSession bifrost;
    private readonly ChaosSession chaos;
    private readonly MonsterStoneSession monsterStone;
    private readonly SelfSession self;
    private readonly IRankRequests rankRequests;
    private readonly MsgOutFeature msgOut;
    private readonly RankFeature rank;
    private readonly EventPlatesFeature eventPlates;
    private readonly ChaosGuideFeature guide;
    private readonly IBifrostRequests bifrostRequests;

    public ChaosFeature(IWorldContext context, BifrostSession bifrost, ChaosSession chaos, MonsterStoneSession monsterStone, SelfSession self, IRankRequests rankRequests, MsgOutFeature msgOut, RankFeature rank, EventPlatesFeature eventPlates, ChaosGuideFeature chaosGuide, IBifrostRequests bifrostRequests)
    {
        this.context = context;
        this.bifrost = bifrost;
        this.chaos = chaos;
        this.monsterStone = monsterStone;
        this.self = self;
        this.rankRequests = rankRequests;
        this.msgOut = msgOut;
        this.rank = rank;
        this.eventPlates = eventPlates;
        guide = chaosGuide;
        this.bifrostRequests = bifrostRequests;
    }

    public void Tick(double delta, double nowSec)
    {
        TickNestTimer();
        ChaosTick();
    }

    private const int LadderFontSize = 13;
    private const int TeamWidth = 52;
    private const int RankWidth = 44;
    private const int NameWidth = 260;
    private const int CountWidth = 60;
    private const int RowHeight = 25;
    private const string WinBadge = "Win";
    private const string LoseBadge = "Lose";
    private static readonly Color OwnRowColour = new(1f, 0.85f, 0.4f, 0.16f);

    private sealed class ChaosLadderRow
    {
        public Control Root = null!;
        public ColorRect Own = null!;
        public Label Team = null!, Rank = null!, Name = null!, Kills = null!, Deaths = null!;
    }

    private CanvasLayer? layer;

    internal HudWindow? Ladder { get; private set; }
    private readonly ChaosLadderRow[] rows = new ChaosLadderRow[ChaosBattle.RowsShown];
    private Label? exp;
    private Label? premiumExp;
    internal ChaosRanking? Ranking { get; set; }
    private double rankingAt = double.NegativeInfinity;
    private bool ladderShown;
    private bool final;
    private bool finalPending;

    public void Build()
    {
        NestTimerInit();
        chaos.ChaosStartedEvent += OnStarted;
        chaos.ChaosFinishEvent += OnFinish;
        chaos.ChaosRankEvent += OnRanking;
        if (context.Zone == ChaosBattle.Zone && !chaos.ChaosBannerPrinted)
        {
            chaos.NoteBannerPrinted();
            PrintBanner();
        }
    }

    public void Dispose()
    {
        NestTimerDispose();
        chaos.ChaosStartedEvent -= OnStarted;
        chaos.ChaosFinishEvent -= OnFinish;
        chaos.ChaosRankEvent -= OnRanking;
    }

    private void PrintBanner()
    {
        context.Log.Notice(ChaosBattle.BannerRule, ChaosBattle.BannerColour);
        foreach (int text in ChaosBattle.BannerTexts)
            context.Log.Notice(ItemData.Text(text, ""), ChaosBattle.BannerColour);
        context.Log.Notice(ChaosBattle.BannerRule, ChaosBattle.BannerColour);
    }

    private void OnStarted()
    {
        msgOut.ShowSkill(ItemData.Text(ChaosBattle.StartedText, "The battle has begun."));
        if (guide.ChaosGuide != null && GodotObject.IsInstanceValid(guide.ChaosGuide)) guide.ChaosGuide.Visible = false;
    }

    private void OnFinish(uint seconds)
    {
        msgOut.ShowSkill(ItemData.Text(ChaosBattle.OverText, "The battle is over."));
    }

    private void ChaosTick()
    {
        if (context.Zone != ChaosBattle.Zone) return;
        if (chaos.ChaosOver && context.Now >= msgOut.Until)
        {
            chaos.ConsumeOver();
            finalPending = true;
            rankRequests.RankRequest(RankType.ChaosDungeon);
        }
        if (final) return;

        bool typing = context.Root.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
        if (typing || !KeyBinds.Held(KeyAction.PkLadder))
        {
            if (ladderShown) HideLadder();
            return;
        }
        if (ladderShown || chaos.Finished) return;
        ShowLadder(final: false);
        if (context.Now - rankingAt >= ChaosBattle.RankingThrottleSeconds)
        {
            rankingAt = context.Now;
            rankRequests.RankRequest(RankType.ChaosDungeon);
        }
    }

    private void OnRanking(ChaosRanking ranking)
    {
        this.Ranking = ranking;
        if (finalPending)
        {
            finalPending = false;
            ShowLadder(final: true);
            return;
        }
        if (ladderShown) RenderLadder();
    }

    internal void ShowLadder(bool final)
    {
        EnsureLadder();
        this.final = final;
        Ladder!.SetCloseVisible(final);
        Ladder.MouseFilter = final ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        RenderLadder();
        UiKit.ShowOnTop(Ladder);
        ladderShown = true;
    }

    private void HideLadder()
    {
        ladderShown = false;
        final = false;
        if (Ladder != null && GodotObject.IsInstanceValid(Ladder)) Ladder.Visible = false;
    }

    private void EnsureLadder()
    {
        if (Ladder != null && GodotObject.IsInstanceValid(Ladder)) return;
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);
        Ladder = new HudWindow("chaos_ladder", "Combat status") { Visible = false };
        Ladder.Closed += HideLadder;
        layer.AddChild(Ladder);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        Ladder.Body.AddChild(column);

        var header = ChaosLine();
        header.AddChild(Cell("Team", TeamWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(Cell("Rank", RankWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(Cell("Character name", NameWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(Cell("Kill", CountWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(Cell("Death", CountWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        column.AddChild(header);

        for (int i = 0; i < rows.Length; i++)
            rows[i] = AddRow(column);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 24);
        footer.AddChild(UiTheme.Text("Experience acquired:", LadderFontSize, UiTheme.TextLo));
        exp = UiTheme.Text("0", LadderFontSize, UiTheme.Gold);
        footer.AddChild(exp);
        footer.AddChild(UiTheme.Text("Premium experience acquired:", LadderFontSize, UiTheme.TextLo));
        premiumExp = UiTheme.Text("0", LadderFontSize, UiTheme.Gold);
        footer.AddChild(premiumExp);
        column.AddChild(new HSeparator());
        column.AddChild(footer);
    }

    private static HBoxContainer ChaosLine()
    {
        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 6);
        return line;
    }

    private static Label Cell(string text, int width, Color colour, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = UiTheme.Text(text, LadderFontSize, colour, align);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.ClipText = true;
        return label;
    }

    private static ChaosLadderRow AddRow(VBoxContainer column)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(0, RowHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        var own = new ColorRect { Color = OwnRowColour, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        own.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(own);
        var line = ChaosLine();
        line.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(line);
        var row = new ChaosLadderRow
        {
            Root = holder,
            Own = own,
            Team = Cell("", TeamWidth, UiTheme.Gold, HorizontalAlignment.Center),
            Rank = Cell("", RankWidth, UiTheme.TextHi, HorizontalAlignment.Center),
            Name = Cell("", NameWidth, UiTheme.TextHi, HorizontalAlignment.Center),
            Kills = Cell("", CountWidth, UiTheme.TextHi, HorizontalAlignment.Center),
            Deaths = Cell("", CountWidth, UiTheme.TextHi, HorizontalAlignment.Center),
        };
        line.AddChild(row.Team);
        line.AddChild(row.Rank);
        line.AddChild(row.Name);
        line.AddChild(row.Kills);
        line.AddChild(row.Deaths);
        column.AddChild(holder);
        return row;
    }

    private void RenderLadder()
    {
        if (Ladder == null) return;
        var ranking = this.Ranking;
        var lines = ranking == null ? new List<ChaosLadderLine>() : ChaosBattle.Ladder(ranking.Rows, self.LastEnter.Name ?? "");
        bool realNames = final || self.IsGm;
        int nation = self.LastEnter.Nation;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (i >= lines.Count)
            {
                row.Root.Visible = false;
                continue;
            }
            var line = lines[i];
            row.Root.Visible = true;
            row.Own.Visible = line.Own;
            row.Team.Text = line.Winner ? WinBadge : line.Loser ? LoseBadge : "";
            row.Team.AddThemeColorOverride("font_color", line.Winner ? UiTheme.GoldBright : UiTheme.TextLo);
            row.Rank.Text = line.Rank.ToString();
            row.Rank.AddThemeColorOverride("font_color", line.Winner ? ChaosBattle.WinnerRank : ChaosBattle.OtherRank);
            row.Name.Text = realNames ? line.Name : ItemData.Text(ChaosBattle.MaskedNameText(line.Own, nation), line.Name);
            row.Name.AddThemeColorOverride("font_color", line.Winner ? ChaosBattle.WinnerName : ChaosBattle.OtherName);
            row.Kills.Text = line.Kills.ToString();
            row.Deaths.Text = line.Deaths.ToString();
        }
        exp!.Text = (ranking?.Exp ?? 0).ToString();
        premiumExp!.Text = (ranking?.PremiumExp ?? 0).ToString();
        Ladder.ResetSize();
    }

    private void ShowPlate()
    {
        string title = ChaosBattle.EventTitle;
        if (chaos.Finished)
        {
            int left = chaos.FinishSecondsLeft(Time.GetTicksMsec());
            ShowTimerPlate(title, ItemData.Text(ChaosBattle.PlateOverText, "Chaos Battle is over."),
                NestDungeon.TerminationLine(left, completed: true));
            return;
        }
        int clock = monsterStone.NestSecondsLeft(Time.GetTicksMsec());
        if (clock <= 0)
        {
            if (NestTimerPanel is { Visible: true }) NestTimerPanel.Visible = false;
            return;
        }
        ShowTimerPlate(title, null, NestDungeon.TerminationLine(clock, completed: false));
    }

    private const string NestTimerFallbackTitle = "Monster Stone";
    private const string DrakiTimerTitle = "Draki's Tower";

    private CanvasLayer? nestTimerLayer;
    internal PanelContainer? NestTimerPanel { get; private set; }
    private Label? nestTimerTitle;
    private Label? nestTimerDone;
    private Label? nestTimerLine;
    private string nestTimerText = "";

    private void NestTimerInit()
    {
        if (!NestDungeon.IsNestZone(context.Zone) && context.Zone != ChaosBattle.Zone) monsterStone.ClearNestTimer();
    }

    private void NestTimerDispose()
    {
        if (nestTimerLayer != null && GodotObject.IsInstanceValid(nestTimerLayer)) nestTimerLayer.QueueFree();
        nestTimerLayer = null;
        NestTimerPanel = null;
    }

    private void EnsureNestTimer()
    {
        if (NestTimerPanel != null) return;

        nestTimerLayer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(nestTimerLayer);

        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        nestTimerLayer.AddChild(anchor);

        NestTimerPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        NestTimerPanel.AddThemeStyleboxOverride("panel", QuestProgressToastFeature.ToastStyle());
        NestTimerPanel.GuiInput += OnTimerPlateInput;
        anchor.AddChild(NestTimerPanel);

        var rowsBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rowsBox.AddThemeConstantOverride("separation", 6);
        NestTimerPanel.AddChild(rowsBox);
        rowsBox.AddChild(QuestProgressToastFeature.ToastRule());

        var text = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("margin_left", 58);
        text.AddThemeConstantOverride("margin_right", 58);
        rowsBox.AddChild(text);

        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 3);
        text.AddChild(lines);

        nestTimerTitle = NestTimerLabel(14, UiTheme.Gold);
        nestTimerDone = NestTimerLabel(16, UiTheme.Good);
        nestTimerLine = NestTimerLabel(16, UiTheme.TextHi);
        lines.AddChild(nestTimerTitle);
        lines.AddChild(nestTimerDone);
        lines.AddChild(nestTimerLine);
        rowsBox.AddChild(QuestProgressToastFeature.ToastRule());

        eventPlates.AddPlate(NestTimerPanel);
    }

    private static Label NestTimerLabel(int size, Color color)
    {
        var label = UiTheme.Text("", size, color, HorizontalAlignment.Center);
        label.AddThemeConstantOverride("font_embolden", 1);
        label.AddThemeConstantOverride("outline_size", 4);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        return label;
    }

    private bool drakiPlate;

    private void ShowDrakiPlate(bool shown)
    {
        if (drakiPlate == shown || NestTimerPanel == null) return;
        drakiPlate = shown;
        NestTimerPanel.MouseFilter = shown ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
        NestTimerPanel.TooltipText = shown ? DrakiTownTip : "";
    }

    private const string DrakiTownTip = "Right-click to go back to town";

    private void OnTimerPlateInput(InputEvent input)
    {
        if (!drakiPlate || input is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) return;
        NestTimerPanel!.AcceptEvent();
        Notice.Confirm(context.Root, DrakiTower.TownPrompt, "Yes", "No", bifrostRequests.DrakiTown);
    }

    private void TickNestTimer()
    {
        int draki = bifrost.DrakiSecondsLeft(Time.GetTicksMsec());
        if (draki > 0)
        {
            ShowTimerPlate(DrakiTimerTitle, $"Stage {bifrost.DrakiStage}-{bifrost.DrakiSubStage}",
                           $"{draki / 60}:{draki % 60:00}");
            ShowDrakiPlate(true);
            return;
        }
        if (bifrost.DrakiFinish is { } finish)
        {
            ShowTimerPlate(DrakiTimerTitle, $"Stage {finish.Stage}-{finish.SubStage}",
                           $"{DrakiTower.StageEndLabel(finish.GaveUp)}  {finish.ElapsedSeconds / 60}:{finish.ElapsedSeconds % 60:00}");
            ShowDrakiPlate(true);
            return;
        }
        ShowDrakiPlate(false);
        if (context.Zone == ChaosBattle.Zone)
        {
            ShowPlate();
            return;
        }
        int left = NestDungeon.IsNestZone(context.Zone) ? monsterStone.NestSecondsLeft(Time.GetTicksMsec()) : 0;
        if (left <= 0)
        {
            if (NestTimerPanel is { Visible: true }) NestTimerPanel.Visible = false;
            return;
        }
        ShowNestTimer(monsterStone.NestStoneItemId, left, monsterStone.NestCompleted);
    }

    internal void ShowNestTimer(int stoneItemId, int secondsLeft, bool completed)
    {
        string title = stoneItemId != 0 ? ItemData.DisplayName(stoneItemId) : NestTimerFallbackTitle;
        ShowTimerPlate(title, completed ? ItemData.Text(NestDungeon.CompletedText, NestDungeon.CompletedFallback) : null,
                       NestDungeon.TerminationLine(secondsLeft, completed,
                           ItemData.Text(NestDungeon.TerminationText, NestDungeon.TerminationFallback),
                           ItemData.Text(NestDungeon.SecondsText, NestDungeon.SecondsFallback)));
    }

    private void ShowTimerPlate(string title, string? middle, string line)
    {
        EnsureNestTimer();
        bool showMiddle = middle != null;
        string key = $"{title}|{middle}|{line}";
        if (key != nestTimerText)
        {
            nestTimerText = key;
            nestTimerTitle!.Text = ItemData.Text(NestDungeon.TitleText, NestDungeon.TitleFallback).Replace("%s", title);
            nestTimerDone!.Text = middle ?? "";
            nestTimerDone.Visible = showMiddle;
            nestTimerLine!.Text = line;
            NestTimerPanel!.ResetSize();
        }
        if (!NestTimerPanel!.Visible) NestTimerPanel.Visible = true;
    }
}
