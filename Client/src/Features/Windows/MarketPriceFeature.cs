using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MarketPriceFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly MarketPriceSession marketPrice;
    private readonly IMarketPriceRequests marketPriceRequests;
    private readonly PremiumSession premium;

    public MarketPriceFeature(IWorldContext context, MarketPriceSession marketPrice, IMarketPriceRequests marketPriceRequests, PremiumSession premium)
    {
        this.context = context;
        this.marketPrice = marketPrice;
        this.marketPriceRequests = marketPriceRequests;
        this.premium = premium;
    }

    public void Build()
    {
        BuildMarketPricePanel();
        marketPrice.MarketPriceEvent += OnMarketPrice;
    }

    public void Dispose()
    {
        marketPrice.MarketPriceEvent -= OnMarketPrice;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!marketPriceShown) return false;
        CloseMarketPrice();
        return true;
    }

    internal const int MarketPriceSelectText = 43400;
    private const int MarketPriceMinText = 43401;
    private const int MarketPriceMaxText = 43402;
    private const int MarketPriceAverageText = 43403;
    private const int MarketPriceNoItemText = 43404;
    private const int MarketPriceNoHistoryText = 43408;
    private const int MarketPriceErrorText = 43409;
    private const int MarketPriceUpdatedText = 43412;
    internal const int MarketPriceSearchTipText = 43413;
    private const int MarketPriceLatestLeftText = 45230;
    private const int MarketPriceTradesText = 45231;
    private const int MarketPriceSearchText = 45232;
    private const int MarketPriceDayText = 45233;
    private const int MarketPriceNotPremiumText = 42205;
    private const int MarketPriceTooSoonText = 40046;

    private const float MarketPriceSlotSize = 46f;

    private readonly Dictionary<int, MarketPriceReply> marketPriceCache = new();

    private CanvasLayer marketPriceLayer = null!;
    internal HudWindow MarketPricePanel { get; private set; } = null!;
    internal ItemSearchPanel MarketPriceSearch { get; private set; } = null!;
    private ItemSlotView marketPriceSlot = null!;
    private Label marketPriceName = null!;
    private Label marketPriceTrades = null!;
    private Label marketPriceUpdated = null!;
    private Label marketPriceStatus = null!;
    private PriceChart marketPriceChart = null!;
    private bool marketPriceShown;
    private bool marketPriceOpenOnReply;
    private int marketPriceItem;
    private int marketPriceAsked;

    internal static string MarketPriceText(int id, string fallback) => ItemData.Text(id, fallback);

    private static string MarketPriceNoHistoryMessage =>
        MarketPriceText(MarketPriceNoHistoryText, "There is no trade history for this item");

    internal void BuildMarketPricePanel()
    {
        marketPriceLayer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(marketPriceLayer);

        MarketPricePanel = new HudWindow("marketprice", "Market Price", bodyMinWidth: 820) { Visible = false };
        MarketPricePanel.Closed += CloseMarketPrice;
        marketPriceLayer.AddChild(MarketPricePanel);

        var body = MarketPricePanel.Body;
        body.AddThemeConstantOverride("separation", 8);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        body.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(310, 0) };
        left.AddThemeConstantOverride("separation", 8);
        columns.AddChild(left);

        MarketPriceSearch = new ItemSearchPanel(
            tradeableOnly: true,
            actionText: "Select",
            onAction: (hit, _) => SelectMarketPriceItem(hit.Id),
            showTooltip: itemId => context.Tooltips.Show(-1, UiKit.TooltipItem(itemId)),
            hideTooltip: context.Tooltips.Hide,
            resultsSize: new Vector2(310, 200),
            showQuantity: false);
        left.AddChild(MarketPriceSearch);

        var pick = UiTheme.Section();
        left.AddChild(pick);
        var pickRow = new HBoxContainer();
        pickRow.AddThemeConstantOverride("separation", 10);
        pick.AddChild(pickRow);
        marketPriceSlot = new ItemSlotView(MarketPriceSlotSize);
        marketPriceSlot.Hovered += view => { if (!view.Item.IsEmpty) context.Tooltips.Show(-1, view.Item); };
        marketPriceSlot.Unhovered += _ => context.Tooltips.Hide();
        pickRow.AddChild(marketPriceSlot);
        var pickText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pickText.AddThemeConstantOverride("separation", 4);
        pickRow.AddChild(pickText);
        marketPriceName = UiTheme.Text("", 13, UiTheme.TextHi);
        marketPriceName.ClipText = true;
        pickText.AddChild(marketPriceName);
        var searchButton = UiTheme.ActionButton(MarketPriceText(MarketPriceSearchText, "Search Price"),
            MarketPriceText(MarketPriceSearchTipText, "Search the price of selected item"));
        searchButton.Pressed += SearchMarketPrice;
        pickText.AddChild(searchButton);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        columns.AddChild(right);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        right.AddChild(head);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        head.AddChild(UiTheme.Text(MarketPriceText(MarketPriceTradesText, "Recent of Trades :"), 13, UiTheme.TextLo));
        marketPriceTrades = UiTheme.Text("0", 13, UiTheme.TextHi);
        head.AddChild(marketPriceTrades);

        string dayWord = MarketPriceText(MarketPriceDayText, "Data");
        marketPriceChart = new PriceChart
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            DayLabel = index => $"{dayWord}{index + 1}",
            ValueTip = (label, value) => $"{label} {UiKit.GoldAmount(value)}",
            AverageLabel = MarketPriceText(MarketPriceAverageText, "AVG:"),
            MaxLabel = MarketPriceText(MarketPriceMaxText, "MAX:"),
            MinLabel = MarketPriceText(MarketPriceMinText, "MIN:"),
        };
        right.AddChild(marketPriceChart);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 6);
        right.AddChild(foot);
        marketPriceUpdated = UiTheme.Text("", 12, UiTheme.Gold);
        foot.AddChild(marketPriceUpdated);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(UiTheme.Text(MarketPriceText(MarketPriceLatestLeftText, "Latest data will show on the left."), 12, UiTheme.TextDim));

        marketPriceStatus = UiTheme.Text("", 13, UiTheme.Warning, HorizontalAlignment.Center);
        body.AddChild(marketPriceStatus);
    }

    private bool HasMarketPriceAccess()
    {
        if (premium.HasPremium) return true;
        Notice.Show(context.Root, MarketPriceText(MarketPriceNotPremiumText, "You are not using premiums"), "Market Price");
        return false;
    }

    internal void OpenMarketPrice(int itemId = 0)
    {
        if (!HasMarketPriceAccess()) return;
        if (itemId == 0)
        {
            marketPriceCache.Clear();
            ClearMarketPriceItem();
            SetMarketPriceStatus(MarketPriceText(MarketPriceSelectText, "Select the item and click Search button"));
        }
        else
        {
            SelectMarketPriceItem(itemId);
        }
        ShowMarketPricePanel();
    }

    internal void RequestMarketPriceWindow(int itemId)
    {
        if (!HasMarketPriceAccess()) return;
        SelectMarketPriceItem(itemId);
        marketPriceOpenOnReply = true;
        AskMarketPrice(itemId);
    }

    internal void ShowMarketPricePanel()
    {
        marketPriceShown = true;
        MarketPricePanel.Visible = true;
        MarketPriceSearch.FocusQuery();
    }

    internal void CloseMarketPrice()
    {
        if (!marketPriceShown) return;
        marketPriceShown = false;
        marketPriceOpenOnReply = false;
        MarketPricePanel.Visible = false;
        context.Tooltips.Hide();
    }

    internal void ClearMarketPriceItem()
    {
        marketPriceItem = 0;
        marketPriceSlot.Clear();
        marketPriceName.Text = "";
        ClearMarketPriceChart();
    }

    private void ClearMarketPriceChart()
    {
        marketPriceChart.Clear();
        marketPriceTrades.Text = "0";
        marketPriceUpdated.Text = "";
    }

    internal void SelectMarketPriceItem(int itemId)
    {
        marketPriceItem = itemId;
        marketPriceSlot.Set(UiKit.TooltipItem(itemId));
        marketPriceName.Text = ItemData.DisplayName(itemId);
        if (marketPriceCache.TryGetValue(itemId, out var cached))
        {
            DrawMarketPrice(cached);
            return;
        }
        ClearMarketPriceChart();
        SetMarketPriceStatus(MarketPriceText(MarketPriceSelectText, "Select the item and click Search button"));
    }

    private void SearchMarketPrice()
    {
        if (marketPriceItem == 0)
        {
            SetMarketPriceStatus(MarketPriceText(MarketPriceNoItemText, "You have not selected item to search"));
            return;
        }
        if (marketPriceCache.TryGetValue(marketPriceItem, out var cached))
        {
            DrawMarketPrice(cached);
            return;
        }
        AskMarketPrice(marketPriceItem);
    }

    internal event System.Action? PriceCached;

    private int quietAsk;

    internal MarketPriceReply? CachedPrice(int itemId) => marketPriceCache.GetValueOrDefault(itemId);

    internal void AskMarketPrice(int itemId, bool quiet = false)
    {
        marketPriceAsked = itemId;
        quietAsk = quiet ? itemId : 0;
        marketPriceRequests.Send(itemId);
    }

    internal void SetMarketPriceStatus(string text)
    {
        marketPriceStatus.Text = text;
        marketPriceStatus.Visible = text.Length > 0;
    }

    private void DrawMarketPrice(MarketPriceReply reply)
    {
        if (!reply.HasHistory)
        {
            ClearMarketPriceChart();
            SetMarketPriceStatus(MarketPriceNoHistoryMessage);
            return;
        }
        marketPriceChart.ShowDays(reply.Days);
        marketPriceTrades.Text = $"{reply.Trades:n0}";
        var local = reply.LastUpdate.ToLocalTime();
        marketPriceUpdated.Text = MarketPriceText(MarketPriceUpdatedText, "Last Update %m-%d-%Y")
            .Replace("%m", local.ToString("MM")).Replace("%d", local.ToString("dd")).Replace("%Y", local.ToString("yyyy"));
        SetMarketPriceStatus("");
    }

    internal void OnMarketPrice(MarketPriceReply reply)
    {
        switch (reply.Result)
        {
            case MarketPrice.History:
                marketPriceCache[reply.ItemId] = reply;
                if (reply.ItemId == marketPriceItem)
                {
                    if (marketPriceOpenOnReply)
                    {
                        marketPriceOpenOnReply = false;
                        ShowMarketPricePanel();
                    }
                    DrawMarketPrice(reply);
                }
                PriceCached?.Invoke();
                break;
            case MarketPrice.NoHistory:
                marketPriceOpenOnReply = false;
                var none = new MarketPriceReply(MarketPrice.NoHistory, marketPriceAsked,
                    new MarketPriceDay[MarketPrice.DaysShown], 0, default);
                marketPriceCache[marketPriceAsked] = none;
                if (marketPriceAsked == marketPriceItem && marketPriceShown) DrawMarketPrice(none);
                if (quietAsk != marketPriceAsked) context.Log.Notice(MarketPriceNoHistoryMessage);
                PriceCached?.Invoke();
                break;
            case MarketPrice.NotPremium:
                marketPriceOpenOnReply = false;
                Notice.Show(context.Root, MarketPriceText(MarketPriceNotPremiumText, "You are not using premiums"), "Market Price");
                break;
            case MarketPrice.TooSoon:
                SetMarketPriceStatus(MarketPriceText(MarketPriceTooSoonText, "Please try again in 1 second"));
                break;
            default:
                SetMarketPriceStatus($"{MarketPriceText(MarketPriceErrorText, "Market Price Error Code:")} {reply.Result}");
                break;
        }
    }
}
