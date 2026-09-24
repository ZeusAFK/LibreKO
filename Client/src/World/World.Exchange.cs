using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _exLayer = null!;
    private HudWindow _exPanel = null!;
    private VBoxContainer _exMineList = null!, _exTheirsList = null!, _exBagList = null!;
    private Label _exMineGold = null!, _exTheirsGold = null!, _exStatus = null!;
    private Button _exConfirmBtn = null!, _exCancelBtn = null!;
    private LineEdit _exGoldEdit = null!;
    private ConfirmationDialog _exAskDialog = null!;

    private bool _exShown;
    private bool _exRequestPending;
    private bool _exConfirmedByMe;
    private int _exPartnerId = -1;
    private string _exPartnerName = "Player";
    private int _exMyGoldOffer, _exTheirGoldOffer;

    private bool _exAddInFlight;
    private PendingExAdd _exPending;
    private readonly List<ExOfferItem> _exMyOffer = new();
    private readonly List<ExOfferItem> _exTheirOffer = new();

    private CanvasLayer _exWaitLayer = null!;
    private Label _exWaitLabel = null!;
    private bool _exWaiting;

    private CanvasLayer _exAmountLayer = null!;
    private TextureRect _exAmountIcon = null!;
    private Label _exAmountName = null!, _exAmountHint = null!;
    private SpinBox _exAmountSpin = null!;
    private bool _exAmountShown;
    private int _exAmountSlot = -1;
    private int _exAmountMax = 1;

    private struct PendingExAdd { public bool IsGold; public int ItemId; public int SourceAbs; public int Count; public short Dura; }
    private struct ExOfferItem { public int ItemId; public int Count; public short Dura; public int SourceAbs; }

    private const float TradeRange = 8f;

    private void ExchangeInit()
    {
        BuildExchangePanel();
        Net.I.ExchangeRequestEvent += OnExchangeRequest;
        Net.I.ExchangeAgreeEvent += OnExchangeAgree;
        Net.I.ExchangeAddResultEvent += OnExchangeAddResult;
        Net.I.ExchangeOtherAddEvent += OnExchangeOtherAdd;
        Net.I.ExchangeOtherDecideEvent += OnExchangeOtherDecide;
        Net.I.ExchangeDoneEvent += OnExchangeDone;
        Net.I.ExchangeCancelEvent += OnExchangeCancel;
    }

    private void ExchangeDispose()
    {
        Net.I.ExchangeRequestEvent -= OnExchangeRequest;
        Net.I.ExchangeAgreeEvent -= OnExchangeAgree;
        Net.I.ExchangeAddResultEvent -= OnExchangeAddResult;
        Net.I.ExchangeOtherAddEvent -= OnExchangeOtherAdd;
        Net.I.ExchangeOtherDecideEvent -= OnExchangeOtherDecide;
        Net.I.ExchangeDoneEvent -= OnExchangeDone;
        Net.I.ExchangeCancelEvent -= OnExchangeCancel;
    }

    private void BuildExchangePanel()
    {
        _exLayer = new CanvasLayer { Layer = 75 };
        AddChild(_exLayer);

        _exPanel = new HudWindow("exchange", "Trade", new Vector2(220, 90)) { Visible = false };
        _exPanel.Closed += () => { if (_exShown) AbortExchange(local: true); };
        _exLayer.AddChild(_exPanel);

        var root = _exPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);
        cols.AddChild(BuildOfferColumn("You offer", out _exMineList, out _exMineGold));
        cols.AddChild(BuildOfferColumn("Partner offers", out _exTheirsList, out _exTheirsGold));

        root.AddChild(new HSeparator());

        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 6);
        var goldLbl = HudStyle.Label(13); goldLbl.Text = "Gold:";
        goldRow.AddChild(goldLbl);
        _exGoldEdit = new LineEdit { PlaceholderText = "amount", CustomMinimumSize = new Vector2(110, 0) };
        goldRow.AddChild(_exGoldEdit);
        var addGoldBtn = new Button { Text = "Add gold", FocusMode = Control.FocusModeEnum.None };
        addGoldBtn.Pressed += OnAddGold;
        goldRow.AddChild(addGoldBtn);
        root.AddChild(goldRow);

        root.AddChild(UiTheme.SectionTitle("My backpack"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 200), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _exBagList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _exBagList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_exBagList);

        root.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        _exConfirmBtn = new Button { Text = "Confirm", FocusMode = Control.FocusModeEnum.None };
        _exConfirmBtn.Pressed += OnExchangeConfirm;
        footer.AddChild(_exConfirmBtn);
        _exCancelBtn = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        _exCancelBtn.Pressed += () => AbortExchange(local: true);
        footer.AddChild(_exCancelBtn);
        _exStatus = HudStyle.Label(13, HorizontalAlignment.Right);
        _exStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_exStatus);
        root.AddChild(footer);

        _exAskDialog = new ConfirmationDialog { Title = "Trade request" };
        _exAskDialog.GetOkButton().Text = "Accept";
        _exAskDialog.GetCancelButton().Text = "Decline";
        _exAskDialog.Confirmed += () => AnswerExchangeRequest(true);
        _exAskDialog.Canceled += () => AnswerExchangeRequest(false);
        _exLayer.AddChild(_exAskDialog);

        BuildExchangeWaitPanel();
        BuildExchangeAmountPrompt();
    }

    private void BuildExchangeWaitPanel()
    {
        _exWaitLayer = new CanvasLayer { Layer = 76, Visible = false };
        AddChild(_exWaitLayer);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        _exWaitLayer.AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 16, 13, 16, 13);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        _exWaitLabel = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        _exWaitLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _exWaitLabel.CustomMinimumSize = new Vector2(280, 0);
        root.AddChild(_exWaitLabel);

        var buttons = new HBoxContainer();
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += CancelExchangeRequest;
        buttons.AddChild(cancel);
    }

    private void BuildExchangeAmountPrompt()
    {
        _exAmountLayer = new CanvasLayer { Layer = 76, Visible = false };
        AddChild(_exAmountLayer);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _exAmountLayer.AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centre.MouseFilter = Control.MouseFilterEnum.Ignore;
        _exAmountLayer.AddChild(centre);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        centre.AddChild(box);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 14, 12, 14, 12);
        box.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        root.AddChild(head);
        _exAmountIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(38, 38),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(_exAmountIcon);
        var headText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headText.AddThemeConstantOverride("separation", -2);
        head.AddChild(headText);
        _exAmountName = UiTheme.Text("", 14, UiTheme.TextHi);
        headText.AddChild(_exAmountName);
        _exAmountHint = UiTheme.Text("", 11, UiTheme.TextLo);
        headText.AddChild(_exAmountHint);

        var amountRow = new HBoxContainer();
        amountRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(amountRow);
        var amountLbl = UiTheme.Text("Quantity", 13, UiTheme.TextLo);
        amountLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        amountRow.AddChild(amountLbl);
        _exAmountSpin = new SpinBox
        {
            MinValue = 1,
            MaxValue = Inventory.StackMax,
            Step = 1,
            Value = 1,
            CustomMinimumSize = new Vector2(110, 0),
        };
        _exAmountSpin.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
        amountRow.AddChild(_exAmountSpin);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.Pressed += CloseExchangeAmount;
        buttons.AddChild(cancel);
        var ok = new Button { Text = "Offer", FocusMode = Control.FocusModeEnum.None };
        ok.Pressed += ConfirmExchangeAmount;
        buttons.AddChild(ok);
    }

    private static Control BuildOfferColumn(string heading, out VBoxContainer list, out Label goldLbl)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(180, 0) };
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(UiTheme.SectionTitle(heading));
        list = new VBoxContainer { CustomMinimumSize = new Vector2(180, 150) };
        list.AddThemeConstantOverride("separation", 2);
        box.AddChild(list);
        goldLbl = UiTheme.Text("", 12, UiTheme.Gold);
        box.AddChild(goldLbl);
        return box;
    }

    private void TryTradeNearest()
    {
        if (_exShown) { SetExStatus("Already trading.", true); return; }
        if (_selfDead) return;
        if (TryBrowseNearestMerchant()) return;
        int bestId = -1; float bestD = TradeRange;
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            if (e.IsNpc || e.Dead || kv.Key == _myId) continue;
            float d = e.Body.Position.DistanceTo(_self.Position);
            if (d < bestD) { bestD = d; bestId = kv.Key; }
        }
        if (bestId < 0) { CombatNotice("No player nearby to trade with."); return; }
        BeginTradeRequest(bestId, _ents.TryGetValue(bestId, out var pe) ? pe.Name : "Player");
    }

    private void BeginTradeRequest(int charId, string name)
    {
        if (_ents.TryGetValue(charId, out var partner)
            && partner.Nation != Net.I.Nation
            && !Net.I.CurrentZoneAbility.CanTrade)
        {
            CombatNotice("The two nations cannot trade here.");
            return;
        }

        _exPartnerId = charId;
        _exPartnerName = name;
        Net.I.SendExchangeRequest(charId);
        ShowExchangeWait($"Waiting for {name} to accept the trade…");
    }

    private void ShowExchangeWait(string text)
    {
        _exWaitLabel.Text = text;
        _exWaitLayer.Visible = true;
        _exWaiting = true;
    }

    private void HideExchangeWait()
    {
        if (!_exWaiting) return;
        _exWaiting = false;
        _exWaitLayer.Visible = false;
    }

    private void CancelExchangeRequest()
    {
        if (!_exWaiting) return;
        HideExchangeWait();
        Net.I.SendExchangeCancel();
        ResetExchangeState();
        _exPartnerId = -1;
        CombatNotice("Trade request cancelled.");
    }

    private void OnExchangeRequest(int requesterCharId)
    {
        if (_exShown || _exWaiting) { Net.I.SendExchangeAgree(false); return; }
        _exPartnerId = requesterCharId;
        _exPartnerName = _ents.TryGetValue(requesterCharId, out var e) ? e.Name : "Player";
        _exRequestPending = true;
        _exAskDialog.DialogText = $"{_exPartnerName} wants to trade.\nAccept?";
        _exAskDialog.PopupCentered();
        CombatNotice($"{_exPartnerName} wants to trade. (Press T to trade)");
    }

    private void AnswerExchangeRequest(bool accept)
    {
        if (!_exRequestPending) return;
        _exRequestPending = false;
        Net.I.SendExchangeAgree(accept);
        if (accept) OpenExchange();
    }

    private void OnExchangeAgree(bool accepted)
    {
        HideExchangeWait();
        if (accepted) OpenExchange();
        else { CombatNotice($"{_exPartnerName} declined the trade."); ResetExchangeState(); }
    }

    private void OpenExchange()
    {
        ResetExchangeState();
        _exPanel.Title = $"Trade — {_exPartnerName}";
        _exConfirmBtn.Disabled = false;
        _exConfirmBtn.Text = "Confirm";
        SetExStatus("", false);
        RefreshExchangeBag();
        RefreshExchangeOffers();
        _exPanel.Visible = true;
        _exShown = true;
    }

    private void AbortExchange(bool local)
    {
        if (!_exShown) return;
        if (local) Net.I.SendExchangeCancel();
        RestoreMyOffer();
        CloseExchangeWindow();
    }

    private void CloseExchangeWindow()
    {
        HideItemTooltip();
        _exShown = false;
        _exPanel.Visible = false;
        ResetExchangeState();
    }

    private void ResetExchangeState()
    {
        _exMyOffer.Clear();
        _exTheirOffer.Clear();
        _exMyGoldOffer = 0;
        _exTheirGoldOffer = 0;
        _exConfirmedByMe = false;
        _exAddInFlight = false;
        CloseExchangeAmount();
        if (_exGoldEdit != null) _exGoldEdit.Text = "";
    }

    private void RefreshExchangeBag()
    {
        HideItemTooltip();
        foreach (var c in _exBagList.GetChildren()) c.QueueFree();
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty) continue;
            var slot = Inv[abs];
            int absSlot = abs;
            var def = ItemData.Get(slot.ItemId);
            string sub = def != null && def.Weight > 0 ? $"{def.Weight * ItemData.CarriedUnits(def, slot)} wt" : "";
            _exBagList.AddChild(BuildTradeRow(
                slot.ItemId, sub, "Offer",
                () => OfferSlot(absSlot), () => OfferSlot(absSlot), absSlot, slot, slot.Count));
        }
        if (_exBagList.GetChildCount() == 0)
        {
            var empty = HudStyle.Label(13); empty.Text = "Your bags are empty.";
            _exBagList.AddChild(empty);
        }
    }

    private void RefreshExchangeOffers()
    {
        RefreshOfferColumn(_exMineList, _exMyOffer);
        RefreshOfferColumn(_exTheirsList, _exTheirOffer);
        _exMineGold.Text = _exMyGoldOffer > 0 ? $"+ {_exMyGoldOffer:n0} gold" : "";
        _exTheirsGold.Text = _exTheirGoldOffer > 0 ? $"+ {_exTheirGoldOffer:n0} gold" : "";
    }

    private static void RefreshOfferColumn(VBoxContainer list, List<ExOfferItem> offer)
    {
        foreach (var c in list.GetChildren()) c.QueueFree();
        foreach (var o in offer)
        {
            var hb = new HBoxContainer();
            hb.AddThemeConstantOverride("separation", 6);
            hb.AddChild(new TextureRect
            {
                Texture = ItemData.Icon(o.ItemId),
                CustomMinimumSize = new Vector2(26, 26),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var name = UiTheme.Text("", 12, UiTheme.TextHi);
            name.Text = o.Count > 1 ? $"{ItemData.DisplayName(o.ItemId)} x{o.Count}" : ItemData.DisplayName(o.ItemId);
            hb.AddChild(name);
            list.AddChild(hb);
        }
    }

    private void OfferSlot(int absSlot)
    {
        if (_exConfirmedByMe) { SetExStatus("You already confirmed.", true); return; }
        if (_exAddInFlight) return;
        if (absSlot < 0 || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        if (_exMyOffer.Count >= 12) { SetExStatus("Offer is full (12 items).", true); return; }
        var slot = Inv[absSlot];
        int have = Mathf.Max(1, (int)slot.Count);

        if (have > 1 && (ItemData.Get(slot.ItemId)?.Countable ?? 0) != 0)
        {
            OpenExchangeAmount(absSlot, have);
            return;
        }

        OfferSlotAmount(absSlot, have);
    }

    private void OfferSlotAmount(int absSlot, int count)
    {
        if (absSlot < 0 || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        var slot = Inv[absSlot];
        count = Mathf.Clamp(count, 1, Mathf.Max(1, (int)slot.Count));

        _exPending = new PendingExAdd { IsGold = false, ItemId = slot.ItemId, SourceAbs = absSlot, Count = count, Dura = slot.Durability };
        _exAddInFlight = true;
        Net.I.SendExchangeAddItem((byte)(absSlot - GridStart), slot.ItemId, count);
    }

    private void OpenExchangeAmount(int absSlot, int max)
    {
        HideItemTooltip();
        _exAmountSlot = absSlot;
        _exAmountMax = max;
        int itemId = Inv[absSlot].ItemId;
        _exAmountIcon.Texture = ItemData.Icon(itemId);
        _exAmountName.Text = ItemData.DisplayName(itemId);
        _exAmountHint.Text = $"You have {max:n0}";
        _exAmountSpin.MaxValue = max;
        _exAmountSpin.Value = max;
        _exAmountSpin.GetLineEdit().Text = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _exAmountLayer.Visible = true;
        _exAmountShown = true;
        _exAmountSpin.GetLineEdit().GrabFocus();
        _exAmountSpin.GetLineEdit().SelectAll();
    }

    private void CloseExchangeAmount()
    {
        if (!_exAmountShown) return;
        _exAmountShown = false;
        _exAmountLayer.Visible = false;
        _exAmountSlot = -1;
    }

    private void ConfirmExchangeAmount()
    {
        if (!_exAmountShown || _exAmountSlot < 0) return;
        string typed = _exAmountSpin.GetLineEdit().Text.Trim();
        int value = int.TryParse(typed, out int parsed) ? parsed : (int)_exAmountSpin.Value;
        int slotAbs = _exAmountSlot;
        int count = Mathf.Clamp(value, 1, _exAmountMax);
        CloseExchangeAmount();
        OfferSlotAmount(slotAbs, count);
    }

    private void OnAddGold()
    {
        if (_exConfirmedByMe) { SetExStatus("You already confirmed.", true); return; }
        if (_exAddInFlight) return;
        if (!int.TryParse(_exGoldEdit.Text.Trim(), out int amount) || amount <= 0) { SetExStatus("Enter a gold amount.", true); return; }
        if (amount > Sheet.Gold) { SetExStatus("Not enough gold.", true); return; }

        _exPending = new PendingExAdd { IsGold = true, Count = amount };
        _exAddInFlight = true;
        Net.I.SendExchangeAddGold(amount);
    }

    private void OnExchangeAddResult(bool committed)
    {
        if (!_exAddInFlight) return;
        _exAddInFlight = false;
        if (!committed) { SetExStatus("That item can't be traded.", true); return; }

        if (_exPending.IsGold)
        {
            _exMyGoldOffer += _exPending.Count;
            Sheet.Spend(_exPending.Count);
            Net.I.RaiseGold(Sheet.Gold);
            _exGoldEdit.Text = "";
        }
        else
        {
            _exMyOffer.Add(new ExOfferItem { ItemId = _exPending.ItemId, Count = _exPending.Count, Dura = _exPending.Dura, SourceAbs = _exPending.SourceAbs });
            int abs = _exPending.SourceAbs;
            if (abs >= 0 && abs < Inv.Length)
            {
                int left = Inv[abs].Count - _exPending.Count;
                if (left > 0)
                {
                    var kept = Inv[abs];
                    kept.Count = (short)left;
                    Inv[abs] = kept;
                }
                else Inv[abs] = default;
                Net.I.MirrorInventorySlot(abs, Inv[abs]);
            }
            if (CharTabOpen()) RefreshInventoryUI();
            RefreshExchangeBag();
        }
        RefreshExchangeOffers();
        SetExStatus("", false);
    }

    private void OnExchangeOtherAdd(int itemId, int count, short dura)
    {
        if (!_exShown) return;
        if (itemId == Net.ExchangeGoldItem) _exTheirGoldOffer += count;
        else _exTheirOffer.Add(new ExOfferItem { ItemId = itemId, Count = count, Dura = dura });
        RefreshExchangeOffers();
    }

    private void OnExchangeConfirm()
    {
        if (!_exShown || _exConfirmedByMe) return;
        _exConfirmedByMe = true;
        _exConfirmBtn.Disabled = true;
        _exConfirmBtn.Text = "Confirmed";
        SetExStatus("Waiting for partner…", false);
        Net.I.SendExchangeDecide();
    }

    private void OnExchangeOtherDecide()
    {
        if (!_exShown) return;
        SetExStatus(_exConfirmedByMe ? "Finalising…" : "Partner confirmed — press Confirm.", false);
    }

    private void OnExchangeDone(bool ok, int money, List<(byte DstPos, ItemSlot Slot)> received)
    {
        if (!_exShown) return;
        if (!ok)
        {
            SetExStatus("Trade failed (bags full / overweight).", true);
            RestoreMyOffer();
            CloseExchangeWindow();
            return;
        }
        foreach (var (dstPos, slot) in received)
        {
            int abs = GridStart + dstPos;
            if (abs >= 0 && abs < Inv.Length)
            {
                Inv[abs] = slot;
                Net.I.MirrorInventorySlot(abs, slot);
            }
            Floaters?.Item(slot.ItemId, slot.Count);
        }
        int goldGained = money - Sheet.Gold;
        if (goldGained > 0) Floaters?.Gold(goldGained);
        Sheet.SetGold(money);
        if (CharTabOpen()) RefreshInventoryUI();
        CombatNotice($"Trade with {_exPartnerName} complete.");
        CloseExchangeWindow();
    }

    private void OnExchangeCancel()
    {
        if (_exWaiting)
        {
            HideExchangeWait();
            ResetExchangeState();
            CombatNotice($"{_exPartnerName} is not available to trade.");
            return;
        }
        if (_exRequestPending)
        {
            _exRequestPending = false;
            _exAskDialog.Hide();
            ResetExchangeState();
            CombatNotice("The trade request was withdrawn.");
            return;
        }
        if (!_exShown) return;
        CombatNotice("The trade was cancelled.");
        RestoreMyOffer();
        CloseExchangeWindow();
    }

    private void RestoreMyOffer()
    {
        foreach (var o in _exMyOffer)
        {
            int abs = o.SourceAbs;
            bool sameStack = abs >= GridStart && abs < Inv.Length
                             && !Inv[abs].IsEmpty && Inv[abs].ItemId == o.ItemId;
            if (!sameStack && (abs < GridStart || abs >= Inv.Length || !Inv[abs].IsEmpty))
                abs = Inv.FirstFreeGridSlot();
            if (abs < 0) continue;

            if (sameStack)
            {
                var merged = Inv[abs];
                merged.Count = (short)Mathf.Min(Inventory.StackMax, merged.Count + o.Count);
                Inv[abs] = merged;
            }
            else
                Inv[abs] = new ItemSlot { ItemId = o.ItemId, Count = (short)o.Count, Durability = o.Dura };
            Net.I.MirrorInventorySlot(abs, Inv[abs]);
        }
        if (_exMyGoldOffer > 0) { Sheet.Receive(_exMyGoldOffer); Net.I.RaiseGold(Sheet.Gold); }
        _exMyOffer.Clear();
        _exMyGoldOffer = 0;
        if (CharTabOpen()) RefreshInventoryUI();
    }

    private void SetExStatus(string text, bool warn)
    {
        _exStatus.Text = text;
        _exStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
