using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int SiegeLayerIndex = 75;
    private const int SiegeGuardWidth = 360;
    private const float SiegeSpeechHeight = 64f;
    private const int SiegeTableInset = 16;
    private const string CastleGuardTitle = "Castle Guard";
    private const string GuardGreeting = "I'm the guard of Delos Castle Arendil !!";
    private const string GuardQuestion = "What is it that you want?";
    private const string ChallengerLabel = "Challenger";
    private const string ScheduleLabel = "Castle Siege War Schedule";
    private const string AssaultLabel = "Assault";
    private const string WalkAwayLabel = "Walk away";

    private CanvasLayer _siegeLayer = null!;
    private HudWindow _siegeGuardPanel = null!;
    private bool _siegeGuardShown;

    private void SiegeInit()
    {
        BuildSiegeWindows();

        Net.I.SiegeGuardEvent += OpenSiegeGuard;
        Net.I.SiegeScheduleEvent += OnSiegeSchedule;
        Net.I.SiegeApplyEvent += OnSiegeApply;
        Net.I.SiegeChallengersEvent += OnSiegeChallengers;
        Net.I.SiegeDefendersEvent += OnSiegeDefenders;
        Net.I.SiegeOfficeEvent += OpenSiegeOffice;
        Net.I.SiegeCollectedEvent += OnSiegeCollected;
        Net.I.SiegeTaxRatesEvent += OnSiegeTaxRates;
        Net.I.SiegeRateChangedEvent += OnSiegeRateChanged;
    }

    private void SiegeDispose()
    {
        Net.I.SiegeGuardEvent -= OpenSiegeGuard;
        Net.I.SiegeScheduleEvent -= OnSiegeSchedule;
        Net.I.SiegeApplyEvent -= OnSiegeApply;
        Net.I.SiegeChallengersEvent -= OnSiegeChallengers;
        Net.I.SiegeDefendersEvent -= OnSiegeDefenders;
        Net.I.SiegeOfficeEvent -= OpenSiegeOffice;
        Net.I.SiegeCollectedEvent -= OnSiegeCollected;
        Net.I.SiegeTaxRatesEvent -= OnSiegeTaxRates;
        Net.I.SiegeRateChangedEvent -= OnSiegeRateChanged;
    }

    private void BuildSiegeWindows()
    {
        _siegeLayer = new CanvasLayer { Layer = SiegeLayerIndex };
        AddChild(_siegeLayer);
        BuildSiegeGuard();
        BuildSiegeSchedule();
        BuildSiegeChallengers();
        BuildSiegeDefenders();
        BuildSiegeOffice();
        BuildSiegeTaxList();
        BuildSiegeTaxRate();
    }

    private static string SiegeTitle => KingText(SiegeWarfare.CastleWarText, "Castle Siege War");

    private void SiegeMessage(int textId, params object[] args)
    {
        if (textId != 0) Notice.Show(this, KingFill(textId, "", args), SiegeTitle);
    }

    private void BuildSiegeGuard()
    {
        _siegeGuardPanel = ServiceWindow(_siegeLayer, "siegeguard", CastleGuardTitle, SiegeGuardWidth, CloseSiegeGuard);
        var body = _siegeGuardPanel.Body;
        body.AddChild(NpcSpeech(SiegeSpeechHeight, out var greeting, out var question));
        SetSpeech(greeting, GuardGreeting);
        SetSpeech(question, GuardQuestion);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        options.AddChild(NpcOption(ChallengerLabel, AskSiegeChallengers));
        options.AddChild(NpcOption(ScheduleLabel, () => Net.I.SendSiegeSchedule()));
        options.AddChild(NpcOption(AssaultLabel, () => Net.I.SendSiegeAssault()));
        options.AddChild(NpcOption(WalkAwayLabel, CloseSiegeGuard));
    }

    private void OpenSiegeGuard()
    {
        _siegeGuardPanel.Title = NpcWindowTitle(CastleGuardTitle);
        _siegeGuardShown = true;
        _siegeGuardPanel.Visible = true;
    }

    private void CloseSiegeGuard()
    {
        _siegeGuardShown = false;
        _siegeGuardPanel.Visible = false;
    }

    private void AskSiegeChallengers()
    {
        CloseSiegeGuard();
        Net.I.SendSiegeChallengers();
    }

    private static Label SiegeCell(string text, float width, Color colour, bool expand = false)
    {
        var cell = UiTheme.Text(text, 13, colour);
        cell.VerticalAlignment = VerticalAlignment.Center;
        cell.CustomMinimumSize = new Vector2(width, 0);
        cell.ClipText = true;
        if (expand) cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return cell;
    }

    private static VBoxContainer SiegeTable(VBoxContainer body, float height, (string Title, float Width)[] columns)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        var headPad = new MarginContainer();
        UiTheme.Margins(headPad, SiegeTableInset, 0, SiegeTableInset, 0);
        headPad.AddChild(head);
        body.AddChild(headPad);
        for (int i = 0; i < columns.Length; i++)
        {
            var title = UiTheme.SectionTitle(columns[i].Title);
            title.CustomMinimumSize = new Vector2(columns[i].Width, 0);
            if (i == 0) title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(title);
        }

        var frame = UiTheme.Section();
        body.AddChild(frame);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, height),
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        frame.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(rows);
        return rows;
    }

    private static void ClearSiegeRows(VBoxContainer rows)
    {
        foreach (var child in rows.GetChildren())
        {
            rows.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static PanelContainer SiegeRow(params Label[] cells)
    {
        var panel = UiTheme.RowPanel();
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);
        foreach (var cell in cells) line.AddChild(cell);
        return panel;
    }
}
