using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int MarketPriceSelectText = 43400;
    private const int MarketPriceMinText = 43401;
    private const int MarketPriceMaxText = 43402;
    private const int MarketPriceAverageText = 43403;
    private const int MarketPriceNoItemText = 43404;
    private const int MarketPriceNoHistoryText = 43408;
    private const int MarketPriceErrorText = 43409;
    private const int MarketPriceUpdatedText = 43412;
    private const int MarketPriceSearchTipText = 43413;
    private const int MarketPriceLatestLeftText = 45230;
    private const int MarketPriceTradesText = 45231;
    private const int MarketPriceSearchText = 45232;
    private const int MarketPriceDayText = 45233;
    private const int MarketPriceNotPremiumText = 42205;
    private const int MarketPriceTooSoonText = 40046;
    private const int MarketPriceNoActivityText = 44670;
    private const int MarketPriceLowText = 44673;
    private const int MarketPriceExpensiveText = 44674;
    private const int MarketPriceLayer = 78;
    private const float MarketPriceSlotSize = 46f;

    private readonly Dictionary<int, MarketPriceReply> _marketPriceCache = new();
    private CanvasLayer _marketPriceLayer = null!;
    private HudWindow _marketPricePanel = null!;
    private ItemSearchPanel _marketPriceSearch = null!;
    private ItemSlotView _marketPriceSlot = null!;
    private Label _marketPriceName = null!, _marketPriceTrades = null!, _marketPriceUpdated = null!, _marketPriceStatus = null!;
    private PriceChart _marketPriceChart = null!;
    private bool _marketPriceShown, _marketPriceOpenOnReply;
    private int _marketPriceItem, _marketPriceAsked, _amountMarketItem;
    private HBoxContainer _amountMarketRow = null!;
    private Label _amountMarketHint = null!;

    private void MarketPriceInit()
    {
        BuildMarketPricePanel();
        Net.I.MarketPriceEvent += OnMarketPrice;
    }

    private void MarketPriceDispose()
    {
        Net.I.MarketPriceEvent -= OnMarketPrice;
    }

    private static string MarketPriceText(int id, string fallback) => ItemData.Text(id, fallback);

    private static string MarketPriceNoHistoryMessage =>
        MarketPriceText(MarketPriceNoHistoryText, "There is no trade history for this item");

    private static string GoldAmount(long value) => $"{value:n0} gold";

    private void BuildMarketPricePanel()
    {
        _marketPriceLayer = new CanvasLayer { Layer = MarketPriceLayer };
        AddChild(_marketPriceLayer);

        _marketPricePanel = new HudWindow("marketprice", "Market Price", bodyMinWidth: 820) { Visible = false };
        _marketPricePanel.Closed += CloseMarketPrice;
        _marketPriceLayer.AddChild(_marketPricePanel);

        var body = _marketPricePanel.Body;
        body.AddThemeConstantOverride("separation", 8);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        body.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(310, 0) };
        left.AddThemeConstantOverride("separation", 8);
        columns.AddChild(left);

        _marketPriceSearch = new ItemSearchPanel(
            tradeableOnly: true,
            actionText: "Select",
            onAction: (hit, _) => SelectMarketPriceItem(hit.Id),
            showTooltip: itemId => ShowItemTooltip(-1, TooltipItem(itemId)),
            hideTooltip: HideItemTooltip,
            resultsSize: new Vector2(310, 200),
            showQuantity: false);
        left.AddChild(_marketPriceSearch);

        var pick = UiTheme.Section();
        left.AddChild(pick);
        var pickRow = new HBoxContainer();
        pickRow.AddThemeConstantOverride("separation", 10);
        pick.AddChild(pickRow);
        _marketPriceSlot = new ItemSlotView(MarketPriceSlotSize);
        _marketPriceSlot.Hovered += view => { if (!view.Item.IsEmpty) ShowItemTooltip(-1, view.Item); };
        _marketPriceSlot.Unhovered += _ => HideItemTooltip();
        pickRow.AddChild(_marketPriceSlot);
        var pickText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pickText.AddThemeConstantOverride("separation", 4);
        pickRow.AddChild(pickText);
        _marketPriceName = UiTheme.Text("", 13, UiTheme.TextHi);
        _marketPriceName.ClipText = true;
        pickText.AddChild(_marketPriceName);
        var search = UiTheme.ActionButton(MarketPriceText(MarketPriceSearchText, "Search Price"),
            MarketPriceText(MarketPriceSearchTipText, "Search the price of selected item"));
        search.Pressed += SearchMarketPrice;
        pickText.AddChild(search);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        columns.AddChild(right);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        right.AddChild(head);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        head.AddChild(UiTheme.Text(MarketPriceText(MarketPriceTradesText, "Recent of Trades :"), 13, UiTheme.TextLo));
        _marketPriceTrades = UiTheme.Text("0", 13, UiTheme.TextHi);
        head.AddChild(_marketPriceTrades);

        string dayWord = MarketPriceText(MarketPriceDayText, "Data");
        _marketPriceChart = new PriceChart
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            DayLabel = index => $"{dayWord}{index + 1}",
            ValueTip = (label, value) => $"{label} {GoldAmount(value)}",
            AverageLabel = MarketPriceText(MarketPriceAverageText, "AVG:"),
            MaxLabel = MarketPriceText(MarketPriceMaxText, "MAX:"),
            MinLabel = MarketPriceText(MarketPriceMinText, "MIN:"),
        };
        right.AddChild(_marketPriceChart);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 6);
        right.AddChild(foot);
        _marketPriceUpdated = UiTheme.Text("", 12, UiTheme.Gold);
        foot.AddChild(_marketPriceUpdated);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        foot.AddChild(UiTheme.Text(MarketPriceText(MarketPriceLatestLeftText, "Latest data will show on the left."), 12, UiTheme.TextDim));

        _marketPriceStatus = UiTheme.Text("", 13, UiTheme.Warning, HorizontalAlignment.Center);
        body.AddChild(_marketPriceStatus);
    }

    private bool HasMarketPriceAccess()
    {
        if (Net.I.HasPremium) return true;
        Notice.Show(this, MarketPriceText(MarketPriceNotPremiumText, "You are not using premiums"), "Market Price");
        return false;
    }

    private void OpenMarketPrice(int itemId = 0)
    {
        if (!HasMarketPriceAccess()) return;
        if (itemId == 0)
        {
            _marketPriceCache.Clear();
            ClearMarketPriceItem();
            SetMarketPriceStatus(MarketPriceText(MarketPriceSelectText, "Select the item and click Search button"));
        }
        else
        {
            SelectMarketPriceItem(itemId);
        }
        ShowMarketPricePanel();
    }

    private void RequestMarketPriceWindow(int itemId)
    {
        if (!HasMarketPriceAccess()) return;
        SelectMarketPriceItem(itemId);
        _marketPriceOpenOnReply = true;
        AskMarketPrice(itemId);
    }

    private void ShowMarketPricePanel()
    {
        _marketPriceShown = true;
        _marketPricePanel.Visible = true;
        _marketPriceSearch.FocusQuery();
    }

    private void CloseMarketPrice()
    {
        if (!_marketPriceShown) return;
        _marketPriceShown = false;
        _marketPriceOpenOnReply = false;
        _marketPricePanel.Visible = false;
        HideItemTooltip();
    }

    private void ClearMarketPriceItem()
    {
        _marketPriceItem = 0;
        _marketPriceSlot.Clear();
        _marketPriceName.Text = "";
        ClearMarketPriceChart();
    }

    private void ClearMarketPriceChart()
    {
        _marketPriceChart.Clear();
        _marketPriceTrades.Text = "0";
        _marketPriceUpdated.Text = "";
    }

    private void SelectMarketPriceItem(int itemId)
    {
        _marketPriceItem = itemId;
        _marketPriceSlot.Set(TooltipItem(itemId));
        _marketPriceName.Text = ItemData.DisplayName(itemId);
        if (_marketPriceCache.TryGetValue(itemId, out var cached))
        {
            DrawMarketPrice(cached);
            return;
        }
        ClearMarketPriceChart();
        SetMarketPriceStatus(MarketPriceText(MarketPriceSelectText, "Select the item and click Search button"));
    }

    private void SearchMarketPrice()
    {
        if (_marketPriceItem == 0)
        {
            SetMarketPriceStatus(MarketPriceText(MarketPriceNoItemText, "You have not selected item to search"));
            return;
        }
        if (_marketPriceCache.TryGetValue(_marketPriceItem, out var cached))
        {
            DrawMarketPrice(cached);
            return;
        }
        AskMarketPrice(_marketPriceItem);
    }

    private void AskMarketPrice(int itemId)
    {
        _marketPriceAsked = itemId;
        Net.I.SendMarketPrice(itemId);
    }

    private void SetMarketPriceStatus(string text)
    {
        _marketPriceStatus.Text = text;
        _marketPriceStatus.Visible = text.Length > 0;
    }

    private void DrawMarketPrice(MarketPriceReply reply)
    {
        if (!reply.HasHistory)
        {
            ClearMarketPriceChart();
            SetMarketPriceStatus(MarketPriceNoHistoryMessage);
            return;
        }
        _marketPriceChart.ShowDays(reply.Days);
        _marketPriceTrades.Text = $"{reply.Trades:n0}";
        var local = reply.LastUpdate.ToLocalTime();
        _marketPriceUpdated.Text = MarketPriceText(MarketPriceUpdatedText, "Last Update %m-%d-%Y")
            .Replace("%m", local.ToString("MM")).Replace("%d", local.ToString("dd")).Replace("%Y", local.ToString("yyyy"));
        SetMarketPriceStatus("");
    }

    private void OnMarketPrice(MarketPriceReply reply)
    {
        switch (reply.Result)
        {
            case MarketPrice.History:
                _marketPriceCache[reply.ItemId] = reply;
                if (reply.ItemId == _marketPriceItem)
                {
                    if (_marketPriceOpenOnReply)
                    {
                        _marketPriceOpenOnReply = false;
                        ShowMarketPricePanel();
                    }
                    DrawMarketPrice(reply);
                }
                RefreshStallPriceHint();
                break;
            case MarketPrice.NoHistory:
                _marketPriceOpenOnReply = false;
                var none = new MarketPriceReply(MarketPrice.NoHistory, _marketPriceAsked,
                    new MarketPriceDay[MarketPrice.DaysShown], 0, default);
                _marketPriceCache[_marketPriceAsked] = none;
                if (_marketPriceAsked == _marketPriceItem && _marketPriceShown) DrawMarketPrice(none);
                if (!_amountLayer.Visible) CombatNotice(MarketPriceNoHistoryMessage);
                RefreshStallPriceHint();
                break;
            case MarketPrice.NotPremium:
                _marketPriceOpenOnReply = false;
                Notice.Show(this, MarketPriceText(MarketPriceNotPremiumText, "You are not using premiums"), "Market Price");
                break;
            case MarketPrice.TooSoon:
                SetMarketPriceStatus(MarketPriceText(MarketPriceTooSoonText, "Please try again in 1 second"));
                break;
            default:
                SetMarketPriceStatus($"{MarketPriceText(MarketPriceErrorText, "Market Price Error Code:")} {reply.Result}");
                break;
        }
    }

    private void ShowStallPriceHint(int itemId)
    {
        _amountMarketItem = itemId > 0 && Net.I.HasPremium ? itemId : 0;
        _amountMarketRow.Visible = _amountMarketItem > 0;
        if (_amountMarketItem == 0) return;
        _amountMarketHint.Text = "";
        if (_marketPriceCache.ContainsKey(itemId)) RefreshStallPriceHint();
        else AskMarketPrice(itemId);
    }

    private void RefreshStallPriceHint()
    {
        if (_amountMarketItem == 0 || !_amountLayer.Visible) return;
        if (!_marketPriceCache.TryGetValue(_amountMarketItem, out var reply)) return;

        long average = MarketPrice.MarketAverage(reply.Days);
        var verdict = MarketPrice.Judge(_amountPrice.Value, average);
        switch (verdict)
        {
            case MarketPriceVerdict.Low:
                _amountMarketHint.Text = $"{MarketPriceText(MarketPriceLowText, "Low Price")} · {GoldAmount(average)}";
                _amountMarketHint.AddThemeColorOverride("font_color", UiTheme.Good);
                break;
            case MarketPriceVerdict.Expensive:
                _amountMarketHint.Text = $"{MarketPriceText(MarketPriceExpensiveText, "Expensive")} · {GoldAmount(average)}";
                _amountMarketHint.AddThemeColorOverride("font_color", UiTheme.Bad);
                break;
            default:
                _amountMarketHint.Text = MarketPriceText(MarketPriceNoActivityText, "There's no recent Trading Activity.");
                _amountMarketHint.AddThemeColorOverride("font_color", UiTheme.TextDim);
                break;
        }
    }
}
