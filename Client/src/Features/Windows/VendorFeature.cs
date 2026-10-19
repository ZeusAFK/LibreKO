using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class VendorFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly NpcQuestSession npcQuest;
    private readonly VendorSession vendor;
    private readonly ZoneAbilitySession zoneAbility;
    private readonly IVendorRequests requests;
    private readonly IInteractionDialogs interactionDialogs;

    public VendorFeature(IWorldContext context, ProgressSession progress, SelfSession self, NpcQuestSession npcQuest, VendorSession vendor, ZoneAbilitySession zoneAbility, IVendorRequests requests, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.progress = progress;
        this.self = self;
        this.npcQuest = npcQuest;
        this.vendor = vendor;
        this.zoneAbility = zoneAbility;
        this.requests = requests;
        this.interactionDialogs = interactionDialogs;
    }

    private const int Columns = 6;
    private const float CellSize = 44f;
    private const float DetailSlotSize = 40f;
    private const int LoyaltyMerchantGroup = 249000;
    private const int TradeRefusedNoMoney = 3;
    private const int TradeRefusedNoRoom = 4;
    private const int SellConfirmText = 11500;
    private const string Hint = "Right-click or drag to trade";
    private const string UntradableText = "This item cannot be sold.";
    private const string StaleText = "The item or quantity changed. Please try again.";

    private static string TariffHint(int tariff, bool loyaltyShop) =>
        tariff > 0 && !loyaltyShop ? $"Purchases pay a {tariff}% tariff" : Hint;

    private struct PendingTrade
    {
        public bool Buy;
        public int ItemId;
        public int AbsSlot;
        public int Count;
        public bool Stack;
    }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    internal LineEdit Search { get; private set; } = null!;
    private Label matches = null!;
    private ServicePager pager = null!;

    internal int Page => pager.Page;

    internal int Pages => pager.Pages;

    private Label empty = null!;
    internal ItemSlotView[] Cells { get; } = new ItemSlotView[ShopCatalogue.PageSize];
    internal int[] CellIds { get; } = new int[ShopCatalogue.PageSize];
    private DetailStrip detail = null!;
    private Button buy = null!;
    private FooterBand footer = null!;
    private MoneyPlaque wallet = null!;
    internal QuantityPrompt TradePrompt { get; private set; } = null!;

    private bool shown;

    public bool IsOpen => shown;

    private int group;

    internal bool LoyaltyShop => group == LoyaltyMerchantGroup;

    private string Currency => LoyaltyShop ? "NP" : "gold";

    internal long Wallet => LoyaltyShop ? self.Sheet.Np : self.Sheet.Gold;

    internal ShopCatalogue Catalogue { get; private set; } = ShopCatalogue.Empty;
    private Dictionary<int, ItemData.SellEntry> entries = new();
    internal IReadOnlyList<int> Results { get; private set; } = System.Array.Empty<int>();
    private int selected;
    private BagCompanion? companion;

    private PendingTrade pendingTrade;

    private bool Blocked => vendor.TradeInFlight || context.Inventory.MoveBusy || context.Self.Dead;

    private bool Searching => Search.Text.Trim().Length > 0;

    private int PageCount => Searching
    ? ShopCatalogue.PagesFor(Results.Count)
    : Mathf.Max(1, Catalogue.Pages.Count);

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        vendor.TradeNpcEvent += OnOpen;
        vendor.ItemTradeResultEvent += OnTradeResult;
        vendor.ItemTradeMovedEvent += OnTradeMoved;
        progress.GoldChangeEvent += OnGold;
        progress.LoyaltyChangeEvent += OnLoyalty;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (TradePrompt.IsOpen) TradePrompt.Close();
        else if (IsOpen) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        vendor.EndTrade();
        vendor.TradeNpcEvent -= OnOpen;
        vendor.ItemTradeResultEvent -= OnTradeResult;
        vendor.ItemTradeMovedEvent -= OnTradeMoved;
        progress.GoldChangeEvent -= OnGold;
        progress.LoyaltyChangeEvent -= OnLoyalty;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("vendor", "Merchant") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);

        Search = new LineEdit
        {
            PlaceholderText = "Search by name or item number",
            ClearButtonEnabled = true,
        };
        Search.AddThemeFontSizeOverride("font_size", 13);
        Search.TextChanged += _ => OnSearch();
        root.AddChild(Search);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        matches = UiTheme.Text("", 11, UiTheme.TextDim);
        matches.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        matches.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(matches);
        pager = new ServicePager();
        pager.PageChanged += ShowPage;
        head.AddChild(pager);
        root.AddChild(head);

        var well = new DropWell { CanDrop = CanSellDrop, Dropped = SellDrop };
        well.Wheeled += pager.Step;
        root.AddChild(well);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        well.AddChild(stack);
        var grid = new GridContainer { Columns = Columns, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        stack.AddChild(grid);
        for (int i = 0; i < Cells.Length; i++)
        {
            var cell = new ItemSlotView(CellSize) { Index = i };
            cell.Clicked += OnCellClicked;
            cell.DoubleClicked += OnCellBuy;
            cell.RightClicked += OnCellBuy;
            cell.Hovered += OnCellHovered;
            cell.Unhovered += _ => context.Tooltips.Hide();
            cell.Wheeled += (_, step) => pager.Step(step);
            cell.DragOut = DragOut;
            cell.CanDrop = (_, data) => CanSellDrop(data);
            cell.Dropped = (_, data) => SellDrop(data);
            Cells[i] = cell;
            grid.AddChild(cell);
        }
        empty = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        empty.Visible = false;
        stack.AddChild(empty);

        detail = new DetailStrip(DetailSlotSize);
        buy = UiTheme.ActionButton("Buy", "Buy this item");
        buy.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        buy.Pressed += () => AskBuy(selected, -1);
        detail.Right.AddChild(buy);
        root.AddChild(detail);

        footer = new FooterBand { Hint = Hint };
        wallet = new MoneyPlaque("");
        footer.Left.AddChild(wallet);
        root.AddChild(footer);

        TradePrompt = new QuantityPrompt(UiLayers.Prompts);
        context.Root.AddChild(TradePrompt);
    }

    internal void OnOpen(int sellingGroup) => Open(sellingGroup);

    internal void Open(int sellingGroup)
    {
        TradePrompt.Close();
        group = sellingGroup;
        context.NpcDialog.Close();
        var sellEntries = ItemData.SellGroup(sellingGroup).Where(e => ItemData.Get(e.Id) != null).ToList();
        entries = sellEntries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        Catalogue = new ShopCatalogue(sellEntries.Select(e => new ShopCatalogue.Entry(e.Id, e.Line, e.List)), ItemData.DisplayName);
        Results = System.Array.Empty<int>();
        selected = 0;
        Panel.Title = npcQuest.LastNpcName;
        footer.Hint = TariffHint(zoneAbility.CurrentZoneAbility.Tariff, LoyaltyShop);
        Search.Text = "";
        RefreshWallet();
        Panel.Visible = true;
        shown = true;
        companion ??= new BagCompanion(TakeFromBag, FitAt, BagNote, Close, (from, to, _) => IntoBag(from, to));
        context.BagCompanions.Attach(companion);
        ShowPage(0);
    }

    public void Close()
    {
        TradePrompt.Close();
        context.Tooltips.Hide();
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        if (companion != null) context.BagCompanions.Detach(companion);
        context.Plugins.Vendor();
    }

    internal void OnSearch()
    {
        Results = Catalogue.Search(Search.Text);
        ShowPage(0);
    }

    internal void ShowPage(int page)
    {
        context.Tooltips.Hide();
        int pages = PageCount;
        page = Paging.Step(page, 0, pages);
        pager.Set(page, pages);
        matches.Text = !Searching ? ""
            : Results.Count == 1 ? "1 match" : $"{Results.Count:n0} matches";
        FillCells(Searching
            ? ShopCatalogue.Slice(Results, page)
            : Catalogue.Pages.Count == 0 ? new int[ShopCatalogue.PageSize] : Catalogue.Page(Catalogue.Pages[page]));
        SelectItem(selected != 0 && CellIds.Contains(selected)
            ? selected : CellIds.FirstOrDefault(id => id != 0));
        context.Plugins.Vendor();
    }

    private void FillCells(int[] ids)
    {
        bool any = false;
        for (int i = 0; i < Cells.Length; i++)
        {
            CellIds[i] = ids[i];
            Cells[i].Set(ids[i] == 0 ? default : UiKit.TooltipItem(ids[i]));
            any |= ids[i] != 0;
        }
        empty.Visible = !any;
        empty.Text = Searching ? "No items match the search." : "Nothing for sale.";
    }

    internal void SelectItem(int itemId)
    {
        selected = itemId;
        for (int i = 0; i < Cells.Length; i++)
            Cells[i].Look = itemId != 0 && CellIds[i] == itemId ? SlotLook.Selected : SlotLook.Normal;
        if (itemId == 0 || ItemData.Get(itemId) is null)
        {
            detail.Slot.Clear();
            detail.Title.Text = "Choose an item";
            detail.Title.AddThemeColorOverride("font_color", UiTheme.TextLo);
            detail.Sub.Text = "";
            buy.Disabled = true;
            return;
        }
        detail.Slot.Set(UiKit.TooltipItem(itemId));
        detail.Title.Text = ItemData.DisplayName(itemId);
        detail.Title.AddThemeColorOverride("font_color", ItemGrade.Tint(itemId));
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        if (selected == 0 || ItemData.Get(selected) is not { } def) return;
        bool ok = CanBuy(selected, 1, -1, out _, out _, out string problem);
        buy.Disabled = !ok || vendor.TradeInFlight;
        detail.Sub.Text = ok
            ? $"{ItemData.BuyPrice(selected):n0} {Currency} each · {def.Weight / 10f:0.0} wt"
            : problem;
        detail.Sub.AddThemeColorOverride("font_color", ok ? UiTheme.Gold : UiTheme.Bad);
    }

    private Variant DragOut(ItemSlotView cell)
    {
        if (CellIds[cell.Index] == 0) return default;
        return new Godot.Collections.Dictionary { { "companionFrom", cell.Index } };
    }

    private void OnCellClicked(ItemSlotView cell)
    {
        if (CellIds[cell.Index] != 0) SelectItem(CellIds[cell.Index]);
    }

    private void OnCellBuy(ItemSlotView cell)
    {
        if (CellIds[cell.Index] == 0) return;
        SelectItem(CellIds[cell.Index]);
        AskBuy(CellIds[cell.Index], -1);
    }

    private void OnCellHovered(ItemSlotView cell)
    {
        if (CellIds[cell.Index] != 0) context.Tooltips.Show(-1, UiKit.TooltipItem(CellIds[cell.Index]), shop: new ShopPricing(LoyaltyShop, Wallet));
    }

    private bool IntoBag(int cell, int abs)
    {
        if (cell < 0 || cell >= CellIds.Length || CellIds[cell] == 0) return false;
        AskBuy(CellIds[cell], abs);
        return true;
    }

    internal void AskBuy(int itemId, int preferred)
    {
        if (Blocked || !entries.TryGetValue(itemId, out var entry)) return;
        if (ItemData.Get(itemId) is not { } def) return;
        int price = ItemData.BuyPrice(itemId);
        long? freeWeight = self.Sheet.MaxWeight > 0 ? self.Sheet.MaxWeight - context.Inventory.CarriedWeight() : null;
        int max = BuyLimit.Max(Wallet, price, freeWeight, def.Weight, BuyRoom(itemId, def, preferred));
        if (max <= 0)
        {
            CanBuy(itemId, 1, preferred, out _, out _, out string problem);
            footer.Status(problem.Length > 0 ? problem : "You can't buy that.", bad: true);
            return;
        }
        if (def.Countable == 0)
        {
            BuyAmount(entry, 1, preferred);
            return;
        }
        TradePrompt.Open(ItemIcons.Get(itemId), $"Buy {ItemData.DisplayName(itemId)}",
            $"{price:n0} {Currency} each · up to {max:n0}", max, 1, n => BuyAmount(entry, (int)n, preferred),
            "Buy", n => $"Total {(long)price * n:n0} {Currency} · {(long)def.Weight * n / 10f:0.0} wt");
    }

    private int BuyRoom(int itemId, ItemData.Item def, int preferred)
    {
        bool countable = def.Countable != 0;
        if (self.Inventory.InMainBag(preferred))
        {
            if (self.Inventory[preferred].IsEmpty) return countable ? Inventory.StackMax : 1;
            if (countable && self.Inventory[preferred].ItemId == itemId) return Inventory.StackMax - self.Inventory[preferred].Count;
        }
        return self.Inventory.GridRoomFor(itemId, countable);
    }

    private void BuyAmount(ItemData.SellEntry entry, int count, int preferred)
    {
        if (Blocked || !shown || !entries.ContainsKey(entry.Id)) return;
        count = Mathf.Clamp(count, 1, Inventory.StackMax);
        if (!CanBuy(entry.Id, count, preferred, out int dest, out bool stack, out string problem))
        {
            footer.Status(problem, bad: true);
            return;
        }
        pendingTrade = new PendingTrade { Buy = true, ItemId = entry.Id, AbsSlot = dest, Count = count, Stack = stack };
        vendor.BeginTrade();
        RefreshDetail();
        requests.Buy(group, npcQuest.LastNpcId, entry.Id, (byte)(dest - Inventory.GridStart), (ushort)count,
            (byte)entry.Line, (byte)entry.List);
    }

    internal bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        AskSell(abs);
        return true;
    }

    private BagFit FitAt(int abs)
    {
        if (LoyaltyShop || !self.Inventory.InMainBag(abs)) return BagFit.Unfit;
        return SellUnit(self.Inventory[abs].ItemId) > 0 && ItemStacks.UseRefusal(ItemUse.NpcSell, self.Inventory[abs]) == ItemUseRules.Allowed
            ? BagFit.Normal
            : BagFit.Unfit;
    }

    private string BagNote(int abs)
    {
        if (LoyaltyShop || !self.Inventory.InMainBag(abs)) return "";
        int unit = SellUnit(self.Inventory[abs].ItemId);
        if (unit <= 0) return "";
        return self.Inventory[abs].Count > 1 ? $"Sells for {unit:n0} gold each" : $"Sells for {unit:n0} gold";
    }

    private static int SellUnit(int itemId) => ItemData.IsSellable(itemId) ? ItemData.SellPrice(itemId) : 0;

    private bool CanSellDrop(Variant data) =>
        data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary().ContainsKey("invFrom")
    && self.Inventory.InMainBag(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void SellDrop(Variant data) => AskSell(data.AsGodotDictionary()["invFrom"].AsInt32());

    internal void AskSell(int abs)
    {
        if (Blocked || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty) return;
        if (context.BagCompanions.RefuseItemInUse(abs)) return;
        if (LoyaltyShop)
        {
            footer.Status("The Loyalty Merchant buys nothing back.", bad: true);
            return;
        }
        var slot = self.Inventory[abs];
        if (ItemStacks.UseRefusal(ItemUse.NpcSell, slot) is not ItemUseRules.Allowed and var sellRefusal)
        {
            footer.Status(ItemData.Text(sellRefusal, "Non tradable item"), bad: true);
            return;
        }
        int unit = SellUnit(slot.ItemId);
        if (unit <= 0)
        {
            footer.Status("The merchant won't buy that.", bad: true);
            return;
        }
        if (slot.Count <= 1 || ItemData.Get(slot.ItemId) is not { Countable: not 0 })
        {
            int soldId = slot.ItemId;
            Notice.Confirm(context.Root,
                $"{ItemData.Text(SellConfirmText, "Will you sell %s?").Replace("%s", ItemData.DisplayName(soldId))}\n{unit:n0} gold",
                "Sell", "Cancel",
                () => { if (self.Inventory.InMainBag(abs) && self.Inventory[abs].ItemId == soldId) SellSlot(abs, 1, soldId); });
            return;
        }
        TradePrompt.Open(ItemIcons.Get(slot.ItemId), $"Sell {ItemData.DisplayName(slot.ItemId)}",
            $"{unit:n0} gold each · you carry {slot.Count:n0}", slot.Count, slot.Count, n => SellSlot(abs, (int)n, slot.ItemId),
            "Sell", n => $"Sells for {(long)unit * n:n0} gold");
    }

    private void SellSlot(int absSlot, int count, int itemId)
    {
        if (Blocked || !shown || LoyaltyShop || !self.Inventory.InMainBag(absSlot)) return;
        if (context.BagCompanions.RefuseItemInUse(absSlot)) return;
        var slot = self.Inventory[absSlot];
        if (slot.ItemId != itemId || count < 1 || count > Mathf.Max(1, (int)slot.Count))
        {
            footer.Status(StaleText, bad: true);
            return;
        }
        if (!slot.IsTradable || SellUnit(itemId) <= 0)
        {
            footer.Status(UntradableText, bad: true);
            return;
        }
        pendingTrade = new PendingTrade { Buy = false, ItemId = slot.ItemId, AbsSlot = absSlot, Count = count };
        vendor.BeginTrade();
        RefreshDetail();
        requests.Sell(group, npcQuest.LastNpcId, slot.ItemId, (byte)(absSlot - Inventory.GridStart), (ushort)count);
    }

    private void OnTradeResult(bool ok, int code, int money, int price)
    {
        if (!vendor.TradeInFlight) return;
        vendor.EndTrade();
        if (!ok)
        {
            string refusal = code switch
            {
                TradeRefusedNoMoney => LoyaltyShop ? "Not enough National Points." : "Not enough gold.",
                TradeRefusedNoRoom => ItemData.Text(GameText.LootNoRoom, "You cannot trade or pick up items because you have either exceeded the possible quantity or the weight."),
                _ => "The merchant won't trade that.",
            };
            if (!shown)
            {
                context.Log.Notice(refusal);
                return;
            }
            footer.Status(refusal, bad: true);
            RefreshDetail();
            return;
        }
        ApplyTradeToInventory(pendingTrade);
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        if (!shown) return;
        RefreshDetail();
        footer.Status(pendingTrade.Buy
            ? $"Bought {ItemData.DisplayName(pendingTrade.ItemId)} (−{price:n0})"
            : $"Sold {ItemData.DisplayName(pendingTrade.ItemId)} (+{price:n0})", bad: false);
        int textId = !pendingTrade.Buy ? ShopReceivedText : LoyaltyShop ? ShopUsedContText : ShopPaidText;
        context.Log.Add(ItemData.Text(textId, "%d").Replace("%d", price.ToString("n0")), CombatLogKind.Resource, ShopLogTint, tab: LogTab.Item);
    }

    private const int ShopPaidText = 7683;
    private const int ShopReceivedText = 7682;
    private const int ShopUsedContText = 15717;
    private static readonly Color ShopLogTint = Color.Color8(255, 102, 0);

    private void OnTradeMoved()
    {
        if (shown) RefreshDetail();
    }

    private void OnGold(GoldChange change)
    {
        if (!shown) return;
        RefreshWallet();
        RefreshDetail();
    }

    private void OnLoyalty(LoyaltyChange change)
    {
        if (!shown || !LoyaltyShop) return;
        RefreshWallet();
        RefreshDetail();
    }

    private void RefreshWallet()
    {
        wallet.Caption = LoyaltyShop ? "National Points" : "";
        wallet.Value = Wallet;
    }

    internal bool CanBuy(int itemId, int count, int preferred, out int dest, out bool stack, out string problem)
    {
        dest = -1;
        stack = false;
        problem = "";

        var def = ItemData.Get(itemId);
        if (def == null) { problem = "The merchant won't trade that."; return false; }

        bool countable = def.Countable != 0;
        if (self.Inventory.InMainBag(preferred))
        {
            if (self.Inventory[preferred].IsEmpty) dest = preferred;
            else if (countable && self.Inventory[preferred].ItemId == itemId && self.Inventory[preferred].Count + count <= Inventory.StackMax)
            { dest = preferred; stack = true; }
        }
        if (dest < 0 && countable)
        {
            for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
                if (self.Inventory[abs].ItemId == itemId && self.Inventory[abs].Count + count <= Inventory.StackMax)
                { dest = abs; stack = true; break; }
        }
        if (dest < 0) dest = self.Inventory.FirstFreeGridSlot();
        if (dest < 0) { problem = "Your bags are full."; return false; }

        if ((long)ItemData.BuyPrice(itemId) * count > Wallet)
        {
            problem = LoyaltyShop ? "Not enough National Points." : "Not enough gold.";
            return false;
        }

        if (self.Sheet.MaxWeight > 0)
        {
            long free = self.Sheet.MaxWeight - context.Inventory.CarriedWeight();
            if ((long)def.Weight * count > free)
            {
                problem = $"Too heavy — only {Mathf.Max(0f, free / 10f):0.0} wt free "
                    + $"of {self.Sheet.MaxWeight / 10f:0.0}.";
                return false;
            }
        }

        return true;
    }

    private void ApplyTradeToInventory(PendingTrade t)
    {
        int abs = t.AbsSlot;
        if (abs < 0 || abs >= self.Inventory.Length) return;
        var held = self.Inventory[abs];
        if (!t.Buy)
        {
            int remaining = held.Count - t.Count;
            self.SetInventorySlot(abs, remaining > 0 ? new ItemSlot { ItemId = t.ItemId, Count = (short)remaining, Durability = held.Durability } : default);
        }
        else if (t.Stack && held.ItemId == t.ItemId)
            self.SetInventorySlot(abs, Inventory.Stacked(held, t.Count));
        else
            self.SetInventorySlot(abs, new ItemSlot
            {
                ItemId = t.ItemId,
                Count = (short)t.Count,
                Durability = ItemData.MaxDurabilityOf(t.ItemId),
            });
    }
}
