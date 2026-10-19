using Godot;

namespace LibreKO.Features.Hud;

internal sealed class NereidFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly BattleEventSession battleEvent;
    private readonly SelfSession self;
    private readonly BattleEventFeature battleEventWindow;
    private readonly MapFeature map;

    public NereidFeature(IWorldContext context, BattleEventSession battleEvent, SelfSession self, BattleEventFeature battleEventWindow, MapFeature map)
    {
        this.context = context;
        this.battleEvent = battleEvent;
        this.self = self;
        this.battleEventWindow = battleEventWindow;
        this.map = map;
    }

    private const float MonumentBlipRadius = 7f;
    internal const float StripHeight = 22f;
    internal const float BoardWidth = 260f;
    private const float BoardTop = 60f;
    private const float BoardBottom = 70f;
    private static readonly Color MonumentFree = new(0.62f, 0.6f, 0.55f);
    private static readonly Color MonumentKarus = new(0.82f, 0.2f, 0.18f);
    private static readonly Color MonumentElMorad = new(0.2f, 0.45f, 0.9f);
    private static readonly Color LineColour = new(1f, 1f, 0f);
    private static readonly Color ContColour = new("9b9bff");

    internal Label Strip { get; set; } = null!;
    internal Label FlagBoard { get; set; } = null!;
    internal Label ScoreBoard { get; set; } = null!;
    private int karusPoints;
    private int elmoPoints;
    private bool pointsKnown;

    internal bool In => context.Zone == NereidMonuments.Zone;

    public void Build()
    {
        map.AddMiniSource(AddMonumentBlips);
        Strip = UiTheme.Text("", 12, UiTheme.TextHi, HorizontalAlignment.Center);
        Strip.AddThemeStyleboxOverride("normal", UiTheme.MeterTrack());
        Strip.Visible = false;
        if (map.MiniMap != null)
        {
            map.MiniMap.AddChild(Strip);
            Strip.Position = new Vector2(0f, MiniMap.MapSize - StripHeight);
            Strip.Size = new Vector2(MiniMap.MapSize, StripHeight);
        }

        FlagBoard = Board(Control.LayoutPreset.CenterTop, BoardTop);
        ScoreBoard = Board(Control.LayoutPreset.CenterBottom, -BoardBottom);
        battleEvent.MapEventEvent += OnMapEvent;
        Refresh();
    }

    public void Dispose() => battleEvent.MapEventEvent -= OnMapEvent;

    internal Label Board(Control.LayoutPreset preset, float offset)
    {
        var board = UiTheme.Text("", 15, UiTheme.GoldBright, HorizontalAlignment.Center);
        board.AddThemeStyleboxOverride("normal", UiTheme.MeterTrack());
        board.SetAnchorsPreset(preset);
        board.OffsetLeft = -BoardWidth * 0.5f;
        board.OffsetRight = BoardWidth * 0.5f;
        board.OffsetTop = offset;
        board.OffsetBottom = offset + StripHeight;
        board.MouseFilter = Control.MouseFilterEnum.Ignore;
        board.Visible = false;
        map.FullLayer.AddChild(board);
        return board;
    }

    private void OnMapEvent(MapEventReply reply)
    {
        switch (reply.Kind)
        {
            case MapEventKind.Captured:
                if (!NereidMonuments.Has(reply.Index)) break;
                var name = NereidMonuments.Name(reply.Index);
                int text = NereidMonuments.CaptureText(reply.Nation, self.Nation);
                context.Log.Notice(ItemData.Text(text, text == NereidMonuments.OccupiedText
                        ? "You have occupied %s." : "%s has been occupied by the opponent country.")
                    .Replace("%s", ItemData.Text(name.Id, name.Fallback)), LineColour);
                break;
            case MapEventKind.Points:
                karusPoints = (int)reply.A;
                elmoPoints = (int)reply.B;
                pointsKnown = true;
                break;
            case MapEventKind.Provisions:
                var nation = NereidMonuments.NationName(reply.Nation);
                context.Log.Notice(ItemData.Text(NereidMonuments.ProvisionsText, "%s has secured provisions.")
                    .Replace("%s", ItemData.Text(nation.Id, nation.Fallback)), LineColour);
                if (reply.A != 0)
                    context.Log.Notice(TextTemplate.Fill(ItemData.Text(NereidMonuments.ProvisionPointsText, "(+ %d occupation point acquired)"), reply.A),
                        LineColour);
                break;
            case MapEventKind.Cont:
                context.Log.Notice(ItemData.Text(NereidMonuments.ContText, "Acquired %u Nereidth occupation CONT")
                    .Replace("%u", battleEvent.NereidContGain.ToString()), ContColour);
                break;
            case MapEventKind.Result:
                var owners = battleEvent.MonumentOwners;
                var winner = NereidMonuments.NationName(reply.Nation);
                string result = TextTemplate.Fill(
                    ItemData.Text(NereidMonuments.ResultText, "*Called* Karus %d: El Morad %d       %s has succeeded in occupying Nereidth."),
                    NereidMonuments.Held(owners, Nations.Karus), NereidMonuments.Held(owners, Nations.ElMorad),
                    ItemData.Text(winner.Id, winner.Fallback));
                battleEventWindow.ShowBanner(result);
                break;
        }
        Refresh();
    }

    private void Refresh()
    {
        var owners = battleEvent.MonumentOwners;
        bool shown = In && owners.Count > 0;
        int karus = NereidMonuments.Held(owners, Nations.Karus);
        int elmo = NereidMonuments.Held(owners, Nations.ElMorad);
        Strip.Visible = shown;
        Strip.Text = $"El Morad  {elmo} : {karus}  Karus";
        FlagBoard.Visible = shown;
        FlagBoard.Text = $"Total Point   Elmo: {elmo}   Karu: {karus}";
        ScoreBoard.Visible = shown && pointsKnown;
        ScoreBoard.Text = $"TotalPoint   Elmo: {elmoPoints}   Karu: {karusPoints}";
    }

    private void AddMonumentBlips(List<MiniMap.Blip> blips)
    {
        if (!In) return;
        var owners = battleEvent.MonumentOwners;
        for (int i = 0; i < owners.Count && NereidMonuments.Has(i); i++)
        {
            var (x, z) = NereidMonuments.Spot(i);
            var colour = owners[i] switch
            {
                Nations.Karus => MonumentKarus,
                Nations.ElMorad => MonumentElMorad,
                _ => MonumentFree,
            };
            blips.Add(new MiniMap.Blip(x, z, colour, MonumentBlipRadius, false));
        }
    }
}
