using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int VendorColumns = 6;
    private const float VendorCellSize = 44f;
    private const float VendorDetailSlotSize = 40f;
    private const int LoyaltyMerchantGroup = 249000;
    private const int TradeRefusedNoMoney = 3;
    private const int TradeRefusedNoRoom = 4;
    private const string VendorHint = "Right-click or drag to trade";

    private struct PendingTrade
    {
        public bool Buy;
        public int ItemId;
        public int AbsSlot;
        public int Count;
        public bool Stack;
    }

    private CanvasLayer _vendorLayer = null!;
    private HudWindow _vendorPanel = null!;
    private LineEdit _vendorSearch = null!;
    private Label _vendorMatches = null!;
    private ServicePager _vendorPager = null!;
    private Label _vendorEmpty = null!;
    private readonly ItemSlotView[] _vendorCells = new ItemSlotView[ShopCatalogue.PageSize];
    private readonly int[] _vendorCellIds = new int[ShopCatalogue.PageSize];
    private DetailStrip _vendorDetail = null!;
    private Button _vendorBuy = null!;
    private FooterBand _vendorFooter = null!;
    private MoneyPlaque _vendorWallet = null!;
    private QuantityPrompt _tradePrompt = null!;
    private bool _vendorShown;

    private int _vendorGroup;
    private bool LoyaltyShop => _vendorGroup == LoyaltyMerchantGroup;
    private string VendorCurrency => LoyaltyShop ? "NP" : "gold";
    private long VendorWallet => LoyaltyShop ? Sheet.Np : Sheet.Gold;
    private int _vendorNpcId;
    private string _vendorNpcName = "Merchant";

    private ShopCatalogue _vendorCatalogue = ShopCatalogue.Empty;
    private Dictionary<int, ItemData.SellEntry> _vendorEntries = new();
    private IReadOnlyList<int> _vendorResults = System.Array.Empty<int>();
    private int _vendorSelected;
    private BagCompanion? _vendorCompanion;

    private bool _tradeInFlight;
    private PendingTrade _pendingTrade;

    private bool VendorSearching => _vendorSearch.Text.Trim().Length > 0;

    private int VendorPageCount => VendorSearching
        ? ShopCatalogue.PagesFor(_vendorResults.Count)
        : Mathf.Max(1, _vendorCatalogue.Pages.Count);

    private void VendorInit()
    {
        BuildVendorPanel();
        Net.I.TradeNpcEvent += OnVendorOpen;
        Net.I.ItemTradeResultEvent += OnTradeResult;
        Net.I.ItemTradeMovedEvent += OnTradeMoved;
        Net.I.GoldChangeEvent += OnVendorGold;
        Net.I.LoyaltyChangeEvent += OnVendorLoyalty;
    }

    private void VendorDispose()
    {
        Net.I.TradeNpcEvent -= OnVendorOpen;
        Net.I.ItemTradeResultEvent -= OnTradeResult;
        Net.I.ItemTradeMovedEvent -= OnTradeMoved;
        Net.I.GoldChangeEvent -= OnVendorGold;
        Net.I.LoyaltyChangeEvent -= OnVendorLoyalty;
    }

    private void BuildVendorPanel()
    {
        _vendorLayer = new CanvasLayer { Layer = 74 };
        AddChild(_vendorLayer);

        _vendorPanel = new HudWindow("vendor", "Merchant") { Visible = false };
        _vendorPanel.Closed += CloseVendor;
        _vendorLayer.AddChild(_vendorPanel);

        var root = _vendorPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        _vendorSearch = new LineEdit
        {
            PlaceholderText = "Search by name or item number",
            ClearButtonEnabled = true,
        };
        _vendorSearch.AddThemeFontSizeOverride("font_size", 13);
        _vendorSearch.TextChanged += _ => OnVendorSearch();
        root.AddChild(_vendorSearch);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        _vendorMatches = UiTheme.Text("", 11, UiTheme.TextDim);
        _vendorMatches.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _vendorMatches.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_vendorMatches);
        _vendorPager = new ServicePager();
        _vendorPager.PageChanged += ShowVendorPage;
        head.AddChild(_vendorPager);
        root.AddChild(head);

        var well = new DropWell { CanDrop = CanSellDrop, Dropped = SellDrop };
        well.Wheeled += _vendorPager.Step;
        root.AddChild(well);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        well.AddChild(stack);
        var grid = new GridContainer { Columns = VendorColumns, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        stack.AddChild(grid);
        for (int i = 0; i < _vendorCells.Length; i++)
        {
            var cell = new ItemSlotView(VendorCellSize) { Index = i };
            cell.Clicked += OnVendorCellClicked;
            cell.DoubleClicked += OnVendorCellBuy;
            cell.RightClicked += OnVendorCellBuy;
            cell.Hovered += OnVendorCellHovered;
            cell.Unhovered += _ => HideItemTooltip();
            cell.Wheeled += (_, step) => _vendorPager.Step(step);
            cell.DragOut = VendorDragOut;
            cell.CanDrop = (_, data) => CanSellDrop(data);
            cell.Dropped = (_, data) => SellDrop(data);
            _vendorCells[i] = cell;
            grid.AddChild(cell);
        }
        _vendorEmpty = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _vendorEmpty.Visible = false;
        stack.AddChild(_vendorEmpty);

        _vendorDetail = new DetailStrip(VendorDetailSlotSize);
        _vendorBuy = UiTheme.ActionButton("Buy", "Buy this item");
        _vendorBuy.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _vendorBuy.Pressed += () => AskBuy(_vendorSelected, -1);
        _vendorDetail.Right.AddChild(_vendorBuy);
        root.AddChild(_vendorDetail);

        _vendorFooter = new FooterBand { Hint = VendorHint };
        _vendorWallet = new MoneyPlaque("");
        _vendorFooter.Left.AddChild(_vendorWallet);
        root.AddChild(_vendorFooter);

        _tradePrompt = new QuantityPrompt(76);
        AddChild(_tradePrompt);
    }

    private void OnVendorOpen(int sellingGroup) => OpenVendor(sellingGroup);

    private void OpenVendor(int sellingGroup)
    {
        _vendorGroup = sellingGroup;
        _tradeInFlight = false;
        CloseNpcDialog();
        var entries = ItemData.SellGroup(sellingGroup).Where(e => ItemData.Get(e.Id) != null).ToList();
        _vendorEntries = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        _vendorCatalogue = new ShopCatalogue(entries.Select(e => new ShopCatalogue.Entry(e.Id, e.Line, e.List)), ItemData.DisplayName);
        _vendorResults = System.Array.Empty<int>();
        _vendorSelected = 0;
        _vendorPanel.Title = _vendorNpcName;
        _vendorSearch.Text = "";
        RefreshVendorWallet();
        _vendorPanel.Visible = true;
        _vendorShown = true;
        _vendorCompanion ??= new BagCompanion(VendorTakeFromBag, VendorBagFit, VendorBagNote, CloseVendor, VendorIntoBag);
        AttachBagCompanion(_vendorCompanion);
        ShowVendorPage(0);
    }

    private void CloseVendor()
    {
        _tradePrompt.Close();
        HideItemTooltip();
        if (!_vendorShown) return;
        _vendorShown = false;
        _vendorPanel.Visible = false;
        if (_vendorCompanion != null) DetachBagCompanion(_vendorCompanion);
    }

    private void OnVendorSearch()
    {
        _vendorResults = _vendorCatalogue.Search(_vendorSearch.Text);
        ShowVendorPage(0);
    }

    private void ShowVendorPage(int page)
    {
        HideItemTooltip();
        int pages = VendorPageCount;
        page = Paging.Step(page, 0, pages);
        _vendorPager.Set(page, pages);
        _vendorMatches.Text = !VendorSearching ? ""
            : _vendorResults.Count == 1 ? "1 match" : $"{_vendorResults.Count:n0} matches";
        FillVendorCells(VendorSearching
            ? ShopCatalogue.Slice(_vendorResults, page)
            : _vendorCatalogue.Pages.Count == 0 ? new int[ShopCatalogue.PageSize] : _vendorCatalogue.Page(_vendorCatalogue.Pages[page]));
        SelectVendorItem(_vendorSelected != 0 && _vendorCellIds.Contains(_vendorSelected)
            ? _vendorSelected : _vendorCellIds.FirstOrDefault(id => id != 0));
    }

    private void FillVendorCells(int[] ids)
    {
        bool any = false;
        for (int i = 0; i < _vendorCells.Length; i++)
        {
            _vendorCellIds[i] = ids[i];
            _vendorCells[i].Set(ids[i] == 0 ? default : TooltipItem(ids[i]));
            any |= ids[i] != 0;
        }
        _vendorEmpty.Visible = !any;
        _vendorEmpty.Text = VendorSearching ? "No items match the search." : "Nothing for sale.";
    }

    private void SelectVendorItem(int itemId)
    {
        _vendorSelected = itemId;
        for (int i = 0; i < _vendorCells.Length; i++)
            _vendorCells[i].Look = itemId != 0 && _vendorCellIds[i] == itemId ? SlotLook.Selected : SlotLook.Normal;
        if (itemId == 0 || ItemData.Get(itemId) is null)
        {
            _vendorDetail.Slot.Clear();
            _vendorDetail.Title.Text = "Choose an item";
            _vendorDetail.Title.AddThemeColorOverride("font_color", UiTheme.TextLo);
            _vendorDetail.Sub.Text = "";
            _vendorBuy.Disabled = true;
            return;
        }
        _vendorDetail.Slot.Set(TooltipItem(itemId));
        _vendorDetail.Title.Text = ItemData.DisplayName(itemId);
        _vendorDetail.Title.AddThemeColorOverride("font_color", ItemGrade.Tint(itemId));
        RefreshVendorDetail();
    }

    private void RefreshVendorDetail()
    {
        if (_vendorSelected == 0 || ItemData.Get(_vendorSelected) is not { } def) return;
        bool ok = CanBuy(_vendorSelected, 1, -1, out _, out _, out string problem);
        _vendorBuy.Disabled = !ok || _tradeInFlight;
        _vendorDetail.Sub.Text = ok
            ? $"{ItemData.BuyPrice(_vendorSelected):n0} {VendorCurrency} each · {def.Weight / 10f:0.0} wt"
            : problem;
        _vendorDetail.Sub.AddThemeColorOverride("font_color", ok ? UiTheme.Gold : UiTheme.Bad);
    }

    private Variant VendorDragOut(ItemSlotView cell)
    {
        if (_vendorCellIds[cell.Index] == 0) return default;
        return new Godot.Collections.Dictionary { { "companionFrom", cell.Index } };
    }

    private void OnVendorCellClicked(ItemSlotView cell)
    {
        if (_vendorCellIds[cell.Index] != 0) SelectVendorItem(_vendorCellIds[cell.Index]);
    }

    private void OnVendorCellBuy(ItemSlotView cell)
    {
        if (_vendorCellIds[cell.Index] == 0) return;
        SelectVendorItem(_vendorCellIds[cell.Index]);
        AskBuy(_vendorCellIds[cell.Index], -1);
    }

    private void OnVendorCellHovered(ItemSlotView cell)
    {
        if (_vendorCellIds[cell.Index] != 0) ShowItemTooltip(-1, TooltipItem(_vendorCellIds[cell.Index]));
    }

    private bool VendorIntoBag(int cell, int abs)
    {
        if (cell < 0 || cell >= _vendorCellIds.Length || _vendorCellIds[cell] == 0) return false;
        AskBuy(_vendorCellIds[cell], abs);
        return true;
    }

    private void AskBuy(int itemId, int preferred)
    {
        if (_tradeInFlight || _selfDead || !_vendorEntries.TryGetValue(itemId, out var entry)) return;
        if (ItemData.Get(itemId) is not { } def) return;
        int price = ItemData.BuyPrice(itemId);
        long? freeWeight = Sheet.MaxWeight > 0 ? Sheet.MaxWeight - CarriedWeight() : null;
        int max = BuyLimit.Max(VendorWallet, price, freeWeight, def.Weight, BuyRoom(itemId, def, preferred));
        if (max <= 0)
        {
            CanBuy(itemId, 1, preferred, out _, out _, out string problem);
            _vendorFooter.Status(problem.Length > 0 ? problem : "You can't buy that.", bad: true);
            return;
        }
        if (def.Countable == 0)
        {
            BuyAmount(entry, 1, preferred);
            return;
        }
        _tradePrompt.Open(ItemData.Icon(itemId), $"Buy {ItemData.DisplayName(itemId)}",
            $"{price:n0} {VendorCurrency} each · up to {max:n0}", max, 1, n => BuyAmount(entry, (int)n, preferred),
            "Buy", n => $"Total {(long)price * n:n0} {VendorCurrency} · {(long)def.Weight * n / 10f:0.0} wt");
    }

    private int BuyRoom(int itemId, ItemData.Item def, int preferred)
    {
        bool countable = def.Countable != 0;
        if (InMainBag(preferred))
        {
            if (Inv[preferred].IsEmpty) return countable ? Inventory.StackMax : 1;
            if (countable && Inv[preferred].ItemId == itemId) return Inventory.StackMax - Inv[preferred].Count;
        }
        return Inv.GridRoomFor(itemId, countable);
    }

    private void BuyAmount(ItemData.SellEntry entry, int count, int preferred)
    {
        if (_tradeInFlight || _selfDead) return;
        count = Mathf.Clamp(count, 1, Inventory.StackMax);
        if (!CanBuy(entry.Id, count, preferred, out int dest, out bool stack, out string problem))
        {
            _vendorFooter.Status(problem, bad: true);
            return;
        }
        _pendingTrade = new PendingTrade { Buy = true, ItemId = entry.Id, AbsSlot = dest, Count = count, Stack = stack };
        _tradeInFlight = true;
        RefreshVendorDetail();
        Net.I.SendVendorBuy(_vendorGroup, _vendorNpcId, entry.Id, (byte)(dest - GridStart), (ushort)count,
            (byte)entry.Line, (byte)entry.List);
    }

    private bool InMainBag(int abs) => abs >= GridStart && abs < GridStart + GridCount && abs < Inv.Length;

    private bool VendorTakeFromBag(int abs)
    {
        if (!InMainBag(abs)) return false;
        AskSell(abs);
        return true;
    }

    private BagFit VendorBagFit(int abs)
    {
        if (LoyaltyShop || !InMainBag(abs)) return BagFit.Unfit;
        return VendorSellUnit(Inv[abs].ItemId) > 0 ? BagFit.Normal : BagFit.Unfit;
    }

    private string VendorBagNote(int abs)
    {
        if (LoyaltyShop || !InMainBag(abs)) return "";
        int unit = VendorSellUnit(Inv[abs].ItemId);
        return unit > 0 ? $"Sells for {(long)unit * Mathf.Max(1, (int)Inv[abs].Count):n0} gold" : "";
    }

    private static int VendorSellUnit(int itemId) => ItemData.IsSellable(itemId) ? ItemData.SellPrice(itemId) : 0;

    private bool CanSellDrop(Variant data) =>
        data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary().ContainsKey("invFrom")
        && InMainBag(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void SellDrop(Variant data) => AskSell(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void AskSell(int abs)
    {
        if (_tradeInFlight || _selfDead || !InMainBag(abs) || Inv[abs].IsEmpty) return;
        if (RefuseItemInUse(abs)) return;
        if (LoyaltyShop)
        {
            _vendorFooter.Status("The Loyalty Merchant buys nothing back.", bad: true);
            return;
        }
        var slot = Inv[abs];
        int unit = VendorSellUnit(slot.ItemId);
        if (unit <= 0)
        {
            _vendorFooter.Status("The merchant won't buy that.", bad: true);
            return;
        }
        if (slot.Count <= 1 || ItemData.Get(slot.ItemId) is not { Countable: not 0 })
        {
            SellSlot(abs, 1);
            return;
        }
        _tradePrompt.Open(ItemData.Icon(slot.ItemId), $"Sell {ItemData.DisplayName(slot.ItemId)}",
            $"{unit:n0} gold each · you carry {slot.Count:n0}", slot.Count, slot.Count, n => SellSlot(abs, (int)n),
            "Sell", n => $"Sells for {(long)unit * n:n0} gold");
    }

    private void SellSlot(int absSlot, int count)
    {
        if (_tradeInFlight || _selfDead || !InMainBag(absSlot) || Inv[absSlot].IsEmpty) return;
        var slot = Inv[absSlot];
        count = Mathf.Clamp(count, 1, Mathf.Max(1, (int)slot.Count));
        _pendingTrade = new PendingTrade { Buy = false, ItemId = slot.ItemId, AbsSlot = absSlot, Count = count };
        _tradeInFlight = true;
        RefreshVendorDetail();
        Net.I.SendVendorSell(_vendorGroup, _vendorNpcId, slot.ItemId, (byte)(absSlot - GridStart), (ushort)count);
    }

    private void OnTradeResult(bool ok, int code, int money, int price)
    {
        if (!_tradeInFlight) return;
        _tradeInFlight = false;
        if (!ok)
        {
            _vendorFooter.Status(code switch
            {
                TradeRefusedNoMoney => LoyaltyShop ? "Not enough National Points." : "Not enough gold.",
                TradeRefusedNoRoom => ItemData.Text(LootNoRoomText, "You cannot trade or pick up items because you have either exceeded the possible quantity or the weight."),
                _ => "The merchant won't trade that.",
            }, bad: true);
            RefreshVendorDetail();
            return;
        }
        ApplyTradeToInventory(_pendingTrade);
        if (CharTabOpen()) RefreshInventoryUI();
        RefreshVendorDetail();
        _vendorFooter.Status(_pendingTrade.Buy
            ? $"Bought {ItemData.DisplayName(_pendingTrade.ItemId)} (−{price:n0})"
            : $"Sold {ItemData.DisplayName(_pendingTrade.ItemId)} (+{price:n0})", bad: false);
    }

    private void OnTradeMoved()
    {
        _tradeInFlight = false;
        if (_vendorShown) RefreshVendorDetail();
    }

    private void OnVendorGold(int total)
    {
        if (!_vendorShown) return;
        RefreshVendorWallet();
        RefreshVendorDetail();
    }

    private void OnVendorLoyalty(int total, int monthly)
    {
        if (!_vendorShown || !LoyaltyShop) return;
        RefreshVendorWallet();
        RefreshVendorDetail();
    }

    private void RefreshVendorWallet()
    {
        _vendorWallet.Caption = LoyaltyShop ? "National Points" : "";
        _vendorWallet.Value = VendorWallet;
    }

    private bool CanBuy(int itemId, int count, int preferred, out int dest, out bool stack, out string problem)
    {
        dest = -1;
        stack = false;
        problem = "";

        var def = ItemData.Get(itemId);
        if (def == null) { problem = "The merchant won't trade that."; return false; }

        bool countable = def.Countable != 0;
        if (InMainBag(preferred))
        {
            if (Inv[preferred].IsEmpty) dest = preferred;
            else if (countable && Inv[preferred].ItemId == itemId && Inv[preferred].Count + count <= Inventory.StackMax)
            { dest = preferred; stack = true; }
        }
        if (dest < 0 && countable)
        {
            for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
                if (Inv[abs].ItemId == itemId && Inv[abs].Count + count <= Inventory.StackMax)
                { dest = abs; stack = true; break; }
        }
        if (dest < 0) dest = Inv.FirstFreeGridSlot();
        if (dest < 0) { problem = "Your bags are full."; return false; }

        if ((long)ItemData.BuyPrice(itemId) * count > VendorWallet)
        {
            problem = LoyaltyShop ? "Not enough National Points." : "Not enough gold.";
            return false;
        }

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

    private void ApplyTradeToInventory(PendingTrade t)
    {
        int abs = t.AbsSlot;
        if (abs < 0 || abs >= Inv.Length) return;
        if (t.Buy)
        {
            if (t.Stack && Inv[abs].ItemId == t.ItemId)
                Inv.Stack(abs, t.Count);
            else
                Inv[abs] = new ItemSlot
                {
                    ItemId = t.ItemId,
                    Count = (short)t.Count,
                    Durability = ItemData.MaxDurabilityOf(t.ItemId),
                };
        }
        else
        {
            int remaining = Inv[abs].Count - t.Count;
            Inv[abs] = remaining > 0 ? new ItemSlot { ItemId = t.ItemId, Count = (short)remaining, Durability = Inv[abs].Durability } : default;
        }
        Net.I.MirrorInventorySlot(abs, Inv[abs]);
    }
}
