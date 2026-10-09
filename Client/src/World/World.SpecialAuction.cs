using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string SpecialAuctionTablePath = "res://assets/ui/special_auction.json";
    private const int SpecialAuctionLayer = 78;
    private const int SpecialAuctionTitleText = 43680;
    private const int SpecialAuctionBidPriceText = 43650;
    private const int SpecialAuctionBalanceText = 43651;
    private const int SpecialAuctionCurrentBidText = 43652;
    private const int SpecialAuctionAvailableBidText = 43653;
    private const int SpecialAuctionRemainingText = 43654;
    private const int SpecialAuctionEnterPriceText = 43655;
    private const int SpecialAuctionBidListText = 43660;
    private const int SpecialAuctionWonListText = 43661;
    private const int SpecialAuctionMyBidText = 43662;
    private const int SpecialAuctionPlaceBidText = 43670;
    private const int SpecialAuctionRetractText = 43671;
    private const int SpecialAuctionReceiveText = 43672;
    private const int SpecialAuctionMaintenanceText = 43681;
    private const int SpecialAuctionSecretText = 43684;
    private const int SpecialAuctionConfirmBidText = 43685;
    private const int SpecialAuctionFailedText = 43693;
    private const int SpecialAuctionSoldText = 43694;
    private const int SpecialAuctionEndedText = 43704;
    private const int SpecialAuctionMiscarriedText = 43710;
    private const int SpecialAuctionAllCancelledText = 43711;
    private const int SpecialAuctionNoHistoryText = 43698;
    private const int SpecialAuctionDelayText = 16810;
    private const int SpecialAuctionRefreshWaitText = 16820;
    private const double SpecialAuctionClickDelay = 1.0;
    private const double SpecialAuctionRefreshDelay = 5.0;
    private const float SpecialAuctionLotSize = 52f;
    private const float SpecialAuctionRowSlotSize = 42f;
    private const float SpecialAuctionPageHeight = 430f;
    private const int SpecialAuctionGridColumns = 4;

    private static readonly Color SpecialAuctionEndedColour = new("ffff00");
    private static readonly Color SpecialAuctionCancelledColour = new("dc143c");

    private enum AuctionTab { Today, Schedule, MyInfo, Log }

    private AuctionScheduleLot[] _auctionTable = Array.Empty<AuctionScheduleLot>();
    private CanvasLayer _specialAuctionLayer = null!;
    private HudWindow _specialAuctionPanel = null!;
    private readonly Dictionary<AuctionTab, Button> _auctionTabButtons = new();
    private readonly Dictionary<AuctionTab, Control> _auctionPages = new();
    private readonly List<ItemSlotView> _auctionLotSlots = new();
    private readonly List<Label> _auctionLotMarks = new();
    private readonly List<AuctionBidRow> _auctionBids = new();
    private readonly List<AuctionBidRow> _auctionWins = new();
    private IReadOnlyList<AuctionLot> _auctionLots = Array.Empty<AuctionLot>();
    private Label _auctionSelectedName = null!, _auctionGold = null!, _auctionChecks = null!, _auctionTotal = null!, _auctionWords = null!;
    private Label _auctionCurrent = null!, _auctionMinimum = null!, _auctionClock = null!, _auctionStatus = null!;
    private SpinBox _auctionMillions = null!, _auctionCheckInput = null!;
    private VBoxContainer _auctionSchedule = null!, _auctionBidList = null!, _auctionWinList = null!, _auctionLog = null!;
    private Godot.Timer _auctionTicker = null!;
    private AuctionTab _auctionTab;
    private AuctionToday _auctionToday;
    private readonly AuctionTransactions _auctionTransactions = new();
    private readonly List<Button> _auctionRowButtons = new();
    private Notice? _auctionConfirmation;
    private Button _auctionPlaceBid = null!, _auctionRefresh = null!;
    private int _auctionSelected = -1;
    private float _auctionSecondsLeft;
    private double _auctionClickedAt = double.NegativeInfinity, _auctionRefreshedAt = double.NegativeInfinity;
    private bool _specialAuctionShown, _auctionOpening, _auctionRefreshing;

    private void SpecialAuctionInit()
    {
        BuildSpecialAuctionPanel();
        Net.I.AuctionTodayEvent += OnAuctionToday;
        Net.I.AuctionBidEvent += OnAuctionBid;
        Net.I.AuctionCollectEvent += OnAuctionCollect;
        Net.I.AuctionMyInfoEvent += OnAuctionMyInfo;
        Net.I.AuctionEndedEvent += OnAuctionEnded;
        Net.I.AuctionMiscarriedEvent += OnAuctionMiscarried;
        Net.I.AuctionClaimEvent += OnAuctionClaim;
        Net.I.AuctionLogEvent += OnAuctionLog;
    }

    private void SpecialAuctionDispose()
    {
        Net.I.AuctionTodayEvent -= OnAuctionToday;
        Net.I.AuctionBidEvent -= OnAuctionBid;
        Net.I.AuctionCollectEvent -= OnAuctionCollect;
        Net.I.AuctionMyInfoEvent -= OnAuctionMyInfo;
        Net.I.AuctionEndedEvent -= OnAuctionEnded;
        Net.I.AuctionMiscarriedEvent -= OnAuctionMiscarried;
        Net.I.AuctionClaimEvent -= OnAuctionClaim;
        Net.I.AuctionLogEvent -= OnAuctionLog;
    }

    private static string AuctionText(int id, string fallback) => ItemData.Text(id, fallback);

    private static string AuctionPlayerName() => Net.I?.LastEnter.Name ?? "";

    private void EnsureAuctionTable()
    {
        if (_auctionTable.Length > 0 || !Godot.FileAccess.FileExists(SpecialAuctionTablePath)) return;
        _auctionTable = SpecialAuction.Parse(Godot.FileAccess.GetFileAsString(SpecialAuctionTablePath));
    }

    private void BuildSpecialAuctionPanel()
    {
        _specialAuctionLayer = new CanvasLayer { Layer = SpecialAuctionLayer };
        AddChild(_specialAuctionLayer);

        _specialAuctionPanel = new HudWindow("specialauction", AuctionText(SpecialAuctionTitleText, "Akara's Altar"), bodyMinWidth: 780)
            { Visible = false };
        _specialAuctionPanel.Closed += CloseSpecialAuction;
        _specialAuctionLayer.AddChild(_specialAuctionPanel);

        _auctionTicker = new Godot.Timer { WaitTime = 1.0, OneShot = false };
        _auctionTicker.Timeout += TickSpecialAuction;
        _specialAuctionPanel.AddChild(_auctionTicker);

        var body = _specialAuctionPanel.Body;
        body.AddThemeConstantOverride("separation", 8);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        body.AddChild(tabs);
        AddAuctionTab(tabs, AuctionTab.Today, "Ongoing Auction");
        AddAuctionTab(tabs, AuctionTab.Schedule, "Schedule");
        AddAuctionTab(tabs, AuctionTab.MyInfo, "My Auction Status");
        AddAuctionTab(tabs, AuctionTab.Log, "Auction History");

        var pages = new Control { CustomMinimumSize = new Vector2(0, SpecialAuctionPageHeight) };
        body.AddChild(pages);
        AddAuctionPage(pages, AuctionTab.Today, BuildAuctionTodayPage());
        AddAuctionPage(pages, AuctionTab.Schedule, BuildAuctionSchedulePage());
        AddAuctionPage(pages, AuctionTab.MyInfo, BuildAuctionMyInfoPage());
        AddAuctionPage(pages, AuctionTab.Log, BuildAuctionLogPage());

        _auctionStatus = UiTheme.Text("", 12, UiTheme.Warning, HorizontalAlignment.Center);
        body.AddChild(_auctionStatus);
        var notice = UiTheme.Text(AuctionText(SpecialAuctionMaintenanceText, "Please be aware that auction is not available during maintenance"),
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
        _auctionTabButtons[tab] = button;
    }

    private void AddAuctionPage(Control pages, AuctionTab tab, Control page)
    {
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.Visible = false;
        pages.AddChild(page);
        _auctionPages[tab] = page;
    }

    private Control BuildAuctionTodayPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);

        var lots = UiTheme.Section();
        page.AddChild(lots);
        var grid = new GridContainer { Columns = SpecialAuctionGridColumns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        lots.AddChild(grid);
        for (int i = 0; i < SpecialAuction.LotsPerDay; i++)
        {
            var cell = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            cell.AddThemeConstantOverride("separation", 3);
            grid.AddChild(cell);
            var slot = new ItemSlotView(SpecialAuctionLotSize) { Index = i, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
            slot.Clicked += view => SelectAuctionLot(view.Index);
            slot.Hovered += view => { if (!view.Item.IsEmpty) ShowItemTooltip(-1, view.Item); };
            slot.Unhovered += _ => HideItemTooltip();
            cell.AddChild(slot);
            _auctionLotSlots.Add(slot);
            var mark = UiTheme.Text("", 11, UiTheme.GoldBright, HorizontalAlignment.Center);
            cell.AddChild(mark);
            _auctionLotMarks.Add(mark);
        }

        var nameBar = UiTheme.Section();
        page.AddChild(nameBar);
        _auctionSelectedName = UiTheme.Text("-", 15, UiTheme.TextHi, HorizontalAlignment.Center);
        nameBar.AddChild(_auctionSelectedName);

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

        box.AddChild(UiTheme.SectionTitle(AuctionText(SpecialAuctionBalanceText, "Current Balance")));
        var balance = new HBoxContainer();
        balance.AddThemeConstantOverride("separation", 10);
        box.AddChild(balance);
        _auctionGold = UiTheme.Text("0", 13, UiTheme.Gold);
        _auctionGold.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        balance.AddChild(_auctionGold);
        balance.AddChild(AuctionCheckIcon());
        _auctionChecks = UiTheme.Text("0", 13, UiTheme.TextHi);
        balance.AddChild(_auctionChecks);

        box.AddChild(UiTheme.SectionTitle(AuctionText(SpecialAuctionEnterPriceText, "Enter a price")));
        var entry = new HBoxContainer();
        entry.AddThemeConstantOverride("separation", 4);
        box.AddChild(entry);
        _auctionMillions = UiTheme.NumberBox(0, SpecialAuction.MaxMillions, 1, 70);
        _auctionMillions.ValueChanged += _ => RefreshAuctionTotal();
        _auctionMillions.GetLineEdit().TextChanged += _ => RefreshAuctionTotal();
        entry.AddChild(_auctionMillions);
        var suffix = UiTheme.Text(",000,000", 13, UiTheme.TextLo);
        suffix.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        entry.AddChild(suffix);
        entry.AddChild(AuctionCheckIcon());
        _auctionCheckInput = UiTheme.NumberBox(0, 0, 1, 56);
        _auctionCheckInput.ValueChanged += _ => RefreshAuctionTotal();
        _auctionCheckInput.GetLineEdit().TextChanged += _ => RefreshAuctionTotal();
        entry.AddChild(_auctionCheckInput);
        return section;
    }

    private static Control AuctionCheckIcon() => new TextureRect
    {
        Texture = ItemData.Icon(SpecialAuction.MythrilCheck),
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

        box.AddChild(UiTheme.SectionTitle(AuctionText(SpecialAuctionBidPriceText, "Bid Price")));
        _auctionTotal = UiTheme.Text("0", 18, UiTheme.GoldBright, HorizontalAlignment.Center);
        box.AddChild(_auctionTotal);
        _auctionWords = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        box.AddChild(_auctionWords);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var place = UiTheme.ActionButton(AuctionText(SpecialAuctionPlaceBidText, "Place Bid"), "");
        place.Pressed += PlaceAuctionBid;
        _auctionPlaceBid = place;
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

        box.AddChild(UiTheme.SectionTitle(AuctionText(SpecialAuctionCurrentBidText, "Current Bid")));
        _auctionCurrent = UiTheme.Text("-", 13, UiTheme.TextHi);
        box.AddChild(_auctionCurrent);
        box.AddChild(UiTheme.SectionTitle(AuctionText(SpecialAuctionAvailableBidText, "Available Bid")));
        _auctionMinimum = UiTheme.Text("-", 13, UiTheme.TextHi);
        box.AddChild(_auctionMinimum);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 8);
        box.AddChild(foot);
        var refresh = UiTheme.SmallButton("Refresh", "Refresh the bids");
        refresh.Pressed += RefreshAuctionToday;
        _auctionRefresh = refresh;
        foot.AddChild(refresh);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(UiTheme.Text(AuctionText(SpecialAuctionRemainingText, "Remaining time"), 12, UiTheme.TextLo));
        _auctionClock = UiTheme.Text("00:00:00", 13, UiTheme.TextHi);
        foot.AddChild(_auctionClock);
        return section;
    }

    private Control BuildAuctionSchedulePage()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        _auctionSchedule = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _auctionSchedule.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_auctionSchedule);
        return scroll;
    }

    private Control BuildAuctionMyInfoPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);
        _auctionBidList = AddAuctionListSection(page, AuctionText(SpecialAuctionBidListText, "Bid Item List"));
        _auctionWinList = AddAuctionListSection(page, AuctionText(SpecialAuctionWonListText, "Successful Bid Item List"));
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
        _auctionLog = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _auctionLog.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_auctionLog);
        return section;
    }

    private void OpenSpecialAuction()
    {
        if (_specialAuctionShown)
        {
            CloseSpecialAuction();
            return;
        }
        if (_auctionTransactions.Busy)
        {
            Notice.Show(this, AuctionText(SpecialAuctionDelayText, "Try again later"), AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
            return;
        }
        EnsureAuctionTable();
        ResetSpecialAuction();
        _auctionOpening = true;
        Net.I.SendAuctionRequest(Net.AuctionTodaySub);
    }

    private void ResetSpecialAuction()
    {
        _auctionSelected = -1;
        _auctionLots = Array.Empty<AuctionLot>();
        _auctionMillions.Value = 0;
        _auctionCheckInput.Value = 0;
        _auctionClickedAt = double.NegativeInfinity;
        _auctionRefreshedAt = double.NegativeInfinity;
        SetAuctionStatus("");
    }

    private void ShowSpecialAuction(AuctionTab tab)
    {
        _specialAuctionShown = true;
        _auctionOpening = false;
        _specialAuctionPanel.Visible = true;
        ShowAuctionTab(tab);
        RefreshAuctionBalance();
        if (_auctionTicker.IsInsideTree()) _auctionTicker.Start();
    }

    private void CloseSpecialAuction()
    {
        _auctionOpening = false;
        _auctionConfirmation?.Close();
        _auctionConfirmation = null;
        if (!_specialAuctionShown) return;
        _specialAuctionShown = false;
        _specialAuctionPanel.Visible = false;
        _auctionTicker.Stop();
        HideItemTooltip();
    }

    private void ShowAuctionTab(AuctionTab tab)
    {
        _auctionTab = tab;
        foreach (var (key, page) in _auctionPages) page.Visible = key == tab;
        foreach (var (key, button) in _auctionTabButtons)
        {
            bool on = key == tab;
            button.SetPressedNoSignal(on);
            button.AddThemeStyleboxOverride("normal", UiTheme.TopTab(on));
            button.AddThemeStyleboxOverride("hover", UiTheme.TopTab(on, true));
            button.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
        }
        if (tab == AuctionTab.Schedule) RenderAuctionSchedule();
    }

    private double AuctionNow => Time.GetTicksMsec() / 1000.0;

    private bool TakeAuctionClick()
    {
        if (AuctionNow - _auctionClickedAt < SpecialAuctionClickDelay)
        {
            SetAuctionStatus(AuctionText(SpecialAuctionDelayText, "Try again later"));
            return false;
        }
        _auctionClickedAt = AuctionNow;
        SetAuctionStatus("");
        return true;
    }

    private void PressAuctionTab(AuctionTab tab)
    {
        foreach (var (key, button) in _auctionTabButtons) button.SetPressedNoSignal(key == _auctionTab);
        if (!TakeAuctionClick()) return;
        if (_auctionTransactions.Busy && tab is AuctionTab.Today or AuctionTab.MyInfo)
        {
            ShowAuctionTab(tab);
            return;
        }
        switch (tab)
        {
            case AuctionTab.Today:
                Net.I.SendAuctionRequest(Net.AuctionTodaySub);
                break;
            case AuctionTab.Schedule:
                ShowAuctionTab(tab);
                break;
            case AuctionTab.MyInfo:
                Net.I.SendAuctionRequest(Net.AuctionMyInfoSub);
                break;
            case AuctionTab.Log:
                ShowAuctionTab(tab);
                Net.I.SendAuctionRequest(Net.AuctionLogSub);
                break;
        }
    }

    private void RefreshAuctionToday()
    {
        if (_auctionTransactions.Busy) return;
        double waited = AuctionNow - _auctionRefreshedAt;
        if (waited < SpecialAuctionRefreshDelay)
        {
            int left = (int)Math.Ceiling(SpecialAuctionRefreshDelay - waited);
            SetAuctionStatus(AuctionText(SpecialAuctionRefreshWaitText, "It is available in %d seconds").Replace("%d", left.ToString()));
            return;
        }
        _auctionRefreshedAt = AuctionNow;
        _auctionRefreshing = true;
        SetAuctionStatus("");
        Net.I.SendAuctionRequest(Net.AuctionTodaySub);
    }

    private void SetAuctionStatus(string text)
    {
        _auctionStatus.Text = text;
        _auctionStatus.Visible = text.Length > 0;
    }

    private void TickSpecialAuction()
    {
        if (_auctionTab != AuctionTab.Today) return;
        _auctionSecondsLeft = Math.Max(0, _auctionSecondsLeft - 1);
        _auctionClock.Text = SpecialAuction.Clock((int)_auctionSecondsLeft);
        RefreshAuctionBalance();
    }

    private void OnAuctionToday(AuctionToday today)
    {
        if (!_specialAuctionShown && !_auctionOpening) return;
        _auctionRefreshing = false;
        if (today.Status != SpecialAuction.NothingToBid && (today.Group == 0 || today.Day > SpecialAuction.DaysPerGroup))
        {
            _auctionOpening = false;
            return;
        }

        _auctionToday = today;
        _auctionSecondsLeft = today.Seconds;
        _auctionClock.Text = SpecialAuction.Clock(today.Seconds);
        _auctionLots = today.Status == SpecialAuction.Bidding ? SpecialAuction.Live(_auctionTable, today) : Array.Empty<AuctionLot>();
        RenderAuctionLots();
        ShowSpecialAuction(AuctionTab.Today);

        int notice = today.Status switch
        {
            SpecialAuction.NothingToBid => SpecialAuction.NoItemText,
            SpecialAuction.CollectOnly => SpecialAuction.CollectOnlyText,
            SpecialAuction.Settling => SpecialAuction.SettlingText,
            SpecialAuction.Bidding when _auctionLots.Count == 0 => SpecialAuction.NoItemText,
            _ => 0,
        };
        if (notice != 0) Notice.Show(this, AuctionText(notice, ""), AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
    }

    private void RenderAuctionLots()
    {
        string me = AuctionPlayerName();
        for (int i = 0; i < _auctionLotSlots.Count; i++)
        {
            var lot = _auctionLots.FirstOrDefault(l => l.Slot == i);
            var slot = _auctionLotSlots[i];
            if (lot == null)
            {
                slot.Clear();
                slot.Look = SlotLook.Dimmed;
                _auctionLotMarks[i].Text = "";
                continue;
            }
            slot.Set(new ItemSlot { ItemId = lot.ItemId, Count = (short)lot.Count, Durability = TooltipItem(lot.ItemId).Durability });
            slot.Look = i == _auctionSelected ? SlotLook.Selected : SlotLook.Normal;
            _auctionLotMarks[i].Text = me.Length > 0 && lot.TopBidder == me ? "Your bid" : "";
        }
        if (_auctionLots.All(l => l.Slot != _auctionSelected)) _auctionSelected = -1;
        RenderAuctionSelection();
    }

    private AuctionLot? SelectedAuctionLot => _auctionLots.FirstOrDefault(l => l.Slot == _auctionSelected);

    private void SelectAuctionLot(int slot)
    {
        if (_auctionTransactions.Bid != null || _auctionLots.All(l => l.Slot != slot)) return;
        _auctionSelected = slot;
        for (int i = 0; i < _auctionLotSlots.Count; i++)
            if (!_auctionLotSlots[i].Item.IsEmpty)
                _auctionLotSlots[i].Look = i == slot ? SlotLook.Selected : SlotLook.Normal;
        RenderAuctionSelection();
    }

    private void RenderAuctionSelection()
    {
        RefreshAuctionTransactions();
        var lot = SelectedAuctionLot;
        if (lot == null)
        {
            _auctionSelectedName.Text = "-";
            _auctionSelectedName.AddThemeColorOverride("font_color", UiTheme.TextHi);
            _auctionCurrent.Text = "-";
            _auctionMinimum.Text = "-";
            return;
        }
        _auctionSelectedName.Text = ItemData.DisplayName(lot.ItemId);
        _auctionSelectedName.AddThemeColorOverride("font_color", ItemGrade.Tint(lot.ItemId));
        _auctionCurrent.Text = $"{lot.Current:n0}";
        _auctionMinimum.Text = $"{lot.MinimumBid:n0}";
    }

    private int AuctionChecksInBag => SpecialAuction.CheckSlots(Inv).Count;

    private void RefreshAuctionBalance()
    {
        int checks = AuctionChecksInBag;
        _auctionGold.Text = GoldAmount(Sheet.Gold);
        _auctionChecks.Text = checks.ToString();
        _auctionMillions.MaxValue = Math.Max(0, SpecialAuction.ClampMillions(SpecialAuction.MaxMillions, Sheet.Gold));
        _auctionCheckInput.MaxValue = checks;
        RefreshAuctionTotal();
    }

    private (int Millions, int Checks) AuctionEntry =>
        (SpecialAuction.ClampMillions((long)_auctionMillions.Value, Sheet.Gold),
         SpecialAuction.ClampChecks((long)_auctionCheckInput.Value, AuctionChecksInBag));

    private void RefreshAuctionTotal()
    {
        var (millions, checks) = AuctionEntry;
        long total = SpecialAuction.Total(millions, checks);
        _auctionTotal.Text = $"{total:n0}";
        _auctionWords.Text = SpecialAuction.InWords(total);
    }

    private void RefreshAuctionTransactions()
    {
        bool busy = _auctionTransactions.Busy;
        _auctionPlaceBid.Disabled = busy || SelectedAuctionLot == null;
        _auctionRefresh.Disabled = busy;
        _auctionMillions.Editable = _auctionCheckInput.Editable = !busy;
        foreach (var button in _auctionRowButtons) button.Disabled = busy;
    }

    private void PlaceAuctionBid()
    {
        if (_auctionTransactions.Busy || !TakeAuctionClick()) return;
        var (millions, checks) = AuctionEntry;
        if (SpecialAuction.Total(millions, checks) <= 0)
        {
            Notice.Show(this, AuctionText(SpecialAuction.EnterPriceText, "Please try again after entering bidding price"),
                AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
            return;
        }
        _auctionConfirmation = Notice.Confirm(this, AuctionText(SpecialAuctionConfirmBidText, "Do you wish to place a bid?"), "Yes", "No",
            ConfirmAuctionBid, title: AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
    }

    private void ConfirmAuctionBid()
    {
        _auctionConfirmation = null;
        if (!_specialAuctionShown || _auctionTransactions.Busy) return;
        var lot = SelectedAuctionLot;
        var (millions, checks) = AuctionEntry;
        var checkSlots = SpecialAuction.CheckSlots(Inv);
        int refusal = SpecialAuction.BidRefusal(lot, millions, checks, checkSlots.Count);
        if (refusal != 0)
        {
            Notice.Show(this, AuctionText(refusal, ""), AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
            return;
        }
        if (!_auctionTransactions.BeginBid(lot!, SpecialAuction.Total(millions, checks), _auctionToday.Group, _auctionToday.Day)) return;
        RefreshAuctionTransactions();
        Net.I.SendAuctionBid(lot!, checkSlots.Take(checks).ToList(), millions);
    }

    private void OnAuctionBid(short result)
    {
        var submitted = _auctionTransactions.CompleteBid();
        if (submitted == null) return;
        if (result == SpecialAuction.Success && submitted.IsFor(_auctionToday))
        {
            _auctionLots = submitted.Apply(_auctionLots, AuctionPlayerName());
            _auctionMillions.Value = 0;
            _auctionCheckInput.Value = 0;
            RenderAuctionLots();
        }
        RefreshAuctionBalance();
        RefreshAuctionTransactions();
        ShowAuctionResult(SpecialAuction.BidResultText(result));
    }

    private void ShowAuctionResult(int textId)
    {
        string text = AuctionText(textId, "");
        if (_specialAuctionShown) Notice.Show(this, text, AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
        else if (text.Length > 0) CombatNotice(text);
    }

    private void OnAuctionMyInfo(short result, IReadOnlyList<AuctionBidRow> rows)
    {
        if (!_specialAuctionShown) return;
        _auctionBids.Clear();
        _auctionWins.Clear();
        if (result == SpecialAuction.Success)
        {
            _auctionBids.AddRange(rows.Where(r => SpecialAuction.IsBid(r.Status)));
            _auctionWins.AddRange(rows.Where(r => r.Status == SpecialAuction.Won));
        }
        RenderAuctionMyInfo();
        ShowAuctionTab(AuctionTab.MyInfo);
        if (result == SpecialAuction.MyInfoSettling)
            Notice.Show(this, AuctionText(SpecialAuction.ClaimLaterText, ""), AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
        else if (result != SpecialAuction.Success)
            Notice.Show(this, AuctionText(SpecialAuction.DatabaseErrorText, "Database Error"), AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
    }

    private void RenderAuctionMyInfo()
    {
        _auctionRowButtons.Clear();
        FillAuctionRows(_auctionBidList, _auctionBids, won: false);
        FillAuctionRows(_auctionWinList, _auctionWins, won: true);
        RefreshAuctionTransactions();
    }

    private void FillAuctionRows(VBoxContainer list, List<AuctionBidRow> rows, bool won)
    {
        ClearChildren(list);
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

        var slot = new ItemSlotView(SpecialAuctionRowSlotSize) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        slot.Set(new ItemSlot { ItemId = row.ItemId, Count = row.Count, Durability = TooltipItem(row.ItemId).Durability });
        slot.Hovered += view => { if (!view.Item.IsEmpty) ShowItemTooltip(-1, view.Item); };
        slot.Unhovered += _ => HideItemTooltip();
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
                row.Status == SpecialAuction.TopBidder ? UiTheme.Good : UiTheme.Warning);
            text.AddChild(state);
            text.AddChild(UiTheme.Text($"{AuctionText(SpecialAuctionMyBidText, "My Bid Price")}  {row.Price:n0}", 12, UiTheme.TextLo));
        }

        if (won || SpecialAuction.CanRetract(row.Status))
        {
            var button = UiTheme.SmallButton(won ? AuctionText(SpecialAuctionReceiveText, "Receive") : AuctionText(SpecialAuctionRetractText, "Retract Bid"), "");
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            button.Pressed += () => PressAuctionRow(row, won);
            line.AddChild(button);
            _auctionRowButtons.Add(button);
        }
        return panel;
    }

    private void PressAuctionRow(AuctionBidRow row, bool won)
    {
        if (_auctionTransactions.Busy || !TakeAuctionClick()) return;
        bool listed = won ? row.Status == SpecialAuction.Won && _auctionWins.Contains(row)
            : SpecialAuction.CanRetract(row.Status) && _auctionBids.Contains(row);
        if (!listed || !_auctionTransactions.BeginRow(row, won)) return;
        RefreshAuctionTransactions();
        if (won) Net.I.SendAuctionReceive(row);
        else Net.I.SendAuctionRetract(row);
    }

    private void OnAuctionCollect(short result)
    {
        var request = _auctionTransactions.CompleteRow(claim: false);
        if (request == null) return;
        if (SpecialAuction.Collected(result))
        {
            _auctionBids.Remove(request.Row);
            RenderAuctionMyInfo();
        }
        RefreshAuctionBalance();
        RefreshAuctionTransactions();
        ShowAuctionResult(SpecialAuction.CollectResultText(result));
    }

    private void OnAuctionClaim(short result)
    {
        var request = _auctionTransactions.CompleteRow(claim: true);
        if (request == null) return;
        if (result == SpecialAuction.Success)
        {
            _auctionWins.Remove(request.Row);
            RenderAuctionMyInfo();
        }
        RefreshAuctionTransactions();
        ShowAuctionResult(SpecialAuction.ClaimResultText(result));
    }

    private void RenderAuctionSchedule()
    {
        ClearChildren(_auctionSchedule);
        if (_auctionToday.Group == 0) return;
        foreach (var day in SpecialAuction.Upcoming(_auctionTable, _auctionToday.Group, _auctionToday.Day))
            _auctionSchedule.AddChild(BuildAuctionScheduleDay(day));
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
                line.AddChild(UiTheme.IconTile("?", AuctionText(SpecialAuctionSecretText, "Result will be shown on the auction day"),
                    new Vector2(SpecialAuctionRowSlotSize, SpecialAuctionRowSlotSize)));
                continue;
            }
            var slot = new ItemSlotView(SpecialAuctionRowSlotSize);
            slot.Set(new ItemSlot { ItemId = lot.Item, Count = (short)lot.Count, Durability = TooltipItem(lot.Item).Durability });
            slot.Hovered += view => { if (!view.Item.IsEmpty) ShowItemTooltip(-1, view.Item); };
            slot.Unhovered += _ => HideItemTooltip();
            line.AddChild(slot);
        }
        return panel;
    }

    private void OnAuctionLog(IReadOnlyList<IReadOnlyList<AuctionResultLine>> days)
    {
        if (!_specialAuctionShown) return;
        ClearChildren(_auctionLog);
        for (int d = 0; d < days.Count; d++)
        {
            var colour = new Color(SpecialAuction.LogColour(d));
            foreach (var result in days[d])
            {
                string text = AuctionResultText(result);
                if (text.Length > 0) _auctionLog.AddChild(UiTheme.Text(text, 13, colour));
            }
        }
        if (_auctionLog.GetChildCount() == 0)
            _auctionLog.AddChild(UiTheme.Text(AuctionText(SpecialAuctionNoHistoryText, "There is no transaction history"), 13, UiTheme.TextDim));
        ShowAuctionTab(AuctionTab.Log);
    }

    private static string AuctionResultText(AuctionResultLine result)
    {
        if (result.ItemId == 0 || ItemData.Get(result.ItemId) == null) return "";
        string name = ItemData.DisplayName(result.ItemId);
        return result.Status switch
        {
            SpecialAuction.Won => ReplaceFirst(ReplaceFirst(AuctionText(SpecialAuctionSoldText, "[%s] Item bid has been successfully accepted to [%s]"), name), $"{result.Price:n0}"),
            SpecialAuction.Cancelled => ReplaceFirst(AuctionText(SpecialAuctionFailedText, "[%s] Item has been failed in bidding"), name),
            _ => "",
        };
    }

    private static string ReplaceFirst(string text, string value)
    {
        int at = text.IndexOf("%s", StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + value + text[(at + 2)..];
    }

    private void OnAuctionEnded(IReadOnlyList<AuctionResultLine> results)
    {
        CloseSpecialAuction();
        var lines = new List<string> { AuctionText(SpecialAuctionEndedText, "[Today's auction has been ended]") };
        foreach (var result in results)
        {
            string text = AuctionResultText(result);
            if (text.Length == 0) continue;
            lines.Add(text);
            CombatNotice(text);
        }
        ShowUpgradeNoticeBanner(string.Join("\n", lines), SpecialAuctionEndedColour);
    }

    private void OnAuctionMiscarried(bool ok)
    {
        CloseSpecialAuction();
        if (!ok)
        {
            Notice.Show(this, "Shutdown Command Failed", AuctionText(SpecialAuctionTitleText, "Akara's Altar"));
            return;
        }
        ShowUpgradeNoticeBanner($"{AuctionText(SpecialAuctionMiscarriedText, "[Akara's Altar Auction has been miscarried]")}\n"
            + AuctionText(SpecialAuctionAllCancelledText, "[All auction items has been canceled in bidding]"), SpecialAuctionCancelledColour);
    }
}
