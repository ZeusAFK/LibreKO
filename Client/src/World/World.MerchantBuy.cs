using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int MerchantWishMaxStack = 9_999;

    private HudWindow _wishPanel = null!;
    private MerchantCell[] _wishCells = null!;
    private Label _wishTotal = null!, _wishStatus = null!;
    private VBoxContainer _wishSummary = null!;
    private bool _wishShown;
    private readonly MerchantWishItem[] _wishes = new MerchantWishItem[StallSlots];

    private HudWindow _wishFindPanel = null!;
    private ItemSearchPanel _wishFind = null!;
    private bool _wishFindShown;
    private int _wishFindTarget = -1;

    private HudWindow _wantedPanel = null!;
    private MerchantCell[] _wantedCells = null!;
    private MerchantCell[] _wantedBagCells = null!;
    private Label _wantedStatus = null!, _wantedBalance = null!;
    private bool _wantedShown;
    private int _wantedMerchantId = -1;
    private MerchantStallItem[] _wantedItems = new MerchantStallItem[StallSlots];

    private CanvasLayer _amountLayer = null!;
    private TextureRect _amountIcon = null!;
    private Label _amountName = null!, _amountHint = null!, _amountTotal = null!;
    private SpinBox _amountCount = null!;
    private MoneyEdit _amountPrice = null!;
    private Label _amountPriceFixed = null!;
    private HBoxContainer _amountPriceRow = null!, _amountCountRow = null!;
    private PanelContainer _amountTotalRow = null!;
    private Label _amountPriceLabel = null!;
    private System.Action<int, int>? _amountAccept;

    private void BuyMerchantInit()
    {
        Net.I.BuyMerchantOpenEvent += OnBuyMerchantOpen;
        Net.I.BuyMerchantInsertEvent += OnBuyMerchantInsert;
        Net.I.BuyMerchantListEvent += OnBuyMerchantList;
        Net.I.BuyMerchantResultEvent += OnBuyMerchantResult;
        Net.I.BuyMerchantSoldEvent += OnBuyMerchantSold;
        Net.I.BuyMerchantBoughtEvent += OnBuyMerchantBought;
        Net.I.BuyMerchantClosedEvent += OnBuyMerchantClosed;
    }

    private void BuyMerchantDispose()
    {
        Net.I.BuyMerchantOpenEvent -= OnBuyMerchantOpen;
        Net.I.BuyMerchantInsertEvent -= OnBuyMerchantInsert;
        Net.I.BuyMerchantListEvent -= OnBuyMerchantList;
        Net.I.BuyMerchantResultEvent -= OnBuyMerchantResult;
        Net.I.BuyMerchantSoldEvent -= OnBuyMerchantSold;
        Net.I.BuyMerchantBoughtEvent -= OnBuyMerchantBought;
        Net.I.BuyMerchantClosedEvent -= OnBuyMerchantClosed;
    }

    private void BuildBuyMerchantPanels()
    {
        BuildWishPanel();
        BuildWishFindPanel();
        BuildWantedPanel();
        BuildAmountPrompt();
    }

    private void BuildWishPanel()
    {
        _wishPanel = new HudWindow("wishlist", "Item Wish List", new Vector2(160, 90)) { Visible = false };
        _wishPanel.Closed += CloseWishList;
        _mctLayer.AddChild(_wishPanel);

        var root = _wishPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("What you want to buy"));
        _wishCells = BuildMerchantGrid(root, StallSlots, StallColumns, OnWishSlotClicked);

        var summary = UiTheme.Section();
        root.AddChild(summary);
        _wishSummary = new VBoxContainer();
        _wishSummary.AddThemeConstantOverride("separation", 2);
        _wishSummary.CustomMinimumSize = new Vector2(300, 76);
        summary.AddChild(_wishSummary);

        var totalRow = UiTheme.Section();
        root.AddChild(totalRow);
        totalRow.AddChild(MoneyRow("Noah", out _wishTotal, UiTheme.GoldBright));

        _wishStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _wishStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _wishStatus.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(_wishStatus);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        var ok = new Button { Text = "O  K", FocusMode = Control.FocusModeEnum.None };
        ok.CustomMinimumSize = new Vector2(104, 28);
        ok.Pressed += ConfirmWishList;
        footer.AddChild(ok);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.CustomMinimumSize = new Vector2(104, 28);
        cancel.Pressed += CloseWishList;
        footer.AddChild(cancel);
    }

    private void BuildWishFindPanel()
    {
        _wishFindPanel = new HudWindow("wishfind", "Item Search", new Vector2(520, 90)) { Visible = false };
        _wishFindPanel.Closed += CloseWishFind;
        _mctLayer.AddChild(_wishFindPanel);

        _wishFind = new ItemSearchPanel(
            tradeableOnly: true,
            actionText: "Registration",
            onAction: OnWishItemPicked,
            showTooltip: itemId => ShowItemTooltip(-1, TooltipItem(itemId)),
            hideTooltip: HideItemTooltip,
            resultsSize: new Vector2(430, 300),
            showQuantity: false);
        _wishFindPanel.Body.AddChild(_wishFind);
    }

    private void BuildWantedPanel()
    {
        _wantedPanel = new HudWindow("wantedstall", "Buying Merchant", new Vector2(120, 70)) { Visible = false };
        _wantedPanel.Closed += CloseWantedStall;
        _mctLayer.AddChild(_wantedPanel);

        var root = _wantedPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("Wanted"));
        _wantedCells = BuildMerchantGrid(root, StallSlots, StallColumns, null,
            acceptKey: "bagFrom", onDropFrom: SellToWanted);

        var money = UiTheme.Section();
        root.AddChild(money);
        money.AddChild(MoneyRow("Current Balance", out _wantedBalance, UiTheme.TextHi));

        root.AddChild(UiTheme.SectionTitle("Your bags"));
        _wantedBagCells = BuildMerchantGrid(root, GridCount, StallBagColumns, null,
            dragKey: "bagFrom");

        _wantedStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _wantedStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _wantedStatus.CustomMinimumSize = new Vector2(340, 0);
        root.AddChild(_wantedStatus);
    }

    private void OpenWishList()
    {
        CloseMerchantMenu();
        if (_selfDead) return;
        for (int i = 0; i < _wishes.Length; i++) _wishes[i] = default;
        SetWishStatus("", false);
        RefreshWishList();
        _wishPanel.Visible = true;
        _wishShown = true;
        StopForInteraction();
    }

    private void CloseWishList()
    {
        HideItemTooltip();
        CloseWishFind();
        if (!_wishShown) return;
        _wishShown = false;
        _wishPanel.Visible = false;
    }

    private void OnWishSlotClicked(int slot)
    {
        if (slot < 0 || slot >= _wishes.Length) return;
        if (!_wishes[slot].IsEmpty)
        {
            _wishes[slot] = default;
            RefreshWishList();
            return;
        }
        _wishFindTarget = slot;
        _wishFindPanel.Visible = true;
        _wishFindShown = true;
        _wishFind.FocusQuery();
    }

    private void CloseWishFind()
    {
        if (!_wishFindShown) return;
        _wishFindShown = false;
        _wishFindPanel.Visible = false;
        _wishFindTarget = -1;
        HideItemTooltip();
    }

    private void OnWishItemPicked(ItemSearchHit hit, int count)
    {
        int slot = _wishFindTarget;
        if (slot < 0 || slot >= _wishes.Length) return;

        bool stackable = hit.Def.Countable > 0;
        int most = stackable ? MerchantWishMaxStack : 1;
        var preview = new ItemSlot { ItemId = hit.Id, Count = 1, Durability = (short)hit.Def.Duration };

        AskAmount(preview, "Wish Noah Amount to Purchase", ItemData.BuyPrice(hit.Id), most, stackable,
            (wantCount, price) =>
            {
                _wishes[slot] = new MerchantWishItem { ItemId = hit.Id, Count = wantCount, Price = price };
                CloseWishFind();
                RefreshWishList();
            }, defaultCount: 1);
    }

    private void RefreshWishList()
    {
        HideItemTooltip();
        long total = 0;
        int filled = 0;
        foreach (Node child in _wishSummary.GetChildren()) child.QueueFree();

        for (int i = 0; i < _wishCells.Length; i++)
        {
            var wish = _wishes[i];
            var slot = new ItemSlot { ItemId = wish.ItemId, Count = (short)wish.Count };
            _wishCells[i].Set(slot, PriceNote("Offering", wish.Price, wish.Count));
            if (wish.IsEmpty) continue;

            filled++;
            long line = (long)wish.Price * wish.Count;
            total += line;
            _wishSummary.AddChild(UiTheme.Text(
                $"{ItemData.DisplayName(wish.ItemId)}   [{Money(wish.Price)} Coin] x {wish.Count}",
                12, UiTheme.TextHi));
        }

        if (filled == 0)
            _wishSummary.AddChild(UiTheme.Text("Nothing on the list yet.", 12, UiTheme.TextDim));

        _wishTotal.Text = total.ToString("n0");
        _wishTotal.AddThemeColorOverride("font_color", total > Sheet.Gold ? UiTheme.Bad : UiTheme.GoldBright);
    }

    private void ConfirmWishList()
    {
        var wanted = new List<MerchantWishItem>();
        long total = 0;
        foreach (var wish in _wishes)
        {
            if (wish.IsEmpty) continue;
            wanted.Add(wish);
            total += (long)wish.Price * wish.Count;
        }

        if (wanted.Count == 0) { SetWishStatus("Add at least one item first.", true); return; }
        if (total > Sheet.Gold) { SetWishStatus("You are not carrying that much gold.", true); return; }

        _pendingWishes = wanted;
        Net.I.SendBuyMerchantOpen();
    }

    private List<MerchantWishItem>? _pendingWishes;

    private void OnBuyMerchantOpen(byte result)
    {
        if (result != Net.BuyMerchantAccepted)
        {
            _pendingWishes = null;
            SetWishStatus(BuyMerchantMessage(result), true);
            return;
        }

        if (_pendingWishes == null || _pendingWishes.Count == 0) return;
        Net.I.SendBuyMerchantInsert(_pendingWishes);
    }

    private void OnBuyMerchantInsert(byte result)
    {
        var wanted = _pendingWishes;
        _pendingWishes = null;

        if (result != Net.BuyMerchantAccepted)
        {
            SetWishStatus(BuyMerchantMessage(result), true);
            return;
        }

        CloseWishList();
        SetMerchantLock(true);

        var ids = new List<int>();
        if (wanted != null)
            foreach (var wish in wanted) ids.Add(wish.ItemId);
        PlaceStall(_myId, isBuying: true, flags: 0, ids.ToArray());
        CombatNotice("Your buying stall is open. You cannot move while it is.");
    }

    private void OnBuyMerchantClosed(int charId)
    {
        RemoveStall(charId);
        if (_wantedShown && _wantedMerchantId == charId) CloseWantedStall();
        if (charId == _myId)
        {
            SetMerchantLock(false);
            CombatNotice("Your buying stall closed.");
        }
    }

    private void OnBuyMerchantList(int merchantId, MerchantStallItem[] wanted)
    {
        _wantedMerchantId = merchantId;
        _wantedItems = wanted;
        _wantedPanel.Title = _ents.TryGetValue(merchantId, out var ent) ? ent.Name : "Buying Merchant";
        RefreshWantedStall();
        SetWantedStatus("", false);
        _wantedPanel.Visible = true;
        _wantedShown = true;
        StopForInteraction();
    }

    private void CloseWantedStall()
    {
        HideItemTooltip();
        if (!_wantedShown) return;
        _wantedShown = false;
        _wantedPanel.Visible = false;
        _wantedMerchantId = -1;
        Net.I.SendMerchantTradeCancel();
    }

    private void RefreshWantedStall()
    {
        HideItemTooltip();
        for (int i = 0; i < _wantedCells.Length; i++)
        {
            var item = _wantedItems[i];
            _wantedCells[i].Set(StallSlot(item), PriceNote("Pays", item.Price, item.Count));
        }
        for (int i = 0; i < _wantedBagCells.Length; i++)
        {
            int absSlot = GridStart + i;
            var slot = absSlot < Inv.Length ? Inv[absSlot] : default;
            var cell = _wantedBagCells[i];
            cell.TipSlot = absSlot;
            cell.Set(slot);
            cell.Modulate = WantedMatch(slot) ? Colors.White : new Color(1, 1, 1, 0.45f);
        }
        _wantedBalance.Text = Sheet.Gold.ToString("n0");
    }

    private bool WantedMatch(ItemSlot slot)
    {
        if (slot.IsEmpty) return false;
        foreach (var wanted in _wantedItems)
            if (!wanted.IsEmpty && wanted.ItemId == slot.ItemId) return true;
        return false;
    }

    private void SellToWanted(int gridIndex, int wantedSlot)
    {
        if (wantedSlot < 0 || wantedSlot >= _wantedItems.Length) return;
        int absSlot = GridStart + gridIndex;
        if (absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;

        var wanted = _wantedItems[wantedSlot];
        var held = Inv[absSlot];
        if (wanted.IsEmpty || held.ItemId != wanted.ItemId)
        {
            SetWantedStatus("That is not the item they asked for.", true);
            return;
        }

        int most = System.Math.Max(1, System.Math.Min(held.Count, wanted.Count));
        var def = ItemData.Get(held.ItemId);
        bool countable = def != null && def.Countable != 0 && most > 1;

        AskTrade(held, "Sell to this shop", wanted.Price, most, countable,
            (count, _) => Net.I.SendBuyMerchantSell((byte)gridIndex, (byte)wantedSlot, count));
    }

    private void OnBuyMerchantResult(byte result)
    {
        if (result == Net.BuyMerchantAccepted) return;
        SetWantedStatus(BuyMerchantMessage(result), true);
    }

    private void OnBuyMerchantSold(int wantedSlot, int wantedRemaining, int sellerSlot, int sellerRemaining)
    {
        int absSlot = GridStart + sellerSlot;
        if (absSlot >= 0 && absSlot < Inv.Length)
        {
            int itemId = Inv[absSlot].ItemId;
            int sold = Inv[absSlot].Count - sellerRemaining;
            if (sellerRemaining <= 0) Inv[absSlot] = default;
            else Inv[absSlot] = new ItemSlot
            {
                ItemId = itemId,
                Count = (short)sellerRemaining,
                Durability = Inv[absSlot].Durability,
            };
            Net.I.MirrorInventorySlot(absSlot, Inv[absSlot]);

            if (wantedSlot >= 0 && wantedSlot < _wantedItems.Length && sold > 0)
            {
                long paid = (long)_wantedItems[wantedSlot].Price * sold;
                SetWantedStatus($"Sold {sold} x {ItemData.DisplayName(itemId)} for {Money(paid)}.", false);
            }
        }

        if (wantedSlot >= 0 && wantedSlot < _wantedItems.Length)
        {
            _wantedItems[wantedSlot].Count = wantedRemaining;
            if (wantedRemaining <= 0) _wantedItems[wantedSlot] = default;
        }

        if (CharTabOpen()) RefreshInventoryUI();
        RefreshWantedStall();
    }

    private void OnBuyMerchantBought(int wantedSlot, int remaining, string sellerName)
    {
        string who = sellerName.Length > 0 ? sellerName : "Someone";
        CombatNotice(remaining > 0
            ? $"{who} sold to your stall — {remaining} still wanted."
            : $"{who} filled one of your orders.");
    }

    private static string BuyMerchantMessage(byte result) => result switch
    {
        Net.BuyMerchantWhileDead => "You can't do that right now.",
        Net.BuyMerchantWhileMerchanting => "You are already running a stall.",
        Net.BuyMerchantNotAllowedHere => "A buying stall can only be opened in Moradon.",
        Net.BuyMerchantWrongItemSetup => "One of those items can't be bought this way.",
        Net.BuyMerchantWrongStallSetup => "That stall is no longer open.",
        Net.BuyMerchantWrongPurchaseCount => "That quantity isn't allowed.",
        Net.BuyMerchantNoSuchItemWanted => "They don't want that item.",
        Net.BuyMerchantSellerFundsTooLow => "You are not carrying that much gold.",
        Net.BuyMerchantBuyerFundsTooLow => "The buyer ran out of gold.",
        Net.BuyMerchantItemNotSellable => "That item can't be sold.",
        Net.BuyMerchantInventoryFull => "Their bags are full.",
        Net.BuyMerchantOverMaxLimit => "That would take you over the gold limit.",
        Net.BuyMerchantNeedsRepair => "Repair it first.",
        Net.BuyMerchantUnderLevelled => "You must be level 35 to open a buying stall.",
        _ => "The stall refused that.",
    };

    private void SetWishStatus(string text, bool warn)
    {
        _wishStatus.Text = text;
        _wishStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void SetWantedStatus(string text, bool warn)
    {
        _wantedStatus.Text = text;
        _wantedStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }
}
