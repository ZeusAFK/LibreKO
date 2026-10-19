using Godot;

namespace LibreKO.Features.Windows;

internal sealed class DrakiBoardFeature : IFeature, IClosableWindow
{
    private const string Title = "Draki's Tower";
    private const string RankHeading = "RANK";
    private const string EntranceLabel = "Remaining Entrance Limit :";
    private const string EnterLabel = "Enter";
    private const string ClosedSectorLabel = "x";
    private const int TextSize = 11;
    private const int HeadingSize = 12;
    private static readonly Vector2 Origin = new(8, 59);
    private static readonly Vector2 Canvas = new(404, 444);
    private static readonly Color HeaderColour = new("808000");
    private static readonly Color ChapterColour = new("ff8040");
    private static readonly Color LabelColour = new("c0c0c0");
    private static readonly Color RefusalColour = new("ff3b3b");

    private static readonly int[] RowTops = { 128, 153, 178, 204, 228 };
    private static readonly int[] ChapterTops = { 333, 365, 398, 430, 462 };

    private readonly IWorldContext context;
    private readonly BifrostSession bifrost;
    private readonly SelfSession self;
    private readonly IBifrostRequests bifrostRequests;
    internal CanvasLayer Layer { get; set; } = null!;
    private HudWindow? window;
    private readonly Label[] names = new Label[DrakiWire.TopRows];
    private readonly Label[] times = new Label[DrakiWire.TopRows];
    private readonly Label[] stages = new Label[DrakiWire.TopRows];
    private readonly Button[] enterButtons = new Button[DrakiTower.Chapters];
    private Label myRank = null!;
    private Label myName = null!;
    private Label myTime = null!;
    private Label myStage = null!;
    private Label entrances = null!;
    private DrakiRankList? list;

    public DrakiBoardFeature(IWorldContext context, BifrostSession bifrost, SelfSession self, IBifrostRequests bifrostRequests)
    {
        this.context = context;
        this.bifrost = bifrost;
        this.self = self;
        this.bifrostRequests = bifrostRequests;
    }

    public bool IsOpen => window is { Visible: true };

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(Layer);
        bifrost.DrakiRankListEvent += OnRankList;
        bifrost.DrakiEnterResultEvent += OnEnterResult;
    }

    public void Dispose()
    {
        bifrost.DrakiRankListEvent -= OnRankList;
        bifrost.DrakiEnterResultEvent -= OnEnterResult;
    }

    internal void Request() => bifrostRequests.DrakiRankList();

    public void Close()
    {
        if (window != null) window.Visible = false;
    }

    private void OnRankList(DrakiRankList rankList) => ShowRanks(rankList);

    internal HudWindow ShowRanks(DrakiRankList rankList)
    {
        list = rankList;
        window ??= BuildBoard();
        Render(rankList);
        window.Visible = true;
        return window;
    }

    private void OnEnterResult(DrakiEnterResult result)
    {
        if (DrakiTower.EnterText(result) is not { } text)
        {
            if (window != null) window.Visible = true;
            return;
        }
        string message = GameText.System(text.Id, text.Fallback);
        if (message.Length == 0) return;
        context.Log.Notice(message, RefusalColour);
        Notice.Show(context.Root, message, Title);
    }

    private void Enter(int chapter)
    {
        if (list is { EntrancesLeft: > 0 })
        {
            bifrostRequests.DrakiEnter(DrakiTower.NoCertificate, chapter);
            Close();
            return;
        }
        if (!HasCertificate())
        {
            Notice.Show(context.Root, GameText.System(DrakiTower.NoCertificateText, DrakiTower.NoCertificateFallback), Title);
            return;
        }
        Notice.Confirm(context.Root, GameText.System(DrakiTower.UseCertificateText, DrakiTower.UseCertificateFallback), "Yes", "No", () =>
        {
            bifrostRequests.DrakiEnter(DrakiTower.CertificateItem, chapter);
            Close();
        }, title: Title);
    }

    private bool HasCertificate() => self.Inventory.CountInBackpack(DrakiTower.CertificateItem) > 0;

    private void Render(DrakiRankList rankList)
    {
        for (int i = 0; i < DrakiWire.TopRows; i++)
        {
            var row = i < rankList.Top.Count ? rankList.Top[i] : default;
            bool filled = !string.IsNullOrEmpty(row.Name);
            names[i].Text = filled ? row.Name : "";
            times[i].Text = filled ? DrakiTower.FinishTime(row.FinishSeconds) : "";
            stages[i].Text = filled ? row.Stage.ToString() : "";
        }
        var mine = rankList.Mine;
        myRank.Text = DrakiTower.Ranked(mine.Rank) ? mine.Rank.ToString() : GameText.System(DrakiTower.OffrankedText, DrakiTower.OffrankedFallback);
        myName.Text = mine.Name ?? "";
        myTime.Text = DrakiTower.Ranked(mine.Rank) ? DrakiTower.FinishTime(mine.FinishSeconds) : "";
        myStage.Text = DrakiTower.Ranked(mine.Rank) ? mine.Stage.ToString() : "";
        entrances.Text = rankList.EntrancesLeft.ToString();
        for (int i = 0; i < DrakiTower.Chapters; i++)
        {
            bool open = DrakiTower.SectorOpen(i + 1, rankList.MaxStage);
            enterButtons[i].Disabled = !open;
            enterButtons[i].Text = open ? EnterLabel : ClosedSectorLabel;
        }
    }

    private HudWindow BuildBoard()
    {
        var board = new HudWindow("draki_board", Title, new Vector2(320, 90)) { Visible = false };
        board.Closed += Close;
        Layer.AddChild(board);
        var canvas = new Control { CustomMinimumSize = Canvas, MouseFilter = Control.MouseFilterEnum.Pass };
        board.Body.AddChild(canvas);

        var heading = Label(canvas, RankHeading, new Rect2(126, 70, 162, 17), LabelColour);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        heading.AddThemeFontSizeOverride("font_size", HeadingSize);

        var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        Place(canvas, frame, new Rect2(41, 93, 335, 180));
        var columnRule = UiTheme.Rule(vertical: true);
        columnRule.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(canvas, columnRule, new Rect2(305, 95, 7, 150));

        Label(canvas, "Rank", new Rect2(48, 103, 39, 13), HeaderColour);
        Label(canvas, "Character", new Rect2(103, 102, 116, 13), HeaderColour);
        Label(canvas, "Time", new Rect2(239, 103, 62, 13), HeaderColour);
        Label(canvas, "Stage", new Rect2(314, 103, 54, 13), HeaderColour);

        for (int i = 0; i < DrakiWire.TopRows; i++)
        {
            int top = RowTops[i];
            Label(canvas, (i + 1).ToString(), new Rect2(49, top, 37, 17), UiTheme.TextHi);
            names[i] = Label(canvas, "", new Rect2(98, top, 123, 16), UiTheme.TextHi);
            times[i] = Label(canvas, "", new Rect2(233, top, 73, 16), UiTheme.TextHi);
            stages[i] = Label(canvas, "", new Rect2(321, top, 39, 16), UiTheme.TextHi);
        }

        var separator = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(canvas, separator, new Rect2(44, 245, 327, 4));
        myRank = Label(canvas, "", new Rect2(49, 250, 37, 17), UiTheme.TextHi);
        myName = Label(canvas, "", new Rect2(98, 252, 123, 16), UiTheme.TextHi);
        myTime = Label(canvas, "", new Rect2(233, 252, 73, 16), UiTheme.TextHi);
        myStage = Label(canvas, "", new Rect2(321, 252, 39, 16), UiTheme.TextHi);

        var entranceLabel = Label(canvas, EntranceLabel, new Rect2(163, 275, 185, 13), LabelColour);
        entranceLabel.HorizontalAlignment = HorizontalAlignment.Right;
        entrances = Label(canvas, "", new Rect2(352, 275, 33, 13), LabelColour);

        for (int i = 0; i < DrakiTower.Chapters; i++)
        {
            int top = ChapterTops[i];
            int chapter = i + 1;
            var row = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            Place(canvas, row, new Rect2(28, top, 292, 31));
            var line = Label(canvas, DrakiTower.ChapterLines[i], new Rect2(36, top, 284, 31), ChapterColour);
            line.ClipText = false;
            var enter = enterButtons[i] = UiTheme.SmallButton(EnterLabel, DrakiTower.ChapterLines[i]);
            enter.Pressed += () => Enter(chapter);
            Place(canvas, enter, new Rect2(329, top + 2, 60, 26));
        }
        return board;
    }

    private static Label Label(Control canvas, string text, Rect2 reference, Color colour)
    {
        var label = UiTheme.Text(text, TextSize, colour);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(canvas, label, reference);
        return label;
    }

    private static void Place(Control canvas, Control child, Rect2 reference)
    {
        canvas.AddChild(child);
        child.Position = reference.Position - Origin;
        child.Size = reference.Size;
        child.CustomMinimumSize = reference.Size;
    }
}
