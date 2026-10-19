using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class RankBoardFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly BattleEventSession battleEvent;
    private readonly SelfSession self;
    private readonly RankFeature rank;
    private readonly ClanMarksFeature clanMarks;

    public RankBoardFeature(IWorldContext context, BattleEventSession battleEvent, SelfSession self, RankFeature rank, ClanMarksFeature clanMarks)
    {
        this.context = context;
        this.battleEvent = battleEvent;
        this.self = self;
        this.rank = rank;
        this.clanMarks = clanMarks;
    }

    private const string KarusTitle = "Karus Board";
    private const string ElMoradTitle = "El Morad Board";

    private const string Footer =
    ">>>The representatives on the list of selection criteria changes at 7am the day after a lunar war.<<<";

    private const string EmptyCell = "-";
    private const int Font = 12;
    private const int HeadFont = 11;
    private const float RankWidth = 34f;
    private const float MarkWidth = 66f;
    private const float NameWidth = 150f;
    private const float ClanWidth = 150f;
    private const float WorkWidth = 120f;
    private const float MarkSize = 18f;
    private const float RowHeight = 24f;
    private static readonly Color HeadColour = new("ff9d3c");
    private static readonly Color TitleColour = new("f7eea4");
    private static readonly Color FirstColour = new("80ffff");
    private static readonly Color ClanColour = new("b6e0d9");
    private static readonly Color FooterColour = new(1f, 1f, 0f);

    private sealed class Cells
    {
        public Label Rank = null!;
        public TextureRect Mark = null!;
        public Label Name = null!;
        public Label Clan = null!;
        public Label? Work;
    }

    private CanvasLayer? layer;

    internal HudWindow BoardWindow { get; private set; } = null!;

    private Label titleLabel = null!;
    private Cells king = null!;
    private readonly Cells[] clans = new Cells[RankBoard.Rows];
    private readonly Cells[] players = new Cells[RankBoard.Rows];
    private readonly Cells[] commanders = new Cells[RankBoard.HallRows];
    private readonly Cells[] killers = new Cells[RankBoard.HallRows];
    internal List<RankBoardRow> Rows { get; set; } = new();

    public void Build()
    {
        battleEvent.RankBoardEvent += On;
        clanMarks.MarkChanged += OnMarkChanged;
    }

    private void OnMarkChanged(int clanId) => RefreshMarks();

    public void Dispose()
    {
        battleEvent.RankBoardEvent -= On;
        clanMarks.MarkChanged -= OnMarkChanged;
        layer = null;
    }

    private void On(List<RankBoardRow> rows)
    {
        if (rows.Count == 0)
        {
            Notice.Show(context.Root, ItemData.Text(RankBoard.EmptyBoardText, "No content in current board"));
            return;
        }
        context.NpcDialog.Close();
        if (layer == null || !GodotObject.IsInstanceValid(layer)) BuildRankBoard();
        this.Rows = rows;
        Fill();
        layer!.Visible = true;
        BoardWindow.Visible = true;
    }

    internal void Fill()
    {
        titleLabel.Text = self.LastEnter.Nation == Nations.Karus ? KarusTitle : ElMoradTitle;
        FillCells(king, RankBoard.Column(Rows, RankBoardKind.King, 1)[0], -1);
        FillColumn(clans, RankBoardKind.Clan);
        FillColumn(players, RankBoardKind.Player);
        FillColumn(commanders, RankBoardKind.CommanderKiller);
        FillColumn(killers, RankBoardKind.TopKiller);
    }

    private void FillColumn(Cells[] cells, RankBoardKind kind)
    {
        var column = RankBoard.Column(Rows, kind, cells.Length);
        for (int i = 0; i < cells.Length; i++) FillCells(cells[i], column[i], i);
    }

    private void FillCells(Cells cells, RankBoardRow? row, int index)
    {
        cells.Name.Text = row?.Name is { Length: > 0 } name ? name : EmptyCell;
        cells.Clan.Text = row is { HasClan: true } ? row.Value.ClanName : "";
        var mark = row is { HasClan: true } ? clanMarks.Mark(row.Value.ClanId, row.Value.MarkVersion) : null;
        cells.Mark.Texture = mark;
        if (cells.Work != null)
            cells.Work.Text = row is { Kind: RankBoardKind.TopKiller, Tail: > 0 } killer ? $"{killer.Tail:n0}" : EmptyCell;
        if (index >= 0) cells.Rank.Text = $"{index + 1}";
    }

    private void RefreshMarks()
    {
        if (layer != null && GodotObject.IsInstanceValid(layer) && BoardWindow.Visible) Fill();
    }

    internal void BuildRankBoard()
    {
        layer = new CanvasLayer { Layer = UiLayers.Boards };
        context.Root.AddChild(layer);
        BoardWindow = new HudWindow("rank_board", "Rank Board") { Visible = false };
        BoardWindow.Closed += () => BoardWindow.Visible = false;
        layer.AddChild(BoardWindow);
        var body = BoardWindow.Body;
        body.AddThemeConstantOverride("separation", 10);

        titleLabel = Label("", Font + 2, TitleColour, HorizontalAlignment.Center);
        body.AddChild(titleLabel);

        var kingRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        kingRow.AddThemeConstantOverride("separation", 8);
        var kingLabel = Label("King", Font, HeadColour, HorizontalAlignment.Right);
        kingLabel.CustomMinimumSize = new Vector2(NameWidth / 2f, 0);
        kingRow.AddChild(kingLabel);
        king = RowCells(kingRow, withRank: false, withWork: false);
        body.AddChild(Section(kingRow));

        var ladders = new HBoxContainer();
        ladders.AddThemeConstantOverride("separation", 12);
        ladders.AddChild(Table("Clan Ranking", "Clan Master", clans, withWork: false));
        ladders.AddChild(Table("Ladder Ranking", "Character name", players, withWork: false));
        body.AddChild(ladders);

        var halls = new HBoxContainer();
        halls.AddThemeConstantOverride("separation", 12);
        halls.AddChild(Table("War Hall of fame · Hero who killed an enemy commander", "Character name", commanders, withWork: true));
        halls.AddChild(Table("War Hall of fame · Hero who has killed the most", "Character name", killers, withWork: true));
        body.AddChild(halls);

        var footer = Label(Footer, HeadFont, FooterColour, HorizontalAlignment.Center);
        body.AddChild(footer);
    }

    private Control Table(string title, string nameHead, Cells[] cells, bool withWork)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(Label(title, Font, TitleColour, HorizontalAlignment.Left));

        var head = new HBoxContainer();
        head.AddChild(HeadCell("Rank", RankWidth));
        head.AddChild(HeadCell("Clan mark", MarkWidth));
        head.AddChild(HeadCell(nameHead, NameWidth, stretch: true));
        head.AddChild(HeadCell("Clan name", ClanWidth, stretch: true));
        if (withWork) head.AddChild(HeadCell("Hero achievements", WorkWidth));
        box.AddChild(head);

        for (int i = 0; i < cells.Length; i++)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
            cells[i] = RowCells(row, withRank: true, withWork: withWork);
            if (i == 0) cells[i].Rank.AddThemeColorOverride("font_color", FirstColour);
            box.AddChild(row);
        }
        return Section(box);
    }

    private Cells RowCells(HBoxContainer row, bool withRank, bool withWork)
    {
        var cells = new Cells
        {
            Rank = Label("", Font, UiTheme.TextHi, HorizontalAlignment.Center),
            Mark = new TextureRect
            {
                CustomMinimumSize = new Vector2(MarkWidth, MarkSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            },
            Name = Label(EmptyCell, Font, UiTheme.TextHi, HorizontalAlignment.Center),
            Clan = Label("", Font, ClanColour, HorizontalAlignment.Center),
        };
        cells.Rank.CustomMinimumSize = new Vector2(RankWidth, 0);
        cells.Name.CustomMinimumSize = new Vector2(NameWidth, 0);
        cells.Clan.CustomMinimumSize = new Vector2(ClanWidth, 0);
        cells.Name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cells.Clan.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (withRank) row.AddChild(cells.Rank);
        row.AddChild(cells.Mark);
        row.AddChild(cells.Name);
        row.AddChild(cells.Clan);
        if (withWork)
        {
            cells.Work = Label(EmptyCell, HeadFont, UiTheme.TextHi, HorizontalAlignment.Center);
            cells.Work.CustomMinimumSize = new Vector2(WorkWidth, 0);
            row.AddChild(cells.Work);
        }
        return cells;
    }

    private static PanelContainer Section(Control content)
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        section.AddChild(content);
        return section;
    }

    private static Label HeadCell(string text, float width, bool stretch = false)
    {
        var label = Label(text, HeadFont, HeadColour, HorizontalAlignment.Center);
        label.CustomMinimumSize = new Vector2(width, 0);
        if (stretch) label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    private static Label Label(string text, int size, Color colour, HorizontalAlignment align)
    {
        var label = UiTheme.Text(text, size, colour, align);
        label.ClipText = true;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }
}
