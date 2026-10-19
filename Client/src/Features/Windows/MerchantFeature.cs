using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MerchantFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly MerchantSession merchant;
    private readonly PremiumSession premium;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly IMerchantRequests requests;
    private readonly EntityPlatesSystem entityPlates;
    private readonly PlayerMenuFeature playerMenu;
    private readonly MerchantStallsFeature stalls;
    private readonly MarketPriceFeature marketPriceWindow;

    public MerchantFeature(IWorldContext context, MerchantSession merchant, PremiumSession premium, ProgressSession progress, SelfSession self, IMerchantRequests requests, EntityPlatesSystem entityPlates, PlayerMenuFeature playerMenu, MerchantStallsFeature stalls, MarketPriceFeature marketPriceWindow)
    {
        this.context = context;
        this.merchant = merchant;
        this.premium = premium;
        this.progress = progress;
        this.self = self;
        this.requests = requests;
        this.entityPlates = entityPlates;
        this.playerMenu = playerMenu;
        this.stalls = stalls;
        this.marketPriceWindow = marketPriceWindow;
    }

    private const int AdvertMax = 40;

    private MerchantUi.PendingTrade? purchase;
    internal CanvasLayer ServiceLayer { get; private set; } = null!;
    internal HudWindow Menu { get; private set; } = null!;
    internal bool MenuShown { get; set; }
    internal HudWindow SellStallPanel { get; private set; } = null!;
    internal MerchantCell[] SellStallCells { get; private set; } = null!;
    private MerchantCell[] sellBagCells = null!;
    internal LineEdit SellAdvert { get; private set; } = null!;
    private Label sellTotal = null!;
    private Label sellBalance = null!;
    private Label sellStatus = null!;
    private bool sellStallShown;

    private readonly MerchantStallItem[] myStall = new MerchantStallItem[MerchantUi.StallSlots];
    private readonly int[] myStallSrc = new int[MerchantUi.StallSlots];

    internal HudWindow ShopPanel { get; private set; } = null!;
    private MerchantCell[] shopCells = null!;
    private MerchantCell[] shopBagCells = null!;
    private Label shopStatus = null!;
    private Label shopBalance = null!;
    private bool shopShown;
    private int shopTargetId = -1;
    private MerchantStallItem[] shopItems = new MerchantStallItem[MerchantUi.StallSlots];

    internal Dictionary<int, string> Adverts { get; } = new();

    public void Build()
    {
        BuildPanels();
        BuildAmountPrompt();
        progress.GoldChangeEvent += OnGold;
        merchant.MerchantOpenResultEvent += OnOpenResult;
        merchant.MerchantItemAddEvent += OnItemAdd;
        merchant.MerchantItemCancelEvent += OnItemCancel;
        merchant.MerchantListEvent += OnList;
        merchant.MerchantBuyEvent += OnBuy;
        merchant.MerchantSoldEvent += OnSold;
        merchant.MerchantInsertedEvent += OnInserted;
        merchant.MerchantStallClosedEvent += OnStallClosed;
        for (int i = 0; i < myStallSrc.Length; i++) myStallSrc[i] = -1;
        marketPriceWindow.PriceCached += RefreshStallPriceHint;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (AmountLayer.Visible) CloseAmountPrompt();
        else if (shopShown) CloseShop();
        else return false;
        return true;
    }

    internal IEscapable SellStallAndMenu => new EscapeGroup(() => new IEscapable[]
    {
        new ClosablePanel(() => sellStallShown, CloseSellStall),
        new ClosablePanel(() => MenuShown, CloseMenu),
    });

    public void Dispose()
    {
        marketPriceWindow.PriceCached -= RefreshStallPriceHint;
        progress.GoldChangeEvent -= OnGold;
        merchant.MerchantOpenResultEvent -= OnOpenResult;
        merchant.MerchantItemAddEvent -= OnItemAdd;
        merchant.MerchantItemCancelEvent -= OnItemCancel;
        merchant.MerchantListEvent -= OnList;
        merchant.MerchantBuyEvent -= OnBuy;
        merchant.MerchantSoldEvent -= OnSold;
        merchant.MerchantInsertedEvent -= OnInserted;
        merchant.MerchantStallClosedEvent -= OnStallClosed;
    }

    private void BuildPanels()
    {
        ServiceLayer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(ServiceLayer);

        BuildMenu();
        BuildSellStallPanel();
        BuildShopPanel();
    }

    internal event System.Action? BuyingMerchantRequested;

    private void BuildMenu()
    {
        Menu = new HudWindow("merchantmenu", "Merchant") { Visible = false };
        Menu.Closed += CloseMenu;
        ServiceLayer.AddChild(Menu);

        var root = Menu.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.CustomMinimumSize = new Vector2(250, 0);

        root.AddChild(MenuButton("Selling Merchant", OpenSellStall));
        root.AddChild(MenuButton("Buying Merchant", () => BuyingMerchantRequested?.Invoke()));
        root.AddChild(MenuButton("Market Price", OpenMarketPriceFromMenu));
    }

    internal static Button MenuButton(string text, System.Action onPress)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.CustomMinimumSize = new Vector2(0, 30);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += onPress;
        return button;
    }

    private void BuildSellStallPanel()
    {
        SellStallPanel = new HudWindow("sellstall", "Selling Merchant") { Visible = false };
        SellStallPanel.Closed += CloseSellStall;
        ServiceLayer.AddChild(SellStallPanel);

        var root = SellStallPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("On the stall"));
        SellStallCells = MerchantUi.BuildGrid(context.Tooltips, root, MerchantUi.StallSlots, MerchantUi.StallColumns, null,
            dragKey: "stallFrom", acceptKey: "bagFrom", onDropFrom: (bag, _) => StageStallItem(bag));

        var money = UiTheme.Section();
        root.AddChild(money);
        var moneyBox = new VBoxContainer();
        moneyBox.AddThemeConstantOverride("separation", 3);
        money.AddChild(moneyBox);
        moneyBox.AddChild(MerchantUi.MoneyRow("Total Selling Price", out sellTotal, UiTheme.GoldBright));
        moneyBox.AddChild(MerchantUi.MoneyRow("Current Balance", out sellBalance, UiTheme.TextHi));

        root.AddChild(UiTheme.SectionTitle("Your bags"));
        sellBagCells = MerchantUi.BuildGrid(context.Tooltips, root, Inventory.GridCount, MerchantUi.StallBagColumns, null,
            dragKey: "bagFrom", acceptKey: "stallFrom", onDropFrom: (stall, _) => UnstageStallItem(stall));

        var advertRow = new HBoxContainer();
        advertRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(advertRow);
        var advertLabel = UiTheme.Text("Shop name", 12, UiTheme.TextLo);
        advertRow.AddChild(advertLabel);
        SellAdvert = new LineEdit
        {
            PlaceholderText = "optional",
            MaxLength = AdvertMax,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        advertRow.AddChild(SellAdvert);

        sellStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        sellStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(sellStatus);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        var confirm = new Button { Text = "Confirm", FocusMode = Control.FocusModeEnum.None };
        confirm.CustomMinimumSize = new Vector2(104, 28);
        confirm.Pressed += ConfirmSellStall;
        footer.AddChild(confirm);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.CustomMinimumSize = new Vector2(104, 28);
        cancel.Pressed += CloseSellStall;
        footer.AddChild(cancel);
    }

    private void BuildShopPanel()
    {
        ShopPanel = new HudWindow("shop", "Shop") { Visible = false };
        ShopPanel.Closed += CloseShop;
        ServiceLayer.AddChild(ShopPanel);

        var root = ShopPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("For sale"));
        shopCells = MerchantUi.BuildGrid(context.Tooltips, root, MerchantUi.StallSlots, MerchantUi.StallColumns, BuyFromStall,
            dragKey: "shopFrom");

        var money = UiTheme.Section();
        root.AddChild(money);
        money.AddChild(MerchantUi.MoneyRow("Current Balance", out shopBalance, UiTheme.TextHi));

        root.AddChild(UiTheme.SectionTitle("Your bags"));
        shopBagCells = MerchantUi.BuildGrid(context.Tooltips, root, Inventory.GridCount, MerchantUi.StallBagColumns, null,
            acceptKey: "shopFrom", onDropFrom: (shopSlot, _) => BuyFromStall(shopSlot));

        shopStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        shopStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        shopStatus.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(shopStatus);
    }

    internal void ToggleMenu()
    {
        if (MenuShown) { CloseMenu(); return; }
        if (context.Self.Dead || entityPlates.RefusedInSiegeWar()) return;
        Menu.Visible = true;
        MenuShown = true;
    }

    internal void CloseMenu()
    {
        Menu.Visible = false;
        MenuShown = false;
    }

    private void OpenSellStall()
    {
        CloseMenu();
        if (context.Self.Dead) return;
        requests.Open();
    }

    private void OpenMarketPriceFromMenu()
    {
        CloseMenu();
        marketPriceWindow.OpenMarketPrice();
    }

    internal void OnOpenResult(MerchantOpenResult status)
    {
        if (status != MerchantOpenResult.Accepted)
        {
            context.Log.Notice(status switch
            {
                MerchantOpenResult.UnderLevelled => "You must be level 30 to open a shop.",
                MerchantOpenResult.WhileDead => "You can't open a shop right now.",
                MerchantOpenResult.WhileTrading => "Finish your trade first.",
                MerchantOpenResult.WhileMerchanting => "Your shop is already open.",
                _ => "Couldn't open a shop.",
            });
            return;
        }

        for (int i = 0; i < myStall.Length; i++) { myStall[i] = default; myStallSrc[i] = -1; }
        SetSellStatus("", false);
        RefreshSellStall();
        SellStallPanel.Visible = true;
        sellStallShown = true;
        context.Movement.StopForInteraction();
    }

    internal void CloseSellStall()
    {
        context.Tooltips.Hide();
        if (!sellStallShown) return;
        sellStallShown = false;
        SellStallPanel.Visible = false;
        requests.Close();
    }

    private void ConfirmSellStall()
    {
        int staged = 0;
        foreach (var slot in myStall) if (!slot.IsEmpty) staged++;
        if (staged == 0) { SetSellStatus("Put at least one item on the stall.", true); return; }

        sellStallShown = false;
        SellStallPanel.Visible = false;
        context.Tooltips.Hide();
        requests.BeginSell(SellAdvert.Text.Trim());
    }

    internal void StageStallItem(int gridIndex)
    {
        if (gridIndex < 0 || gridIndex >= Inventory.GridCount) return;
        int absSlot = Inventory.GridStart + gridIndex;
        if (absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        if (System.Array.IndexOf(myStallSrc, absSlot) >= 0)
        {
            SetSellStatus("That one is already on the stall.", true);
            return;
        }

        int dst = -1;
        for (int i = 0; i < myStall.Length; i++) if (myStall[i].IsEmpty) { dst = i; break; }
        if (dst < 0) { SetSellStatus($"The stall only holds {MerchantUi.StallSlots} items.", true); return; }

        var slot = self.Inventory[absSlot];
        if (ItemStacks.UseRefusal(ItemUse.Trade, slot) is not ItemUseRules.Allowed and var refusal)
        {
            SetSellStatus(ItemData.Text(refusal, "This item cannot be traded."), true);
            return;
        }
        AskStallPrice(slot, absSlot, dst);
    }

    private void UnstageStallItem(int stallSlot)
    {
        if (stallSlot < 0 || stallSlot >= myStall.Length || myStall[stallSlot].IsEmpty) return;
        requests.RemoveItem((byte)stallSlot);
    }

    internal void OnItemAdd(MerchantItemAddReply reply)
    {
        if (!reply.Succeeded) { SetSellStatus("That item can't be sold from a stall.", true); return; }
        if (reply.DestinationPos >= 0 && reply.DestinationPos < myStall.Length)
        {
            myStall[reply.DestinationPos] = new MerchantStallItem
            {
                ItemId = reply.ItemId, Count = reply.Count, Durability = reply.Durability, Price = reply.Price,
            };
            myStallSrc[reply.DestinationPos] = Inventory.GridStart + reply.SourcePos;
        }
        SetSellStatus("", false);
        RefreshSellStall();
    }

    private void OnItemCancel(bool ok, int slot)
    {
        if (!ok || slot < 0 || slot >= myStall.Length) return;
        myStall[slot] = default;
        myStallSrc[slot] = -1;
        RefreshSellStall();
    }

    private void RefreshSellStall()
    {
        context.Tooltips.Hide();
        long total = 0;
        for (int i = 0; i < SellStallCells.Length; i++)
        {
            var item = myStall[i];
            SellStallCells[i].Set(MerchantUi.StallSlot(item), MerchantUi.PriceNote("Asking", item.Price, item.Count));
            if (!item.IsEmpty) total += (long)item.Price * item.Count;
        }
        sellTotal.Text = total.ToString("n0");
        sellBalance.Text = self.Sheet.Gold.ToString("n0");
        RefreshSellBags();
    }

    private void RefreshSellBags()
    {
        for (int i = 0; i < sellBagCells.Length; i++)
        {
            int absSlot = Inventory.GridStart + i;
            var slot = absSlot < self.Inventory.Length ? self.Inventory[absSlot] : default;
            bool listed = System.Array.IndexOf(myStallSrc, absSlot) >= 0;
            var cell = sellBagCells[i];
            cell.TipSlot = absSlot;
            cell.Set(slot, listed ? "listed" : "");
            cell.Modulate = listed ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }
    }

    private const int StallSoldText = 7703;
    private const int StallOpenedText = 7706;
    private const int StallClosedText = 7707;
    private static readonly Color StallLogTint = Color.Color8(255, 102, 0);

    private void StallLog(int textId, string fallback, params object[] extra)
    {
        var now = System.DateTime.Now;
        var args = new object[extra.Length + 2];
        args[0] = now.Hour;
        args[1] = now.Minute;
        extra.CopyTo(args, 2);
        context.Log.Add(TextTemplate.Fill(ItemData.Text(textId, fallback), args), CombatLogKind.Resource, StallLogTint,
            tab: textId == StallSoldText ? LogTab.Item : LogTab.System);
    }

    private void OnSold(int itemId, string buyerName) =>
        StallLog(StallSoldText, "Item Sold: %dh %dm ; Buyer: %s ; Item: %s", buyerName, ItemData.DisplayName(itemId));

    private void OnInserted(MerchantStallOpened stall)
    {
        if (!stall.Succeeded)
        {
            SetSellStatus("The shop could not be opened.", true);
            sellStallShown = true;
            SellStallPanel.Visible = true;
            return;
        }

        Adverts[stall.OwnerId] = stall.Advert;
        stalls.PlaceStall(stall.OwnerId, isBuying: false, stall.Flags, stall.ItemIds);

        if (stall.OwnerId != context.Self.CharId) return;

        sellStallShown = false;
        SellStallPanel.Visible = false;
        context.Tooltips.Hide();
        stalls.SetLock(true);
        StallLog(StallOpenedText, "Initiated vendor mode at %dhour %dminute.");
    }

    private void OnStallClosed(int charId)
    {
        Adverts.Remove(charId);
        stalls.RemoveStall(charId);
        if (shopShown && shopTargetId == charId) CloseShop();
        if (charId == context.Self.CharId)
        {
            StallLog(StallClosedText, "Ended vendor mode at %dhour %dminute.");
            stalls.SetLock(false);
            if (sellStallShown)
            {
                sellStallShown = false;
                SellStallPanel.Visible = false;
                context.Tooltips.Hide();
            }
            for (int i = 0; i < myStall.Length; i++) { myStall[i] = default; myStallSrc[i] = -1; }
        }
    }

    internal bool HasNearbyStall()
    {
        if (!context.Ready || context.Self.Body == null || context.Self.Dead || stalls.Stalls.Count == 0) return false;
        foreach (var (id, ent) in context.Entities.Views)
        {
            if (id == context.Self.CharId || ent.IsNpc || ent.Dead) continue;
            if (!stalls.Stalls.ContainsKey(id)) continue;
            if (Coord.FlatDistance(context.Self.Body.Position, ent.Body.Position) <= ExchangeFeature.TradeRange)
                return true;
        }
        return false;
    }

    internal bool TryBrowseNearest()
    {
        if (!playerMenu.TryFindInteraction(true, out int bestId, out _)) return false;

        if (stalls.Stalls[bestId].IsBuying) requests.BuyMerchantList(bestId);
        else requests.List(bestId);
        return true;
    }

    internal void OnList(int merchantId, MerchantStallItem[] items)
    {
        CloseAmountPrompt();
        SetShopStatus("", false);
        shopTargetId = merchantId;
        shopItems = items;
        ShopPanel.Title = ShopTitle(merchantId);
        RefreshShop();
        ShopPanel.Visible = true;
        shopShown = true;
        context.Movement.StopForInteraction();
    }

    internal string ShopTitle(int merchantId)
    {
        string owner = context.Entities.Views.TryGetValue(merchantId, out var ent) ? ent.Name : "Shop";
        return Adverts.TryGetValue(merchantId, out string? advert) && advert.Length > 0
            ? $"{owner} — {advert}"
            : owner;
    }

    private void RefreshShopBags()
    {
        for (int i = 0; i < shopBagCells.Length; i++)
        {
            int absSlot = Inventory.GridStart + i;
            var cell = shopBagCells[i];
            cell.TipSlot = absSlot;
            cell.Set(absSlot < self.Inventory.Length ? self.Inventory[absSlot] : default);
        }
    }

    private void RefreshShop()
    {
        context.Tooltips.Hide();
        RefreshShopBags();
        int listed = 0;
        for (int i = 0; i < shopCells.Length; i++)
        {
            var item = shopItems[i];
            shopCells[i].Set(MerchantUi.StallSlot(item), MerchantUi.PriceNote("Price", item.Price, item.Count));
            if (!item.IsEmpty) listed++;
        }
        shopBalance.Text = self.Sheet.Gold.ToString("n0");
        if (listed == 0) SetShopStatus("This shop has nothing left.", false);
    }

    internal void BuyFromStall(int merchantSlot)
    {
        if (purchase != null || !shopShown || merchantSlot < 0 || merchantSlot >= shopItems.Length) return;
        var item = shopItems[merchantSlot];
        if (item.IsEmpty || item.Price < 1) return;
        if (item.Price > self.Sheet.Gold) { SetShopStatus("You don't have enough gold.", true); return; }

        var def = ItemData.Get(item.ItemId);
        bool stackable = def != null && def.Countable != 0;
        int room = self.Inventory.GridRoomFor(item.ItemId, stackable);
        if (room < 1) { SetShopStatus("Your bags are full.", true); return; }
        int most = System.Math.Max(1, System.Math.Min(System.Math.Min(item.Count, self.Sheet.Gold / item.Price), room));
        bool countable = stackable && most > 1;

        string itemName = ItemData.DisplayName(item.ItemId);
        string hint = countable
            ? $"Buy {itemName} for {MerchantUi.Money(item.Price)} each?"
            : $"Buy {itemName} for {MerchantUi.Money(item.Price)}?";

        AskTrade(MerchantUi.StallSlot(item), hint, item.Price, most, countable,
            (count, _) =>
            {
                if (!shopShown || purchase != null || shopItems[merchantSlot].ItemId != item.ItemId) return;
                int buyerSlot = self.Inventory.GridSlotFor(item.ItemId, count, stackable);
                if (buyerSlot < 0) { SetShopStatus("Your bags are full.", true); return; }
                if ((long)count * item.Price > self.Sheet.Gold) { SetShopStatus("You don't have enough gold.", true); return; }
                purchase = new MerchantUi.PendingTrade(shopTargetId, item.ItemId, count, item.Price);
                requests.Buy(item.ItemId, count, (byte)merchantSlot, (byte)(buyerSlot - Inventory.GridStart));
            });
    }

    private void OnGold(GoldChange change)
    {
        foreach (var label in new[] { sellBalance, shopBalance })
            if (GodotObject.IsInstanceValid(label)) label.Text = MerchantUi.Money(change.Total);
    }

    private void OnBuy(MerchantBuyReply reply)
    {
        var request = purchase;
        purchase = null;
        if (request is not { } action) return;
        string message = reply.Succeeded
            ? $"Bought {action.Count} x {ItemData.DisplayName(action.ItemId)} for {MerchantUi.Money((long)action.Price * action.Count)}."
            : "That purchase was refused.";
        if (!shopShown || shopTargetId != action.MerchantId) { context.Log.Notice(message); return; }
        if (!reply.Succeeded) { SetShopStatus(message, true); return; }

        if (reply.MerchantSlot >= 0 && reply.MerchantSlot < shopItems.Length && shopItems[reply.MerchantSlot].ItemId == reply.ItemId)
        {
            shopItems[reply.MerchantSlot].Count = reply.Remaining;
            if (reply.Remaining <= 0) shopItems[reply.MerchantSlot] = default;
        }

        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
        RefreshShop();
        SetShopStatus(message, false);
    }

    internal void CloseShop()
    {
        CloseAmountPrompt();
        context.Tooltips.Hide();
        if (!shopShown) return;
        shopShown = false;
        ShopPanel.Visible = false;
        shopTargetId = -1;
        requests.TradeCancel();
    }

    private void SetSellStatus(string text, bool warn)
    {
        sellStatus.Text = text;
        sellStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void SetShopStatus(string text, bool warn)
    {
        shopStatus.Text = text;
        shopStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    internal CanvasLayer AmountLayer { get; private set; } = null!;
    private TextureRect amountIcon = null!;
    private Label amountName = null!;
    private Label amountHint = null!;
    internal Label AmountTotal { get; private set; } = null!;
    private SpinBox amountCount = null!;
    internal MoneyEdit AmountPrice { get; private set; } = null!;
    private Label amountPriceFixed = null!;
    private HBoxContainer amountPriceRow = null!;
    private HBoxContainer amountCountRow = null!;
    private PanelContainer amountTotalRow = null!;
    private Label amountPriceLabel = null!;
    private Button amountConfirmBtn = null!;

    private System.Action<int, int>? amountAccept;

    private const int PriceMax = 2_000_000_000;

    private void BuildAmountPrompt()
    {
        AmountLayer = new CanvasLayer { Layer = UiLayers.TopPrompts, Visible = false };
        context.Root.AddChild(AmountLayer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        AmountLayer.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        AmountLayer.AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 14, 12, 14, 12);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        root.AddChild(head);
        amountIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(amountIcon);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        amountName = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(amountName);
        amountHint = UiTheme.Text("", 11, UiTheme.Gold);
        headText.AddChild(amountHint);

        amountCountRow = new HBoxContainer();
        amountCountRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(amountCountRow);
        var countLabel = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        countLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        amountCountRow.AddChild(countLabel);
        amountCount = UiTheme.NumberBox(1, 9_999, 1, 120);
        amountCount.ValueChanged += _ => RefreshAmountTotal();
        amountCountRow.AddChild(amountCount);
        var most = UiTheme.SmallButton("Max", "Use the largest amount");
        most.Pressed += () => amountCount.Value = amountCount.MaxValue;
        amountCountRow.AddChild(most);

        amountPriceRow = new HBoxContainer();
        amountPriceRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(amountPriceRow);
        amountPriceLabel = UiTheme.Text("Price each", 13, UiTheme.TextLo);
        amountPriceLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        amountPriceRow.AddChild(amountPriceLabel);
        AmountPrice = new MoneyEdit(PriceMax, 160);
        AmountPrice.ValueChanged += _ => RefreshAmountTotal();
        amountPriceRow.AddChild(AmountPrice);
        amountPriceFixed = UiTheme.Text("", 13, UiTheme.GoldBright);
        amountPriceFixed.HorizontalAlignment = HorizontalAlignment.Right;
        amountPriceFixed.CustomMinimumSize = new Vector2(160, 0);
        amountPriceRow.AddChild(amountPriceFixed);

        amountTotalRow = UiTheme.Section();
        root.AddChild(amountTotalRow);
        amountTotalRow.AddChild(MerchantUi.MoneyRow("Total", out var total, UiTheme.GoldBright));
        AmountTotal = total;

        amountMarketRow = new HBoxContainer { Visible = false };
        amountMarketRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(amountMarketRow);
        amountMarketHint = UiTheme.Text("", 12, UiTheme.TextDim);
        amountMarketHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        amountMarketRow.AddChild(amountMarketHint);
        var marketPriceButton = UiTheme.SmallButton("Market Price", MarketPriceFeature.MarketPriceText(MarketPriceFeature.MarketPriceSearchTipText, "Search the price of selected item"));
        marketPriceButton.Pressed += () => marketPriceWindow.OpenMarketPrice(AmountMarketItem);
        amountMarketRow.AddChild(marketPriceButton);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        amountConfirmBtn = new Button { Text = "Yes", FocusMode = Control.FocusModeEnum.None };
        amountConfirmBtn.CustomMinimumSize = new Vector2(104, 28);
        amountConfirmBtn.Pressed += AcceptAmount;
        footer.AddChild(amountConfirmBtn);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.CustomMinimumSize = new Vector2(104, 28);
        cancel.Pressed += CloseAmountPrompt;
        footer.AddChild(cancel);
    }

    private void AskStallPrice(ItemSlot slot, int absSlot, int stallSlot)
    {
        int suggested = System.Math.Max(1, ItemData.SellPrice(slot.ItemId));
        AskAmount(slot, "Price this item", suggested, slot.Count, slot.Count > 1,
            (count, price) => requests.AddItem(
                slot.ItemId, count, price, (byte)(absSlot - Inventory.GridStart), (byte)stallSlot),
            defaultCount: slot.Count, priceHintItem: slot.ItemId);
    }

    internal void AskTrade(
        ItemSlot slot, string hint, int price, int maxCount, bool countable,
        System.Action<int, int> accept)
    {
        AskAmount(slot, hint, price, maxCount, countable, accept, priceEditable: false, defaultCount: 1);
    }

    internal void AskAmount(
        ItemSlot slot, string hint, int price, int maxCount, bool countable,
        System.Action<int, int> accept, bool priceEditable = true, int defaultCount = 0, int priceHintItem = 0)
    {
        amountAccept = accept;
        amountIcon.Texture = ItemIcons.Get(slot.ItemId);
        amountName.Text = ItemData.DisplayName(slot.ItemId);
        amountHint.Text = hint;

        bool pickQuantity = countable && maxCount > 1;
        amountCountRow.Visible = pickQuantity;
        amountTotalRow.Visible = pickQuantity;
        amountPriceLabel.Text = pickQuantity ? "Price each" : "Price";
        amountCount.MaxValue = System.Math.Max(1, maxCount);
        amountCount.Value = pickQuantity
            ? System.Math.Clamp(defaultCount > 0 ? defaultCount : maxCount, 1, maxCount)
            : 1;

        amountPriceRow.Visible = price > 0;
        AmountPrice.Value = price;
        AmountPrice.Visible = priceEditable;
        amountPriceFixed.Visible = !priceEditable;
        amountPriceFixed.Text = MerchantUi.Money(price);

        RefreshAmountTotal();
        amountConfirmBtn.Text = priceEditable ? "Confirm" : "Yes";
        AmountLayer.Visible = true;
        ShowStallPriceHint(priceHintItem);
    }

    private void RefreshAmountTotal()
    {
        long count = (long)amountCount.Value;
        long price = amountPriceRow.Visible ? AmountPrice.Value : 0;
        AmountTotal.Text = MerchantUi.Money(count * price);
        RefreshStallPriceHint();
    }

    private void AcceptAmount()
    {
        var accept = amountAccept;
        int count = (int)amountCount.Value;
        int price = (int)System.Math.Clamp(AmountPrice.Value, 1, PriceMax);
        CloseAmountPrompt();
        accept?.Invoke(count, price);
    }

    internal void CloseAmountPrompt()
    {
        amountAccept = null;
        AmountLayer.Visible = false;
    }

    internal bool SelfStallOpen => sellStallShown || stalls.HasStall(context.Self.CharId);

    private const int MarketPriceNoActivityText = 44670;
    private const int MarketPriceLowText = 44673;
    private const int MarketPriceExpensiveText = 44674;

    internal int AmountMarketItem { get; private set; }
    private HBoxContainer amountMarketRow = null!;
    private Label amountMarketHint = null!;

    private void ShowStallPriceHint(int itemId)
    {
        AmountMarketItem = itemId > 0 && premium.HasPremium ? itemId : 0;
        amountMarketRow.Visible = AmountMarketItem > 0;
        if (AmountMarketItem == 0) return;
        amountMarketHint.Text = "";
        if (marketPriceWindow.CachedPrice(itemId) != null) RefreshStallPriceHint();
        else marketPriceWindow.AskMarketPrice(itemId, quiet: true);
    }

    private void RefreshStallPriceHint()
    {
        if (AmountMarketItem == 0 || !AmountLayer.Visible) return;
        if (marketPriceWindow.CachedPrice(AmountMarketItem) is not { } reply) return;

        long average = MarketPrice.MarketAverage(reply.Days);
        var verdict = MarketPrice.Judge(AmountPrice.Value, average);
        switch (verdict)
        {
            case MarketPriceVerdict.Low:
                amountMarketHint.Text = $"{MarketPriceFeature.MarketPriceText(MarketPriceLowText, "Low Price")} · {UiKit.GoldAmount(average)}";
                amountMarketHint.AddThemeColorOverride("font_color", UiTheme.Good);
                break;
            case MarketPriceVerdict.Expensive:
                amountMarketHint.Text = $"{MarketPriceFeature.MarketPriceText(MarketPriceExpensiveText, "Expensive")} · {UiKit.GoldAmount(average)}";
                amountMarketHint.AddThemeColorOverride("font_color", UiTheme.Bad);
                break;
            default:
                amountMarketHint.Text = MarketPriceFeature.MarketPriceText(MarketPriceNoActivityText, "There's no recent Trading Activity.");
                amountMarketHint.AddThemeColorOverride("font_color", UiTheme.TextDim);
                break;
        }
    }
}
