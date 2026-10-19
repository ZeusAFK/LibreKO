using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PowerUpStoreCatalogView
{
    private const string RechargeUrl = "https://libreko.org";
    private const int CashIconSize = 15;
    private const int CashFontSize = 14;
    internal const int CategoryWidth = 228;
    private const int CategoryFontSize = 13;
    private const int CategoryIconSize = 14;
    private const float CategoryRowHeight = 40f;
    private const float CategoryTouchRowHeight = 54f;
    private const float CardWidth = 128f;
    private const float CardHeight = 168f;
    private const float CardGap = 8f;
    private const float CardIconSide = 64f;
    private const int CardSideMargin = 8;
    private const int CardTopMargin = 26;
    private const int CardBottomMargin = 8;
    private const int CardNameFontSize = 13;
    private const int CardNameLines = 2;
    private const int CardPriceFontSize = 14;
    private const int ChipInset = 5;
    private const float BadgeSide = 26f;
    private const int TitleFontSize = 17;
    private const float SortWidth = 172f;
    private const float SearchWidth = 240f;
    private const float SearchHeight = 32f;
    private const int SearchFontSize = 13;
    private static readonly Color CardFill = new(0.072f, 0.071f, 0.080f, 0.9f);
    private static readonly Color FeaturedFill = new(0.16f, 0.13f, 0.08f, 0.92f);

    private sealed class CardView
    {
        public required PowerUpStoreEntry Entry;
        public required PanelContainer Card;
        public required PanelContainer CartChip;
        public required Label CartCount;
    }

    private readonly IItemTooltips tooltips;
    private readonly PowerUpStoreModel model;
    private readonly SelfSession self;

    public PowerUpStoreCatalogView(IItemTooltips tooltips, PowerUpStoreModel model, SelfSession self)
    {
        this.tooltips = tooltips;
        this.model = model;
        this.self = self;
    }

    internal event Action<PowerUpStoreEntry>? EntryPicked;

    private Label cashValue = null!;
    private VBoxContainer categoryList = null!;
    private Label gridTitle = null!;
    private LineEdit searchInput = null!;
    private OptionButton sortPick = null!;
    private ScrollContainer gridScroll = null!;
    private GridContainer grid = null!;
    private Label gridEmpty = null!;
    internal int Category { get; set; } = PowerUpStoreCatalog.FeaturedCategory;
    private PowerUpStoreSort sort = PowerUpStoreCatalog.DefaultSort;
    private readonly Dictionary<int, CardView> cards = new();

    private readonly List<(Label Label, DateTime EndsAt)> pusTimerLabels = new();
    private int hoveredCard = -1;
    private bool modalOpen;

    internal IEnumerable<(Label Label, DateTime EndsAt)> Timers => pusTimerLabels;

    internal Control BuildCategoryColumn()
    {
        var well = PowerUpStoreUi.Well();
        well.CustomMinimumSize = new Vector2(CategoryWidth, 0);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        UiTheme.ThinScrollbar(scroll.GetVScrollBar());
        well.AddChild(scroll);
        categoryList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        categoryList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(categoryList);
        return well;
    }

    internal Control BuildGridColumn()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 8);

        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 8);
        column.AddChild(title);
        gridTitle = UiTheme.Text("", TitleFontSize, UiTheme.GoldBright);
        gridTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        gridTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        gridTitle.ClipText = true;
        gridTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.AddChild(gridTitle);
        title.AddChild(BuildCashRow());

        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 8);
        column.AddChild(tools);
        tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var search = PowerUpStoreUi.SearchField("Search items", SearchFontSize, SearchHeight, out searchInput);
        search.CustomMinimumSize = new Vector2(SearchWidth, SearchHeight);
        searchInput.TextChanged += _ =>
        {
            RenderCategories();
            RenderGrid();
        };
        tools.AddChild(search);
        sortPick = UiTheme.Dropdown(PowerUpStoreCatalog.Sorts.Select(PowerUpStoreCatalog.Label).ToList());
        sortPick.CustomMinimumSize = new Vector2(SortWidth, SearchHeight);
        sortPick.TooltipText = "Sort the items";
        sortPick.Selected = PowerUpStoreCatalog.Sorts.ToList().IndexOf(sort);
        sortPick.ItemSelected += index =>
        {
            sort = PowerUpStoreCatalog.Sorts[(int)index];
            RenderGrid();
        };
        tools.AddChild(sortPick);

        gridScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        UiTheme.ThinScrollbar(gridScroll.GetVScrollBar());
        gridScroll.Resized += FitGridColumns;
        column.AddChild(gridScroll);
        var badgeRoom = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        badgeRoom.AddThemeConstantOverride("margin_top", (int)(BadgeSide / 2) + 2);
        gridScroll.AddChild(badgeRoom);
        grid = new GridContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", (int)CardGap);
        grid.AddThemeConstantOverride("v_separation", (int)(CardGap + BadgeSide / 2));
        badgeRoom.AddChild(grid);

        gridEmpty = UiTheme.Text("", 14, UiTheme.TextLo, HorizontalAlignment.Center);
        gridEmpty.VerticalAlignment = VerticalAlignment.Center;
        gridEmpty.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        gridEmpty.Visible = false;
        column.AddChild(gridEmpty);
        return column;
    }

    private Control BuildCashRow()
    {
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddThemeConstantOverride("separation", 6);
        var pill = new PanelContainer { TooltipText = "Your cash", MouseFilter = Control.MouseFilterEnum.Pass };
        var style = UiTheme.Chip();
        style.ContentMarginTop = style.ContentMarginBottom = 2;
        pill.AddThemeStyleboxOverride("panel", style);
        var cash = new HBoxContainer();
        cash.AddThemeConstantOverride("separation", 5);
        pill.AddChild(cash);
        cash.AddChild(PowerUpStoreUi.Gem(CashIconSize));
        cashValue = UiTheme.Text("0", CashFontSize, UiTheme.TextHi);
        cash.AddChild(cashValue);
        row.AddChild(pill);
        var recharge = UiTheme.IconButton("+", "Get more cash on libreko.org");
        recharge.Pressed += () => OS.ShellOpen(RechargeUrl);
        row.AddChild(recharge);
        return row;
    }

    internal void UpdateWallet()
    {
        if (!GodotObject.IsInstanceValid(cashValue)) return;
        cashValue.Text = self.Sheet.KnightCash.ToString("n0");
    }

    internal void SetModalOpen(bool open)
    {
        modalOpen = open;
        hoveredCard = -1;
        RefreshCardStates();
    }

    private void FitGridColumns()
    {
        var bar = gridScroll.GetVScrollBar();
        var width = gridScroll.Size.X - (bar.Visible ? bar.Size.X : 0);
        var columns = PowerUpStoreLayout.Columns(width, CardWidth, CardGap);
        if (grid.Columns != columns) grid.Columns = columns;
    }

    internal void RenderCategories()
    {
        UiKit.ClearChildren(categoryList);
        var searching = searchInput.Text.Trim().Length > 0;
        if (model.Catalog.Exists(e => e.Featured))
            categoryList.AddChild(CategoryButton(PowerUpStoreCatalog.FeaturedCategory, PowerUpStoreUi.FeaturedName, searching));
        foreach (var mallCategory in model.Categories)
            categoryList.AddChild(CategoryButton(mallCategory.Id, mallCategory.Name, searching));
    }

    private Button CategoryButton(int id, string name, bool searching)
    {
        var selected = id == Category && !searching;
        var button = new Button
        {
            Text = name,
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, Platform.Pick(CategoryRowHeight, CategoryTouchRowHeight)),
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        if (PowerUpStoreCatalog.HasDiscount(model.Catalog, id))
        {
            button.Icon = UiIcons.Get("system/clock");
            button.IconAlignment = HorizontalAlignment.Right;
            button.TooltipText = "Items on sale";
            button.AddThemeConstantOverride("icon_max_width", CategoryIconSize);
            foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
                button.AddThemeColorOverride(state, PowerUpStoreUi.TimerChipColor);
        }
        button.AddThemeFontSizeOverride("font_size", CategoryFontSize);
        button.AddThemeColorOverride("font_color", selected ? UiTheme.GoldBright : UiTheme.TextLo);
        button.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        button.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
        button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(selected));
        button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += () =>
        {
            Category = id;
            searchInput.Text = "";
            Callable.From(() =>
            {
                RenderCategories();
                RenderGrid();
            }).CallDeferred();
        };
        return button;
    }

    internal void RenderGrid()
    {
        tooltips.Hide();
        UiKit.ClearChildren(grid);
        cards.Clear();
        pusTimerLabels.Clear();
        hoveredCard = -1;
        var search = searchInput.Text.Trim();
        var mallCategory = model.Categories.FirstOrDefault(c => c.Id == Category);
        var featured = Category == PowerUpStoreCatalog.FeaturedCategory;
        gridTitle.Text = search.Length > 0 ? $"Results for \"{search}\""
            : featured ? PowerUpStoreUi.FeaturedName
            : mallCategory.Name ?? "Power-Up Store";

        var shownEntries = PowerUpStoreCatalog.View(model.Catalog, Category, search, sort);
        foreach (var entry in shownEntries)
            grid.AddChild(BuildCard(entry));
        RefreshCardStates();

        string empty = model.StoreError.Length > 0 ? model.StoreError
            : !model.Loaded ? "Loading the store…"
            : shownEntries.Count > 0 ? ""
            : search.Length > 0 ? $"No items match \"{search}\"."
            : "Nothing is sold in this category yet.";
        gridEmpty.Text = empty;
        gridEmpty.Visible = empty.Length > 0;
        gridScroll.Visible = empty.Length == 0;
        if (gridEmpty.Visible) gridEmpty.AddThemeColorOverride("font_color", model.StoreError.Length > 0 ? UiTheme.Bad : UiTheme.TextLo);
        Callable.From(FitGridColumns).CallDeferred();
    }

    private static StyleBoxFlat CardStyle(bool featured, bool active)
    {
        var style = new StyleBoxFlat
        {
            BgColor = featured ? FeaturedFill : active ? UiTheme.RowHover : CardFill,
            BorderColor = active ? UiTheme.GoldVivid : featured ? new Color(UiTheme.Gold, 0.45f) : new Color(UiTheme.EdgeSoft, 0.45f),
        };
        style.SetBorderWidthAll(active ? 2 : 1);
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = style.ContentMarginRight = CardSideMargin;
        style.ContentMarginTop = CardTopMargin;
        style.ContentMarginBottom = CardBottomMargin;
        return style;
    }

    private Control BuildCard(PowerUpStoreEntry entry)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(CardWidth, CardHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        card.MouseEntered += () =>
        {
            hoveredCard = entry.Id;
            RefreshCardStates();
        };
        card.MouseExited += () =>
        {
            if (hoveredCard != entry.Id || card.GetGlobalRect().HasPoint(card.GetGlobalMousePosition())) return;
            hoveredCard = -1;
            RefreshCardStates();
        };
        card.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            card.AcceptEvent();
            Callable.From(() => EntryPicked?.Invoke(entry)).CallDeferred();
        };

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);

        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(CardIconSide, CardIconSide),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Slot());
        frame.MouseEntered += () => tooltips.Show(-1, UiKit.TooltipItem(entry.ItemId));
        frame.MouseExited += tooltips.Hide;
        frame.AddChild(new TextureRect
        {
            Texture = ItemIcons.Get(entry.ItemId),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        box.AddChild(frame);

        var name = UiTheme.Text(entry.Name, CardNameFontSize, UiTheme.TextHi, HorizontalAlignment.Center);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.MaxLinesVisible = CardNameLines;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.CustomMinimumSize = new Vector2(0, (CardNameFontSize + 4) * CardNameLines);
        name.VerticalAlignment = VerticalAlignment.Center;
        box.AddChild(name);

        var price = PowerUpStoreUi.PriceTag(entry, CardPriceFontSize);
        price.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(price);

        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddChild(overlay);
        if (entry.Featured) overlay.AddChild(FeaturedBadge());

        var cartChip = PowerUpStoreUi.Chip("system/cart", "", UiTheme.GoldVivid, UiTheme.Ink, out var cartCount);
        cartChip.TooltipText = "In your cart";
        cartChip.Position = new Vector2(ChipInset - CardSideMargin, ChipInset - CardTopMargin);
        overlay.AddChild(cartChip);

        if (entry.Discounted && entry.DiscountEndsAt is { } endsAt)
        {
            var timer = PowerUpStoreUi.Chip("system/clock", PowerUpStoreTimer.Label(endsAt - DateTime.UtcNow), PowerUpStoreUi.TimerChipColor, UiTheme.Ink, out var timerLabel);
            timer.TooltipText = "Time left at this price";
            timer.AnchorLeft = timer.AnchorRight = 1f;
            timer.OffsetLeft = timer.OffsetRight = CardSideMargin - ChipInset;
            timer.OffsetTop = timer.OffsetBottom = ChipInset - CardTopMargin;
            timer.GrowHorizontal = Control.GrowDirection.Begin;
            overlay.AddChild(timer);
            pusTimerLabels.Add((timerLabel, endsAt));
        }

        cards[entry.Id] = new CardView { Entry = entry, Card = card, CartChip = cartChip, CartCount = cartCount };
        return card;
    }

    private static Control FeaturedBadge()
    {
        var badge = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = PowerUpStoreUi.FeaturedName,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -BadgeSide / 2,
            OffsetRight = BadgeSide / 2,
            OffsetTop = -CardTopMargin - BadgeSide / 2,
            OffsetBottom = -CardTopMargin + BadgeSide / 2,
        };
        var style = new StyleBoxFlat { BgColor = UiTheme.GlassDeep, BorderColor = PowerUpStoreUi.TimerChipColor };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll((int)(BadgeSide / 2));
        style.SetContentMarginAll(4);
        badge.AddThemeStyleboxOverride("panel", style);
        badge.AddChild(UiIcons.Image("system/star", new Vector2(BadgeSide - 10, BadgeSide - 10), PowerUpStoreUi.TimerChipColor));
        return badge;
    }

    internal void RefreshCardStates()
    {
        foreach (var view in cards.Values)
        {
            if (!GodotObject.IsInstanceValid(view.Card)) continue;
            var inCart = model.Cart.Lines.FirstOrDefault(l => l.Entry.Id == view.Entry.Id)?.Count ?? 0;
            view.CartChip.Visible = inCart > 0;
            view.CartCount.Text = $"×{inCart}";
            var active = inCart > 0 || (hoveredCard == view.Entry.Id && !modalOpen);
            view.Card.AddThemeStyleboxOverride("panel", CardStyle(view.Entry.Featured, active));
        }
    }
}
