using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ShoppingMallFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly ShoppingMallSession shoppingMall;
    private readonly IShoppingMallRequests requests;
    private readonly PowerUpStoreGiftFeature gift;

    public ShoppingMallFeature(IWorldContext context, SelfSession self, ShoppingMallSession shoppingMall, IShoppingMallRequests requests, PowerUpStoreGiftFeature gift)
    {
        this.context = context;
        this.self = self;
        this.shoppingMall = shoppingMall;
        this.requests = requests;
        this.gift = gift;
        CatalogView = new PowerUpStoreCatalogView(context.Tooltips, Model, self);
        CartView = new PowerUpStoreCartView(context.Tooltips, Model, self);
        DetailsView = new PowerUpStoreDetailsView(context.Tooltips, Model, self);
        CatalogView.EntryPicked += OpenDetails;
        CartView.Rendered += CatalogView.RefreshCardStates;
        CartView.GiftRequested += OpenGiftPicker;
        CartView.CheckoutRequested += BuyCart;
        DetailsView.CloseRequested += CloseModal;
        DetailsView.AddRequested += AddDetailsToCart;
        DetailsView.BuyRequested += BuyDetailsNow;
    }

    private const int ColumnGap = 10;
    private const double TimerTickSeconds = 15;
    private const float ModalDim = 0.62f;
    private const short OpenSucceeded = 1;
    private const short OpenWhileDead = -2;
    private const short OpenWhileTrading = -3;
    private const short OpenWhileVending = -4;
    private const short OpenZoneClosed = -5;
    private static readonly Color WindowFill = new(0.066f, 0.068f, 0.075f, 0.975f);

    internal enum Modal { None, Details, Gift }

    internal PowerUpStoreModel Model { get; } = new();
    internal PowerUpStoreCatalogView CatalogView { get; }
    internal PowerUpStoreCartView CartView { get; }
    internal PowerUpStoreDetailsView DetailsView { get; }

    internal CanvasLayer Layer { get; private set; } = null!;
    internal HudWindow Window { get; private set; } = null!;
    private Viewport? viewport;
    private Godot.Timer ticker = null!;
    private bool shown;
    private bool refreshing;
    private Control overlay = null!;
    private Modal modal;

    internal bool IsOpen => shown;

    public void Build()
    {
        BuildPowerUpStore();
        shoppingMall.ShoppingMallOpenEvent += OnOpen;
        shoppingMall.ShoppingMallCatalogEvent += OnCatalog;
        shoppingMall.ShoppingMallCategoriesEvent += OnCategories;
        shoppingMall.ShoppingMallBalanceEvent += OnBalance;
        shoppingMall.ShoppingMallPurchaseEvent += OnPurchase;
        gift.CloseRequested += CloseModal;
        gift.RecipientConfirmed += OnRecipientConfirmed;
        viewport = context.Root.GetViewport();
        if (viewport != null) viewport.SizeChanged += FitPowerUpStore;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (IsOpen && modal != Modal.None) CloseModal();
        else if (IsOpen) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        shoppingMall.ShoppingMallOpenEvent -= OnOpen;
        shoppingMall.ShoppingMallCatalogEvent -= OnCatalog;
        shoppingMall.ShoppingMallCategoriesEvent -= OnCategories;
        shoppingMall.ShoppingMallBalanceEvent -= OnBalance;
        shoppingMall.ShoppingMallPurchaseEvent -= OnPurchase;
        gift.CloseRequested -= CloseModal;
        gift.RecipientConfirmed -= OnRecipientConfirmed;
        if (viewport != null && GodotObject.IsInstanceValid(viewport))
            viewport.SizeChanged -= FitPowerUpStore;
        viewport = null;
    }

    internal void BuildPowerUpStore()
    {
        Layer = new CanvasLayer { Layer = UiLayers.Boards };
        context.Root.AddChild(Layer);

        Window = new HudWindow("shoppingmall", "Power-Up Store", persistLayout: false) { Visible = false };
        var panel = UiTheme.WindowPanel();
        panel.BgColor = WindowFill;
        Window.AddThemeStyleboxOverride("panel", panel);
        Window.Closed += Close;
        Layer.AddChild(Window);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", ColumnGap);
        Window.Body.AddChild(columns);
        columns.AddChild(CatalogView.BuildCategoryColumn());
        columns.AddChild(CatalogView.BuildGridColumn());
        columns.AddChild(CartView.BuildCartColumn());

        BuildModal();

        ticker = new Godot.Timer { WaitTime = TimerTickSeconds };
        ticker.Timeout += TickTimers;
        Layer.AddChild(ticker);
    }

    private void BuildModal()
    {
        overlay = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        Window.AddChild(overlay);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, ModalDim), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            dim.AcceptEvent();
            Callable.From(CloseModal).CallDeferred();
        };
        overlay.AddChild(dim);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(centre);
        centre.AddChild(DetailsView.BuildDetailsBox());
        centre.AddChild(gift.BuildGiftBox());
    }

    public void Toggle()
    {
        if (shown) Close();
        else Open();
    }

    public void OpenPowerUpStore() => Open();

    public void Open()
    {
        if (shown) return;
        Model.StoreError = "";
        Show();
        CatalogView.UpdateWallet();
        CatalogView.RenderCategories();
        CatalogView.RenderGrid();
        CartView.RenderCart();
        ticker.Start();
        Callable.From(FitPowerUpStore).CallDeferred();
        requests.Open();
    }

    internal void Show()
    {
        shown = true;
        Window.Visible = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        ShowModal(Modal.None);
        ticker.Stop();
        Window.Visible = false;
        requests.Close();
    }

    internal void FitPowerUpStore()
    {
        if (!shown || !GodotObject.IsInstanceValid(Window) || !Window.IsInsideTree()) return;
        var target = PowerUpStoreLayout.WindowSize(Window.GetViewportRect().Size, Platform.TouchUi);
        var chrome = Window.GetCombinedMinimumSize() - Window.Body.GetCombinedMinimumSize();
        Window.Body.CustomMinimumSize = (target - chrome).Max(Vector2.Zero);
        Window.ResetSize();
        CentrePowerUpStore();
        Window.GetTree().Connect(SceneTree.SignalName.ProcessFrame, Callable.From(CentrePowerUpStore),
            (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void CentrePowerUpStore()
    {
        if (!shown || !GodotObject.IsInstanceValid(Window)) return;
        Window.Position = WindowPlacement.Centre(Window.GetViewportRect().Size, Window.Size);
    }

    internal void ShowModal(Modal modal)
    {
        context.Tooltips.Hide();
        this.modal = modal;
        DetailsView.SetShown(modal == Modal.Details);
        gift.SetShown(modal == Modal.Gift);
        overlay.Visible = modal != Modal.None;
        CatalogView.SetModalOpen(modal != Modal.None);
    }

    internal void CloseModal()
    {
        if (!GodotObject.IsInstanceValid(overlay)) return;
        if (modal == Modal.Details && Model.Pending == PowerUpStorePurchase.BuyNow) return;
        ShowModal(Modal.None);
    }

    private void OnOpen(short error, short freeSlot)
    {
        Model.StoreError = error switch
        {
            OpenSucceeded => "",
            OpenWhileDead => "You can't shop while dead.",
            OpenWhileTrading => "Close your trade first.",
            OpenWhileVending => "Close your stall first.",
            OpenZoneClosed => "The store is closed in this zone.",
            _ => "The store couldn't open.",
        };
        CatalogView.RenderGrid();
    }

    internal void OnCatalog(List<PowerUpStoreEntry> entries)
    {
        Model.ReplaceCatalog(entries
            .Select(entry => (Entry: entry, Item: ItemData.Get(entry.ItemId)))
            .Where(known => known.Item != null)
            .Select(known => known.Entry with
            {
                Name = ItemData.DisplayName(known.Entry.ItemId),
                Description = string.Join("\n", ItemText.DescriptionLines(known.Item!.Desc)),
            }));
        Model.Loaded = true;
        var repriced = Model.Cart.Reprice(Model.Catalog);
        if (refreshing && repriced)
            CartView.SetCartStatus("Prices changed. Check your cart before buying.", true);
        refreshing = false;
        if (!Model.Catalog.Exists(e => e.Featured) && CatalogView.Category == PowerUpStoreCatalog.FeaturedCategory && Model.Categories.Count > 0)
            CatalogView.Category = Model.Categories[0].Id;
        CatalogView.RenderCategories();
        CatalogView.RenderGrid();
        CartView.RenderCart();
    }

    internal void OnCategories(List<ShoppingMallCategory> categories)
    {
        var known = Model.Categories;
        known.Clear();
        known.AddRange(categories);
        if (CatalogView.Category != PowerUpStoreCatalog.FeaturedCategory
            && !known.Exists(c => c.Id == CatalogView.Category) && known.Count > 0)
            CatalogView.Category = known[0].Id;
        if (!Model.Catalog.Exists(e => e.Featured) && CatalogView.Category == PowerUpStoreCatalog.FeaturedCategory && known.Count > 0)
            CatalogView.Category = known[0].Id;
        CatalogView.RenderCategories();
        CatalogView.RenderGrid();
    }

    private void RefreshCatalogue()
    {
        if (refreshing) return;
        refreshing = true;
        requests.Open();
    }

    private void OnBalance(int knightCash)
    {
        context.Inventory.RefreshCash();
        CatalogView.UpdateWallet();
        CartView.RenderCart();
        DetailsView.RenderDetails();
    }

    private void TickTimers()
    {
        var now = DateTime.UtcNow;
        var ended = false;
        var timers = DetailsView.Timer is { } details ? CatalogView.Timers.Append(details) : CatalogView.Timers;
        foreach (var (label, endsAt) in timers)
        {
            if (!GodotObject.IsInstanceValid(label)) continue;
            var text = PowerUpStoreTimer.Label(endsAt - now);
            label.Text = text;
            ended |= text.Length == 0;
        }
        if (ended && shown) RefreshCatalogue();
    }

    private void BuyCart()
    {
        if (Model.Cart.IsEmpty || Model.Busy) return;
        if (!Model.Cart.Affordable(self.Sheet.KnightCash))
        {
            CartView.SetCartStatus("You don't have enough cash for this.", true);
            return;
        }
        Model.Pending = PowerUpStorePurchase.Cart;
        CartView.SetCartStatus("", false);
        requests.PowerUpStorePurchase(Model.GiftRecipient?.Name ?? "", Model.Cart.Lines);
        CartView.RenderCart();
    }

    internal void OpenDetails(PowerUpStoreEntry entry)
    {
        DetailsView.Fill(entry);
        ShowModal(Modal.Details);
        DetailsView.RenderDetails();
    }

    private void AddDetailsToCart()
    {
        if (DetailsView.Entry is not { } entry) return;
        Model.Cart.Add(entry);
        CloseModal();
        CartView.RenderCart();
    }

    private void BuyDetailsNow()
    {
        if (DetailsView.Entry is not { } entry || Model.Busy || entry.Price > self.Sheet.KnightCash) return;
        var single = new PowerUpStoreCart();
        single.Add(entry);
        Model.Pending = PowerUpStorePurchase.BuyNow;
        DetailsView.SetDetailsStatus("");
        requests.PowerUpStorePurchase("", single.Lines);
        DetailsView.RenderDetails();
        CartView.RenderCart();
    }

    private void OpenGiftPicker()
    {
        if (Model.Busy) return;
        ShowModal(Modal.Gift);
        gift.Open();
    }

    private void OnRecipientConfirmed(PowerUpStoreRecipient recipient)
    {
        Model.GiftRecipient = recipient;
        CartView.SetCartStatus("", false);
        CloseModal();
        CartView.RenderCart();
    }

    private void OnPurchase(PowerUpStoreResult result, int knightCash)
    {
        var pending = Model.Pending;
        Model.Pending = PowerUpStorePurchase.None;
        if (knightCash >= 0) OnBalance(knightCash);

        if (result == PowerUpStoreResult.Succeeded)
        {
            string message;
            if (pending == PowerUpStorePurchase.BuyNow)
            {
                ShowModal(Modal.None);
                message = "Bought. The item is waiting in your mailbox.";
            }
            else if (Model.GiftRecipient is { } recipient)
            {
                message = $"Gift sent. {recipient.Name} receives it by mail.";
                Model.Cart.Clear();
                Model.GiftRecipient = null;
            }
            else
            {
                message = "Bought. Your items are waiting in your mailbox.";
                Model.Cart.Clear();
            }
            CartView.SetCartStatus(message, false);
            context.Log.Notice(message);
            CartView.RenderCart();
            return;
        }

        if (result == PowerUpStoreResult.PriceChanged)
            RefreshCatalogue();
        var failure = FailureText(result, pending);
        if (pending == PowerUpStorePurchase.BuyNow) DetailsView.SetDetailsStatus(failure);
        else CartView.SetCartStatus(failure, true);
        CartView.RenderCart();
        DetailsView.RenderDetails();
    }

    private static string FailureText(PowerUpStoreResult result, PowerUpStorePurchase pending) => result switch
    {
        PowerUpStoreResult.NotEnoughCash => "You don't have enough cash for this.",
        PowerUpStoreResult.Unavailable => pending == PowerUpStorePurchase.BuyNow
            ? "This item is no longer sold."
            : "Something in your cart is no longer sold. Remove it and try again.",
        PowerUpStoreResult.RecipientNotFound => "The gift recipient no longer exists. Choose the recipient again.",
        PowerUpStoreResult.RecipientIsSelf => "You can't send a gift to yourself.",
        PowerUpStoreResult.RecipientNotAllowed => "Gifts go only to friends and clan members. Choose the recipient again.",
        PowerUpStoreResult.PriceChanged => "A price changed. The store was refreshed; check the price and buy again.",
        _ => "The purchase didn't go through. Try again.",
    };
}
