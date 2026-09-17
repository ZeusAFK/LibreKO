using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _mbbsLayer = null!;
    private HudWindow _mbbsPanel = null!;
    private VBoxContainer _mbbsList = null!;
    private LineEdit _mbbsItem = null!, _mbbsPrice = null!, _mbbsCount = null!;
    private OptionButton _mbbsType = null!;
    private Label _mbbsStatus = null!;
    private bool _mbbsShown;

    private void MarketBbsInit()
    {
        _mbbsLayer = new CanvasLayer { Layer = 73 };
        AddChild(_mbbsLayer);
        _mbbsPanel = new HudWindow("marketbbs", "Trade Board", new Vector2(150, 90)) { Visible = false };
        _mbbsPanel.Closed += CloseMarketBbs;
        _mbbsLayer.AddChild(_mbbsPanel);
        var root = _mbbsPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        root.AddChild(UiTheme.SectionTitle("Trade ads"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 250), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _mbbsList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mbbsList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_mbbsList);

        root.AddChild(new HSeparator());
        root.AddChild(UiTheme.SectionTitle("Post an ad"));
        var form = new HBoxContainer(); form.AddThemeConstantOverride("separation", 5);
        _mbbsType = new OptionButton();
        _mbbsType.AddItem("Selling"); _mbbsType.AddItem("Buying");
        form.AddChild(_mbbsType);
        _mbbsItem = new LineEdit { PlaceholderText = "item id", CustomMinimumSize = new Vector2(80, 0) };
        form.AddChild(_mbbsItem);
        _mbbsCount = new LineEdit { PlaceholderText = "qty", CustomMinimumSize = new Vector2(50, 0) };
        form.AddChild(_mbbsCount);
        _mbbsPrice = new LineEdit { PlaceholderText = "price", CustomMinimumSize = new Vector2(90, 0) };
        form.AddChild(_mbbsPrice);
        var post = new Button { Text = "Post", FocusMode = Control.FocusModeEnum.None };
        post.Pressed += OnPostAd;
        form.AddChild(post);
        root.AddChild(form);
        _mbbsStatus = HudStyle.Label(13);
        root.AddChild(_mbbsStatus);

        Net.I.MarketAdListEvent += OnMarketAdList;
        Net.I.MarketRegisterEvent += OnMarketRegister;
        Net.I.MarketDeleteEvent += OnMarketDelete;
    }

    private void MarketBbsDispose()
    {
        Net.I.MarketAdListEvent -= OnMarketAdList;
        Net.I.MarketRegisterEvent -= OnMarketRegister;
        Net.I.MarketDeleteEvent -= OnMarketDelete;
    }

    private void ToggleMarketBbs()
    {
        if (_mbbsShown) { CloseMarketBbs(); return; }
        _mbbsPanel.Visible = true;
        _mbbsShown = true;
        _mbbsStatus.Text = "";
        Net.I.SendMarketOpen();
    }

    private void CloseMarketBbs()
    {
        if (!_mbbsShown) return;
        _mbbsShown = false;
        _mbbsPanel.Visible = false;
    }

    private void OnMarketAdList(List<MarketAd> ads)
    {
        foreach (var c in _mbbsList.GetChildren()) c.QueueFree();
        if (ads.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No ads posted.";
            _mbbsList.AddChild(e);
            return;
        }
        foreach (var ad in ads)
        {
            int adId = ad.AdId;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var tag = UiTheme.Text(ad.BuyType == 2 ? "WTB" : "WTS", 12, ad.BuyType == 2 ? UiTheme.TextLo : UiTheme.Gold);
            tag.CustomMinimumSize = new Vector2(36, 0);
            hb.AddChild(tag);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", -2);
            info.AddChild(UiTheme.Text($"{ItemData.DisplayName(ad.ItemId)} x{ad.Count}", 13, UiTheme.TextHi));
            info.AddChild(UiTheme.Text($"{ad.Seller}  ·  {ad.Price:n0} gold", 11, UiTheme.TextLo));
            hb.AddChild(info);
            if (ad.SellerId == _myId)
            {
                var del = new Button { Text = "Delete", FocusMode = Control.FocusModeEnum.None };
                del.Pressed += () => Net.I.SendMarketDelete(adId);
                hb.AddChild(del);
            }
            _mbbsList.AddChild(row);
        }
    }

    private void OnPostAd()
    {
        if (!int.TryParse(_mbbsItem.Text.Trim(), out int itemId) || itemId <= 0) { _mbbsStatus.Text = "Enter an item id."; return; }
        int.TryParse(_mbbsCount.Text.Trim(), out int count); if (count <= 0) count = 1;
        if (!int.TryParse(_mbbsPrice.Text.Trim(), out int price) || price < 0) { _mbbsStatus.Text = "Enter a price."; return; }
        byte buyType = (byte)(_mbbsType.Selected == 1 ? 2 : 1);
        Net.I.SendMarketRegister(itemId, price, count, buyType, "");
    }

    private void OnMarketRegister(bool ok, int adId)
    {
        _mbbsStatus.Text = ok ? "Ad posted." : "Couldn't post (board full?).";
        if (ok) { _mbbsItem.Text = ""; _mbbsPrice.Text = ""; _mbbsCount.Text = ""; Net.I.SendMarketOpen(); }
    }

    private void OnMarketDelete(bool ok)
    {
        if (ok) Net.I.SendMarketOpen();
    }
}
