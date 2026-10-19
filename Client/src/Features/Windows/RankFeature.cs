using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class RankFeature : IFeature, ITickFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly RankSession rank;
    private readonly SelfSession self;
    private readonly IRankRequests requests;

    public RankFeature(IWorldContext context, RankSession rank, SelfSession self, IRankRequests requests)
    {
        this.context = context;
        this.rank = rank;
        this.self = self;
        this.requests = requests;
    }

    private const int LadderWidth = 34;
    private const int LadderMarkSize = 20;
    private const int LadderMarkWidth = 36;
    private const int LadderNameWidth = 120;
    private const int LadderClanWidth = 120;
    private const int LadderPointWidth = 120;
    private const int LadderFontSize = 12;
    private static readonly Color LadderBonusColour = new("ff6a6a");

    private sealed class LadderRow
    {
        public Label Rank = null!, Name = null!, Clan = null!, Points = null!, Bonus = null!;
        public TextureRect Mark = null!;
    }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private readonly LadderRow[] ladderKarus = new LadderRow[PkLadder.RowsShown];
    private readonly LadderRow[] ladderElMorad = new LadderRow[PkLadder.RowsShown];
    private LadderRow ladderOwnKarus = null!;
    private LadderRow ladderOwnElMorad = null!;
    private bool shown, held, ladderRequested;

    public bool IsOpen => shown;

    private double ladderReceivedAt;
    internal PkLadderReply? Ladder { get; set; }

    public void Tick(double delta, double nowSec) => PkLadderTick();

    public void Build()
    {
        BuildPanel();
        rank.RankEvent += OnData;
    }

    public void Dispose()
    {
        rank.RankEvent -= OnData;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("rank", ItemData.Text(PkLadder.TitleText, "Today's Rank")) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var halves = new HBoxContainer();
        halves.AddThemeConstantOverride("separation", 18);
        Panel.Body.AddChild(halves);
        halves.AddChild(BuildLadderSide("Karus", ladderKarus, out ladderOwnKarus));
        halves.AddChild(BuildLadderSide("El Morad", ladderElMorad, out ladderOwnElMorad));

        var notice = UiTheme.Text(PkLadder.Notice, LadderFontSize, UiTheme.TextLo, HorizontalAlignment.Center);
        Panel.Body.AddChild(notice);
    }

    private static Control BuildLadderSide(string nation, LadderRow[] rows, out LadderRow own)
    {
        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 3);
        side.AddChild(UiTheme.SectionTitle(nation));

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        header.AddChild(LadderCell("Rank", LadderWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(LadderCell("Mark", LadderMarkWidth, UiTheme.TextLo, HorizontalAlignment.Center));
        header.AddChild(LadderCell("Name", LadderNameWidth, UiTheme.TextLo));
        header.AddChild(LadderCell("Clan", LadderClanWidth, UiTheme.TextLo));
        header.AddChild(LadderCell("Point", LadderPointWidth, UiTheme.TextLo, HorizontalAlignment.Right));
        side.AddChild(header);

        for (int i = 0; i < rows.Length; i++)
            rows[i] = AddLadderRow(side);
        side.AddChild(new HSeparator());
        own = AddLadderRow(side);
        return side;
    }

    private static LadderRow AddLadderRow(VBoxContainer side)
    {
        var line = new HBoxContainer { CustomMinimumSize = new Vector2(0, LadderMarkSize + 2) };
        line.AddThemeConstantOverride("separation", 6);
        var row = new LadderRow
        {
            Rank = LadderCell("", LadderWidth, UiTheme.TextHi, HorizontalAlignment.Center),
            Mark = new TextureRect
            {
                CustomMinimumSize = new Vector2(LadderMarkWidth, LadderMarkSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            },
            Name = LadderCell("", LadderNameWidth, UiTheme.TextHi),
            Clan = LadderCell("", LadderClanWidth, UiTheme.TextLo),
        };
        var points = new HBoxContainer { CustomMinimumSize = new Vector2(LadderPointWidth, 0), Alignment = BoxContainer.AlignmentMode.End };
        points.AddThemeConstantOverride("separation", 4);
        row.Points = UiTheme.Text("", LadderFontSize, UiTheme.Gold);
        row.Bonus = UiTheme.Text("", LadderFontSize, LadderBonusColour);
        points.AddChild(row.Points);
        points.AddChild(row.Bonus);

        line.AddChild(row.Rank);
        line.AddChild(row.Mark);
        line.AddChild(row.Name);
        line.AddChild(row.Clan);
        line.AddChild(points);
        side.AddChild(line);
        return row;
    }

    private static Label LadderCell(string text, int width, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var lbl = UiTheme.Text(text, LadderFontSize, color, align);
        lbl.CustomMinimumSize = new Vector2(width, 0);
        lbl.ClipText = true;
        return lbl;
    }

    internal void PkLadderTick()
    {
        bool typing = context.Root.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
        if (!PkLadder.IsLadderZone(context.Zone) || typing || !KeyBinds.Held(KeyAction.PkLadder))
        {
            if (held) Close();
            held = false;
            return;
        }
        held = true;
        if (shown) return;
        if (PkLadder.IsFresh(ladderReceivedAt, context.Now))
        {
            UiKit.ShowOnTop(Panel);
            shown = true;
            return;
        }
        if (ladderRequested) return;
        ladderRequested = true;
        requests.RankRequest(RankType.PkZone);
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
    }

    private void OnData(PkLadderReply reply)
    {
        Ladder = reply;
        ladderReceivedAt = context.Now;
        ladderRequested = false;
        RenderLadder();
    }

    internal void RenderLadder()
    {
        if (this.Ladder is not { } ladder) return;
        FillLadderSide(ladderKarus, ladder.Karus);
        FillLadderSide(ladderElMorad, ladder.ElMorad);

        var me = self.LastEnter;
        bool karus = me.Nation == Nations.Karus;
        ClearLadderRow(karus ? ladderOwnElMorad : ladderOwnKarus);
        FillLadderRow(karus ? ladderOwnKarus : ladderOwnElMorad, PkLadder.Place(ladder.MyRank),
            RankSymbol.PlatePath(me.KnightsRank, me.PersonalRank), me.Name, me.ClanName, ladder.MyPoints, ladder.MyBonus);
    }

    private static void FillLadderSide(LadderRow[] rows, System.Collections.Generic.IReadOnlyList<RankEntry> entries)
    {
        for (int i = 0; i < rows.Length; i++)
        {
            if (i >= entries.Count) { ClearLadderRow(rows[i]); continue; }
            var e = entries[i];
            FillLadderRow(rows[i], PkLadder.Place(e.Place), RankSymbol.TexturePath(e.Symbol), e.Name, e.ClanName, e.Loyalty, e.Bonus);
        }
    }

    private static void FillLadderRow(LadderRow row, string place, string? mark, string name, string clan, int points, int bonus)
    {
        row.Rank.Text = place;
        row.Mark.Texture = mark != null ? GD.Load<Texture2D>(mark) : null;
        row.Name.Text = name;
        row.Clan.Text = clan;
        row.Points.Text = PkLadder.Points(points);
        row.Bonus.Text = PkLadder.Bonus(bonus);
        row.Bonus.Visible = row.Bonus.Text.Length > 0;
    }

    private static void ClearLadderRow(LadderRow row)
    {
        row.Rank.Text = row.Name.Text = row.Clan.Text = row.Points.Text = row.Bonus.Text = "";
        row.Mark.Texture = null;
    }
}
