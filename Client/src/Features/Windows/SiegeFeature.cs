using Godot;

namespace LibreKO.Features.Windows;

internal sealed class SiegeFeature : IFeature, ITickFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly NpcQuestSession npcQuest;
    private readonly SiegeSession siege;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly ISiegeRequests requests;
    private readonly TopIconsFeature topIcons;
    private readonly IInteractionDialogs interactionDialogs;

    public SiegeFeature(IWorldContext context, ClanSession clan, NpcQuestSession npcQuest, SiegeSession siege, ZoneAbilitySession zoneAbility, ISiegeRequests requests, TopIconsFeature topIcons, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.clan = clan;
        this.npcQuest = npcQuest;
        this.siege = siege;
        this.zoneAbility = zoneAbility;
        this.requests = requests;
        this.topIcons = topIcons;
        this.interactionDialogs = interactionDialogs;
    }
    private const int GuardWidth = 360;
    private const float SpeechHeight = 64f;
    private const int TableInset = 16;
    private const string CastleGuardTitle = "Castle Guard";
    private const string GuardGreeting = "I'm the guard of Delos Castle Arendil !!";
    private const string GuardQuestion = "What is it that you want?";
    private const string ChallengerLabel = "Challenger";
    private const string ScheduleLabel = "Castle Siege War Schedule";
    private const string AssaultLabel = "Assault";
    private const string WalkAwayLabel = "Walk away";

    private CanvasLayer layer = null!;
    internal HudWindow GuardPanel { get; private set; } = null!;
    private bool guardShown;

    public void Build()
    {
        foreach (var panel in Panels) interactionDialogs.InteractionCloses(panel);
        BuildWindows();

        siege.SiegeGuardEvent += OpenGuard;
        siege.SiegeScheduleEvent += OnSchedule;
        siege.SiegeApplyEvent += OnApply;
        siege.SiegeChallengersEvent += OnChallengers;
        siege.SiegeDefendersEvent += OnDefenders;
        siege.SiegeOfficeEvent += OpenOffice;
        siege.SiegeCollectedEvent += OnCollected;
        siege.SiegeTaxRatesEvent += OnTaxRates;
        siege.SiegeRateChangedEvent += OnRateChanged;
        WarInit();
    }

    private IClosableWindow[]? panels;

    private IReadOnlyList<IClosableWindow> Panels => panels ??=
    [
        new ClosablePanel(() => taxRateShown, CloseTaxRate),
        new ClosablePanel(() => taxListShown, CloseTaxList),
        new ClosablePanel(() => officeShown, CloseOffice),
        new ClosablePanel(() => defendersShown, CloseDefenders),
        new ClosablePanel(() => challengersShown, CloseChallengers),
        new ClosablePanel(() => scheduleShown, CloseSchedule),
        new ClosablePanel(() => guardShown, CloseGuard),
    ];

    bool IEscapable.CloseOnEscape() => EscapeOrder.CloseFirst(Panels);

    public void Dispose()
    {
        WarDispose();
        siege.SiegeGuardEvent -= OpenGuard;
        siege.SiegeScheduleEvent -= OnSchedule;
        siege.SiegeApplyEvent -= OnApply;
        siege.SiegeChallengersEvent -= OnChallengers;
        siege.SiegeDefendersEvent -= OnDefenders;
        siege.SiegeOfficeEvent -= OpenOffice;
        siege.SiegeCollectedEvent -= OnCollected;
        siege.SiegeTaxRatesEvent -= OnTaxRates;
        siege.SiegeRateChangedEvent -= OnRateChanged;
    }

    internal void BuildWindows()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);
        BuildGuard();
        BuildSchedule();
        BuildChallengers();
        BuildDefenders();
        BuildOffice();
        BuildTaxList();
        BuildTaxRate();
    }

    private static string SiegeTitle => NpcServiceKit.Text(SiegeWarfare.CastleWarText, "Castle Siege War");

    private void Message(int textId, params object[] args)
    {
        if (textId != 0) Notice.Show(context.Root, NpcServiceKit.Fill(textId, "", args), SiegeTitle);
    }

    private void BuildGuard()
    {
        GuardPanel = NpcServiceKit.ServiceWindow(layer, "siegeguard", CastleGuardTitle, GuardWidth, CloseGuard);
        var body = GuardPanel.Body;
        body.AddChild(NpcServiceKit.NpcSpeech(SpeechHeight, out var greeting, out var question));
        NpcServiceKit.SetSpeech(greeting, GuardGreeting);
        NpcServiceKit.SetSpeech(question, GuardQuestion);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        options.AddChild(NpcServiceKit.NpcOption(ChallengerLabel, AskChallengers));
        options.AddChild(NpcServiceKit.NpcOption(ScheduleLabel, () => requests.Schedule()));
        options.AddChild(NpcServiceKit.NpcOption(AssaultLabel, () => requests.Assault()));
        options.AddChild(NpcServiceKit.NpcOption(WalkAwayLabel, CloseGuard));
    }

    internal void OpenGuard()
    {
        GuardPanel.Title = NpcServiceKit.WindowTitle(npcQuest, CastleGuardTitle);
        guardShown = true;
        GuardPanel.Visible = true;
    }

    internal void CloseGuard()
    {
        guardShown = false;
        GuardPanel.Visible = false;
    }

    private void AskChallengers()
    {
        CloseGuard();
        requests.Challengers();
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

    private static VBoxContainer Table(VBoxContainer body, float height, (string Title, float Width)[] columns)
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        var headPad = new MarginContainer();
        UiTheme.Margins(headPad, TableInset, 0, TableInset, 0);
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

    private static void ClearRows(VBoxContainer rows)
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

    private const int OfficeWidth = 360;
    private const int TaxRateWidth = 390;
    private const string CastleLordTitle = "Lord of Delos";
    private const string OfficeGreeting = "Lord of Delos!!!";
    private const string OfficeQuestion = "What is it that you want? I shall do as you command.";
    private const string ChangeTaxLabel = "Change tax rate";
    private const string CollectTaxLabel = "Collect tax";
    private const string MaintenanceLabel = "Castle maintenance";
    private const string ExitLabel = "Exit";
    private const string TaxListGreeting = "Lord of Delos!!";
    private const string TaxListQuestion = "Please select the tax rate you want to change.";
    private const string MoradonAreaLabel = "Moradon Area";
    private const string DelosAreaLabel = "Delos Area";
    private const string DungeonFeeLabel = "Dungeon Entrance Fee";

    internal HudWindow OfficePanel { get; private set; } = null!;
    internal HudWindow TaxListPanel { get; private set; } = null!;
    internal HudWindow TaxRatePanel { get; private set; } = null!;
    private Label taxRatePrompt = null!;
    private Label taxRateValue = null!;
    private SiegeOffice office;
    private SiegeTaxRates rates;
    private SiegeRateKind rateKind;
    private int rateValue;
    private int rateCurrent;
    private bool officeShown;
    private bool taxListShown;
    private bool taxRateShown;

    private void BuildOffice()
    {
        OfficePanel = NpcServiceKit.ServiceWindow(layer, "siegeoffice", CastleLordTitle, OfficeWidth, CloseOffice);
        var body = OfficePanel.Body;
        body.AddChild(NpcServiceKit.NpcSpeech(SpeechHeight, out var greeting, out var question));
        NpcServiceKit.SetSpeech(greeting, OfficeGreeting);
        NpcServiceKit.SetSpeech(question, OfficeQuestion);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        options.AddChild(NpcServiceKit.NpcOption(ChangeTaxLabel, () => requests.TaxList()));
        options.AddChild(NpcServiceKit.NpcOption(CollectTaxLabel, AskCollect));
        options.AddChild(NpcServiceKit.NpcOption(MaintenanceLabel, CloseOffice));
        options.AddChild(NpcServiceKit.NpcOption(ExitLabel, CloseOffice));
    }

    internal void OpenOffice(SiegeOffice office)
    {
        this.office = office;
        OfficePanel.Title = NpcServiceKit.WindowTitle(npcQuest, CastleLordTitle);
        officeShown = true;
        OfficePanel.Visible = true;
    }

    internal void CloseOffice()
    {
        officeShown = false;
        OfficePanel.Visible = false;
    }

    private void AskCollect()
    {
        if (office.Collectable == 0)
        {
            Message(SiegeWarfare.NothingCollectedText);
            return;
        }
        Notice.Confirm(context.Root, NpcServiceKit.Fill(SiegeWarfare.CollectableText, "", office.Collectable), NpcServiceKit.YesLabel, NpcServiceKit.NoLabel,
            () => requests.Collect(), title: SiegeTitle);
    }

    private void OnCollected(SiegeCollected collected)
    {
        if (collected.Result != SiegeWire.Success)
        {
            Message(SiegeWarfare.CollectErrorText(collected.Result));
            return;
        }
        CloseOffice();
        Message(SiegeWarfare.CollectedText, collected.Collected);
    }

    private void BuildTaxList()
    {
        TaxListPanel = NpcServiceKit.ServiceWindow(layer, "siegetaxlist", CastleLordTitle, OfficeWidth, CloseTaxList);
        var body = TaxListPanel.Body;
        body.AddChild(NpcServiceKit.NpcSpeech(SpeechHeight, out var greeting, out var question));
        NpcServiceKit.SetSpeech(greeting, TaxListGreeting);
        NpcServiceKit.SetSpeech(question, TaxListQuestion);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        options.AddChild(NpcServiceKit.NpcOption(MoradonAreaLabel, () => AskRate(SiegeRateKind.Moradon)));
        options.AddChild(NpcServiceKit.NpcOption(DelosAreaLabel, () => AskRate(SiegeRateKind.Delos)));
        options.AddChild(NpcServiceKit.NpcOption(DungeonFeeLabel, () => AskRate(SiegeRateKind.DungeonFee)));
        options.AddChild(NpcServiceKit.NpcOption(ExitLabel, CloseTaxList));
    }

    internal void OnTaxRates(SiegeTaxRates rates)
    {
        if (rates.Result != SiegeWire.Success)
        {
            Message(SiegeWarfare.TaxErrorText(rates.Result));
            return;
        }
        this.rates = rates;
        TaxListPanel.Title = NpcServiceKit.WindowTitle(npcQuest, CastleLordTitle);
        taxListShown = true;
        TaxListPanel.Visible = true;
    }

    internal void CloseTaxList()
    {
        taxListShown = false;
        TaxListPanel.Visible = false;
    }

    private int RateOf(SiegeRateKind kind) => kind switch
    {
        SiegeRateKind.Moradon => rates.Moradon,
        SiegeRateKind.Delos => rates.Delos,
        _ => rates.DungeonFee,
    };

    private void AskRate(SiegeRateKind kind)
    {
        Notice.Confirm(context.Root, NpcServiceKit.Fill(SiegeWarfare.ConfirmText(kind), "", RateOf(kind)), ChangeTaxLabel,
            NpcServiceKit.Text(KingElection.OkText, "OK"), () => OpenTaxRate(kind), title: SiegeTitle);
    }

    private void BuildTaxRate()
    {
        TaxRatePanel = NpcServiceKit.ServiceWindow(layer, "siegetaxrate", ChangeTaxLabel, TaxRateWidth, CloseTaxRate);
        var body = TaxRatePanel.Body;
        taxRatePrompt = NpcServiceKit.SpeechLabel(UiTheme.TextHi);
        taxRatePrompt.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(taxRatePrompt);
        body.AddChild(NpcServiceKit.TaxArrows(out taxRateValue, () => StepTaxRate(false), () => StepTaxRate(true)));

        var ok = UiTheme.ActionButton(NpcServiceKit.Text(KingElection.OkText, "OK"), "");
        ok.Pressed += SubmitTaxRate;
        var cancel = UiTheme.SmallButton(NpcServiceKit.CancelLabel, "");
        cancel.Pressed += CloseTaxRate;
        body.AddChild(NpcServiceKit.FooterButtons(ok, cancel));
    }

    internal void OpenTaxRate(SiegeRateKind kind)
    {
        rateKind = kind;
        rateCurrent = RateOf(kind);
        rateValue = rateCurrent;
        taxRatePrompt.Text = NpcServiceKit.Text(SiegeWarfare.PromptText(kind));
        RefreshTaxRate();
        taxRateShown = true;
        TaxRatePanel.Visible = true;
    }

    private void StepTaxRate(bool up)
    {
        rateValue = SiegeWarfare.Step(rateKind, rateValue, up);
        RefreshTaxRate();
    }

    private void RefreshTaxRate() =>
        taxRateValue.Text = rateKind == SiegeRateKind.DungeonFee
            ? $"{NationTreasury.Coins(rateValue)} {NpcServiceKit.Text(SiegeWarfare.CoinText, "Coin")}"
            : $"{rateValue} %";

    private void SubmitTaxRate()
    {
        if (rateValue != rateCurrent) requests.Rate(rateKind, rateValue);
        CloseTaxRate();
    }

    internal void CloseTaxRate()
    {
        taxRateShown = false;
        TaxRatePanel.Visible = false;
    }

    private void OnRateChanged(SiegeRateChanged changed)
    {
        if (changed.Result != SiegeWire.Success)
        {
            Message(SiegeWarfare.TaxErrorText(changed.Result));
            return;
        }
        context.Log.Notice(NpcServiceKit.Fill(SiegeWarfare.ChangedText(changed.Sub), "", changed.Value));
        switch (changed.Sub)
        {
            case SiegeOfficeSubOpcode.MoradonRate:
                rates = rates with { Moradon = (short)changed.Value };
                break;
            case SiegeOfficeSubOpcode.DelosRate:
                rates = rates with { Delos = (short)changed.Value };
                break;
            default:
                rates = rates with { DungeonFee = changed.Value };
                break;
        }
        if (changed.Sub != SiegeOfficeSubOpcode.DungeonFee && changed.Zone == context.Zone) zoneAbility.SetTariff(changed.Value);
    }

    private const int ListWidth = 380;
    private const float ScheduleHeight = 190f;
    private const float ChallengersHeight = 150f;
    private const float DefendersHeight = 230f;
    private const float ColumnWidth = 64f;
    private const float DayColumnWidth = 90f;
    private const float NationColumnWidth = 84f;
    private const string WarLabel = "War";
    internal const string DayLabel = "Day";
    internal const string TimeLabel = "Time";
    private const string ChallengersTitle = "List of Challengers";
    private const string KnightsLabel = "Knights";
    private const string NationLabel = "Nation";
    private const string MembersLabel = "Members";
    private const string ApplicationLabel = "Application";
    private const string ViewDefendersLabel = "View Defending Confederacy";
    private const string DefendersTitle = "Defending Confederacy";
    private const string ConfederacyLabel = "Confederacy";
    private const string ChallengeListLabel = "Challenge List";
    private const string ReadAgainLabel = "Read again";

    internal HudWindow SchedulePanel { get; private set; } = null!;
    internal HudWindow ChallengersPanel { get; private set; } = null!;
    internal HudWindow DefendersPanel { get; private set; } = null!;
    private VBoxContainer scheduleRows = null!;
    private VBoxContainer challengerRows = null!;
    private VBoxContainer defenderRows = null!;
    private Label signedUp = null!;
    private Label chosen = null!;
    private Label fee = null!;
    private Label period = null!;
    private Button apply = null!;
    private Button cancel = null!;
    private uint challengeFee;
    private bool scheduleShown;
    private bool challengersShown;
    private bool defendersShown;

    private void BuildSchedule()
    {
        SchedulePanel = NpcServiceKit.ServiceWindow(layer, "siegeschedule", WarLabel, ListWidth, CloseSchedule);
        var body = SchedulePanel.Body;
        scheduleRows = Table(body, ScheduleHeight,
            [(WarLabel, ColumnWidth), (DayLabel, DayColumnWidth), (TimeLabel, ColumnWidth)]);

        var ok = UiTheme.ActionButton(NpcServiceKit.Text(KingElection.OkText, "OK"), "");
        ok.Pressed += CloseSchedule;
        var again = UiTheme.SmallButton(ReadAgainLabel, "");
        again.Pressed += () => requests.Schedule();
        body.AddChild(NpcServiceKit.FooterButtons(ok, again));
    }

    internal void OnSchedule(SiegeSchedule schedule)
    {
        if (schedule.Result != SiegeWire.Success)
        {
            Message(SiegeWarfare.ListErrorText(schedule.Result, coins: false));
            return;
        }
        ClearRows(scheduleRows);
        foreach (var row in schedule.Rows)
            scheduleRows.AddChild(SiegeRow(
                SiegeCell(NpcServiceKit.Text(SiegeWarfare.WarText(row.WarType)), ColumnWidth, UiTheme.TextHi, expand: true),
                SiegeCell(NpcServiceKit.Text(SiegeWarfare.DayText(row.Weekday)), DayColumnWidth, UiTheme.TextLo),
                SiegeCell(SiegeWarfare.Clock(row.Hour, row.Minute), ColumnWidth, UiTheme.Gold)));
        scheduleShown = true;
        SchedulePanel.Visible = true;
    }

    internal void CloseSchedule()
    {
        scheduleShown = false;
        SchedulePanel.Visible = false;
    }

    private static (string, float)[] SiegeClanColumns(string first) =>
    [(first, ColumnWidth), (NationLabel, NationColumnWidth), (MembersLabel, ColumnWidth)];

    private static void FillClans(VBoxContainer rows, IEnumerable<SiegeClanRow> clans)
    {
        ClearRows(rows);
        foreach (var siegeClanRow in clans)
            rows.AddChild(SiegeRow(
                SiegeCell(siegeClanRow.Name, ColumnWidth, UiTheme.TextHi, expand: true),
                SiegeCell(NpcServiceKit.Text(SiegeWarfare.NationText(siegeClanRow.Nation)), NationColumnWidth, UiTheme.TextLo),
                SiegeCell(siegeClanRow.Members.ToString(), ColumnWidth, UiTheme.Gold)));
    }

    private void BuildChallengers()
    {
        ChallengersPanel = NpcServiceKit.ServiceWindow(layer, "siegechallengers", ChallengersTitle, ListWidth, CloseChallengers);
        var body = ChallengersPanel.Body;
        challengerRows = Table(body, ChallengersHeight, SiegeClanColumns(KnightsLabel));

        var info = UiTheme.Section();
        body.AddChild(info);
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 3);
        info.AddChild(lines);
        signedUp = NpcServiceKit.SpeechLabel(UiTheme.TextLo);
        chosen = NpcServiceKit.SpeechLabel(UiTheme.TextLo);
        fee = NpcServiceKit.SpeechLabel(UiTheme.Gold);
        period = NpcServiceKit.SpeechLabel(UiTheme.TextHi);
        foreach (var line in new[] { signedUp, chosen, fee, period }) lines.AddChild(line);

        apply = UiTheme.ActionButton(ApplicationLabel, "");
        apply.Pressed += () => requests.Apply(join: true);
        cancel = UiTheme.SmallButton(NpcServiceKit.CancelLabel, "");
        cancel.Pressed += AskCancel;
        var defenders = UiTheme.SmallButton(ViewDefendersLabel, "");
        defenders.Pressed += () => requests.Defenders();
        var footer = NpcServiceKit.FooterButtons(apply, cancel, defenders);
        defenders.CustomMinimumSize = new Vector2(0, 30);
        body.AddChild(footer);
    }

    private void OnApply(short result)
    {
        if (result != SiegeWire.Success) Message(SiegeWarfare.ListErrorText(result, coins: true));
    }

    internal void OnChallengers(SiegeChallengers list)
    {
        if (list.Result != SiegeWire.Success)
        {
            Message(SiegeWarfare.ListErrorText(list.Result, coins: true));
            return;
        }
        FillClans(challengerRows, list.Clans);
        challengeFee = list.Fee;
        signedUp.Text = NpcServiceKit.Fill(SiegeWarfare.SignedUpText, "", list.SignedUp);
        chosen.Text = NpcServiceKit.Fill(SiegeWarfare.ChosenText, "", list.Chosen);
        fee.Text = NpcServiceKit.Fill(SiegeWarfare.FeeText, "", list.Fee);
        period.Text = SiegeWarfare.RegistrationPeriod(NpcServiceKit.Text(SiegeWarfare.PeriodText),
            NpcServiceKit.Text(SiegeWarfare.DayText(list.StartDay)), list.StartHour, list.StartMinute, NpcServiceKit.Text(SiegeWarfare.DayText(list.EndDay)));

        string mine = clan.MyClan.InClan ? clan.MyClan.Name : "";
        bool applied = list.Clans.Any(c => SiegeWarfare.IsOwnClan(c.Name, mine));
        apply.Visible = !applied;
        cancel.Visible = applied;
        challengersShown = true;
        ChallengersPanel.Visible = true;
    }

    private void AskCancel()
    {
        Notice.Confirm(context.Root, NpcServiceKit.Fill(SiegeWarfare.CancelChallengeText, "", challengeFee), NpcServiceKit.YesLabel, NpcServiceKit.NoLabel,
            () => requests.Apply(join: false), title: SiegeTitle);
    }

    internal void CloseChallengers()
    {
        challengersShown = false;
        ChallengersPanel.Visible = false;
    }

    private void BuildDefenders()
    {
        DefendersPanel = NpcServiceKit.ServiceWindow(layer, "siegedefenders", DefendersTitle, ListWidth, CloseDefenders);
        var body = DefendersPanel.Body;
        defenderRows = Table(body, DefendersHeight, SiegeClanColumns(ConfederacyLabel));

        var challengers = UiTheme.SmallButton(ChallengeListLabel, "");
        challengers.Pressed += () => requests.Challengers();
        var again = UiTheme.SmallButton(ReadAgainLabel, "");
        again.Pressed += () => requests.Defenders();
        body.AddChild(NpcServiceKit.FooterButtons(challengers, again));
    }

    internal void OnDefenders(SiegeDefenders list)
    {
        if (list.Result != SiegeWire.Success)
        {
            Message(SiegeWarfare.ListErrorText(list.Result, coins: false));
            return;
        }
        FillClans(defenderRows, list.Clans);
        defendersShown = true;
        DefendersPanel.Visible = true;
    }

    internal void CloseDefenders()
    {
        defendersShown = false;
        DefendersPanel.Visible = false;
    }

    private const string WarTitle = "Castle Siege War";
    private const float StateIconSize = 32f;
    private const float WarWindowRightGap = 314f;
    private const float WarWindowTop = 70f;
    private const int WarBodyWidth = 195;

    private Button icon = null!;
    private TextureRect iconImage = null!;
    private CanvasLayer warLayer = null!;
    internal HudWindow WarPanel { get; private set; } = null!;
    private TextureRect state = null!;
    private Label warClock = null!;
    private Label holdClock = null!;
    private Label owner = null!;
    private double warLeft;
    private double holdLeft;
    private SiegeWarStatus shown = SiegeWarStatus.None;

    private void WarInit()
    {
        icon = topIcons.Add(TopIconSlot.Siege, "system/combat-defence", WarTitle, ToggleWar, out iconImage);
        icon.Resized += topIcons.PlaceAll;
        icon.VisibilityChanged += topIcons.PlaceAll;
        BuildWarWindow();
        siege.SiegeWarEvent += OnWar;
        siege.SiegeBaseEvent += OnBase;
        siege.SiegePointsEvent += OnPoints;
        OnWar();
    }

    private void WarDispose()
    {
        siege.SiegeWarEvent -= OnWar;
        siege.SiegeBaseEvent -= OnBase;
        siege.SiegePointsEvent -= OnPoints;
    }

    internal void BuildWarWindow()
    {
        warLayer = new CanvasLayer { Layer = UiLayers.EventPanels };
        context.Root.AddChild(warLayer);
        float screenWidth = context.Root.IsInsideTree() ? context.Root.GetViewport().GetVisibleRect().Size.X : 0f;
        var origin = new Vector2(Mathf.Max(0f, screenWidth - WarWindowRightGap), WarWindowTop);
        WarPanel = new HudWindow("siege_war", WarTitle, origin, WarBodyWidth) { Visible = false };
        WarPanel.Closed += () => WarPanel.Visible = false;
        warLayer.AddChild(WarPanel);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        WarPanel.Body.AddChild(body);

        state = UiIcons.Image("system/combat-defence", new Vector2(StateIconSize, StateIconSize), UiTheme.TextLo);
        state.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        body.AddChild(state);
        warClock = UiTheme.Text("", 13, UiTheme.TextHi);
        holdClock = UiTheme.Text("", 13, UiTheme.TextHi);
        owner = UiTheme.Text("", 13, UiTheme.GoldBright);
        body.AddChild(warClock);
        body.AddChild(holdClock);
        body.AddChild(owner);
    }

    private void ToggleWar()
    {
        if (WarPanel.Visible) { WarPanel.Visible = false; return; }
        RenderWar();
        UiKit.ShowOnTop(WarPanel);
    }

    private void OnWar()
    {
        var status = siege.SiegeWar;
        ShowStatus(status);
        bool isShown = SiegeWar.At(context.Zone, status);
        icon.Visible = isShown;
        if (!isShown) WarPanel.Visible = false;
    }

    internal void ShowStatus(SiegeWarStatus status)
    {
        shown = status;
        warLeft = status.WarSeconds;
        holdLeft = status.HoldSeconds;
        bool held = status.Held;
        state.Texture = UiIcons.Get(held ? "system/combat-attack" : "system/combat-defence");
        state.SelfModulate = held ? UiTheme.GoldBright : UiTheme.TextLo;
        if (status.OwnerName.Length > 0)
            owner.Text = ItemData.Text(SiegeWar.OwnerText, "Siege Owner Clan : [%s]").Replace("%s", status.OwnerName);
        RenderWar();
    }

    public void Tick(double delta, double nowSec)
    {
        if (!icon.Visible) return;
        warLeft -= delta;
        if (shown.Held) holdLeft -= delta;
        if (warLeft <= 0)
        {
            WarPanel.Visible = false;
            return;
        }
        if (WarPanel.Visible) RenderWar();
    }

    private void RenderWar()
    {
        warClock.Text = SiegeWar.Clock(ItemData.Text(SiegeWar.WarClockText, "Castle Siege Time : [%2d : %d]"), (int)warLeft);
        int hold = shown.Held ? (int)holdLeft : 0;
        holdClock.Text = SiegeWar.Clock(ItemData.Text(SiegeWar.HoldClockText, "Occupy Time : [%2d : %d]"), hold);
    }

    private void OnBase(short result)
    {
        if (SiegeWar.BaseRefusal(result) is { } text)
            Notice.Show(context.Root, ItemData.Text(text.Id, text.Fallback));
    }

    private void OnPoints(SiegePoints points)
    {
        if (SiegeWar.PointsText(points.Kind) is not { } text) return;
        string line = ItemData.Text(text.Id, text.Fallback);
        line = points.Kind == SiegePointsKind.Renewed
            ? ReplaceFirst(line, "%d", points.Total.ToString())
            : ReplaceFirst(ReplaceFirst(line, "%d", points.Gained.ToString()), "%d", points.Total.ToString());
        context.Log.Notice(line, points.Kind == SiegePointsKind.Renewed ? PointsRenewedColour : PointsColour);
    }

    private static readonly Color PointsColour = Color.Color8(0xE2, 0xC4, 0x77);
    private static readonly Color PointsRenewedColour = Color.Color8(0x87, 0xCE, 0xEB);

    private static string ReplaceFirst(string text, string token, string value)
    {
        int at = text.IndexOf(token, System.StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + value + text[(at + token.Length)..];
    }
}
