using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _auctionLayer = null!;
    private HudWindow _auctionPanel = null!;
    private VBoxContainer _auctionList = null!;
    private LineEdit _auctionItemInput = null!, _auctionStartInput = null!, _auctionBuyoutInput = null!, _auctionCountInput = null!;
    private Label _auctionStatus = null!;
    private bool _auctionShown;
    private readonly Dictionary<int, AuctionLot> _auctionLots = new();

    private void AuctionInit()
    {
        _auctionLayer = new CanvasLayer { Layer = 74 };
        AddChild(_auctionLayer);
        _auctionPanel = new HudWindow("auction", "Auction House", new Vector2(190, 90)) { Visible = false };
        _auctionPanel.Closed += CloseAuction;
        _auctionLayer.AddChild(_auctionPanel);

        var root = _auctionPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Open Lots"));

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _auctionList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _auctionList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_auctionList);

        root.AddChild(UiTheme.SectionTitle("Register a Lot"));
        var form = new HBoxContainer();
        form.AddThemeConstantOverride("separation", 4);
        root.AddChild(form);
        _auctionItemInput = new LineEdit { PlaceholderText = "itemId", CustomMinimumSize = new Vector2(80, 0) };
        _auctionStartInput = new LineEdit { PlaceholderText = "start", CustomMinimumSize = new Vector2(70, 0) };
        _auctionBuyoutInput = new LineEdit { PlaceholderText = "buyout", CustomMinimumSize = new Vector2(70, 0) };
        _auctionCountInput = new LineEdit { PlaceholderText = "count", CustomMinimumSize = new Vector2(55, 0) };
        form.AddChild(_auctionItemInput);
        form.AddChild(_auctionStartInput);
        form.AddChild(_auctionBuyoutInput);
        form.AddChild(_auctionCountInput);
        var regBtn = new Button { Text = "Register", FocusMode = Control.FocusModeEnum.None };
        regBtn.Pressed += OnAuctionRegisterPressed;
        form.AddChild(regBtn);

        _auctionStatus = HudStyle.Label(12);
        _auctionStatus.Text = "";
        root.AddChild(_auctionStatus);

        Net.I.AuctionListEvent += OnAuctionList;
        Net.I.AuctionRegisterEvent += OnAuctionRegister;
        Net.I.AuctionBidEvent += OnAuctionBid;
        Net.I.AuctionBuyoutEvent += OnAuctionBuyout;
    }

    private void AuctionDispose()
    {
        Net.I.AuctionListEvent -= OnAuctionList;
        Net.I.AuctionRegisterEvent -= OnAuctionRegister;
        Net.I.AuctionBidEvent -= OnAuctionBid;
        Net.I.AuctionBuyoutEvent -= OnAuctionBuyout;
    }

    private void ToggleAuction()
    {
        if (_auctionShown) { CloseAuction(); return; }
        _auctionPanel.Visible = true;
        _auctionShown = true;
        Net.I.SendAuctionList();
    }

    private void CloseAuction()
    {
        if (!_auctionShown) return;
        _auctionShown = false;
        _auctionPanel.Visible = false;
    }

    private void OnAuctionList(List<AuctionLot> list)
    {
        _auctionLots.Clear();
        foreach (var c in _auctionList.GetChildren()) c.QueueFree();

        int myId = Net.I.LastEnter.CharId;
        foreach (var lot in list)
        {
            _auctionLots[lot.AuctionId] = lot;

            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var desc = UiTheme.Text($"#{lot.ItemId} x{lot.Count}", 13, UiTheme.TextHi);
            desc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(desc);

            hb.AddChild(UiTheme.Text(lot.Seller, 12, UiTheme.TextLo));

            string bidTxt = lot.CurrentBid > 0 ? $"Bid {lot.CurrentBid:n0}" : "No bid";
            hb.AddChild(UiTheme.Text(bidTxt, 12, UiTheme.Gold));
            if (lot.Buyout > 0)
                hb.AddChild(UiTheme.Text($"Buy {lot.Buyout:n0}", 12, UiTheme.Gold));

            int id = lot.AuctionId;
            bool mine = lot.SellerId == myId;
            if (mine)
            {
                if (lot.CurrentBid <= 0)
                {
                    var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
                    cancel.Pressed += () => Net.I.SendAuctionCancel(id);
                    hb.AddChild(cancel);
                }
                else
                {
                    hb.AddChild(UiTheme.Text("Yours", 12, UiTheme.TextLo));
                }
            }
            else
            {
                var bid = new Button { Text = "Bid", FocusMode = Control.FocusModeEnum.None };
                bid.Pressed += () => Net.I.SendAuctionBid(id, NextBidFor(id));
                hb.AddChild(bid);
                if (lot.Buyout > 0)
                {
                    var buy = new Button { Text = "Buy", FocusMode = Control.FocusModeEnum.None };
                    buy.Pressed += () => Net.I.SendAuctionBuyout(id);
                    hb.AddChild(buy);
                }
            }
            _auctionList.AddChild(row);
        }

        if (_auctionList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13);
            e.Text = "No open lots.";
            _auctionList.AddChild(e);
        }
    }

    private int NextBidFor(int auctionId)
    {
        if (!_auctionLots.TryGetValue(auctionId, out var lot)) return 1;
        return lot.CurrentBid > 0 ? lot.CurrentBid + 1 : 1;
    }

    private void OnAuctionRegisterPressed()
    {
        if (!int.TryParse(_auctionItemInput.Text, out int itemId) || itemId <= 0)
        { _auctionStatus.Text = "Bad item id."; return; }
        if (!int.TryParse(_auctionStartInput.Text, out int start) || start < 0)
        { _auctionStatus.Text = "Bad start price."; return; }
        if (!int.TryParse(_auctionBuyoutInput.Text, out int buyout) || buyout < 0)
            buyout = 0;
        if (!int.TryParse(_auctionCountInput.Text, out int count) || count <= 0)
            count = 1;
        if (buyout > 0 && buyout < start)
        { _auctionStatus.Text = "Buyout below start price."; return; }

        Net.I.SendAuctionRegister(itemId, start, buyout, count);
        _auctionStatus.Text = "Registering...";
    }

    private void OnAuctionRegister(int auctionId, bool ok)
    {
        _auctionStatus.Text = ok ? $"Listed as lot #{auctionId}." : "Could not list that item.";
        if (ok)
        {
            _auctionItemInput.Text = "";
            _auctionStartInput.Text = "";
            _auctionBuyoutInput.Text = "";
            _auctionCountInput.Text = "";
        }
    }

    private void OnAuctionBid(int auctionId, int currentBid, bool ok)
    {
        if (ok)
        {
            Sheet.Spend(currentBid);
            Net.I.RaiseGold(Sheet.Gold);
        }
    }

    private void OnAuctionBuyout(int auctionId, int buyout, bool ok)
    {
        if (ok)
        {
            Sheet.Spend(buyout);
            Net.I.RaiseGold(Sheet.Gold);
        }
    }
}
