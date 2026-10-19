using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MerchantBuyFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly MerchantFeature merchantWindow;
    private readonly MerchantSession merchant;
    private readonly IMerchantRequests requests;
    private readonly SelfSession self;
    private readonly MerchantStallsFeature stalls;
    private readonly ProgressSession progress;

    public MerchantBuyFeature(IWorldContext context, MerchantFeature merchantWindow, MerchantSession merchant, IMerchantRequests requests, SelfSession self, MerchantStallsFeature stalls, ProgressSession progress)
    {
        this.context = context;
        this.merchantWindow = merchantWindow;
        this.merchant = merchant;
        this.requests = requests;
        this.self = self;
        this.stalls = stalls;
        this.progress = progress;
    }

    public void Build()
    {
        BuildBuyPanels();
        merchantWindow.BuyingMerchantRequested += OpenWishList;
        progress.GoldChangeEvent += OnGold;
        merchant.BuyMerchantOpenEvent += OnBuyOpen;
        merchant.BuyMerchantInsertEvent += OnBuyInsert;
        merchant.BuyMerchantListEvent += OnBuyList;
        merchant.BuyMerchantResultEvent += OnBuyResult;
        merchant.BuyMerchantSoldEvent += OnBuySold;
        merchant.BuyMerchantBoughtEvent += OnBuyBought;
        merchant.BuyMerchantClosedEvent += OnBuyClosed;
    }

    public void Dispose()
    {
        merchantWindow.BuyingMerchantRequested -= OpenWishList;
        progress.GoldChangeEvent -= OnGold;
        merchant.BuyMerchantOpenEvent -= OnBuyOpen;
        merchant.BuyMerchantInsertEvent -= OnBuyInsert;
        merchant.BuyMerchantListEvent -= OnBuyList;
        merchant.BuyMerchantResultEvent -= OnBuyResult;
        merchant.BuyMerchantSoldEvent -= OnBuySold;
        merchant.BuyMerchantBoughtEvent -= OnBuyBought;
        merchant.BuyMerchantClosedEvent -= OnBuyClosed;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (wishFindShown) CloseWishFind();
        else if (WishShown) CloseWishList();
        else if (wantedShown) CloseWantedStall();
        else return false;
        return true;
    }

    private void OnGold(GoldChange change)
    {
        if (GodotObject.IsInstanceValid(wantedBalance)) wantedBalance.Text = MerchantUi.Money(change.Total);
        if (GodotObject.IsInstanceValid(wishTotal)) RefreshWishTotal();
    }

    private MerchantUi.PendingTrade? wantedSale;

    private const int WishMaxStack = 9_999;

    internal HudWindow WishPanel { get; private set; } = null!;
    private MerchantCell[] wishCells = null!;
    private Label wishTotal = null!;
    private Label wishStatus = null!;
    private VBoxContainer wishSummary = null!;
    internal bool WishShown { get; private set; }
    internal MerchantWishItem[] Wishes { get; } = new MerchantWishItem[MerchantUi.StallSlots];

    internal HudWindow WishFindPanel { get; private set; } = null!;
    internal ItemSearchPanel WishFind { get; private set; } = null!;
    private bool wishFindShown;
    private int wishFindTarget = -1;

    internal HudWindow WantedPanel { get; private set; } = null!;
    private MerchantCell[] wantedCells = null!;
    private MerchantCell[] wantedBagCells = null!;
    private Label wantedStatus = null!;
    private Label wantedBalance = null!;
    private bool wantedShown;
    private int wantedId = -1;
    private MerchantStallItem[] wantedItems = new MerchantStallItem[MerchantUi.StallSlots];

    private void BuildBuyPanels()
    {
        BuildWishPanel();
        BuildWishFindPanel();
        BuildWantedPanel();
    }

    private void BuildWishPanel()
    {
        WishPanel = new HudWindow("wishlist", "Item Wish List", new Vector2(160, 90)) { Visible = false };
        WishPanel.Closed += CloseWishList;
        merchantWindow.ServiceLayer.AddChild(WishPanel);

        var root = WishPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("What you want to buy"));
        wishCells = MerchantUi.BuildGrid(context.Tooltips, root, MerchantUi.StallSlots, MerchantUi.StallColumns, OnWishSlotClicked,
            acceptKey: "invFrom", onDropFrom: WishFromBag);

        var summary = UiTheme.Section();
        root.AddChild(summary);
        wishSummary = new VBoxContainer();
        wishSummary.AddThemeConstantOverride("separation", 2);
        wishSummary.CustomMinimumSize = new Vector2(300, 76);
        summary.AddChild(wishSummary);

        var totalRow = UiTheme.Section();
        root.AddChild(totalRow);
        totalRow.AddChild(MerchantUi.MoneyRow("Gold", out wishTotal, UiTheme.GoldBright));

        wishStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        wishStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        wishStatus.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(wishStatus);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        var ok = new Button { Text = "O  K", FocusMode = Control.FocusModeEnum.None };
        ok.CustomMinimumSize = new Vector2(104, 28);
        ok.Pressed += ConfirmWishList;
        footer.AddChild(ok);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.CustomMinimumSize = new Vector2(104, 28);
        cancel.Pressed += CloseWishList;
        footer.AddChild(cancel);
    }

    private void BuildWishFindPanel()
    {
        WishFindPanel = new HudWindow("wishfind", "Item Search", new Vector2(520, 90)) { Visible = false };
        WishFindPanel.Closed += CloseWishFind;
        merchantWindow.ServiceLayer.AddChild(WishFindPanel);

        WishFind = new ItemSearchPanel(
            tradeableOnly: true,
            actionText: "Registration",
            onAction: OnWishItemPicked,
            showTooltip: itemId => context.Tooltips.Show(-1, UiKit.TooltipItem(itemId)),
            hideTooltip: context.Tooltips.Hide,
            resultsSize: new Vector2(430, 300),
            showQuantity: false);
        WishFindPanel.Body.AddChild(WishFind);
    }

    private void BuildWantedPanel()
    {
        WantedPanel = new HudWindow("wantedstall", "Buying Merchant") { Visible = false };
        WantedPanel.Closed += CloseWantedStall;
        merchantWindow.ServiceLayer.AddChild(WantedPanel);

        var root = WantedPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("Wanted"));
        wantedCells = MerchantUi.BuildGrid(context.Tooltips, root, MerchantUi.StallSlots, MerchantUi.StallColumns, null,
            acceptKey: "bagFrom", onDropFrom: SellToWanted);
        foreach (var cell in wantedCells) cell.OnSecondary = SellWantedFromBag;

        var money = UiTheme.Section();
        root.AddChild(money);
        money.AddChild(MerchantUi.MoneyRow("Current Balance", out wantedBalance, UiTheme.TextHi));

        root.AddChild(UiTheme.SectionTitle("Your bags"));
        wantedBagCells = MerchantUi.BuildGrid(context.Tooltips, root, Inventory.GridCount, MerchantUi.StallBagColumns, null,
            dragKey: "bagFrom");
        foreach (var cell in wantedBagCells) cell.OnSecondary = SellBagToWanted;

        root.AddChild(UiTheme.Text("Right-click a wanted item or a matching bag item to sell it.", 11, UiTheme.TextDim));

        wantedStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        wantedStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        wantedStatus.CustomMinimumSize = new Vector2(340, 0);
        root.AddChild(wantedStatus);
    }

    internal void OpenWishList()
    {
        merchantWindow.CloseMenu();
        if (context.Self.Dead) return;
        for (int i = 0; i < Wishes.Length; i++) Wishes[i] = default;
        SetWishStatus("", false);
        RefreshWishList();
        WishPanel.Visible = true;
        WishShown = true;
        context.Movement.StopForInteraction();
    }

    internal void CloseWishList()
    {
        context.Tooltips.Hide();
        CloseWishFind();
        if (!WishShown) return;
        WishShown = false;
        WishPanel.Visible = false;
    }

    internal void OnWishSlotClicked(int slot)
    {
        if (slot < 0 || slot >= Wishes.Length) return;
        if (!Wishes[slot].IsEmpty)
        {
            Wishes[slot] = default;
            RefreshWishList();
            return;
        }
        wishFindTarget = slot;
        WishFindPanel.Visible = true;
        wishFindShown = true;
        WishFind.FocusQuery();
    }

    private void CloseWishFind()
    {
        if (!wishFindShown) return;
        wishFindShown = false;
        WishFindPanel.Visible = false;
        wishFindTarget = -1;
        context.Tooltips.Hide();
    }

    private void OnWishItemPicked(ItemSearchHit hit, int count)
    {
        int slot = wishFindTarget;
        if (slot < 0 || slot >= Wishes.Length) return;

        bool stackable = hit.Def.Countable > 0;
        int most = stackable ? WishMaxStack : 1;
        var preview = new ItemSlot { ItemId = hit.Id, Count = 1, Durability = (short)hit.Def.Duration };
        int carried = CarriedCount(hit.Id);
        string hint = carried > 0 ? $"Gold Offered per Item   ·   You have {carried:n0} of these" : "Gold Offered per Item";

        merchantWindow.AskAmount(preview, hint, ItemData.BuyPrice(hit.Id), most, stackable,
            (wantCount, price) =>
            {
                Wishes[slot] = new MerchantWishItem { ItemId = hit.Id, Count = wantCount, Price = price };
                CloseWishFind();
                RefreshWishList();
            }, defaultCount: 1, priceHintItem: hit.Id);
    }

    internal void WishFromBag(int abs, int target)
    {
        if (!WishShown || abs < 0 || abs >= self.Inventory.Length || self.Inventory[abs].IsEmpty) return;
        var def = ItemData.Get(self.Inventory[abs].ItemId);
        if (def == null) return;
        if (!ItemSearch.IsTradeable(def)) { SetWishStatus("This item cannot be traded.", true); return; }
        int slot = target >= 0 && target < Wishes.Length && Wishes[target].IsEmpty ? target : FirstEmptyWish();
        if (slot < 0) { SetWishStatus($"The list only holds {MerchantUi.StallSlots} items.", true); return; }
        wishFindTarget = slot;
        OnWishItemPicked(new ItemSearchHit(def, ItemData.ExtFor(self.Inventory[abs].ItemId)), 1);
    }

    private int FirstEmptyWish()
    {
        for (int i = 0; i < Wishes.Length; i++) if (Wishes[i].IsEmpty) return i;
        return -1;
    }

    private int CarriedCount(int itemId)
    {
        int total = 0;
        for (int abs = 0; abs < self.Inventory.Length; abs++)
        {
            if (self.Inventory[abs].ItemId != itemId || !BagSearch.Searchable(abs)) continue;
            total += ItemData.Get(itemId) is { IsChargeItem: true } || self.Inventory[abs].Count <= 0 ? 1 : self.Inventory[abs].Count;
        }
        return total;
    }

    internal void RefreshWishList()
    {
        context.Tooltips.Hide();
        int filled = 0;
        foreach (Node child in wishSummary.GetChildren()) child.QueueFree();

        for (int i = 0; i < wishCells.Length; i++)
        {
            var wish = Wishes[i];
            var slot = new ItemSlot { ItemId = wish.ItemId, Count = (short)wish.Count };
            wishCells[i].Set(slot, MerchantUi.PriceNote("Offering", wish.Price, wish.Count));
            if (wish.IsEmpty) continue;

            filled++;
            wishSummary.AddChild(UiTheme.Text(
                $"{ItemData.DisplayName(wish.ItemId)}   [{MerchantUi.Money(wish.Price)} gold] x {wish.Count}",
                12, UiTheme.TextHi));
        }

        if (filled == 0)
            wishSummary.AddChild(UiTheme.Text("Nothing on the list yet.", 12, UiTheme.TextDim));

        RefreshWishTotal();
    }

    private void RefreshWishTotal()
    {
        long total = 0;
        foreach (var wish in Wishes)
            if (!wish.IsEmpty) total += (long)wish.Price * wish.Count;
        wishTotal.Text = total.ToString("n0");
        wishTotal.AddThemeColorOverride("font_color", total > self.Sheet.Gold ? UiTheme.Bad : UiTheme.GoldBright);
    }

    private void ConfirmWishList()
    {
        var wanted = new List<MerchantWishItem>();
        long total = 0;
        foreach (var wish in Wishes)
        {
            if (wish.IsEmpty) continue;
            wanted.Add(wish);
            total += (long)wish.Price * wish.Count;
        }

        if (wanted.Count == 0) { SetWishStatus("Add at least one item first.", true); return; }
        if (total > self.Sheet.Gold) { SetWishStatus("You are not carrying that much gold.", true); return; }

        pendingWishes = wanted;
        requests.BuyMerchantOpen();
    }

    private List<MerchantWishItem>? pendingWishes;

    private void OnBuyOpen(BuyingMerchantResult result)
    {
        if (result != BuyingMerchantResult.Accepted)
        {
            pendingWishes = null;
            SetWishStatus(BuyMessage(result), true);
            return;
        }

        if (pendingWishes == null || pendingWishes.Count == 0) return;
        requests.BuyMerchantInsert(pendingWishes);
    }

    private void OnBuyInsert(BuyingMerchantResult result)
    {
        var wanted = pendingWishes;
        pendingWishes = null;

        if (result != BuyingMerchantResult.Accepted)
        {
            SetWishStatus(BuyMessage(result), true);
            return;
        }

        CloseWishList();
        stalls.SetLock(true);

        var ids = new List<int>();
        if (wanted != null)
            foreach (var wish in wanted) ids.Add(wish.ItemId);
        stalls.PlaceStall(context.Self.CharId, isBuying: true, flags: 0, ids.ToArray());
        context.Log.Notice("Your buying stall is open. You cannot move while it is.");
    }

    private void OnBuyClosed(int charId)
    {
        stalls.RemoveStall(charId);
        if (wantedShown && wantedId == charId) CloseWantedStall();
        if (charId == context.Self.CharId)
        {
            stalls.SetLock(false);
            context.Log.Notice("Your buying stall closed.");
        }
    }

    internal void OnBuyList(int merchantId, MerchantStallItem[] wanted)
    {
        wantedId = merchantId;
        wantedItems = wanted;
        WantedPanel.Title = context.Entities.Views.TryGetValue(merchantId, out var ent) ? ent.Name : "Buying Merchant";
        RefreshWantedStall();
        SetWantedStatus("", false);
        WantedPanel.Visible = true;
        wantedShown = true;
        context.Movement.StopForInteraction();
    }

    internal void CloseWantedStall()
    {
        merchantWindow.CloseAmountPrompt();
        context.Tooltips.Hide();
        if (!wantedShown) return;
        wantedShown = false;
        WantedPanel.Visible = false;
        wantedId = -1;
        requests.TradeCancel();
    }

    private void RefreshWantedStall()
    {
        context.Tooltips.Hide();
        for (int i = 0; i < wantedCells.Length; i++)
        {
            var item = wantedItems[i];
            int carried = item.IsEmpty ? 0 : CarriedCount(item.ItemId);
            string note = MerchantUi.PriceNote("Pays", item.Price, item.Count);
            wantedCells[i].Set(MerchantUi.StallSlot(item), carried > 0 ? $"{note}\nYou have {carried:n0} of these" : note);
            wantedCells[i].Modulate = item.IsEmpty || carried > 0 ? Colors.White : WantedUnheldTint;
        }
        for (int i = 0; i < wantedBagCells.Length; i++)
        {
            int absSlot = Inventory.GridStart + i;
            var slot = absSlot < self.Inventory.Length ? self.Inventory[absSlot] : default;
            var cell = wantedBagCells[i];
            cell.TipSlot = absSlot;
            cell.Set(slot);
            cell.Modulate = WantedMatch.FirstWanted(wantedItems, slot.ItemId) >= 0 ? Colors.White : WantedUnheldTint;
        }
        wantedBalance.Text = self.Sheet.Gold.ToString("n0");
    }

    private static readonly Color WantedUnheldTint = new(1, 1, 1, 0.45f);

    private void SellWantedFromBag(int wantedSlot)
    {
        if (wantedSlot < 0 || wantedSlot >= wantedItems.Length || wantedItems[wantedSlot].IsEmpty) return;
        int gridIndex = WantedMatch.FirstBagSlot(BagGrid(), wantedItems[wantedSlot].ItemId);
        if (gridIndex < 0) { SetWantedStatus("You don't have that item.", true); return; }
        SellToWanted(gridIndex, wantedSlot);
    }

    private void SellBagToWanted(int gridIndex)
    {
        int absSlot = Inventory.GridStart + gridIndex;
        if (absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        int wantedSlot = WantedMatch.FirstWanted(wantedItems, self.Inventory[absSlot].ItemId);
        if (wantedSlot < 0) { SetWantedStatus("They don't want that item.", true); return; }
        SellToWanted(gridIndex, wantedSlot);
    }

    private ItemSlot[] BagGrid()
    {
        var grid = new ItemSlot[Inventory.GridCount];
        for (int i = 0; i < Inventory.GridCount && Inventory.GridStart + i < self.Inventory.Length; i++) grid[i] = self.Inventory[Inventory.GridStart + i];
        return grid;
    }

    private void SellToWanted(int gridIndex, int wantedSlot)
    {
        if (!wantedShown || wantedSale != null || gridIndex < 0 || gridIndex >= Inventory.GridCount
            || wantedSlot < 0 || wantedSlot >= wantedItems.Length) return;
        int absSlot = Inventory.GridStart + gridIndex;
        if (absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;

        var wanted = wantedItems[wantedSlot];
        var held = self.Inventory[absSlot];
        if (wanted.IsEmpty || held.ItemId != wanted.ItemId)
        {
            SetWantedStatus("That is not the item they asked for.", true);
            return;
        }

        int most = System.Math.Max(1, System.Math.Min(held.Count, wanted.Count));
        var def = ItemData.Get(held.ItemId);
        bool countable = def != null && def.Countable != 0 && most > 1;

        merchantWindow.AskTrade(held, "Sell to this shop", wanted.Price, most, countable,
            (count, _) =>
            {
                if (!wantedShown || wantedSale != null || wantedItems[wantedSlot].ItemId != held.ItemId
                    || self.Inventory[absSlot].ItemId != held.ItemId || self.Inventory[absSlot].Count < count) return;
                wantedSale = new MerchantUi.PendingTrade(wantedId, held.ItemId, count, wanted.Price);
                requests.BuyMerchantSell((byte)gridIndex, (byte)wantedSlot, count);
            });
    }

    private void OnBuyResult(BuyingMerchantResult result)
    {
        if (result == BuyingMerchantResult.Accepted) return;
        wantedSale = null;
        SetWantedStatus(BuyMessage(result), true);
    }

    private void OnBuySold(int wantedSlot, int wantedRemaining, int sellerSlot, int sellerRemaining)
    {
        var request = wantedSale;
        wantedSale = null;
        if (request is not { } action) return;
        string message = $"Sold {action.Count} x {ItemData.DisplayName(action.ItemId)} for {MerchantUi.Money((long)action.Price * action.Count)}.";
        if (!wantedShown || wantedId != action.MerchantId) { context.Log.Notice(message); return; }

        if (wantedSlot >= 0 && wantedSlot < wantedItems.Length)
        {
            wantedItems[wantedSlot].Count = wantedRemaining;
            if (wantedRemaining <= 0) wantedItems[wantedSlot] = default;
        }

        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        RefreshWantedStall();
        SetWantedStatus(message, false);
    }

    private void OnBuyBought(int wantedSlot, int remaining, string sellerName)
    {
        string who = sellerName.Length > 0 ? sellerName : "Someone";
        context.Log.Notice(remaining > 0
            ? $"{who} sold to your stall — {remaining} still wanted."
            : $"{who} filled one of your orders.");
    }

    private static string BuyMessage(BuyingMerchantResult result) => result switch
    {
        BuyingMerchantResult.WhileDead => "You can't do that right now.",
        BuyingMerchantResult.WhileMerchanting => "You are already running a stall.",
        BuyingMerchantResult.NotAllowedHere => "A buying stall can only be opened in Moradon.",
        BuyingMerchantResult.WrongItemSetup => "One of those items can't be bought this way.",
        BuyingMerchantResult.WrongStallSetup => "That stall is no longer open.",
        BuyingMerchantResult.WrongPurchaseCount => "That quantity isn't allowed.",
        BuyingMerchantResult.NoSuchItemWanted => "They don't want that item.",
        BuyingMerchantResult.SellerFundsTooLow => "You are not carrying that much gold.",
        BuyingMerchantResult.BuyerFundsTooLow => "The buyer ran out of gold.",
        BuyingMerchantResult.ItemNotSellable => "That item can't be sold.",
        BuyingMerchantResult.InventoryFull => "Their bags are full.",
        BuyingMerchantResult.OverMaxLimit => "That would take you over the gold limit.",
        BuyingMerchantResult.NeedsRepair => "Repair it first.",
        BuyingMerchantResult.UnderLevelled => "You must be level 35 to open a buying stall.",
        _ => "The stall refused that.",
    };

    private void SetWishStatus(string text, bool warn)
    {
        wishStatus.Text = text;
        wishStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void SetWantedStatus(string text, bool warn)
    {
        wantedStatus.Text = text;
        wantedStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }
}
