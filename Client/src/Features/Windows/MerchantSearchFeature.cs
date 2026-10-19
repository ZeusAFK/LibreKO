using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MerchantSearchFeature : IFeature, IEscapable, IMerchantSearch
{
    private readonly IWorldContext context;
    private readonly MarketPriceFeature marketPriceWindow;
    private readonly IMerchantRequests requests;
    private readonly IMerchantSearchRequests searchRequests;
    private readonly MerchantSearchSession searchSession;
    private readonly MerchantStallsFeature stalls;
    private readonly WhispersFeature whispers;
    private readonly HotbarFeature hotbar;

    public MerchantSearchFeature(IWorldContext context, MarketPriceFeature marketPriceWindow, IMerchantRequests requests, IMerchantSearchRequests searchRequests, MerchantSearchSession searchSession, MerchantStallsFeature stalls, WhispersFeature whispers, HotbarFeature hotbar)
    {
        this.context = context;
        this.marketPriceWindow = marketPriceWindow;
        this.requests = requests;
        this.searchRequests = searchRequests;
        this.searchSession = searchSession;
        this.stalls = stalls;
        this.whispers = whispers;
        this.hotbar = hotbar;
    }

    public void Build()
    {
        hotbar.AttachMerchantSearch(this);
        BuildSearchPanel();
        searchSession.MerchantSearchOpenEvent += OnSearchOpen;
        searchSession.MerchantSearchRowsEvent += OnSearchRows;
        searchSession.MerchantSearchLoadedEvent += OnSearchLoaded;
        searchSession.MerchantSearchRefusedEvent += OnSearchRefused;
        searchSession.MerchantSearchMovedEvent += OnSearchMoved;
    }

    public void Dispose()
    {
        hotbar.DetachMerchantSearch();
        searchSession.MerchantSearchOpenEvent -= OnSearchOpen;
        searchSession.MerchantSearchRowsEvent -= OnSearchRows;
        searchSession.MerchantSearchLoadedEvent -= OnSearchLoaded;
        searchSession.MerchantSearchRefusedEvent -= OnSearchRefused;
        searchSession.MerchantSearchMovedEvent -= OnSearchMoved;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!searchShown) return false;
        CloseSearch();
        return true;
    }

    bool IMerchantSearch.RequestSearch(int itemId) => RequestSearch(itemId);

    private const int SearchTipText = 40045;
    private const int SearchSearchText = 40042;
    private const int SearchFindText = 40044;
    private const int SearchAllText = 40041;
    private const int SearchScopeText = 40043;
    private const int SearchSellText = 30521;
    private const int SearchBuyText = 30522;
    private const int SearchChatText = 40034;
    private const int SearchLocationText = 40031;
    private const int SearchHistoryText = 40030;
    private const int SearchByNameText = 40033;
    private const int SearchItemPriceText = 40032;
    private const int SearchLowText = 40040;
    private const int SearchHighText = 40039;
    private const int SearchWhisperText = 40035;
    private const int SearchMoveText = 40036;
    private const int SearchViewText = 40037;
    private const int SearchBusyText = 16810;
    private const int SearchCooldownText = 30937;
    private const float SearchActionColumn = 76f;
    private const float SearchPriceColumn = 150f;
    private const float SearchRowHeight = 26f;
    private const double SearchStallDelay = 1.0;

    private sealed class SearchLine
    {
        public HBoxContainer Root = null!;
        public Button Whisper = null!, Move = null!, View = null!;
        public TextureRect Icon = null!;
        public UpgradeBadge Plus = null!;
        public Label Name = null!, Price = null!;
        public MerchantSearchRow? Row;
    }

    private readonly MerchantSearch search = new();
    private readonly List<SearchLine> searchLines = new();
    private CanvasLayer searchLayer = null!;
    internal HudWindow SearchPanel { get; private set; } = null!;
    private LineEdit searchEdit = null!;
    private OptionButton searchScope = null!;
    private HBoxContainer searchPages = null!;
    private Label searchStatus = null!;
    private bool searchShown;
    private bool searchLoading;
    private double searchReadyAt;
    private string searchMoveTo = "";

    private static string SearchText(int id, string fallback) => ItemData.Text(id, fallback);

    internal void BuildSearchPanel()
    {
        searchLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(searchLayer);

        SearchPanel = new HudWindow("merchantsearch", ItemData.DisplayName(MerchantSearch.OfficialListItem).Trim(),
            bodyMinWidth: 720) { Visible = false };
        SearchPanel.Closed += CloseSearch;
        searchLayer.AddChild(SearchPanel);

        var body = SearchPanel.Body;
        body.AddThemeConstantOverride("separation", 8);

        var tip = UiTheme.Text(SearchText(SearchTipText, "An item can be sold or purchased while searching."),
            12, UiTheme.TextDim, HorizontalAlignment.Center);
        body.AddChild(tip);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        body.AddChild(top);
        top.AddChild(SearchLabel(SearchText(SearchSearchText, "Search")));
        searchEdit = new LineEdit { CustomMinimumSize = new Vector2(180, 0) };
        searchEdit.TextSubmitted += _ => RunSearch();
        top.AddChild(searchEdit);
        top.AddChild(GenderChangeFeature.LookActionButton(SearchText(SearchFindText, "Search"), RunSearch));
        top.AddChild(GenderChangeFeature.LookActionButton(SearchText(SearchAllText, "View All"), ShowAllSearch));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        top.AddChild(SearchLabel(SearchText(SearchScopeText, "Scope")));
        searchScope = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 0) };
        searchScope.AddItem(SearchText(SearchSellText, "Sell Merchant"), MerchantSearch.SellingType);
        searchScope.AddItem(SearchText(SearchBuyText, "Buy Merchant"), MerchantSearch.BuyingType);
        searchScope.ItemSelected += index => PickSearchScope((int)index);
        top.AddChild(searchScope);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 4);
        body.AddChild(header);
        header.AddChild(SearchColumnTitle(SearchText(SearchChatText, "Chat"), SearchActionColumn));
        header.AddChild(SearchColumnTitle(SearchText(SearchLocationText, "Sales Location"), SearchActionColumn));
        header.AddChild(SearchColumnTitle(SearchText(SearchHistoryText, "Price"), SearchActionColumn));
        var byName = UiTheme.TopTabButton(SearchText(SearchByNameText, "View by Name"), 12);
        byName.ToggleMode = false;
        byName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        byName.Pressed += () => SortSearch(MerchantSearchSort.Name);
        header.AddChild(byName);
        var priceHeader = new VBoxContainer { CustomMinimumSize = new Vector2(SearchPriceColumn, 0) };
        priceHeader.AddChild(UiTheme.Text(SearchText(SearchItemPriceText, "Item Price"), 12, UiTheme.Gold, HorizontalAlignment.Center));
        var sorts = new HBoxContainer();
        sorts.AddThemeConstantOverride("separation", 4);
        var low = UiTheme.SmallButton(SearchText(SearchLowText, "Low to High"), "");
        low.Pressed += () => SortSearch(MerchantSearchSort.PriceLowToHigh);
        sorts.AddChild(low);
        var high = UiTheme.SmallButton(SearchText(SearchHighText, "High to Low"), "");
        high.Pressed += () => SortSearch(MerchantSearchSort.PriceHighToLow);
        sorts.AddChild(high);
        priceHeader.AddChild(sorts);
        header.AddChild(priceHeader);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        body.AddChild(rows);
        for (int i = 0; i < MerchantSearch.RowsPerPage; i++)
            rows.AddChild(BuildSearchLine().Root);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 4);
        body.AddChild(footer);
        searchStatus = HudStyle.Label(12);
        searchStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(searchStatus);
        footer.AddChild(LookEditor.StepButton("◀", () => { search.Previous(); RefreshSearch(); }));
        searchPages = new HBoxContainer();
        searchPages.AddThemeConstantOverride("separation", 2);
        footer.AddChild(searchPages);
        footer.AddChild(LookEditor.StepButton("▶", () => { search.Next(); RefreshSearch(); }));
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refresh = UiTheme.IconButton("⟳", "Refresh");
        refresh.Pressed += () => RequestSearch(MerchantSearch.OfficialListItem);
        footer.AddChild(refresh);
    }

    private static Label SearchLabel(string text)
    {
        var label = HudStyle.Label(13);
        label.Text = text;
        return label;
    }

    private static Label SearchColumnTitle(string text, float width)
    {
        var label = UiTheme.Text(text, 12, UiTheme.Gold, HorizontalAlignment.Center);
        label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }

    private SearchLine BuildSearchLine()
    {
        var line = new SearchLine { Root = new HBoxContainer { CustomMinimumSize = new Vector2(0, SearchRowHeight) } };
        line.Root.AddThemeConstantOverride("separation", 4);
        line.Whisper = SearchRowButton(SearchText(SearchWhisperText, "Whisper"));
        line.Whisper.Pressed += () => { if (line.Row is { } row) whispers.OpenWhisperWith(row.Seller); };
        line.Root.AddChild(line.Whisper);
        line.Move = SearchRowButton(SearchText(SearchMoveText, "Move"));
        line.Move.Pressed += () => { if (line.Row is { } row) MoveTo(row); };
        line.Root.AddChild(line.Move);
        line.View = SearchRowButton(SearchText(SearchViewText, "View"));
        line.View.Pressed += () => { if (line.Row is { } row) marketPriceWindow.RequestMarketPriceWindow(row.ItemId); };
        line.Root.AddChild(line.View);
        line.Icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(22, 22),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        line.Root.AddChild(line.Icon);
        line.Plus = UpgradeBadge.Attach(line.Icon);
        line.Name = HudStyle.Label(13);
        line.Name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.Name.ClipText = true;
        line.Root.AddChild(line.Name);
        line.Price = HudStyle.Label(13, HorizontalAlignment.Right);
        line.Price.CustomMinimumSize = new Vector2(SearchPriceColumn, 0);
        line.Root.AddChild(line.Price);
        searchLines.Add(line);
        return line;
    }

    private static Button SearchRowButton(string text)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(SearchActionColumn, 0) };
        button.AddThemeFontSizeOverride("font_size", 12);
        return button;
    }

    internal bool RequestSearch(int itemId)
    {
        double left = searchReadyAt - context.Now;
        if (left > 0)
        {
            context.Log.Notice(string.Format(SearchText(SearchCooldownText, "Available after %d seconds.").Replace("%d", "{0}"),
                Mathf.CeilToInt((float)left)));
            return true;
        }
        float recast = SkillData.Get(MerchantSearch.OfficialListSkill)?.RecastSeconds ?? 0f;
        searchReadyAt = context.Now + recast;
        searchRequests.Open(itemId);
        return true;
    }

    internal void OnSearchOpen()
    {
        search.Clear();
        searchLoading = true;
        SearchPanel.Visible = true;
        searchShown = true;
        RefreshSearch();
    }

    internal void OnSearchRows(IReadOnlyList<MerchantSearchRow> rows)
    {
        search.Add(rows);
        if (searchShown) RefreshSearch();
    }

    internal void OnSearchLoaded()
    {
        searchLoading = false;
        if (searchShown) RefreshSearch();
    }

    private void OnSearchRefused(int textId)
    {
        searchLoading = false;
        searchMoveTo = "";
        Notice.Show(context.Root, SearchText(textId, "You cannot use the item."), ItemData.DisplayName(MerchantSearch.OfficialListItem).Trim());
    }

    private void CloseSearch()
    {
        if (!searchShown) return;
        searchShown = false;
        SearchPanel.Visible = false;
    }

    private bool SearchBusy()
    {
        if (!searchLoading) return false;
        Notice.Show(context.Root, SearchText(SearchBusyText, "Try again later"), ItemData.DisplayName(MerchantSearch.OfficialListItem).Trim());
        return true;
    }

    private void RunSearch()
    {
        if (SearchBusy()) return;
        search.Search(searchEdit.Text);
        RefreshSearch();
    }

    private void ShowAllSearch()
    {
        if (SearchBusy()) return;
        searchEdit.Text = "";
        search.ShowAll();
        RefreshSearch();
    }

    private void PickSearchScope(int index)
    {
        search.SetType(searchScope.GetItemId(index));
        RefreshSearch();
    }

    private void SortSearch(MerchantSearchSort sort)
    {
        if (SearchBusy()) return;
        search.SortBy(sort);
        RefreshSearch();
    }

    private void RefreshSearch()
    {
        var visible = search.Visible();
        for (int i = 0; i < searchLines.Count; i++)
        {
            var line = searchLines[i];
            bool shown = i < visible.Count;
            line.Row = shown ? visible[i] : null;
            line.Whisper.Visible = line.Move.Visible = line.View.Visible = shown;
            line.Icon.Texture = shown ? ItemIcons.Get(visible[i].ItemId) : null;
            if (shown) line.Plus.Set(visible[i].ItemId);
            else line.Plus.Clear();
            line.Name.Text = shown ? visible[i].ItemName : "";
            if (shown) line.Name.AddThemeColorOverride("font_color", ItemGrade.Tint(visible[i].ItemId));
            line.Price.Text = shown ? visible[i].Price.ToString("n0") : "";
        }

        foreach (var child in searchPages.GetChildren())
            child.QueueFree();
        foreach (int page in search.GroupPages())
        {
            int target = page;
            var button = UiTheme.TopTabButton((page + 1).ToString(), 12);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            button.CustomMinimumSize = new Vector2(26, 0);
            button.SetPressedNoSignal(page == search.Page);
            button.Pressed += () => { search.GoTo(target); RefreshSearch(); };
            searchPages.AddChild(button);
        }

        searchStatus.Text = searchLoading ? "Loading…" : "";
    }

    private void MoveTo(MerchantSearchRow row)
    {
        searchMoveTo = row.Seller;
        searchRequests.Move(row.SellerId);
    }

    private void OnSearchMoved()
    {
        CloseSearch();
        string seller = searchMoveTo;
        context.Root.After(SearchStallDelay, () => BrowseByName(seller));
    }

    private void BrowseByName(string name)
    {
        if (name.Length == 0) return;
        foreach (var (id, entity) in context.Entities.Views)
        {
            if (entity.IsNpc || id == context.Self.CharId || !stalls.Stalls.TryGetValue(id, out var stall)) continue;
            if (!string.Equals(entity.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (stall.IsBuying) requests.BuyMerchantList(id);
            else requests.List(id);
            return;
        }
    }
}
