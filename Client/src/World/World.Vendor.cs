using System.Collections.Generic;
using System.Globalization;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _vendorLayer = null!;
    private HudWindow _vendorPanel = null!;
    private Label _vendorGoldLbl = null!;
    private Label _vendorStatus = null!;
    private VBoxContainer _vendorBuyList = null!;
    private VBoxContainer _vendorSellList = null!;
    private bool _vendorShown;
    private bool _buyAmountShown;

    private int _vendorGroup;
    private int _vendorNpcId;
    private string _vendorNpcName = "Merchant";

    private bool _tradeInFlight;
    private PendingTrade _pendingTrade;

    private struct PendingTrade
    {
        public bool Buy;
        public int ItemId;
        public int AbsSlot;
        public int Count;
        public bool Stack;
    }

    private CanvasLayer _buyAmountLayer = null!;
    private PanelContainer _buyAmountBox = null!;
    private TextureRect _buyAmountIcon = null!;
    private Label _buyAmountName = null!, _buyAmountUnit = null!, _buyAmountTotal = null!;
    private SpinBox _buyAmountSpin = null!;
    private Button _buyAmountOk = null!;
    private ItemData.SellEntry? _buyAmountEntry;

    private void VendorInit()
    {
        BuildVendorPanel();
        BuildBuyAmountPrompt();
        Net.I.TradeNpcEvent += OnVendorOpen;
        Net.I.ItemTradeResultEvent += OnTradeResult;
        Net.I.ItemTradeMovedEvent += OnTradeMoved;
        Net.I.GoldChangeEvent += OnVendorGold;
    }

    private void VendorDispose()
    {
        Net.I.TradeNpcEvent -= OnVendorOpen;
        Net.I.ItemTradeResultEvent -= OnTradeResult;
        Net.I.ItemTradeMovedEvent -= OnTradeMoved;
        Net.I.GoldChangeEvent -= OnVendorGold;
    }

    private void BuildVendorPanel()
    {
        _vendorLayer = new CanvasLayer { Layer = 74 };
        AddChild(_vendorLayer);

        _vendorPanel = new HudWindow("vendor", "Merchant", new Vector2(180, 110)) { Visible = false };
        _vendorPanel.Closed += CloseVendor;
        _vendorLayer.AddChild(_vendorPanel);

        var root = _vendorPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);
        cols.AddChild(BuildColumn("Buy", out _vendorBuyList));
        cols.AddChild(BuildColumn("Sell", out _vendorSellList));

        root.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        _vendorGoldLbl = HudStyle.Label(14);
        _vendorGoldLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_vendorGoldLbl);
        _vendorStatus = HudStyle.Label(13, HorizontalAlignment.Right);
        _vendorStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_vendorStatus);
        root.AddChild(footer);
    }

    private void BuildBuyAmountPrompt()
    {
        _buyAmountLayer = new CanvasLayer { Layer = 76, Visible = false };
        AddChild(_buyAmountLayer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _buyAmountLayer.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        _buyAmountLayer.AddChild(centre);

        _buyAmountBox = new PanelContainer();
        _buyAmountBox.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(_buyAmountBox);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 14, 12, 14, 12);
        _buyAmountBox.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        root.AddChild(head);
        _buyAmountIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(_buyAmountIcon);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        _buyAmountName = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(_buyAmountName);
        _buyAmountUnit = UiTheme.Text("", 11, UiTheme.Gold);
        headText.AddChild(_buyAmountUnit);

        var amountRow = new HBoxContainer();
        amountRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(amountRow);
        var amountLbl = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        amountLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        amountRow.AddChild(amountLbl);
        _buyAmountSpin = new SpinBox
        {
            MinValue = 1,
            MaxValue = Inventory.StackMax,
            Step = 1,
            Value = 1,
            CustomMinimumSize = new Vector2(110, 0),
        };
        _buyAmountSpin.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
        _buyAmountSpin.ValueChanged += _ => RefreshBuyAmount();
        _buyAmountSpin.GetLineEdit().TextChanged += _ => RefreshBuyAmount();
        amountRow.AddChild(_buyAmountSpin);

        _buyAmountTotal = UiTheme.Text("", 12, UiTheme.TextLo);
        _buyAmountTotal.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buyAmountTotal.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(_buyAmountTotal);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += CloseBuyAmount;
        buttons.AddChild(cancel);
        _buyAmountOk = new Button { Text = "Buy", FocusMode = Control.FocusModeEnum.None };
        _buyAmountOk.Pressed += ConfirmBuyAmount;
        buttons.AddChild(_buyAmountOk);
    }

    private void OpenBuyAmount(ItemData.SellEntry entry, ItemData.Item def)
    {
        HideItemTooltip();
        _buyAmountEntry = entry;
        _buyAmountIcon.Texture = ItemData.Icon(entry.Id);
        _buyAmountName.Text = ItemData.DisplayName(entry.Id);
        _buyAmountUnit.Text = $"{ItemData.BuyPrice(entry.Id):n0} gold each";
        SetBuyAmount(1);
        _buyAmountLayer.Visible = true;
        _buyAmountShown = true;
        _buyAmountSpin.GetLineEdit().GrabFocus();
        _buyAmountSpin.GetLineEdit().SelectAll();
    }

    private void CloseBuyAmount()
    {
        if (!_buyAmountShown) return;
        _buyAmountShown = false;
        _buyAmountLayer.Visible = false;
    }

    // SpinBox pushes Value into its LineEdit deferred, and Value = Value is a no-op: set the text by hand.
    private void SetBuyAmount(int count)
    {
        _buyAmountSpin.Value = count;
        _buyAmountSpin.GetLineEdit().Text = count.ToString(CultureInfo.InvariantCulture);
        RefreshBuyAmount();
    }

    // SpinBox.Value only commits the typed text on focus-out, and the Enter shortcut fires first.
    private int BuyAmountCount()
    {
        string typed = _buyAmountSpin.GetLineEdit().Text.Trim();
        int value = int.TryParse(typed, out int parsed) ? parsed : (int)_buyAmountSpin.Value;
        return Mathf.Clamp(value, 1, Inventory.StackMax);
    }

    private void RefreshBuyAmount()
    {
        if (_buyAmountEntry is not { } entry) return;
        var def = ItemData.Get(entry.Id);
        if (def == null) return;
        int count = BuyAmountCount();

        bool ok = CanBuy(entry.Id, count, out _, out _, out string problem);
        _buyAmountOk.Disabled = !ok;
        _buyAmountTotal.Text = ok
            ? $"Total {(long)ItemData.BuyPrice(entry.Id) * count:n0} gold   ·   {(long)def.Weight * count / 10f:0.0} wt"
              + $"   ·   load {CarriedWeight() / 10f:0.0} / {Sheet.MaxWeight / 10f:0.0}"
            : problem;
        _buyAmountTotal.AddThemeColorOverride("font_color", ok ? UiTheme.TextLo : new Color("ff6a6a"));
    }

    private void ConfirmBuyAmount()
    {
        if (_buyAmountEntry is not { } entry) return;
        int count = BuyAmountCount();
        CloseBuyAmount();
        BuyAmount(entry, count);
    }

    private static Control BuildColumn(string heading, out VBoxContainer list)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
        box.AddThemeConstantOverride("separation", 4);
        var head = UiTheme.SectionTitle(heading);
        box.AddChild(head);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(340, 360),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return box;
    }

    private void OnVendorOpen(int sellingGroup)
    {
        _vendorGroup = sellingGroup;
        _tradeInFlight = false;
        CloseNpcDialog();
        _vendorPanel.Title = _vendorNpcName;
        _vendorStatus.Text = "";
        RefreshVendorGold();
        RefreshBuyList();
        RefreshSellList();

        _vendorPanel.Visible = true;
        _vendorShown = true;
    }

    private void CloseVendor()
    {
        CloseBuyAmount();
        HideItemTooltip();
        if (!_vendorShown) return;
        _vendorShown = false;
        _vendorPanel.Visible = false;
    }

    private void RefreshBuyList()
    {
        HideItemTooltip();
        foreach (var c in _vendorBuyList.GetChildren()) c.QueueFree();
        int shown = 0;
        foreach (var entry in ItemData.SellGroup(_vendorGroup))
        {
            var def = ItemData.Get(entry.Id);
            if (def == null) continue;
            var captured = entry;
            _vendorBuyList.AddChild(BuildTradeRow(
                entry.Id, $"{ItemData.BuyPrice(entry.Id):n0} gold", "Buy",
                () => BuyItem(captured),
                () => BuyItem(captured),
                -1, TooltipItem(entry.Id)));
            shown++;
        }
        if (shown == 0)
        {
            var lbl = HudStyle.Label(13);
            lbl.Text = "Nothing for sale.";
            _vendorBuyList.AddChild(lbl);
        }
    }

    private void RefreshSellList()
    {
        HideItemTooltip();
        foreach (var c in _vendorSellList.GetChildren()) c.QueueFree();
        int shown = 0;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty) continue;
            var slot = Inv[abs];
            var def = ItemData.Get(slot.ItemId);
            if (def == null) continue;
            int unit = ItemData.IsSellable(slot.ItemId) ? ItemData.SellPrice(slot.ItemId) : 0;
            int absSlot = abs;
            long total = (long)unit * slot.Count;
            _vendorSellList.AddChild(BuildTradeRow(
                slot.ItemId, total > 0 ? $"{total:n0} gold" : "No value", "Sell",
                () => SellSlot(absSlot),
                () => SellSlot(absSlot),
                absSlot, slot,
                slot.Count, total <= 0));
            shown++;
        }
        if (shown == 0)
        {
            var lbl = HudStyle.Label(13);
            lbl.Text = "Your bags are empty.";
            _vendorSellList.AddChild(lbl);
        }
    }

    private Control BuildTradeRow(int itemId, string priceText, string action,
        System.Action onButton, System.Action onDoubleClick, int tipSlot, ItemSlot tipItem,
        int count = 0, bool worthless = false)
    {
        var row = new TradeRow(onDoubleClick);
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());
        row.MouseEntered += () => ShowItemTooltip(tipSlot, tipItem);
        row.MouseExited += HideItemTooltip;

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        var icon = new TextureRect
        {
            Texture = ItemData.Icon(itemId),
            CustomMinimumSize = new Vector2(34, 34),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hb.AddChild(icon);

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", -2);
        info.MouseFilter = Control.MouseFilterEnum.Ignore;
        var name = UiTheme.Text("", 13, UiTheme.TextHi);
        name.Text = count > 1 ? $"{ItemData.DisplayName(itemId)}  x{count}" : ItemData.DisplayName(itemId);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(name);
        var price = UiTheme.Text("", 11, worthless ? UiTheme.TextLo : UiTheme.Gold);
        price.Text = priceText;
        price.MouseFilter = Control.MouseFilterEnum.Ignore;
        info.AddChild(price);
        hb.AddChild(info);

        var btn = new Button { Text = action, FocusMode = Control.FocusModeEnum.None };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.Pressed += () => onButton();
        hb.AddChild(btn);
        return row;
    }

    private void BuyItem(ItemData.SellEntry entry)
    {
        if (_tradeInFlight || _selfDead) return;
        var def = ItemData.Get(entry.Id);
        if (def == null) return;

        if (def.Countable != 0) OpenBuyAmount(entry, def);
        else BuyAmount(entry, 1);
    }

    private void BuyAmount(ItemData.SellEntry entry, int count)
    {
        if (_tradeInFlight || _selfDead) return;
        var def = ItemData.Get(entry.Id);
        if (def == null) return;
        count = Mathf.Clamp(count, 1, Inventory.StackMax);

        if (!CanBuy(entry.Id, count, out int dest, out bool stack, out string problem))
        { SetVendorStatus(problem, true); return; }

        _pendingTrade = new PendingTrade { Buy = true, ItemId = entry.Id, AbsSlot = dest, Count = count, Stack = stack };
        _tradeInFlight = true;
        SetVendorStatus("", false);
        Net.I.SendVendorBuy(_vendorGroup, _vendorNpcId, entry.Id, (byte)(dest - GridStart), (ushort)count,
            (byte)entry.Line, (byte)entry.List);
    }

    private bool CanBuy(int itemId, int count, out int dest, out bool stack, out string problem)
    {
        dest = -1;
        stack = false;
        problem = "";

        var def = ItemData.Get(itemId);
        if (def == null) { problem = "The merchant won't trade that."; return false; }

        if (def.Countable != 0)
        {
            for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
                if (Inv[abs].ItemId == itemId && Inv[abs].Count + count <= Inventory.StackMax)
                { dest = abs; stack = true; break; }
        }
        if (dest < 0) dest = Inv.FirstFreeGridSlot();
        if (dest < 0) { problem = "Your bags are full."; return false; }

        if ((long)ItemData.BuyPrice(itemId) * count > Sheet.Gold) { problem = "Not enough gold."; return false; }

        if (Sheet.MaxWeight > 0)
        {
            long free = Sheet.MaxWeight - CarriedWeight();
            if ((long)def.Weight * count > free)
            {
                problem = $"Too heavy — only {Mathf.Max(0f, free / 10f):0.0} wt free "
                    + $"of {Sheet.MaxWeight / 10f:0.0}.";
                return false;
            }
        }

        return true;
    }

    private void SellSlot(int absSlot)
    {
        if (_tradeInFlight || _selfDead) return;
        if (absSlot < 0 || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        var slot = Inv[absSlot];
        int count = Mathf.Max(1, (int)slot.Count);

        _pendingTrade = new PendingTrade { Buy = false, ItemId = slot.ItemId, AbsSlot = absSlot, Count = count };
        _tradeInFlight = true;
        SetVendorStatus("", false);
        Net.I.SendVendorSell(_vendorGroup, _vendorNpcId, slot.ItemId, (byte)(absSlot - GridStart), (ushort)count);
    }

    private void OnTradeResult(bool ok, int code, int money, int price)
    {
        if (!_tradeInFlight) return;
        _tradeInFlight = false;

        if (!ok)
        {
            SetVendorStatus(code switch
            {
                3 => "Not enough gold.",
                4 => "Your bags are full.",
                _ => "The merchant won't trade that.",
            }, true);
            return;
        }

        ApplyTradeToInventory(_pendingTrade);
        RefreshSellList();
        if (CharTabOpen()) RefreshInventoryUI();
        SetVendorStatus(_pendingTrade.Buy
            ? $"Bought {ItemData.DisplayName(_pendingTrade.ItemId)} (−{price:n0})"
            : $"Sold {ItemData.DisplayName(_pendingTrade.ItemId)} (+{price:n0})", false);
    }

    private void ApplyTradeToInventory(PendingTrade t)
    {
        int abs = t.AbsSlot;
        if (abs < 0 || abs >= Inv.Length) return;
        if (t.Buy)
        {
            if (t.Stack && Inv[abs].ItemId == t.ItemId)
                Inv.Stack(abs, t.Count);
            else
            {
                var def = ItemData.Get(t.ItemId);
                Inv[abs] = new ItemSlot
                {
                    ItemId = t.ItemId,
                    Count = (short)t.Count,
                    Durability = (short)(def?.Duration ?? 0),
                };
            }
        }
        else
        {
            int remaining = Inv[abs].Count - t.Count;
            Inv[abs] = remaining > 0 ? new ItemSlot { ItemId = t.ItemId, Count = (short)remaining, Durability = Inv[abs].Durability } : default;
        }
        Net.I.MirrorInventorySlot(abs, Inv[abs]);
    }

    private void OnTradeMoved()
    {
        _tradeInFlight = false;
        if (_vendorShown) RefreshSellList();
    }

    private void OnVendorGold(int total)
    {
        if (_vendorShown) RefreshVendorGold();
    }

    private void RefreshVendorGold() => _vendorGoldLbl.Text = $"Gold  {Sheet.Gold:n0}";

    private void SetVendorStatus(string text, bool warn)
    {
        _vendorStatus.Text = text;
        _vendorStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }

    private sealed partial class TradeRow : PanelContainer
    {
        private readonly System.Action _onDoubleClick;
        public TradeRow(System.Action onDoubleClick) => _onDoubleClick = onDoubleClick;

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left })
                _onDoubleClick();
        }
    }
}
