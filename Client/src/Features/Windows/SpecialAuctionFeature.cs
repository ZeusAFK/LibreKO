using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class SpecialAuctionFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly SpecialAuctionSession specialAuction;
    private readonly ISpecialAuctionRequests requests;
    private readonly UpgradeNoticeFeature upgradeNotice;

    public SpecialAuctionFeature(IWorldContext context, SelfSession self, SpecialAuctionSession specialAuction, ISpecialAuctionRequests requests, UpgradeNoticeFeature upgradeNotice)
    {
        this.context = context;
        this.self = self;
        this.specialAuction = specialAuction;
        this.requests = requests;
        this.upgradeNotice = upgradeNotice;
    }

    private const string TablePath = "res://assets/ui/special_auction.json";
    private const int TitleText = 43680;
    private const int BidPriceText = 43650;
    private const int BalanceText = 43651;
    private const int CurrentBidText = 43652;
    private const int AvailableBidText = 43653;
    private const int RemainingText = 43654;
    private const int EnterPriceText = 43655;
    private const int BidListText = 43660;
    private const int WonListText = 43661;
    private const int MyBidText = 43662;
    private const int PlaceBidText = 43670;
    private const int RetractText = 43671;
    private const int ReceiveText = 43672;
    private const int MaintenanceText = 43681;
    private const int SecretText = 43684;
    private const int ConfirmBidText = 43685;
    private const int FailedText = 43693;
    private const int SoldText = 43694;
    private const int EndedText = 43704;
    private const int MiscarriedText = 43710;
    private const int AllCancelledText = 43711;
    private const int NoHistoryText = 43698;
    private const int DelayText = 16810;
    private const int RefreshWaitText = 16820;
    private const double ClickDelay = 1.0;
    private const double RefreshDelay = 5.0;
    private const float LotSize = 52f;
    private const float RowSlotSize = 42f;
    private const float PageHeight = 430f;
    private const int GridColumns = 4;
    private static readonly Color EndedColour = new("ffff00");
    private static readonly Color CancelledColour = new("dc143c");

    internal enum AuctionTab { Today, Schedule, MyInfo, Log }

    private AuctionScheduleLot[] auctionTable = Array.Empty<AuctionScheduleLot>();

    private CanvasLayer layer = null!;
    internal HudWindow Window { get; private set; } = null!;
    private readonly Dictionary<AuctionTab, Button> auctionTabButtons = new();

    private readonly Dictionary<AuctionTab, Control> auctionPages = new();

    private readonly List<ItemSlotView> auctionLotSlots = new();
    private readonly List<Label> auctionLotMarks = new();
    private readonly List<AuctionBidRow> auctionBids = new();
    private readonly List<AuctionBidRow> auctionWins = new();
    private IReadOnlyList<AuctionLot> auctionLots = Array.Empty<AuctionLot>();
    private Label auctionSelectedName = null!;
    private Label auctionGold = null!;
    private Label auctionChecks = null!;
    private Label auctionTotal = null!;
    private Label auctionWords = null!;
    private Label auctionCurrent = null!;
    private Label auctionMinimum = null!;
    private Label auctionClock = null!;
    private Label auctionStatus = null!;
    internal SpinBox AuctionMillions { get; private set; } = null!;
    internal SpinBox AuctionCheckInput { get; private set; } = null!;
    private VBoxContainer auctionSchedule = null!;
    private VBoxContainer auctionBidList = null!;
    private VBoxContainer auctionWinList = null!;
    private VBoxContainer auctionLog = null!;
    private Godot.Timer auctionTicker = null!;
    private AuctionTab auctionTab;
    private AuctionToday auctionToday;
    private readonly AuctionTransactions auctionTransactions = new();
    private readonly List<Button> auctionRowButtons = new();
    private Notice? auctionConfirmation;
    private Button auctionPlaceBid = null!;
    private Button auctionRefresh = null!;
    private int auctionSelected = -1;
    private float auctionSecondsLeft;
    private double auctionClickedAt = double.NegativeInfinity;
    private double auctionRefreshedAt = double.NegativeInfinity;
    private bool shown;

    public bool IsOpen => shown;

    internal bool AuctionOpening { get; set; }

    public void Build()
    {
        BuildPanel();
        specialAuction.AuctionTodayEvent += OnAuctionToday;
        specialAuction.AuctionBidEvent += OnAuctionBid;
        specialAuction.AuctionCollectEvent += OnAuctionCollect;
        specialAuction.AuctionMyInfoEvent += OnAuctionMyInfo;
        specialAuction.AuctionEndedEvent += OnAuctionEnded;
        specialAuction.AuctionMiscarriedEvent += OnAuctionMiscarried;
        specialAuction.AuctionClaimEvent += OnAuctionClaim;
        specialAuction.AuctionLogEvent += OnAuctionLog;
    }

    public void Dispose()
    {
        specialAuction.AuctionTodayEvent -= OnAuctionToday;
        specialAuction.AuctionBidEvent -= OnAuctionBid;
        specialAuction.AuctionCollectEvent -= OnAuctionCollect;
        specialAuction.AuctionMyInfoEvent -= OnAuctionMyInfo;
        specialAuction.AuctionEndedEvent -= OnAuctionEnded;
        specialAuction.AuctionMiscarriedEvent -= OnAuctionMiscarried;
        specialAuction.AuctionClaimEvent -= OnAuctionClaim;
        specialAuction.AuctionLogEvent -= OnAuctionLog;
    }

    private static string AuctionText(int id, string fallback) => ItemData.Text(id, fallback);

    private string AuctionPlayerName() => self.LastEnter.Name ?? "";

    internal void EnsureAuctionTable()
    {
        if (auctionTable.Length > 0 || !Godot.FileAccess.FileExists(TablePath)) return;
        auctionTable = SpecialAuction.Parse(Godot.FileAccess.GetFileAsString(TablePath));
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(layer);

        Window = new HudWindow("specialauction", AuctionText(TitleText, "Akara's Altar"), bodyMinWidth: 780)
            { Visible = false };
        Window.Closed += Close;
        layer.AddChild(Window);

        auctionTicker = new Godot.Timer { WaitTime = 1.0, OneShot = false };
        auctionTicker.Timeout += TickSpecialAuction;
        Window.AddChild(auctionTicker);

        var body = Window.Body;
        body.AddThemeConstantOverride("separation", 8);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        body.AddChild(tabs);
        AddAuctionTab(tabs, AuctionTab.Today, "Ongoing Auction");
        AddAuctionTab(tabs, AuctionTab.Schedule, "Schedule");
        AddAuctionTab(tabs, AuctionTab.MyInfo, "My Auction Status");
        AddAuctionTab(tabs, AuctionTab.Log, "Auction History");

        var pages = new Control { CustomMinimumSize = new Vector2(0, PageHeight) };
        body.AddChild(pages);
        AddAuctionPage(pages, AuctionTab.Today, BuildAuctionTodayPage());
        AddAuctionPage(pages, AuctionTab.Schedule, BuildAuctionSchedulePage());
        AddAuctionPage(pages, AuctionTab.MyInfo, BuildAuctionMyInfoPage());
        AddAuctionPage(pages, AuctionTab.Log, BuildAuctionLogPage());

        auctionStatus = UiTheme.Text("", 12, UiTheme.Warning, HorizontalAlignment.Center);
        body.AddChild(auctionStatus);
        var notice = UiTheme.Text(AuctionText(MaintenanceText, "Please be aware that auction is not available during maintenance"),
            12, UiTheme.TextDim, HorizontalAlignment.Center);
        body.AddChild(notice);
        RefreshAuctionTransactions();
    }

    private void AddAuctionTab(HBoxContainer tabs, AuctionTab tab, string text)
    {
        var button = UiTheme.TopTabButton(text, 13);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.Pressed += () => PressAuctionTab(tab);
        tabs.AddChild(button);
        auctionTabButtons[tab] = button;
    }

    private void AddAuctionPage(Control pages, AuctionTab tab, Control page)
    {
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.Visible = false;
        pages.AddChild(page);
        auctionPages[tab] = page;
    }

    private Control BuildAuctionTodayPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);

        var lots = UiTheme.Section();
        page.AddChild(lots);
        var grid = new GridContainer { Columns = GridColumns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        lots.AddChild(grid);
        for (int i = 0; i < SpecialAuction.LotsPerDay; i++)
        {
            var cell = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            cell.AddThemeConstantOverride("separation", 3);
            grid.AddChild(cell);
            var slot = new ItemSlotView(LotSize) { Index = i, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
            slot.Clicked += view => SelectAuctionLot(view.Index);
            slot.Hovered += view => { if (!view.Item.IsEmpty) context.Tooltips.Show(-1, view.Item); };
            slot.Unhovered += _ => context.Tooltips.Hide();
            cell.AddChild(slot);
            auctionLotSlots.Add(slot);
            var mark = UiTheme.Text("", 11, UiTheme.GoldBright, HorizontalAlignment.Center);
            cell.AddChild(mark);
            auctionLotMarks.Add(mark);
        }

        var nameBar = UiTheme.Section();
        page.AddChild(nameBar);
        auctionSelectedName = UiTheme.Text("-", 15, UiTheme.TextHi, HorizontalAlignment.Center);
        nameBar.AddChild(auctionSelectedName);

        var bottom = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        bottom.AddThemeConstantOverride("separation", 8);
        page.AddChild(bottom);
        bottom.AddChild(BuildAuctionBalanceSection());
        bottom.AddChild(BuildAuctionBidSection());
        bottom.AddChild(BuildAuctionStatusSection());
        return page;
    }

    private Control BuildAuctionBalanceSection()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        section.AddChild(box);

        box.AddChild(UiTheme.SectionTitle(AuctionText(BalanceText, "Current Balance")));
        var balance = new HBoxContainer();
        balance.AddThemeConstantOverride("separation", 10);
        box.AddChild(balance);
        auctionGold = UiTheme.Text("0", 13, UiTheme.Gold);
        auctionGold.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        balance.AddChild(auctionGold);
        balance.AddChild(AuctionCheckIcon());
        auctionChecks = UiTheme.Text("0", 13, UiTheme.TextHi);
        balance.AddChild(auctionChecks);

        box.AddChild(UiTheme.SectionTitle(AuctionText(EnterPriceText, "Enter a price")));
        var entry = new HBoxContainer();
        entry.AddThemeConstantOverride("separation", 4);
        box.AddChild(entry);
        AuctionMillions = UiTheme.NumberBox(0, SpecialAuction.MaxMillions, 1, 70);
        AuctionMillions.ValueChanged += _ => RefreshAuctionTotal();
        AuctionMillions.GetLineEdit().TextChanged += _ => RefreshAuctionTotal();
        entry.AddChild(AuctionMillions);
        var suffix = UiTheme.Text(",000,000", 13, UiTheme.TextLo);
        suffix.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        entry.AddChild(suffix);
        entry.AddChild(AuctionCheckIcon());
        AuctionCheckInput = UiTheme.NumberBox(0, 0, 1, 56);
        AuctionCheckInput.ValueChanged += _ => RefreshAuctionTotal();
        AuctionCheckInput.GetLineEdit().TextChanged += _ => RefreshAuctionTotal();
        entry.AddChild(AuctionCheckInput);
        return section;
    }

    private static Control AuctionCheckIcon() => new TextureRect
    {
        Texture = ItemIcons.Get(SpecialAuction.MythrilCheck),
        CustomMinimumSize = new Vector2(22, 22),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        TooltipText = ItemData.DisplayName(SpecialAuction.MythrilCheck),
    };

    private Control BuildAuctionBidSection()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        section.AddChild(box);

        box.AddChild(UiTheme.SectionTitle(AuctionText(BidPriceText, "Bid Price")));
        auctionTotal = UiTheme.Text("0", 18, UiTheme.GoldBright, HorizontalAlignment.Center);
        box.AddChild(auctionTotal);
        auctionWords = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        box.AddChild(auctionWords);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var place = UiTheme.ActionButton(AuctionText(PlaceBidText, "Place Bid"), "");
        place.Pressed += PlaceAuctionBid;
        auctionPlaceBid = place;
        box.AddChild(place);
        return section;
    }

    private Control BuildAuctionStatusSection()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        section.AddChild(box);

        box.AddChild(UiTheme.SectionTitle(AuctionText(CurrentBidText, "Current Bid")));
        auctionCurrent = UiTheme.Text("-", 13, UiTheme.TextHi);
        box.AddChild(auctionCurrent);
        box.AddChild(UiTheme.SectionTitle(AuctionText(AvailableBidText, "Available Bid")));
        auctionMinimum = UiTheme.Text("-", 13, UiTheme.TextHi);
        box.AddChild(auctionMinimum);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 8);
        box.AddChild(foot);
        var refresh = UiTheme.SmallButton("Refresh", "Refresh the bids");
        refresh.Pressed += RefreshAuctionToday;
        auctionRefresh = refresh;
        foot.AddChild(refresh);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(UiTheme.Text(AuctionText(RemainingText, "Remaining time"), 12, UiTheme.TextLo));
        auctionClock = UiTheme.Text("00:00:00", 13, UiTheme.TextHi);
        foot.AddChild(auctionClock);
        return section;
    }

    private Control BuildAuctionSchedulePage()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        auctionSchedule = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        auctionSchedule.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(auctionSchedule);
        return scroll;
    }

    private Control BuildAuctionMyInfoPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);
        auctionBidList = AddAuctionListSection(page, AuctionText(BidListText, "Bid Item List"));
        auctionWinList = AddAuctionListSection(page, AuctionText(WonListText, "Successful Bid Item List"));
        return page;
    }

    private static VBoxContainer AddAuctionListSection(VBoxContainer page, string title)
    {
        var section = UiTheme.Section();
        section.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        page.AddChild(section);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        section.AddChild(box);
        box.AddChild(UiTheme.SectionTitle(title));
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        box.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        return list;
    }

    private Control BuildAuctionLogPage()
    {
        var section = UiTheme.Section();
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        section.AddChild(scroll);
        auctionLog = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        auctionLog.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(auctionLog);
        return section;
    }

    internal void Open()
    {
        if (shown)
        {
            Close();
            return;
        }
        if (auctionTransactions.Busy)
        {
            Notice.Show(context.Root, AuctionText(DelayText, "Try again later"), AuctionText(TitleText, "Akara's Altar"));
            return;
        }
        EnsureAuctionTable();
        Reset();
        AuctionOpening = true;
        requests.AuctionRequest(SpecialAuctionSubOpcode.Today);
    }

    internal void Reset()
    {
        auctionSelected = -1;
        auctionLots = Array.Empty<AuctionLot>();
        AuctionMillions.Value = 0;
        AuctionCheckInput.Value = 0;
        auctionClickedAt = double.NegativeInfinity;
        auctionRefreshedAt = double.NegativeInfinity;
        SetAuctionStatus("");
    }

    private void Show(AuctionTab tab)
    {
        shown = true;
        AuctionOpening = false;
        Window.Visible = true;
        ShowAuctionTab(tab);
        RefreshAuctionBalance();
        if (auctionTicker.IsInsideTree()) auctionTicker.Start();
    }

    public void Close()
    {
        AuctionOpening = false;
        auctionConfirmation?.Close();
        auctionConfirmation = null;
        if (!shown) return;
        shown = false;
        Window.Visible = false;
        auctionTicker.Stop();
        context.Tooltips.Hide();
    }

    internal void ShowAuctionTab(AuctionTab tab)
    {
        auctionTab = tab;
        foreach (var (key, page) in auctionPages) page.Visible = key == tab;
        foreach (var (key, button) in auctionTabButtons)
        {
            bool on = key == tab;
            button.SetPressedNoSignal(on);
            button.AddThemeStyleboxOverride("normal", UiTheme.TopTab(on));
            button.AddThemeStyleboxOverride("hover", UiTheme.TopTab(on, true));
            button.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
        }
        if (tab == AuctionTab.Schedule) RenderAuctionSchedule();
    }

    private double AuctionNow => FrameClock.Now();

    private bool TakeAuctionClick()
    {
        if (AuctionNow - auctionClickedAt < ClickDelay)
        {
            SetAuctionStatus(AuctionText(DelayText, "Try again later"));
            return false;
        }
        auctionClickedAt = AuctionNow;
        SetAuctionStatus("");
        return true;
    }

    private void PressAuctionTab(AuctionTab tab)
    {
        foreach (var (key, button) in auctionTabButtons) button.SetPressedNoSignal(key == auctionTab);
        if (!TakeAuctionClick()) return;
        if (auctionTransactions.Busy && tab is AuctionTab.Today or AuctionTab.MyInfo)
        {
            ShowAuctionTab(tab);
            return;
        }
        switch (tab)
        {
            case AuctionTab.Today:
                requests.AuctionRequest(SpecialAuctionSubOpcode.Today);
                break;
            case AuctionTab.Schedule:
                ShowAuctionTab(tab);
                break;
            case AuctionTab.MyInfo:
                requests.AuctionRequest(SpecialAuctionSubOpcode.MyInfo);
                break;
            case AuctionTab.Log:
                ShowAuctionTab(tab);
                requests.AuctionRequest(SpecialAuctionSubOpcode.Log);
                break;
        }
    }

    private void RefreshAuctionToday()
    {
        if (auctionTransactions.Busy) return;
        double waited = AuctionNow - auctionRefreshedAt;
        if (waited < RefreshDelay)
        {
            int left = (int)Math.Ceiling(RefreshDelay - waited);
            SetAuctionStatus(AuctionText(RefreshWaitText, "It is available in %d seconds").Replace("%d", left.ToString()));
            return;
        }
        auctionRefreshedAt = AuctionNow;
        SetAuctionStatus("");
        requests.AuctionRequest(SpecialAuctionSubOpcode.Today);
    }

    private void SetAuctionStatus(string text)
    {
        auctionStatus.Text = text;
        auctionStatus.Visible = text.Length > 0;
    }

    private void TickSpecialAuction()
    {
        if (auctionTab != AuctionTab.Today) return;
        auctionSecondsLeft = Math.Max(0, auctionSecondsLeft - 1);
        auctionClock.Text = SpecialAuction.Clock((int)auctionSecondsLeft);
        RefreshAuctionBalance();
    }

    internal void OnAuctionToday(AuctionToday today)
    {
        if (!shown && !AuctionOpening) return;
        if (today.Status != SpecialAuction.NothingToBid && (today.Group == 0 || today.Day > SpecialAuction.DaysPerGroup))
        {
            AuctionOpening = false;
            return;
        }

        auctionToday = today;
        auctionSecondsLeft = today.Seconds;
        auctionClock.Text = SpecialAuction.Clock(today.Seconds);
        auctionLots = today.Status == SpecialAuction.Bidding ? SpecialAuction.Live(auctionTable, today) : Array.Empty<AuctionLot>();
        RenderAuctionLots();
        Show(AuctionTab.Today);

        int notice = today.Status switch
        {
            SpecialAuction.NothingToBid => SpecialAuction.NoItemText,
            SpecialAuction.CollectOnly => SpecialAuction.CollectOnlyText,
            SpecialAuction.Settling => SpecialAuction.SettlingText,
            SpecialAuction.Bidding when auctionLots.Count == 0 => SpecialAuction.NoItemText,
            _ => 0,
        };
        if (notice != 0) Notice.Show(context.Root, AuctionText(notice, ""), AuctionText(TitleText, "Akara's Altar"));
    }

    private void RenderAuctionLots()
    {
        string me = AuctionPlayerName();
        for (int i = 0; i < auctionLotSlots.Count; i++)
        {
            var lot = auctionLots.FirstOrDefault(l => l.Slot == i);
            var slot = auctionLotSlots[i];
            if (lot == null)
            {
                slot.Clear();
                slot.Look = SlotLook.Dimmed;
                auctionLotMarks[i].Text = "";
                continue;
            }
            slot.Set(new ItemSlot { ItemId = lot.ItemId, Count = (short)lot.Count, Durability = UiKit.TooltipItem(lot.ItemId).Durability });
            slot.Look = i == auctionSelected ? SlotLook.Selected : SlotLook.Normal;
            auctionLotMarks[i].Text = me.Length > 0 && lot.TopBidder == me ? "Your bid" : "";
        }
        if (auctionLots.All(l => l.Slot != auctionSelected)) auctionSelected = -1;
        RenderAuctionSelection();
    }

    private AuctionLot? SelectedAuctionLot => auctionLots.FirstOrDefault(l => l.Slot == auctionSelected);

    internal void SelectAuctionLot(int slot)
    {
        if (auctionTransactions.Bid != null || auctionLots.All(l => l.Slot != slot)) return;
        auctionSelected = slot;
        for (int i = 0; i < auctionLotSlots.Count; i++)
            if (!auctionLotSlots[i].Item.IsEmpty)
                auctionLotSlots[i].Look = i == slot ? SlotLook.Selected : SlotLook.Normal;
        RenderAuctionSelection();
    }

    private void RenderAuctionSelection()
    {
        RefreshAuctionTransactions();
        var lot = SelectedAuctionLot;
        if (lot == null)
        {
            auctionSelectedName.Text = "-";
            auctionSelectedName.AddThemeColorOverride("font_color", UiTheme.TextHi);
            auctionCurrent.Text = "-";
            auctionMinimum.Text = "-";
            return;
        }
        auctionSelectedName.Text = ItemData.DisplayName(lot.ItemId);
        auctionSelectedName.AddThemeColorOverride("font_color", ItemGrade.Tint(lot.ItemId));
        auctionCurrent.Text = $"{lot.Current:n0}";
        auctionMinimum.Text = $"{lot.MinimumBid:n0}";
    }

    private int AuctionChecksInBag => SpecialAuction.CheckSlots(self.Inventory).Count;

    private void RefreshAuctionBalance()
    {
        int checks = AuctionChecksInBag;
        auctionGold.Text = UiKit.GoldAmount(self.Sheet.Gold);
        auctionChecks.Text = checks.ToString();
        AuctionMillions.MaxValue = Math.Max(0, SpecialAuction.ClampMillions(SpecialAuction.MaxMillions, self.Sheet.Gold));
        AuctionCheckInput.MaxValue = checks;
        RefreshAuctionTotal();
    }

    private (int Millions, int Checks) AuctionEntry =>
        (SpecialAuction.ClampMillions((long)AuctionMillions.Value, self.Sheet.Gold),
         SpecialAuction.ClampChecks((long)AuctionCheckInput.Value, AuctionChecksInBag));

    internal void RefreshAuctionTotal()
    {
        var (millions, checks) = AuctionEntry;
        long total = SpecialAuction.Total(millions, checks);
        auctionTotal.Text = $"{total:n0}";
        auctionWords.Text = SpecialAuction.InWords(total);
    }

    private void RefreshAuctionTransactions()
    {
        bool busy = auctionTransactions.Busy;
        auctionPlaceBid.Disabled = busy || SelectedAuctionLot == null;
        auctionRefresh.Disabled = busy;
        AuctionMillions.Editable = AuctionCheckInput.Editable = !busy;
        foreach (var button in auctionRowButtons) button.Disabled = busy;
    }

    private void PlaceAuctionBid()
    {
        if (auctionTransactions.Busy || !TakeAuctionClick()) return;
        var (millions, checks) = AuctionEntry;
        if (SpecialAuction.Total(millions, checks) <= 0)
        {
            Notice.Show(context.Root, AuctionText(SpecialAuction.EnterPriceText, "Please try again after entering bidding price"),
                AuctionText(TitleText, "Akara's Altar"));
            return;
        }
        auctionConfirmation = Notice.Confirm(context.Root, AuctionText(ConfirmBidText, "Do you wish to place a bid?"), "Yes", "No",
            ConfirmAuctionBid, title: AuctionText(TitleText, "Akara's Altar"));
    }

    private void ConfirmAuctionBid()
    {
        auctionConfirmation = null;
        if (!shown || auctionTransactions.Busy) return;
        var lot = SelectedAuctionLot;
        var (millions, checks) = AuctionEntry;
        var checkSlots = SpecialAuction.CheckSlots(self.Inventory);
        int refusal = SpecialAuction.BidRefusal(lot, millions, checks, checkSlots.Count);
        if (refusal != 0)
        {
            Notice.Show(context.Root, AuctionText(refusal, ""), AuctionText(TitleText, "Akara's Altar"));
            return;
        }
        if (!auctionTransactions.BeginBid(lot!, SpecialAuction.Total(millions, checks), auctionToday.Group, auctionToday.Day)) return;
        RefreshAuctionTransactions();
        requests.AuctionBid(lot!, checkSlots.Take(checks).ToList(), millions);
    }

    private void OnAuctionBid(short result)
    {
        var submitted = auctionTransactions.CompleteBid();
        if (submitted == null) return;
        if (result == SpecialAuction.Success && submitted.IsFor(auctionToday))
        {
            auctionLots = submitted.Apply(auctionLots, AuctionPlayerName());
            AuctionMillions.Value = 0;
            AuctionCheckInput.Value = 0;
            RenderAuctionLots();
        }
        RefreshAuctionBalance();
        RefreshAuctionTransactions();
        ShowAuctionResult(SpecialAuction.BidResultText(result));
    }

    private void ShowAuctionResult(int textId)
    {
        string text = AuctionText(textId, "");
        if (shown) Notice.Show(context.Root, text, AuctionText(TitleText, "Akara's Altar"));
        else if (text.Length > 0) context.Log.Notice(text);
    }

    internal void OnAuctionMyInfo(short result, IReadOnlyList<AuctionBidRow> rows)
    {
        if (!shown) return;
        auctionBids.Clear();
        auctionWins.Clear();
        if (result == SpecialAuction.Success)
        {
            auctionBids.AddRange(rows.Where(r => SpecialAuction.IsBid(r.Status)));
            auctionWins.AddRange(rows.Where(r => r.Status == AuctionBidStatus.Won));
        }
        RenderAuctionMyInfo();
        ShowAuctionTab(AuctionTab.MyInfo);
        if (result == SpecialAuction.MyInfoSettling)
            Notice.Show(context.Root, AuctionText(SpecialAuction.ClaimLaterText, ""), AuctionText(TitleText, "Akara's Altar"));
        else if (result != SpecialAuction.Success)
            Notice.Show(context.Root, AuctionText(SpecialAuction.DatabaseErrorText, "Database Error"), AuctionText(TitleText, "Akara's Altar"));
    }

    private void RenderAuctionMyInfo()
    {
        auctionRowButtons.Clear();
        FillAuctionRows(auctionBidList, auctionBids, won: false);
        FillAuctionRows(auctionWinList, auctionWins, won: true);
        RefreshAuctionTransactions();
    }

    private void FillAuctionRows(VBoxContainer list, List<AuctionBidRow> rows, bool won)
    {
        UiKit.ClearChildren(list);
        foreach (var row in rows) list.AddChild(BuildAuctionRow(row, won));
    }

    private Control BuildAuctionRow(AuctionBidRow row, bool won)
    {
        var panel = UiTheme.RowPanel();
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 10);
        panel.AddChild(line);

        var channel = UiTheme.Text(row.Channel > 0 ? $"Ch.{row.Channel}" : "", 12, UiTheme.TextLo);
        channel.CustomMinimumSize = new Vector2(36, 0);
        channel.VerticalAlignment = VerticalAlignment.Center;
        line.AddChild(channel);

        var slot = new ItemSlotView(RowSlotSize) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        slot.Set(new ItemSlot { ItemId = row.ItemId, Count = row.Count, Durability = UiKit.TooltipItem(row.ItemId).Durability });
        slot.Hovered += view => { if (!view.Item.IsEmpty) context.Tooltips.Show(-1, view.Item); };
        slot.Unhovered += _ => context.Tooltips.Hide();
        line.AddChild(slot);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        text.AddThemeConstantOverride("separation", 2);
        line.AddChild(text);
        if (won)
        {
            var name = UiTheme.Text(ItemData.DisplayName(row.ItemId), 13, ItemGrade.Tint(row.ItemId));
            text.AddChild(name);
        }
        else
        {
            var state = UiTheme.Text(AuctionText(SpecialAuction.StateText(row.Status), ""), 13,
                row.Status == AuctionBidStatus.Top ? UiTheme.Good : UiTheme.Warning);
            text.AddChild(state);
            text.AddChild(UiTheme.Text($"{AuctionText(MyBidText, "My Bid Price")}  {row.Price:n0}", 12, UiTheme.TextLo));
        }

        if (won || SpecialAuction.CanRetract(row.Status))
        {
            var button = UiTheme.SmallButton(won ? AuctionText(ReceiveText, "Receive") : AuctionText(RetractText, "Retract Bid"), "");
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            button.Pressed += () => PressAuctionRow(row, won);
            line.AddChild(button);
            auctionRowButtons.Add(button);
        }
        return panel;
    }

    private void PressAuctionRow(AuctionBidRow row, bool won)
    {
        if (auctionTransactions.Busy || !TakeAuctionClick()) return;
        bool listed = won ? row.Status == AuctionBidStatus.Won && auctionWins.Contains(row)
            : SpecialAuction.CanRetract(row.Status) && auctionBids.Contains(row);
        if (!listed || !auctionTransactions.BeginRow(row, won)) return;
        RefreshAuctionTransactions();
        if (won) requests.AuctionReceive(row);
        else requests.AuctionRetract(row);
    }

    private void OnAuctionCollect(short result)
    {
        var request = auctionTransactions.CompleteRow(claim: false);
        if (request == null) return;
        if (SpecialAuction.Collected(result))
        {
            auctionBids.Remove(request.Row);
            RenderAuctionMyInfo();
        }
        RefreshAuctionBalance();
        RefreshAuctionTransactions();
        ShowAuctionResult(SpecialAuction.CollectResultText(result));
    }

    private void OnAuctionClaim(short result)
    {
        var request = auctionTransactions.CompleteRow(claim: true);
        if (request == null) return;
        if (result == SpecialAuction.Success)
        {
            auctionWins.Remove(request.Row);
            RenderAuctionMyInfo();
        }
        RefreshAuctionTransactions();
        ShowAuctionResult(SpecialAuction.ClaimResultText(result));
    }

    private void RenderAuctionSchedule()
    {
        UiKit.ClearChildren(auctionSchedule);
        if (auctionToday.Group == 0) return;
        foreach (var day in SpecialAuction.Upcoming(auctionTable, auctionToday.Group, auctionToday.Day))
            auctionSchedule.AddChild(BuildAuctionScheduleDay(day));
    }

    private Control BuildAuctionScheduleDay(AuctionUpcomingDay day)
    {
        var panel = UiTheme.RowPanel();
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 10);
        panel.AddChild(line);
        var when = UiTheme.Text(day.Offset == 1 ? "Tomorrow" : $"In {day.Offset} days", 13, UiTheme.Gold);
        when.CustomMinimumSize = new Vector2(96, 0);
        when.VerticalAlignment = VerticalAlignment.Center;
        line.AddChild(when);
        foreach (var lot in day.Lots)
        {
            if (lot.Secret)
            {
                line.AddChild(UiTheme.IconTile("?", AuctionText(SecretText, "Result will be shown on the auction day"),
                    new Vector2(RowSlotSize, RowSlotSize)));
                continue;
            }
            var slot = new ItemSlotView(RowSlotSize);
            slot.Set(new ItemSlot { ItemId = lot.Item, Count = (short)lot.Count, Durability = UiKit.TooltipItem(lot.Item).Durability });
            slot.Hovered += view => { if (!view.Item.IsEmpty) context.Tooltips.Show(-1, view.Item); };
            slot.Unhovered += _ => context.Tooltips.Hide();
            line.AddChild(slot);
        }
        return panel;
    }

    internal void OnAuctionLog(IReadOnlyList<IReadOnlyList<AuctionResultLine>> days)
    {
        if (!shown) return;
        UiKit.ClearChildren(auctionLog);
        for (int d = 0; d < days.Count; d++)
        {
            var colour = new Color(SpecialAuction.LogColour(d));
            foreach (var result in days[d])
            {
                string text = AuctionResultText(result);
                if (text.Length > 0) auctionLog.AddChild(UiTheme.Text(text, 13, colour));
            }
        }
        if (auctionLog.GetChildCount() == 0)
            auctionLog.AddChild(UiTheme.Text(AuctionText(NoHistoryText, "There is no transaction history"), 13, UiTheme.TextDim));
        ShowAuctionTab(AuctionTab.Log);
    }

    private static string AuctionResultText(AuctionResultLine result)
    {
        if (result.ItemId == 0 || ItemData.Get(result.ItemId) == null) return "";
        string name = ItemData.DisplayName(result.ItemId);
        return result.Status switch
        {
            AuctionBidStatus.Won => ReplaceFirst(ReplaceFirst(AuctionText(SoldText, "[%s] Item bid has been successfully accepted to [%s]"), name), $"{result.Price:n0}"),
            AuctionBidStatus.Cancelled => ReplaceFirst(AuctionText(FailedText, "[%s] Item has been failed in bidding"), name),
            _ => "",
        };
    }

    internal static string ReplaceFirst(string text, string value)
    {
        int at = text.IndexOf("%s", StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + value + text[(at + 2)..];
    }

    private void OnAuctionEnded(IReadOnlyList<AuctionResultLine> results)
    {
        Close();
        var lines = new List<string> { AuctionText(EndedText, "[Today's auction has been ended]") };
        foreach (var result in results)
        {
            string text = AuctionResultText(result);
            if (text.Length == 0) continue;
            lines.Add(text);
            context.Log.Notice(text);
        }
        upgradeNotice.ShowBanner(string.Join("\n", lines), EndedColour);
    }

    private void OnAuctionMiscarried(bool ok)
    {
        Close();
        if (!ok)
        {
            Notice.Show(context.Root, "Shutdown Command Failed", AuctionText(TitleText, "Akara's Altar"));
            return;
        }
        upgradeNotice.ShowBanner($"{AuctionText(MiscarriedText, "[Akara's Altar Auction has been miscarried]")}\n"
            + AuctionText(AllCancelledText, "[All auction items has been canceled in bidding]"), CancelledColour);
    }
}
