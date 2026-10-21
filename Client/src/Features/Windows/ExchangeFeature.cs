using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class ExchangeFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly ExchangeSession exchange;
    private readonly GenieSession genie;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly ZoneSession zone;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly IExchangeRequests requests;
    private readonly VendorFeature vendor;
    private readonly WarehouseFeature warehouse;
    private readonly CharacterSealFeature characterSealWindow;
    private readonly EntityStatesSystem entityStates;

    public ExchangeFeature(IWorldContext context, ExchangeSession exchange, GenieSession genie, ProgressSession progress, SelfSession self, ZoneSession zone, ZoneAbilitySession zoneAbility, IExchangeRequests requests, VendorFeature vendor, WarehouseFeature warehouse, CharacterSealFeature characterSealWindow, EntityStatesSystem entityStates)
    {
        this.context = context;
        this.exchange = exchange;
        this.genie = genie;
        this.progress = progress;
        this.self = self;
        this.zone = zone;
        this.zoneAbility = zoneAbility;
        this.requests = requests;
        this.vendor = vendor;
        this.warehouse = warehouse;
        this.characterSealWindow = characterSealWindow;
        this.entityStates = entityStates;
    }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;

    internal const int TradeOfferSlots = 12;
    private const int TradeGridColumns = 4;
    private const float TradeCellSize = 42f;
    private const int TextTradeOwnGenie = 31082;
    private const int TextTradePartnerGenie = 31083;
    private const int TextTradeBusy = 3504;
    private const int TextTradeDeclined = 6311;
    private const int TextTradeDuplicate = 10761;
    private static readonly Color TradeBusyColour = new("9b9bff");
    private static readonly Color TradeConfirmedColour = new("7ddc6f");

    private readonly ItemSlotView[] mineCells = new ItemSlotView[TradeOfferSlots];
    private readonly ItemSlotView[] theirsCells = new ItemSlotView[TradeOfferSlots];
    private Label mineGold = null!;
    private Label theirsGold = null!;
    private Label status = null!;
    internal Label MineHeader { get; private set; } = null!;
    internal Label TheirsHeader { get; private set; } = null!;
    private Label mineState = null!;
    private Label theirsState = null!;
    private Label purse = null!;
    private Label roomLabel = null!;
    private BagCompanion? companion;
    private Button confirmBtn = null!;
    private Button cancelBtn = null!;
    private LineEdit goldEdit = null!;
    private ConfirmationDialog askDialog = null!;
    private bool shown;

    internal bool IsOpen => shown;

    private bool requestPending;
    internal bool ConfirmedByMe { get; private set; }
    internal bool ConfirmedByPartner { get; set; }
    private int partnerId = -1;
    internal string PartnerName { get; set; } = "Player";
    internal int MyGoldOffer { get; set; }
    internal int TheirGoldOffer { get; private set; }
    private bool addInFlight;
    private PendingAdd pending;
    internal List<OfferItem> MyOffer { get; } = new();
    internal List<OfferItem> TheirOffer { get; } = new();
    private CanvasLayer waitLayer = null!;
    internal Label WaitLabel { get; private set; } = null!;
    internal bool Waiting { get; private set; }
    private CanvasLayer amountLayer = null!;
    internal TextureRect AmountIcon { get; private set; } = null!;
    private Label amountName = null!;
    private Label amountHint = null!;
    private SpinBox amountSpin = null!;
    private bool amountShown;
    private int amountSlot = -1;
    private int amountItem;
    private int amountMax = 1;

    private struct PendingAdd { public bool IsGold; public int ItemId; public int SourceAbs; public int Count; public short Dura; public int UniqueId; }

    internal struct OfferItem { public int ItemId; public int Count; public short Dura; public int SourceAbs; public int UniqueId; }

    internal const float TradeRange = 8f;
    private const int UntradableText = 7701;

    internal bool TradeHoldsInput => shown || Waiting || requestPending;

    public void Build()
    {
        BuildPanel();
        zone.TradeBlockedEvent += OnTradeBlocked;
        exchange.ExchangeRequestEvent += OnRequest;
        exchange.ExchangeAgreeEvent += OnAgree;
        exchange.ExchangeAddResultEvent += OnAddResult;
        exchange.ExchangeOtherAddEvent += OnOtherAdd;
        exchange.ExchangeOtherDecideEvent += OnOtherDecide;
        exchange.ExchangeDoneEvent += OnDone;
        exchange.ExchangeCancelEvent += OnCancel;
        exchange.ExchangeDuplicateEvent += OnDuplicate;
    }

    private void OnTradeBlocked(string text) => Notice.Show(context.Root, text);

    bool IEscapable.CloseOnEscape()
    {
        if (amountShown) CloseAmount();
        else if (Waiting) CancelRequest();
        else if (IsOpen) Abort(local: true);
        else return false;
        return true;
    }

    public void Dispose()
    {
        zone.TradeBlockedEvent -= OnTradeBlocked;
        exchange.ExchangeRequestEvent -= OnRequest;
        exchange.ExchangeAgreeEvent -= OnAgree;
        exchange.ExchangeAddResultEvent -= OnAddResult;
        exchange.ExchangeOtherAddEvent -= OnOtherAdd;
        exchange.ExchangeOtherDecideEvent -= OnOtherDecide;
        exchange.ExchangeDoneEvent -= OnDone;
        exchange.ExchangeCancelEvent -= OnCancel;
        exchange.ExchangeDuplicateEvent -= OnDuplicate;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("exchange", "Trade") { Visible = false };
        Panel.Closed += () => { if (shown) Abort(local: true); };
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);
        cols.AddChild(BuildOfferGrid(mineCells, mine: true, out var mineTitle, out mineState, out mineGold));
        cols.AddChild(BuildOfferGrid(theirsCells, mine: false, out var theirsTitle, out theirsState, out theirsGold));
        MineHeader = mineTitle;
        TheirsHeader = theirsTitle;

        root.AddChild(new HSeparator());

        purse = UiTheme.Text("", 12, UiTheme.Gold);
        root.AddChild(purse);
        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 6);
        var goldLbl = HudStyle.Label(13); goldLbl.Text = "Gold:";
        goldRow.AddChild(goldLbl);
        goldEdit = new LineEdit { PlaceholderText = "amount", CustomMinimumSize = new Vector2(110, 0) };
        goldEdit.TextSubmitted += _ => OnAddGold();
        goldRow.AddChild(goldEdit);
        var allGoldBtn = new Button { Text = "All", FocusMode = Control.FocusModeEnum.None };
        allGoldBtn.Pressed += () => goldEdit.Text = self.Sheet.Gold.ToString();
        goldRow.AddChild(allGoldBtn);
        var addGoldBtn = new Button { Text = "Add gold", FocusMode = Control.FocusModeEnum.None };
        addGoldBtn.Pressed += OnAddGold;
        goldRow.AddChild(addGoldBtn);
        root.AddChild(goldRow);

        roomLabel = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(roomLabel);

        root.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        confirmBtn = new Button { Text = "Confirm", FocusMode = Control.FocusModeEnum.None };
        confirmBtn.Pressed += OnConfirm;
        footer.AddChild(confirmBtn);
        cancelBtn = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancelBtn.Pressed += () => Abort(local: true);
        footer.AddChild(cancelBtn);
        status = HudStyle.Label(13, HorizontalAlignment.Right);
        status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(status);
        root.AddChild(footer);

        askDialog = new ConfirmationDialog { Title = "Trade request" };
        askDialog.GetOkButton().Text = "Accept";
        askDialog.GetCancelButton().Text = "Decline";
        askDialog.Confirmed += () => AnswerRequest(true);
        askDialog.Canceled += () => AnswerRequest(false);
        layer.AddChild(askDialog);

        BuildWaitPanel();
        BuildAmountPrompt();
    }

    internal void BuildWaitPanel()
    {
        waitLayer = new CanvasLayer { Layer = UiLayers.Prompts, Visible = false };
        context.Root.AddChild(waitLayer);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        waitLayer.AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 16, 13, 16, 13);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        WaitLabel = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        WaitLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        WaitLabel.CustomMinimumSize = new Vector2(280, 0);
        root.AddChild(WaitLabel);

        var buttons = new HBoxContainer();
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += CancelRequest;
        buttons.AddChild(cancel);
    }

    internal void BuildAmountPrompt()
    {
        amountLayer = new CanvasLayer { Layer = UiLayers.Prompts, Visible = false };
        context.Root.AddChild(amountLayer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        amountLayer.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        amountLayer.AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 14, 12, 14, 12);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        root.AddChild(head);
        AmountIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(AmountIcon);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        amountName = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(amountName);
        amountHint = UiTheme.Text("", 11, UiTheme.TextLo);
        headText.AddChild(amountHint);

        var amountRow = new HBoxContainer();
        amountRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(amountRow);
        var amountLbl = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        amountLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        amountRow.AddChild(amountLbl);
        amountSpin = new SpinBox
        {
            MinValue = 1,
            MaxValue = Inventory.StackMax,
            Step = 1,
            Value = 1,
            CustomMinimumSize = new Vector2(110, 0),
        };
        amountSpin.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
        amountRow.AddChild(amountSpin);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += CloseAmount;
        buttons.AddChild(cancel);
        var ok = new Button { Text = "Offer", FocusMode = Control.FocusModeEnum.None };
        ok.Pressed += ConfirmAmount;
        buttons.AddChild(ok);
    }

    private Control BuildOfferGrid(ItemSlotView[] cells, bool mine, out Label header, out Label state, out Label count)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        header = UiTheme.Text("", 13, UiTheme.GoldBright);
        header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(header);
        state = UiTheme.Text("", 11, UiTheme.TextLo, HorizontalAlignment.Right);
        head.AddChild(state);
        box.AddChild(head);
        var grid = new GridContainer { Columns = TradeGridColumns };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new ItemSlotView(TradeCellSize) { Index = i, Veiled = true };
            cell.Hovered += view => context.Tooltips.Show(-1, view.Item);
            cell.Unhovered += _ => context.Tooltips.Hide();
            cell.RightClicked += view => characterSealWindow.ShowSealedSheet(view.Item);
            if (mine)
            {
                cell.CanDrop = (_, data) => CanDropOnOffer(data);
                cell.Dropped = (_, data) => DropOnOffer(data);
            }
            cells[i] = cell;
            grid.AddChild(cell);
        }
        box.AddChild(grid);
        count = UiTheme.Text("", 12, UiTheme.Gold);
        box.AddChild(count);
        return box;
    }

    internal void BeginTradeRequest(int charId, string name)
    {
        if (TradeHoldsInput)
        {
            context.Log.Notice("You are already trading.");
            return;
        }
        if (genie.GenieRunning)
        {
            TradeRefusalLine(TextTradeOwnGenie, "You cannot trade while using Genie.");
            return;
        }
        if (entityStates.GenieUsers.Contains(charId))
        {
            TradeRefusalLine(TextTradePartnerGenie, "You cannot trade with users who are using Genie.");
            return;
        }
        if (context.Entities.Find(charId) is { } partner
            && partner.Nation != self.Nation
            && !zoneAbility.CurrentZoneAbility.CanTrade)
        {
            context.Log.Notice("The two nations cannot trade here.");
            return;
        }

        partnerId = charId;
        PartnerName = name;
        requests.ExchangeRequest(charId);
        ShowWait($"Waiting for {name} to accept the trade…");
    }

    internal void ShowWait(string text)
    {
        WaitLabel.Text = text;
        waitLayer.Visible = true;
        Waiting = true;
    }

    private void HideWait()
    {
        if (!Waiting) return;
        Waiting = false;
        waitLayer.Visible = false;
    }

    internal void CancelRequest()
    {
        if (!Waiting) return;
        HideWait();
        requests.Cancel();
        ResetState();
        partnerId = -1;
        context.Log.Notice("Trade request cancelled.");
    }

    private void OnRequest(int requesterCharId)
    {
        if (requestPending) return;
        if (genie.GenieRunning || entityStates.GenieUsers.Contains(requesterCharId))
        {
            if (genie.GenieRunning) TradeRefusalLine(TextTradeOwnGenie, "You cannot trade while using Genie.");
            else TradeRefusalLine(TextTradePartnerGenie, "You cannot trade with users who are using Genie.");
            requests.Agree(false);
            return;
        }
        if (Config.TradeRequestsBlocked)
        {
            context.Log.Refusal(CommandCatalog.TradeRequestsRefusedText, "Has blocked trade requests.");
            requests.Agree(false);
            return;
        }
        if (shown || Waiting)
        {
            context.Log.Add(ItemData.Text(TextTradeBusy, "A trade request has been received from another user while you were trading."),
                CombatLogKind.Status, TradeBusyColour);
            requests.Agree(false);
            return;
        }
        vendor.Close();
        warehouse.Close();
        partnerId = requesterCharId;
        PartnerName = context.Entities.Find(requesterCharId)?.Name ?? "Player";
        requestPending = true;
        askDialog.DialogText = $"{PartnerName} wants to trade.\nAccept?";
        askDialog.PopupCentered();
        context.Log.Notice($"{PartnerName} wants to trade.");
    }

    private void AnswerRequest(bool accept)
    {
        if (!requestPending) return;
        requestPending = false;
        requests.Agree(accept);
        if (accept) Open();
    }

    private void OnAgree(bool accepted)
    {
        HideWait();
        if (accepted) Open();
        else
        {
            TradeRefusalLine(TextTradeDeclined, "The user has declined the request for a trade.");
            ResetState();
        }
    }

    private void Open()
    {
        ResetState();
        Panel.Title = $"Trade — {PartnerName}";
        confirmBtn.Disabled = false;
        confirmBtn.Text = "Confirm";
        SetStatus("", false);
        MineHeader.Text = "You offer";
        TheirsHeader.Text = $"{PartnerName} offers";
        Panel.Visible = true;
        shown = true;
        companion ??= new BagCompanion(TradeTakeFromBag, TradeBagFit, _ => "", () => Abort(local: true));
        context.BagCompanions.Attach(companion);
        RefreshOffers();
    }

    private bool TradeTakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        OfferSlot(abs);
        return true;
    }

    private bool CanDropOnOffer(Variant data) =>
        shown
        && data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary().ContainsKey("invFrom")
    && self.Inventory.InMainBag(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void DropOnOffer(Variant data)
    {
        var d = data.AsGodotDictionary();
        OfferSlot(d["invFrom"].AsInt32(), DragLayer.ChosenAmount(d));
    }

    private BagFit TradeBagFit(int abs) =>
        ItemStacks.UseRefusal(ItemUse.Trade, self.Inventory[abs]) == ItemUseRules.Allowed ? BagFit.Normal : BagFit.Unfit;

    private void TradeRefusalLine(int textId, string fallback) =>
        context.Log.Add(ItemData.Text(textId, fallback), CombatLogKind.Status, UiKit.WhRefusalTint);

    private void OnDuplicate()
    {
        string text = ItemData.Text(TextTradeDuplicate, "This item is a duplicated item.");
        context.Log.Add(text, CombatLogKind.Status, UiKit.WhRefusalTint);
        if (shown) SetStatus(text, true);
    }

    internal void Abort(bool local)
    {
        if (!shown) return;
        if (local) requests.Cancel();
        RestoreMyOffer();
        CloseWindow();
    }

    private void CloseWindow()
    {
        context.Tooltips.Hide();
        shown = false;
        Panel.Visible = false;
        ResetState();
        context.Plugins.Trade();
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    private void ResetState()
    {
        MyOffer.Clear();
        TheirOffer.Clear();
        MyGoldOffer = 0;
        TheirGoldOffer = 0;
        ConfirmedByMe = false;
        ConfirmedByPartner = false;
        addInFlight = false;
        CloseAmount();
        if (goldEdit != null) goldEdit.Text = "";
    }

    internal void RefreshOffers()
    {
        FillOfferGrid(mineCells, MyOffer);
        FillOfferGrid(theirsCells, TheirOffer);
        mineGold.Text = OfferSummary(MyOffer.Count, MyGoldOffer);
        theirsGold.Text = OfferSummary(TheirOffer.Count, TheirGoldOffer);
        SetOfferState(mineState, ConfirmedByMe);
        SetOfferState(theirsState, ConfirmedByPartner);
        purse.Text = $"You have {self.Sheet.Gold:n0} gold.";
        RefreshTradeRoom();
        context.BagCompanions.RefreshFit();
        context.Plugins.Trade();
    }

    private static string OfferSummary(int items, int gold) =>
        gold > 0 ? $"{items} / {TradeOfferSlots} items   + {gold:n0} gold" : $"{items} / {TradeOfferSlots} items";

    private static void SetOfferState(Label state, bool confirmed)
    {
        state.Text = confirmed ? "Confirmed" : "Not confirmed";
        state.AddThemeColorOverride("font_color", confirmed ? TradeConfirmedColour : UiTheme.TextLo);
    }

    private static void FillOfferGrid(ItemSlotView[] cells, List<OfferItem> offer)
    {
        for (int i = 0; i < cells.Length; i++)
            cells[i].Set(i < offer.Count
                ? new ItemSlot { ItemId = offer[i].ItemId, Count = (short)offer[i].Count, Durability = offer[i].Dura, UniqueId = offer[i].UniqueId }
                : default);
    }

    private void RefreshTradeRoom()
    {
        int free = 0;
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].IsEmpty) free++;
        long weight = context.Inventory.CarriedWeight() + TheirOffer.Sum(o => OfferWeight(o)) - MyOffer.Sum(o => OfferWeight(o));
        var room = new TradeRoom(free, TheirOffer.Count, weight, self.Sheet.MaxWeight);
        roomLabel.Text = self.Sheet.MaxWeight > 0
            ? $"Free slots: {free} (incoming {room.Incoming})   Weight after trade: {weight / 10f:0.0} / {self.Sheet.MaxWeight / 10f:0.0}"
            : $"Free slots: {free} (incoming {room.Incoming})";
        roomLabel.AddThemeColorOverride("font_color", room.Fits ? UiTheme.TextLo : UiTheme.Bad);
    }

    private static long OfferWeight(OfferItem offer) =>
        ItemData.Get(offer.ItemId) is { } def
            ? (long)def.Weight * ItemData.CarriedUnits(def, new ItemSlot { ItemId = offer.ItemId, Count = (short)offer.Count, Durability = offer.Dura })
        : 0;

    internal void OfferSlot(int absSlot, int? amount = null)
    {
        if (ConfirmedByMe) { SetStatus("You already confirmed.", true); return; }
        if (addInFlight || ConfirmedByPartner) return;
        if (absSlot < 0 || absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        var slot = self.Inventory[absSlot];
        if (ItemStacks.UseRefusal(ItemUse.Trade, slot) is not ItemUseRules.Allowed and var tradeRefusal)
        {
            SetStatus(ItemData.Text(tradeRefusal, "You cannot trade this item."), true);
            return;
        }
        if (!CanOfferItem(slot)) return;
        int have = Mathf.Max(1, (int)slot.Count);

        if (amount is { } chosen)
        {
            OfferSlotAmount(absSlot, slot.ItemId, chosen);
            return;
        }

        if (have > 1 && (ItemData.Get(slot.ItemId)?.Countable ?? 0) != 0)
        {
            OpenAmount(absSlot, have);
            return;
        }

        OfferSlotAmount(absSlot, slot.ItemId, have);
    }

    private bool CanOfferItem(ItemSlot slot)
    {
        if (!ExchangeOffer.IsOfferable(slot, ItemData.Get(slot.ItemId)))
        {
            SetStatus(ItemData.Text(UntradableText, "That item can't be traded."), true);
            return false;
        }
        if (!ExchangeOffer.HasRoomFor(MyOffer.ConvertAll(o => o.ItemId), slot.ItemId, IsCountableItem))
        {
            SetStatus($"Offer is full ({ExchangeOffer.ItemSlots} items).", true);
            return false;
        }
        return true;
    }

    private static bool IsCountableItem(int itemId) => (ItemData.Get(itemId)?.Countable ?? 0) != 0;

    private void OfferSlotAmount(int absSlot, int itemId, int count)
    {
        if (!shown || ConfirmedByMe || ConfirmedByPartner || addInFlight) return;
        if (absSlot < 0 || absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        var slot = self.Inventory[absSlot];
        if (slot.ItemId != itemId || count < 1 || count > Mathf.Max(1, (int)slot.Count))
        {
            SetStatus("The item or quantity changed. Please try again.", true);
            return;
        }
        if (!CanOfferItem(slot)) return;

        pending = new PendingAdd { IsGold = false, ItemId = slot.ItemId, SourceAbs = absSlot, Count = count, Dura = slot.Durability, UniqueId = slot.UniqueId };
        addInFlight = true;
        requests.AddItem((byte)(absSlot - Inventory.GridStart), slot.ItemId, count);
    }

    internal void OpenAmount(int absSlot, int max)
    {
        context.Tooltips.Hide();
        amountSlot = absSlot;
        amountMax = max;
        int itemId = self.Inventory[absSlot].ItemId;
        amountItem = itemId;
        AmountIcon.Texture = ItemIcons.Get(itemId);
        amountName.Text = ItemData.DisplayName(itemId);
        amountHint.Text = $"You have {max:n0}";
        amountSpin.MaxValue = max;
        amountSpin.Value = max;
        amountSpin.GetLineEdit().Text = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        amountLayer.Visible = true;
        amountShown = true;
        amountSpin.GetLineEdit().GrabFocus();
        amountSpin.GetLineEdit().SelectAll();
    }

    internal void CloseAmount()
    {
        if (!amountShown) return;
        amountShown = false;
        amountLayer.Visible = false;
        amountSlot = -1;
    }

    private void ConfirmAmount()
    {
        if (!amountShown || amountSlot < 0) return;
        if (!ExchangeOffer.TryParseAmount(amountSpin.GetLineEdit().Text, amountMax, out int count))
        {
            amountHint.Text = $"Valid quantity: 1–{amountMax:n0}.";
            amountSpin.GetLineEdit().GrabFocus();
            amountSpin.GetLineEdit().SelectAll();
            return;
        }
        int slotAbs = amountSlot, itemId = amountItem;
        CloseAmount();
        OfferSlotAmount(slotAbs, itemId, count);
    }

    private void OnAddGold()
    {
        if (ConfirmedByMe) { SetStatus("You already confirmed.", true); return; }
        if (addInFlight || ConfirmedByPartner) return;
        if (!int.TryParse(goldEdit.Text.Trim(), out int amount) || amount <= 0) { SetStatus("Enter a gold amount.", true); return; }
        AddTradeGold(amount);
    }

    internal void AddTradeGold(int amount)
    {
        if (ConfirmedByMe) { SetStatus("You already confirmed.", true); return; }
        if (addInFlight || amount <= 0) return;
        if (amount > self.Sheet.Gold) { SetStatus("Not enough gold.", true); return; }

        pending = new PendingAdd { IsGold = true, Count = amount };
        addInFlight = true;
        requests.AddGold(amount);
    }

    private void OnAddResult(bool committed)
    {
        if (!addInFlight) return;
        addInFlight = false;
        if (!committed) { SetStatus("That item can't be traded.", true); return; }

        if (pending.IsGold)
        {
            MyGoldOffer += pending.Count;
            progress.TransferGoldOut(pending.Count);
            goldEdit.Text = "";
        }
        else
        {
            MyOffer.Add(new OfferItem { ItemId = pending.ItemId, Count = pending.Count, Dura = pending.Dura, SourceAbs = pending.SourceAbs, UniqueId = pending.UniqueId });
            int abs = pending.SourceAbs;
            if (abs >= 0 && abs < self.Inventory.Length)
            {
                self.SetInventorySlot(abs, Inventory.Consumed(self.Inventory[abs], pending.Count));
            }
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        }
        RefreshOffers();
        SetStatus("", false);
    }

    private void OnOtherAdd(int itemId, int count, short dura, int uniqueId)
    {
        if (!shown) return;
        if (itemId == ExchangeRequests.ExchangeGoldItem) TheirGoldOffer += count;
        else TheirOffer.Add(new OfferItem { ItemId = itemId, Count = count, Dura = dura, UniqueId = uniqueId });
        RefreshOffers();
    }

    internal void OnConfirm()
    {
        if (!shown || ConfirmedByMe) return;
        if (addInFlight || amountShown) { SetStatus("Finish adding your offer before confirming.", true); return; }
        ConfirmedByMe = true;
        confirmBtn.Disabled = true;
        confirmBtn.Text = "Confirmed";
        SetStatus("Waiting for partner…", false);
        RefreshOffers();
        requests.Decide();
    }

    private void OnOtherDecide()
    {
        if (!shown) return;
        ConfirmedByPartner = true;
        CloseAmount();
        RefreshOffers();
        SetStatus(ConfirmedByMe ? "Finalising…" : "Partner confirmed — press Confirm.", false);
    }

    private void OnDone(bool ok, int money, List<(byte DstPos, ItemSlot Slot)> received)
    {
        if (!shown) return;
        if (!ok)
        {
            SetStatus("Trade failed (bags full / overweight).", true);
            RestoreMyOffer();
            CloseWindow();
            return;
        }
        foreach (var (dstPos, slot) in received)
        {
            int abs = Inventory.GridStart + dstPos;
            if (abs >= 0 && abs < self.Inventory.Length)
                self.SetInventorySlot(abs, slot);
            context.Floaters?.Item(slot.ItemId, slot.Count);
        }
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        context.Log.Notice($"Trade with {PartnerName} complete.");
        CloseWindow();
    }

    private void OnCancel()
    {
        if (Waiting)
        {
            HideWait();
            ResetState();
            context.Log.Notice($"{PartnerName} is not available to trade.");
            return;
        }
        if (requestPending)
        {
            requestPending = false;
            askDialog.Hide();
            ResetState();
            context.Log.Notice("The trade request was withdrawn.");
            return;
        }
        if (!shown) return;
        context.Log.Notice("The trade was cancelled.");
        RestoreMyOffer();
        CloseWindow();
    }

    private void RestoreMyOffer()
    {
        foreach (var o in MyOffer)
        {
            int abs = o.SourceAbs;
            bool sameStack = abs >= Inventory.GridStart && abs < self.Inventory.Length
                             && !self.Inventory[abs].IsEmpty && self.Inventory[abs].ItemId == o.ItemId;
            if (!sameStack && (abs < Inventory.GridStart || abs >= self.Inventory.Length || !self.Inventory[abs].IsEmpty))
                abs = self.Inventory.FirstFreeGridSlot();
            if (abs < 0) continue;

            self.SetInventorySlot(abs, sameStack
                ? Inventory.Stacked(self.Inventory[abs], o.Count)
                : new ItemSlot { ItemId = o.ItemId, Count = (short)o.Count, Durability = o.Dura });
        }
        if (MyGoldOffer > 0) progress.TransferGoldIn(MyGoldOffer);
        MyOffer.Clear();
        MyGoldOffer = 0;
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
    }

    internal void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
