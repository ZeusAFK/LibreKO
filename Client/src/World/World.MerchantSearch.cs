using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int MerchantSearchTipText = 40045;
    private const int MerchantSearchSearchText = 40042;
    private const int MerchantSearchFindText = 40044;
    private const int MerchantSearchAllText = 40041;
    private const int MerchantSearchScopeText = 40043;
    private const int MerchantSearchSellText = 30521;
    private const int MerchantSearchBuyText = 30522;
    private const int MerchantSearchChatText = 40034;
    private const int MerchantSearchLocationText = 40031;
    private const int MerchantSearchHistoryText = 40030;
    private const int MerchantSearchByNameText = 40033;
    private const int MerchantSearchItemPriceText = 40032;
    private const int MerchantSearchLowText = 40040;
    private const int MerchantSearchHighText = 40039;
    private const int MerchantSearchWhisperText = 40035;
    private const int MerchantSearchMoveText = 40036;
    private const int MerchantSearchViewText = 40037;
    private const int MerchantSearchBusyText = 16810;
    private const int MerchantSearchCooldownText = 30937;
    private const float MerchantSearchActionColumn = 76f;
    private const float MerchantSearchPriceColumn = 150f;
    private const float MerchantSearchRowHeight = 26f;
    private const double MerchantSearchStallDelay = 1.0;

    private sealed class MerchantSearchLine
    {
        public HBoxContainer Root = null!;
        public Button Whisper = null!, Move = null!, View = null!;
        public TextureRect Icon = null!;
        public Label Name = null!, Price = null!;
        public MerchantSearchRow? Row;
    }

    private readonly MerchantSearch _merchantSearch = new();
    private readonly List<MerchantSearchLine> _merchantSearchLines = new();
    private CanvasLayer _merchantSearchLayer = null!;
    private HudWindow _merchantSearchPanel = null!;
    private LineEdit _merchantSearchEdit = null!;
    private OptionButton _merchantSearchScope = null!;
    private HBoxContainer _merchantSearchPages = null!;
    private Label _merchantSearchStatus = null!;
    private bool _merchantSearchShown, _merchantSearchLoading;
    private double _merchantSearchReadyAt;
    private string _merchantSearchMoveTo = "";

    private void MerchantSearchInit()
    {
        BuildMerchantSearchPanel();
        Net.I.MerchantSearchOpenEvent += OnMerchantSearchOpen;
        Net.I.MerchantSearchRowsEvent += OnMerchantSearchRows;
        Net.I.MerchantSearchLoadedEvent += OnMerchantSearchLoaded;
        Net.I.MerchantSearchRefusedEvent += OnMerchantSearchRefused;
        Net.I.MerchantSearchMovedEvent += OnMerchantSearchMoved;
    }

    private void MerchantSearchDispose()
    {
        Net.I.MerchantSearchOpenEvent -= OnMerchantSearchOpen;
        Net.I.MerchantSearchRowsEvent -= OnMerchantSearchRows;
        Net.I.MerchantSearchLoadedEvent -= OnMerchantSearchLoaded;
        Net.I.MerchantSearchRefusedEvent -= OnMerchantSearchRefused;
        Net.I.MerchantSearchMovedEvent -= OnMerchantSearchMoved;
    }

    private static string MerchantSearchText(int id, string fallback) => ItemData.Text(id, fallback);

    private void BuildMerchantSearchPanel()
    {
        _merchantSearchLayer = new CanvasLayer { Layer = 74 };
        AddChild(_merchantSearchLayer);

        _merchantSearchPanel = new HudWindow("merchantsearch", ItemData.DisplayName(MerchantSearch.OfficialListItem).Trim(),
            bodyMinWidth: 720) { Visible = false };
        _merchantSearchPanel.Closed += CloseMerchantSearch;
        _merchantSearchLayer.AddChild(_merchantSearchPanel);

        var body = _merchantSearchPanel.Body;
        body.AddThemeConstantOverride("separation", 8);

        var tip = UiTheme.Text(MerchantSearchText(MerchantSearchTipText, "An item can be sold or purchased while searching."),
            12, UiTheme.TextDim, HorizontalAlignment.Center);
        body.AddChild(tip);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        body.AddChild(top);
        top.AddChild(MerchantSearchLabel(MerchantSearchText(MerchantSearchSearchText, "Search")));
        _merchantSearchEdit = new LineEdit { CustomMinimumSize = new Vector2(180, 0) };
        _merchantSearchEdit.TextSubmitted += _ => RunMerchantSearch();
        top.AddChild(_merchantSearchEdit);
        top.AddChild(LookActionButton(MerchantSearchText(MerchantSearchFindText, "Search"), RunMerchantSearch));
        top.AddChild(LookActionButton(MerchantSearchText(MerchantSearchAllText, "View All"), ShowAllMerchantSearch));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        top.AddChild(MerchantSearchLabel(MerchantSearchText(MerchantSearchScopeText, "Scope")));
        _merchantSearchScope = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(130, 0) };
        _merchantSearchScope.AddItem(MerchantSearchText(MerchantSearchSellText, "Sell Merchant"), MerchantSearch.SellingType);
        _merchantSearchScope.AddItem(MerchantSearchText(MerchantSearchBuyText, "Buy Merchant"), MerchantSearch.BuyingType);
        _merchantSearchScope.ItemSelected += index => PickMerchantSearchScope((int)index);
        top.AddChild(_merchantSearchScope);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 4);
        body.AddChild(header);
        header.AddChild(MerchantSearchColumnTitle(MerchantSearchText(MerchantSearchChatText, "Chat"), MerchantSearchActionColumn));
        header.AddChild(MerchantSearchColumnTitle(MerchantSearchText(MerchantSearchLocationText, "Sales Location"), MerchantSearchActionColumn));
        header.AddChild(MerchantSearchColumnTitle(MerchantSearchText(MerchantSearchHistoryText, "Price"), MerchantSearchActionColumn));
        var byName = UiTheme.TopTabButton(MerchantSearchText(MerchantSearchByNameText, "View by Name"), 12);
        byName.ToggleMode = false;
        byName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        byName.Pressed += () => SortMerchantSearch(MerchantSearchSort.Name);
        header.AddChild(byName);
        var priceHeader = new VBoxContainer { CustomMinimumSize = new Vector2(MerchantSearchPriceColumn, 0) };
        priceHeader.AddChild(UiTheme.Text(MerchantSearchText(MerchantSearchItemPriceText, "Item Price"), 12, UiTheme.Gold, HorizontalAlignment.Center));
        var sorts = new HBoxContainer();
        sorts.AddThemeConstantOverride("separation", 4);
        var low = UiTheme.SmallButton(MerchantSearchText(MerchantSearchLowText, "Low to High"), "");
        low.Pressed += () => SortMerchantSearch(MerchantSearchSort.PriceLowToHigh);
        sorts.AddChild(low);
        var high = UiTheme.SmallButton(MerchantSearchText(MerchantSearchHighText, "High to Low"), "");
        high.Pressed += () => SortMerchantSearch(MerchantSearchSort.PriceHighToLow);
        sorts.AddChild(high);
        priceHeader.AddChild(sorts);
        header.AddChild(priceHeader);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        body.AddChild(rows);
        for (int i = 0; i < MerchantSearch.RowsPerPage; i++)
            rows.AddChild(BuildMerchantSearchLine().Root);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 4);
        body.AddChild(footer);
        _merchantSearchStatus = HudStyle.Label(12);
        _merchantSearchStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_merchantSearchStatus);
        footer.AddChild(LookEditor.StepButton("◀", () => { _merchantSearch.Previous(); RefreshMerchantSearch(); }));
        _merchantSearchPages = new HBoxContainer();
        _merchantSearchPages.AddThemeConstantOverride("separation", 2);
        footer.AddChild(_merchantSearchPages);
        footer.AddChild(LookEditor.StepButton("▶", () => { _merchantSearch.Next(); RefreshMerchantSearch(); }));
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refresh = UiTheme.IconButton("⟳", "Refresh");
        refresh.Pressed += () => RequestMerchantSearch(MerchantSearch.OfficialListItem);
        footer.AddChild(refresh);
    }

    private static Label MerchantSearchLabel(string text)
    {
        var label = HudStyle.Label(13);
        label.Text = text;
        return label;
    }

    private static Label MerchantSearchColumnTitle(string text, float width)
    {
        var label = UiTheme.Text(text, 12, UiTheme.Gold, HorizontalAlignment.Center);
        label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }

    private MerchantSearchLine BuildMerchantSearchLine()
    {
        var line = new MerchantSearchLine { Root = new HBoxContainer { CustomMinimumSize = new Vector2(0, MerchantSearchRowHeight) } };
        line.Root.AddThemeConstantOverride("separation", 4);
        line.Whisper = MerchantSearchRowButton(MerchantSearchText(MerchantSearchWhisperText, "Whisper"));
        line.Whisper.Pressed += () => { if (line.Row is { } row) OpenWhisperWith(row.Seller); };
        line.Root.AddChild(line.Whisper);
        line.Move = MerchantSearchRowButton(MerchantSearchText(MerchantSearchMoveText, "Move"));
        line.Move.Pressed += () => { if (line.Row is { } row) MoveToMerchant(row); };
        line.Root.AddChild(line.Move);
        line.View = MerchantSearchRowButton(MerchantSearchText(MerchantSearchViewText, "View"));
        line.View.Disabled = true;
        line.View.TooltipText = "Price history is not available yet.";
        line.Root.AddChild(line.View);
        line.Icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(22, 22),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        line.Root.AddChild(line.Icon);
        line.Name = HudStyle.Label(13);
        line.Name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.Name.ClipText = true;
        line.Root.AddChild(line.Name);
        line.Price = HudStyle.Label(13, HorizontalAlignment.Right);
        line.Price.CustomMinimumSize = new Vector2(MerchantSearchPriceColumn, 0);
        line.Root.AddChild(line.Price);
        _merchantSearchLines.Add(line);
        return line;
    }

    private static Button MerchantSearchRowButton(string text)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(MerchantSearchActionColumn, 0) };
        button.AddThemeFontSizeOverride("font_size", 12);
        return button;
    }

    private bool RequestMerchantSearch(int itemId)
    {
        double left = _merchantSearchReadyAt - Now();
        if (left > 0)
        {
            CombatNotice(string.Format(MerchantSearchText(MerchantSearchCooldownText, "Available after %d seconds.").Replace("%d", "{0}"),
                Mathf.CeilToInt((float)left)));
            return true;
        }
        float recast = SkillData.Get(MerchantSearch.OfficialListSkill)?.RecastSeconds ?? 0f;
        _merchantSearchReadyAt = Now() + recast;
        Net.I.SendMerchantSearchOpen(itemId);
        return true;
    }

    private void OnMerchantSearchOpen()
    {
        _merchantSearch.Clear();
        _merchantSearchLoading = true;
        _merchantSearchPanel.Visible = true;
        _merchantSearchShown = true;
        RefreshMerchantSearch();
    }

    private void OnMerchantSearchRows(IReadOnlyList<MerchantSearchRow> rows)
    {
        _merchantSearch.Add(rows);
        if (_merchantSearchShown) RefreshMerchantSearch();
    }

    private void OnMerchantSearchLoaded()
    {
        _merchantSearchLoading = false;
        if (_merchantSearchShown) RefreshMerchantSearch();
    }

    private void OnMerchantSearchRefused(int textId)
    {
        _merchantSearchLoading = false;
        _merchantSearchMoveTo = "";
        Notice.Show(this, MerchantSearchText(textId, "You cannot use the item."), ItemData.DisplayName(MerchantSearch.OfficialListItem).Trim());
    }

    private void CloseMerchantSearch()
    {
        if (!_merchantSearchShown) return;
        _merchantSearchShown = false;
        _merchantSearchPanel.Visible = false;
    }

    private bool MerchantSearchBusy()
    {
        if (!_merchantSearchLoading) return false;
        Notice.Show(this, MerchantSearchText(MerchantSearchBusyText, "Try again later"), ItemData.DisplayName(MerchantSearch.OfficialListItem).Trim());
        return true;
    }

    private void RunMerchantSearch()
    {
        if (MerchantSearchBusy()) return;
        _merchantSearch.Search(_merchantSearchEdit.Text);
        RefreshMerchantSearch();
    }

    private void ShowAllMerchantSearch()
    {
        if (MerchantSearchBusy()) return;
        _merchantSearchEdit.Text = "";
        _merchantSearch.ShowAll();
        RefreshMerchantSearch();
    }

    private void PickMerchantSearchScope(int index)
    {
        _merchantSearch.SetType(_merchantSearchScope.GetItemId(index));
        RefreshMerchantSearch();
    }

    private void SortMerchantSearch(MerchantSearchSort sort)
    {
        if (MerchantSearchBusy()) return;
        _merchantSearch.SortBy(sort);
        RefreshMerchantSearch();
    }

    private void RefreshMerchantSearch()
    {
        var visible = _merchantSearch.Visible();
        for (int i = 0; i < _merchantSearchLines.Count; i++)
        {
            var line = _merchantSearchLines[i];
            bool shown = i < visible.Count;
            line.Row = shown ? visible[i] : null;
            line.Whisper.Visible = line.Move.Visible = line.View.Visible = shown;
            line.Icon.Texture = shown ? ItemData.Icon(visible[i].ItemId) : null;
            line.Name.Text = shown ? visible[i].ItemName : "";
            if (shown) line.Name.AddThemeColorOverride("font_color", ItemGrade.Tint(visible[i].ItemId));
            line.Price.Text = shown ? visible[i].Price.ToString("n0") : "";
        }

        foreach (var child in _merchantSearchPages.GetChildren())
            child.QueueFree();
        foreach (int page in _merchantSearch.GroupPages())
        {
            int target = page;
            var button = UiTheme.TopTabButton((page + 1).ToString(), 12);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            button.CustomMinimumSize = new Vector2(26, 0);
            button.SetPressedNoSignal(page == _merchantSearch.Page);
            button.Pressed += () => { _merchantSearch.GoTo(target); RefreshMerchantSearch(); };
            _merchantSearchPages.AddChild(button);
        }

        _merchantSearchStatus.Text = _merchantSearchLoading ? "Loading…" : "";
    }

    private void MoveToMerchant(MerchantSearchRow row)
    {
        _merchantSearchMoveTo = row.Seller;
        Net.I.SendMerchantSearchMove(row.SellerId);
    }

    private void OnMerchantSearchMoved()
    {
        CloseMerchantSearch();
        string seller = _merchantSearchMoveTo;
        GetTree().CreateTimer(MerchantSearchStallDelay).Timeout += () => BrowseMerchantByName(seller);
    }

    private void BrowseMerchantByName(string name)
    {
        if (name.Length == 0) return;
        foreach (var (id, entity) in _ents)
        {
            if (entity.IsNpc || id == _myId || !_stalls.TryGetValue(id, out var stall)) continue;
            if (!string.Equals(entity.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (stall.IsBuying) Net.I.SendBuyMerchantList(id);
            else Net.I.SendMerchantList(id);
            return;
        }
    }
}
