using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int StallSlots = Net.MerchantStallSlots;
    private const int StallColumns = 6;
    private const int StallBagColumns = 7;
    private const int MerchantAdvertMax = 40;

    private CanvasLayer _mctLayer = null!;

    private HudWindow _merchantMenu = null!;
    private bool _merchantMenuShown;

    private HudWindow _sellStallPanel = null!;
    private MerchantCell[] _sellStallCells = null!;
    private MerchantCell[] _sellBagCells = null!;
    private LineEdit _sellAdvert = null!;
    private Label _sellTotal = null!, _sellBalance = null!, _sellStatus = null!;
    private bool _sellStallShown;

    private readonly MerchantStallItem[] _myStall = new MerchantStallItem[StallSlots];
    private readonly int[] _myStallSrc = new int[StallSlots];

    private HudWindow _shopPanel = null!;
    private MerchantCell[] _shopCells = null!, _shopBagCells = null!;
    private Label _shopStatus = null!, _shopBalance = null!;
    private bool _shopShown;
    private int _shopTargetId = -1;
    private MerchantStallItem[] _shopItems = new MerchantStallItem[StallSlots];

    private readonly Dictionary<int, string> _merchantAdverts = new();

    private void MerchantInit()
    {
        BuildMerchantPanels();
        BuildBuyMerchantPanels();
        Net.I.MerchantOpenResultEvent += OnMerchantOpenResult;
        Net.I.MerchantItemAddEvent += OnMerchantItemAdd;
        Net.I.MerchantItemCancelEvent += OnMerchantItemCancel;
        Net.I.MerchantListEvent += OnMerchantList;
        Net.I.MerchantBuyEvent += OnMerchantBuy;
        Net.I.MerchantSoldEvent += OnMerchantSold;
        Net.I.MerchantInsertedEvent += OnMerchantInserted;
        Net.I.MerchantStallClosedEvent += OnMerchantStallClosed;
        MerchantStallInit();
        BuyMerchantInit();
        for (int i = 0; i < _myStallSrc.Length; i++) _myStallSrc[i] = -1;
    }

    private void MerchantDispose()
    {
        Net.I.MerchantOpenResultEvent -= OnMerchantOpenResult;
        Net.I.MerchantItemAddEvent -= OnMerchantItemAdd;
        Net.I.MerchantItemCancelEvent -= OnMerchantItemCancel;
        Net.I.MerchantListEvent -= OnMerchantList;
        Net.I.MerchantBuyEvent -= OnMerchantBuy;
        Net.I.MerchantSoldEvent -= OnMerchantSold;
        Net.I.MerchantInsertedEvent -= OnMerchantInserted;
        Net.I.MerchantStallClosedEvent -= OnMerchantStallClosed;
        MerchantStallDispose();
        BuyMerchantDispose();
    }

    private void BuildMerchantPanels()
    {
        _mctLayer = new CanvasLayer { Layer = 75 };
        AddChild(_mctLayer);

        BuildMerchantMenu();
        BuildSellStallPanel();
        BuildShopPanel();
    }

    private void BuildMerchantMenu()
    {
        _merchantMenu = new HudWindow("merchantmenu", "Merchant", new Vector2(260, 150)) { Visible = false };
        _merchantMenu.Closed += CloseMerchantMenu;
        _mctLayer.AddChild(_merchantMenu);

        var root = _merchantMenu.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.CustomMinimumSize = new Vector2(250, 0);

        root.AddChild(MenuButton("Selling Merchant", OpenSellStall));
        root.AddChild(MenuButton("Buying Merchant", OpenWishList));
        root.AddChild(MenuButton("Market Price", OpenMarketPrice));

    }

    private static Button MenuButton(string text, System.Action onPress)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.CustomMinimumSize = new Vector2(0, 30);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.Pressed += onPress;
        return button;
    }

    private void BuildSellStallPanel()
    {
        _sellStallPanel = new HudWindow("sellstall", "Selling Merchant", new Vector2(120, 70)) { Visible = false };
        _sellStallPanel.Closed += CloseSellStall;
        _mctLayer.AddChild(_sellStallPanel);

        var root = _sellStallPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("On the stall"));
        _sellStallCells = BuildMerchantGrid(root, StallSlots, StallColumns, null,
            dragKey: "stallFrom", acceptKey: "bagFrom", onDropFrom: (bag, _) => StageStallItem(bag));

        var money = UiTheme.Section();
        root.AddChild(money);
        var moneyBox = new VBoxContainer();
        moneyBox.AddThemeConstantOverride("separation", 3);
        money.AddChild(moneyBox);
        moneyBox.AddChild(MoneyRow("Total Selling Price", out _sellTotal, UiTheme.GoldBright));
        moneyBox.AddChild(MoneyRow("Current Balance", out _sellBalance, UiTheme.TextHi));

        root.AddChild(UiTheme.SectionTitle("Your bags"));
        _sellBagCells = BuildMerchantGrid(root, GridCount, StallBagColumns, null,
            dragKey: "bagFrom", acceptKey: "stallFrom", onDropFrom: (stall, _) => UnstageStallItem(stall));

        var advertRow = new HBoxContainer();
        advertRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(advertRow);
        var advertLabel = UiTheme.Text("Shop name", 12, UiTheme.TextLo);
        advertRow.AddChild(advertLabel);
        _sellAdvert = new LineEdit
        {
            PlaceholderText = "optional",
            MaxLength = MerchantAdvertMax,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        advertRow.AddChild(_sellAdvert);

        _sellStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _sellStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_sellStatus);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        var confirm = new Button { Text = "Confirm", FocusMode = Control.FocusModeEnum.None };
        confirm.CustomMinimumSize = new Vector2(104, 28);
        confirm.Pressed += ConfirmSellStall;
        footer.AddChild(confirm);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.CustomMinimumSize = new Vector2(104, 28);
        cancel.Pressed += CloseSellStall;
        footer.AddChild(cancel);
    }

    private void BuildShopPanel()
    {
        _shopPanel = new HudWindow("shop", "Shop", new Vector2(420, 110)) { Visible = false };
        _shopPanel.Closed += CloseShop;
        _mctLayer.AddChild(_shopPanel);

        var root = _shopPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("For sale"));
        _shopCells = BuildMerchantGrid(root, StallSlots, StallColumns, BuyFromStall,
            dragKey: "shopFrom");

        var money = UiTheme.Section();
        root.AddChild(money);
        money.AddChild(MoneyRow("Current Balance", out _shopBalance, UiTheme.TextHi));

        root.AddChild(UiTheme.SectionTitle("Your bags"));
        _shopBagCells = BuildMerchantGrid(root, GridCount, StallBagColumns, null,
            acceptKey: "shopFrom", onDropFrom: (shopSlot, _) => BuyFromStall(shopSlot));

        _shopStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _shopStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _shopStatus.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(_shopStatus);
    }

    private static HBoxContainer MoneyRow(string caption, out Label value, Color tint)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = UiTheme.Text(caption, 12, UiTheme.TextLo);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        value = UiTheme.Text("0", 13, tint);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);
        return row;
    }

    private void ToggleMerchantMenu()
    {
        if (_merchantMenuShown) { CloseMerchantMenu(); return; }
        if (_selfDead) return;
        _merchantMenu.Visible = true;
        _merchantMenuShown = true;
    }

    private void CloseMerchantMenu()
    {
        _merchantMenu.Visible = false;
        _merchantMenuShown = false;
    }

    private void OpenSellStall()
    {
        CloseMerchantMenu();
        if (_selfDead) return;
        Net.I.SendMerchantOpen();
    }

    private void OpenMarketPrice()
    {
        CloseMerchantMenu();
        CombatNotice("Market prices are not available on this server yet.");
    }

    private void OnMerchantOpenResult(int status)
    {
        if (status != Net.MerchantOpenAccepted)
        {
            CombatNotice(status switch
            {
                Net.MerchantOpenUnderLevelled => "You must be level 30 to open a shop.",
                Net.MerchantOpenWhileDead => "You can't open a shop right now.",
                Net.MerchantOpenWhileTrading => "Finish your trade first.",
                Net.MerchantOpenWhileMerchanting => "Your shop is already open.",
                _ => "Couldn't open a shop.",
            });
            return;
        }

        for (int i = 0; i < _myStall.Length; i++) { _myStall[i] = default; _myStallSrc[i] = -1; }
        SetSellStatus("", false);
        RefreshSellStall();
        _sellStallPanel.Visible = true;
        _sellStallShown = true;
        StopForInteraction();
    }

    private void CloseSellStall()
    {
        HideItemTooltip();
        if (!_sellStallShown) return;
        _sellStallShown = false;
        _sellStallPanel.Visible = false;
        Net.I.SendMerchantClose();
    }

    private void ConfirmSellStall()
    {
        int staged = 0;
        foreach (var slot in _myStall) if (!slot.IsEmpty) staged++;
        if (staged == 0) { SetSellStatus("Put at least one item on the stall.", true); return; }

        _sellStallShown = false;
        _sellStallPanel.Visible = false;
        HideItemTooltip();
        Net.I.SendMerchantBeginSell(_sellAdvert.Text.Trim());
    }

    private void StageStallItem(int gridIndex)
    {
        if (gridIndex < 0 || gridIndex >= GridCount) return;
        int absSlot = GridStart + gridIndex;
        if (absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        if (System.Array.IndexOf(_myStallSrc, absSlot) >= 0)
        {
            SetSellStatus("That one is already on the stall.", true);
            return;
        }

        int dst = -1;
        for (int i = 0; i < _myStall.Length; i++) if (_myStall[i].IsEmpty) { dst = i; break; }
        if (dst < 0) { SetSellStatus($"The stall only holds {StallSlots} items.", true); return; }

        var slot = Inv[absSlot];
        AskStallPrice(slot, absSlot, dst);
    }

    private void UnstageStallItem(int stallSlot)
    {
        if (stallSlot < 0 || stallSlot >= _myStall.Length || _myStall[stallSlot].IsEmpty) return;
        Net.I.SendMerchantRemoveItem((byte)stallSlot);
    }

    private void OnMerchantItemAdd(bool ok, int itemId, int count, short dura, int price, int srcPos, int dstPos)
    {
        if (!ok) { SetSellStatus("That item can't be sold from a stall.", true); return; }
        if (dstPos >= 0 && dstPos < _myStall.Length)
        {
            _myStall[dstPos] = new MerchantStallItem
            {
                ItemId = itemId, Count = count, Durability = dura, Price = price,
            };
            _myStallSrc[dstPos] = GridStart + srcPos;
        }
        SetSellStatus("", false);
        RefreshSellStall();
    }

    private void OnMerchantItemCancel(bool ok, int slot)
    {
        if (!ok || slot < 0 || slot >= _myStall.Length) return;
        _myStall[slot] = default;
        _myStallSrc[slot] = -1;
        RefreshSellStall();
    }

    private void RefreshSellStall()
    {
        HideItemTooltip();
        long total = 0;
        for (int i = 0; i < _sellStallCells.Length; i++)
        {
            var item = _myStall[i];
            _sellStallCells[i].Set(StallSlot(item), PriceNote("Asking", item.Price, item.Count));
            if (!item.IsEmpty) total += (long)item.Price * item.Count;
        }
        _sellTotal.Text = total.ToString("n0");
        _sellBalance.Text = Sheet.Gold.ToString("n0");
        RefreshSellBags();
    }

    private void RefreshSellBags()
    {
        for (int i = 0; i < _sellBagCells.Length; i++)
        {
            int absSlot = GridStart + i;
            var slot = absSlot < Inv.Length ? Inv[absSlot] : default;
            bool listed = System.Array.IndexOf(_myStallSrc, absSlot) >= 0;
            var cell = _sellBagCells[i];
            cell.TipSlot = absSlot;
            cell.Set(slot, listed ? "listed" : "");
            cell.Modulate = listed ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }
    }

    private void OnMerchantSold(int itemId, string buyerName)
    {
        string who = buyerName.Length > 0 ? buyerName : "Someone";
        CombatNotice($"{who} bought {ItemData.DisplayName(itemId)} from your shop.");
    }

    private void OnMerchantInserted(bool ok, int charId, string advert, byte flags, int[] itemIds)
    {
        if (!ok)
        {
            SetSellStatus("The shop could not be opened.", true);
            _sellStallShown = true;
            _sellStallPanel.Visible = true;
            return;
        }

        _merchantAdverts[charId] = advert;
        PlaceStall(charId, isBuying: false, flags, itemIds);

        if (charId != _myId)
        {
            if (advert.Length > 0 && _ents.TryGetValue(charId, out var seller))
                CombatNotice($"{seller.Name}: {advert}");
            return;
        }

        _sellStallShown = false;
        _sellStallPanel.Visible = false;
        HideItemTooltip();
        SetMerchantLock(true);
        CombatNotice("Your shop is open. You cannot move while it is.");
    }

    private void OnMerchantStallClosed(int charId)
    {
        _merchantAdverts.Remove(charId);
        RemoveStall(charId);
        if (_shopShown && _shopTargetId == charId) CloseShop();
        if (charId == _myId)
        {
            SetMerchantLock(false);
            if (_sellStallShown)
            {
                _sellStallShown = false;
                _sellStallPanel.Visible = false;
                HideItemTooltip();
            }
            for (int i = 0; i < _myStall.Length; i++) { _myStall[i] = default; _myStallSrc[i] = -1; }
        }
    }

    private bool TryBrowseNearestMerchant()
    {
        int bestId = -1;
        float bestDistance = TradeRange;
        foreach (var (id, ent) in _ents)
        {
            if (ent.IsNpc || ent.Dead || id == _myId) continue;
            if (!_stalls.ContainsKey(id)) continue;
            float distance = ent.Body.Position.DistanceTo(_self.Position);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestId = id;
        }
        if (bestId < 0) return false;

        if (_stalls[bestId].IsBuying) Net.I.SendBuyMerchantList(bestId);
        else Net.I.SendMerchantList(bestId);
        return true;
    }

    private void OnMerchantList(int merchantId, MerchantStallItem[] items)
    {
        _shopTargetId = merchantId;
        _shopItems = items;
        _shopPanel.Title = ShopTitle(merchantId);
        RefreshShop();
        _shopPanel.Visible = true;
        _shopShown = true;
        StopForInteraction();
    }

    private string ShopTitle(int merchantId)
    {
        string owner = _ents.TryGetValue(merchantId, out var ent) ? ent.Name : "Shop";
        return _merchantAdverts.TryGetValue(merchantId, out string? advert) && advert.Length > 0
            ? $"{owner} — {advert}"
            : owner;
    }

    private void RefreshShopBags()
    {
        for (int i = 0; i < _shopBagCells.Length; i++)
        {
            int absSlot = GridStart + i;
            var cell = _shopBagCells[i];
            cell.TipSlot = absSlot;
            cell.Set(absSlot < Inv.Length ? Inv[absSlot] : default);
        }
    }

    private void RefreshShop()
    {
        HideItemTooltip();
        RefreshShopBags();
        int listed = 0;
        for (int i = 0; i < _shopCells.Length; i++)
        {
            var item = _shopItems[i];
            _shopCells[i].Set(StallSlot(item), PriceNote("Price", item.Price, item.Count));
            if (!item.IsEmpty) listed++;
        }
        _shopBalance.Text = Sheet.Gold.ToString("n0");
        if (listed == 0) SetShopStatus("This shop has nothing left.", false);
    }

    private void BuyFromStall(int merchantSlot)
    {
        if (merchantSlot < 0 || merchantSlot >= _shopItems.Length) return;
        var item = _shopItems[merchantSlot];
        if (item.IsEmpty) return;
        if (item.Price > Sheet.Gold) { SetShopStatus("You don't have enough gold.", true); return; }

        int most = System.Math.Max(1, item.Count);
        var def = ItemData.Get(item.ItemId);
        bool countable = def != null && def.Countable != 0 && most > 1;

        string itemName = ItemData.DisplayName(item.ItemId);
        string hint = countable
            ? $"Buy {itemName} for {Money(item.Price)} each?"
            : $"Buy {itemName} for {Money(item.Price)}?";

        AskTrade(StallSlot(item), hint, item.Price, most, countable,
            (count, _) =>
            {
                int buyerSlot = Inv.FirstFreeGridSlot();
                if (buyerSlot < 0) { SetShopStatus("Your bags are full.", true); return; }
                Net.I.SendMerchantBuy(item.ItemId, count, (byte)merchantSlot, (byte)(buyerSlot - GridStart));
            });
    }

    private void OnMerchantBuy(bool ok, int itemId, int remaining, int merchantSlot, int buyerSlot)
    {
        if (!ok) { SetShopStatus("That purchase was refused.", true); return; }

        int abs = GridStart + buyerSlot;
        var def = ItemData.Get(itemId);
        if (abs >= 0 && abs < Inv.Length)
        {
            if (def != null && def.Countable != 0 && Inv[abs].ItemId == itemId)
                Inv.Stack(abs, 1);
            else
                Inv[abs] = new ItemSlot { ItemId = itemId, Count = 1, Durability = (short)(def?.Duration ?? 0) };
            Net.I.MirrorInventorySlot(abs, Inv[abs]);
        }

        int price = 0;
        if (merchantSlot >= 0 && merchantSlot < _shopItems.Length && _shopItems[merchantSlot].ItemId == itemId)
        {
            price = _shopItems[merchantSlot].Price;
            _shopItems[merchantSlot].Count = remaining;
            if (remaining <= 0) _shopItems[merchantSlot] = default;
        }

        if (CharTabOpen()) RefreshInventoryUI();
        RefreshShop();
        SetShopStatus($"Bought {ItemData.DisplayName(itemId)} for {Money(price)}.", false);
    }

    private void CloseShop()
    {
        HideItemTooltip();
        if (!_shopShown) return;
        _shopShown = false;
        _shopPanel.Visible = false;
        _shopTargetId = -1;
        Net.I.SendMerchantTradeCancel();
    }

    private static string Money(long amount) => $"{amount:n0}";

    private static string PriceNote(string caption, int price, int count) =>
        count > 1
            ? $"{caption} {Money(price)} each   ({Money((long)price * count)} total)"
            : $"{caption} {Money(price)}";

    private void SetSellStatus(string text, bool warn)
    {
        _sellStatus.Text = text;
        _sellStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void SetShopStatus(string text, bool warn)
    {
        _shopStatus.Text = text;
        _shopStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }
}
