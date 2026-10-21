using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class VipWarehouseFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly VipWarehouseSession vipWarehouse;
    private readonly IVipWarehouseRequests requests;
    private readonly WarehouseFeature warehouse;
    private readonly IInteractionDialogs interactionDialogs;

    public VipWarehouseFeature(IWorldContext context, SelfSession self, VipWarehouseSession vipWarehouse, IVipWarehouseRequests requests, WarehouseFeature warehouse, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.self = self;
        this.vipWarehouse = vipWarehouse;
        this.requests = requests;
        this.warehouse = warehouse;
        this.interactionDialogs = interactionDialogs;
    }

    internal const int Slots = VipWarehouseWire.Slots;
    private const int Columns = 6;
    private const float CellSize = 44f;
    private const string Hint = "Right-click to store or take out · drag to place";
    private const int PinTriesAllowed = 3;
    private const int ExpiredText = 1655;
    private const int KeyFailedText = 1656;
    private const int NotAKeyText = 1657;
    private const int ChoosePinText = 1658;
    private const int ConfirmPinText = 1659;
    private const int PinSetText = 1661;
    private const int PinSetFailedText = 1662;
    private const int PinClearedText = 1663;
    private const int PinClearFailedText = 1664;
    private const int PinChangedText = 1665;
    private const int PinChangeFailedText = 1666;
    private const int EnterPinText = 1667;
    private const int WrongPinText = 1668;
    private const int PinLockedText = 1669;
    private const int PinDigitsText = 1670;
    private const int PinMismatchText = 1671;
    private const int RentalCapText = 1682;
    private const int KeyStaysText = 1651;
    private const int ExtendAskText = 1653;
    private const int AddAskText = 1654;
    private const int ExpiredUseText = 1673;
    private const int NoKeyUseText = 1674;
    private const int SetPinHintText = 1675;
    private const int ChangePinHintText = 1676;
    private const int VaultKeyLook = 800_442_000;
    private const double ClockSeconds = 1.0;

    private static readonly HashSet<int> VaultKeys = new() { 800_442_000, 931_695_000, 810_442_000, 998_019_000 };

    internal static bool IsVaultKey(int itemId) => VaultKeys.Contains(itemId);

    internal ItemSlot[] StoredItems { get; } = new ItemSlot[Slots];
    private uint expiresAt;
    private bool open;
    private bool pinSet;
    private ItemSlotView keyCell = null!;
    private Button setPinBtn = null!;
    private Button clearPinBtn = null!;
    private Godot.Timer clock = null!;
    private Notice? keyAsk;
    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private bool shown;

    public bool IsOpen => shown;

    internal IClosableWindow AmountPromptPanel => amountPrompt;

    private readonly ItemSlotView[] cells = new ItemSlotView[Slots];
    private BagCompanion? companion;
    private Label expiryLbl = null!;
    private Label slotsNote = null!;
    internal StatusLabel Status { get; private set; } = null!;
    private QuantityPrompt amountPrompt = null!;
    private AcceptDialog pinDlg = null!;
    private LineEdit pinEdit = null!;
    private Label pinPrompt = null!;
    private VipWarehouseSubOpcode pinSub;
    private string? pinFirst;

    private struct WhPending { public VipWarehouseSubOpcode Op; public bool Merge; public int InvAbs; public int VipIdx; public int VipTo; public int Count; }

    private WhPending pending;
    private bool inFlight;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        warehouse.BlockTransfersWhile(() => inFlight);
        context.Inventory.Refreshed += OnInventoryRefreshed;
        vipWarehouse.VipWarehouseContentsEvent += OnContents;
        vipWarehouse.VipWarehouseResultEvent += OnResult;
        vipWarehouse.VipWarehouseExpiredEvent += OnExpired;
        vipWarehouse.VipWarehousePinPromptEvent += OnPinPrompt;
        vipWarehouse.VipWarehousePinResultEvent += OnPinResult;
        vipWarehouse.VipWarehousePinRejectedEvent += OnPinRejected;
        vipWarehouse.VipVaultRenewedEvent += OnVaultRenewed;
        vipWarehouse.VipVaultKeyExpiredEvent += OnVaultKeyExpired;
    }

    public void Dispose()
    {
        context.Inventory.Refreshed -= OnInventoryRefreshed;
        vipWarehouse.VipWarehouseContentsEvent -= OnContents;
        vipWarehouse.VipWarehouseResultEvent -= OnResult;
        vipWarehouse.VipWarehouseExpiredEvent -= OnExpired;
        vipWarehouse.VipWarehousePinPromptEvent -= OnPinPrompt;
        vipWarehouse.VipWarehousePinResultEvent -= OnPinResult;
        vipWarehouse.VipWarehousePinRejectedEvent -= OnPinRejected;
        vipWarehouse.VipVaultRenewedEvent -= OnVaultRenewed;
        vipWarehouse.VipVaultKeyExpiredEvent -= OnVaultKeyExpired;
    }

    private void OnVaultRenewed(VipVaultKeyReply reply)
    {
        string? refusal = reply.Result switch
        {
            VipWarehouseResult.Rejected => ItemData.Text(KeyFailedText, "You have failed to use [VIP]Storage Key."),
            VipWarehouseResult.NotAVaultKey => ItemData.Text(NotAKeyText, "It is not [VIP]Storage Key."),
            VipWarehouseResult.RentalAtCap => ItemData.Text(RentalCapText, "You cannot extent the period if the storage has more than 60 days."),
            _ => null,
        };
        if (refusal != null) { context.Log.Notice(refusal); return; }
        if (reply.Result != VipWarehouseResult.Succeeded || reply.Storage != VipVaultContents.OwnStorage) return;
        expiresAt = reply.ExpiresAt;
        open = true;
        if (shown) Refresh();
        else context.Log.Notice(UiKit.ExpirationText((int)reply.ExpiresAt));
    }

    private void OnVaultKeyExpired()
    {
        open = false;
        Close();
        context.Log.Notice(ItemData.Text(ExpiredText, "VIP Storage has been expired."));
    }

    internal void AskKey(int abs)
    {
        var key = self.Inventory[abs];
        if (!IsVaultKey(key.ItemId) || (keyAsk != null && GodotObject.IsInstanceValid(keyAsk))) return;
        string ask = TextTemplate.Fill(open
            ? ItemData.Text(ExtendAskText, "Do you want to extend \n%d VIP Storage?")
            : ItemData.Text(AddAskText, "Do you want to add \n%d VIP Storage?"), VipVaultContents.OwnStorage + 1);
        string note = ItemData.Text(KeyStaysText, "You cannot remove used [VIP]Storage Key.");
        keyAsk = Notice.Confirm(context.Root, ask + "\n" + note, "Yes", "No", () =>
        {
            if (self.Inventory[abs].ItemId != key.ItemId) return;
            requests.VipVaultUseKey(key.ItemId, (byte)(abs - Inventory.GridStart));
        }, title: "VIP Vault");
    }

    private string LockedText() => expiresAt > 0
    ? ItemData.Text(ExpiredUseText, "You cannot use it due to expiration.")
    : ItemData.Text(NoKeyUseText, "You cannot use it without [VIP]Storage Key.");

    private void RefuseLockedVault() => Status.Status(LockedText(), bad: true);

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("vipwarehouse", "VIP Vault") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        amountPrompt = new QuantityPrompt(UiLayers.Prompts);
        context.Root.AddChild(amountPrompt);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(ServiceKit.Section("Stored items", UiIcons.Get("system/package"), out slotsNote));

        var well = ServiceKit.Well();
        root.AddChild(well);
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        well.AddChild(grid);
        for (int i = 0; i < Slots; i++)
        {
            var cell = new ItemSlotView(CellSize) { Index = i, Veiled = true, PicksAmount = true };
            cell.RightClicked += c => AskWithdraw(c.Index, -1);
            cell.Hovered += c => context.Tooltips.Show(-1, c.Item);
            cell.Unhovered += _ => context.Tooltips.Hide();
            cell.DragOut = c => open ? (Variant)new Godot.Collections.Dictionary { { "companionFrom", c.Index } } : default;
            cell.CanDrop = (c, data) => CanDropOnVault(c.Index, data);
            cell.Dropped = (c, data) => DropOnVault(c.Index, data);
            cells[i] = cell;
            grid.AddChild(cell);
        }

        var expiryRow = new HBoxContainer();
        expiryRow.AddThemeConstantOverride("separation", 8);
        keyCell = new ItemSlotView(CellSize);
        keyCell.Hovered += c => context.Tooltips.Show(-1, c.Item);
        keyCell.Unhovered += _ => context.Tooltips.Hide();
        expiryRow.AddChild(keyCell);
        var el = UiTheme.Text("Expires in", 12, UiTheme.TextLo);
        el.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        el.VerticalAlignment = VerticalAlignment.Center;
        expiryRow.AddChild(el);
        expiryLbl = UiTheme.Text("—", 13, UiTheme.Gold, HorizontalAlignment.Right);
        expiryLbl.VerticalAlignment = VerticalAlignment.Center;
        expiryRow.AddChild(expiryLbl);
        root.AddChild(expiryRow);

        Status = new StatusLabel { Hint = Hint };
        root.AddChild(Status);

        var footer = new FooterBand();
        setPinBtn = UiTheme.SmallButton("Set PIN", "");
        setPinBtn.Pressed += () => ShowPinDialog(pinSet ? VipWarehouseSubOpcode.ChangePassword : VipWarehouseSubOpcode.SetPassword,
            ItemData.Text(ChoosePinText, "Please select a 4 digit password."));
        clearPinBtn = UiTheme.SmallButton("Clear PIN", "Remove the vault PIN");
        clearPinBtn.Pressed += () => requests.CancelPin();
        footer.Right.AddChild(setPinBtn);
        footer.Right.AddChild(clearPinBtn);
        root.AddChild(footer);

        clock = new Godot.Timer { WaitTime = ClockSeconds };
        clock.Timeout += TickClock;
        layer.AddChild(clock);

        pinDlg = new AcceptDialog { Title = "VIP Vault PIN", Unresizable = true };
        pinDlg.GetOkButton().Text = "Confirm";
        var pinBox = new VBoxContainer();
        pinBox.AddThemeConstantOverride("separation", 8);
        pinPrompt = UiTheme.Text("Enter your 4-digit PIN:", 13, UiTheme.TextHi);
        pinBox.AddChild(pinPrompt);
        pinEdit = new LineEdit
        {
            PlaceholderText = "1234",
            MaxLength = 4,
            Secret = true,
            CustomMinimumSize = new Vector2(160, 0),
        };
        pinEdit.TextSubmitted += _ => SubmitPin();
        pinBox.AddChild(pinEdit);
        pinDlg.AddChild(pinBox);
        pinDlg.Confirmed += SubmitPin;
        layer.AddChild(pinDlg);
    }

    internal void OpenVaultFromBag()
    {
        if (shown || !VipVaultAccess.ButtonOpens(context.Zone)) return;
        Toggle();
    }

    internal void OpenVaultByCommand()
    {
        if (shown || !VipVaultAccess.CommandOpens(context.Zone)) return;
        Toggle();
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        requests.Open();
    }

    private void Open()
    {
        Status.ResetStatus();
        Panel.Visible = true;
        shown = true;
        companion ??= new BagCompanion(TakeFromBag, VipBagFit, _ => "", Close, WithdrawInto);
        context.BagCompanions.Attach(companion);
        clock.Start();
        Refresh();
    }

    internal bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        if (IsVaultKey(self.Inventory[abs].ItemId)) AskKey(abs);
        else DepositSlot(abs, -1);
        return true;
    }

    private bool WithdrawInto(int vipIdx, int bagAbs, int? amount)
    {
        AskWithdraw(vipIdx, bagAbs, amount);
        return true;
    }

    private BagFit VipBagFit(int abs) =>
        ItemStacks.UseRefusal(ItemUse.VipVault, self.Inventory[abs]) == ItemUseRules.Allowed ? BagFit.Normal : BagFit.Unfit;

    public void Close()
    {
        amountPrompt.Close();
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        clock.Stop();
        context.Tooltips.Hide();
        if (companion != null) context.BagCompanions.Detach(companion);
        context.Plugins.VipVault();
    }

    internal void OnContents(VipVaultContents contents)
    {
        inFlight = false;
        expiresAt = contents.ExpiresAt;
        open = contents.Open;
        pinSet = contents.PinSet;
        for (int i = 0; i < Slots && i < contents.Slots.Length; i++) StoredItems[i] = contents.Slots[i];
        if (!shown) Open();
        else Refresh();
    }

    private void OnExpired()
    {
        string text = ItemData.Text(ExpiredText, "VIP Storage has been expired.");
        context.Log.Notice(text);
        if (shown) { Status.Status(text, bad: true); Refresh(); }
    }

    private void OnInventoryRefreshed() { if (shown) Refresh(); }

    internal void Refresh()
    {
        for (int i = 0; i < Slots; i++)
        {
            cells[i].Set(StoredItems[i]);
            cells[i].Locked = !open;
        }
        keyCell.Set(expiresAt > 0
            ? new ItemSlot { ItemId = VaultKeyLook, Count = 1, ExpiresAt = (int)expiresAt }
            : default);
        keyCell.Locked = !open && expiresAt > 0;
        expiryLbl.Text = ExpiryText();
        int used = StoredItems.Count(s => !s.IsEmpty);
        slotsNote.Text = $"Slots used {used} / {Slots}";
        slotsNote.AddThemeColorOverride("font_color", used >= Slots ? UiTheme.Bad : UiTheme.TextDim);
        setPinBtn.Text = pinSet ? "Change PIN" : "Set PIN";
        setPinBtn.TooltipText = pinSet
            ? ItemData.Text(ChangePinHintText, "To change and cancel the password, please click the blue lock.")
            : ItemData.Text(SetPinHintText, "Please click the lock to set your VIP vault password.");
        clearPinBtn.Visible = pinSet;
        if (!open) RefuseLockedVault();
        context.BagCompanions.RefreshFit();
        context.Plugins.VipVault();
    }

    private void TickClock()
    {
        if (!shown) return;
        if (open && SecondsLeft() <= 0)
        {
            open = false;
            Refresh();
            return;
        }
        expiryLbl.Text = ExpiryText();
    }

    private long SecondsLeft() => expiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    internal string ExpiryText()
    {
        if (expiresAt == 0) return "—";
        long seconds = SecondsLeft();
        if (!open || seconds <= 0) return "expired";
        var left = TimeSpan.FromSeconds(seconds);
        if (left.Days >= 1) return $"{left.Days}d {left.Hours}h";
        if (left.Hours >= 1) return $"{left.Hours}h {left.Minutes}m";
        if (left.Minutes >= 1) return $"{left.Minutes}m {left.Seconds}s";
        return $"{left.Seconds}s";
    }

    private bool FitsInVault(int vipIdx, ItemSlot source, int count) =>
        StorageDestination.Fits(StoredItems[vipIdx], source, count, UiKit.IsStackable(source.ItemId));

    private int VaultDestination(ItemSlot source, int count, out bool merge)
    {
        int vipIdx = StorageDestination.Find(StoredItems, source, count, UiKit.IsStackable(source.ItemId));
        merge = vipIdx >= 0 && !StoredItems[vipIdx].IsEmpty;
        return vipIdx;
    }

    private bool CanDropOnVault(int vipIdx, Variant data)
    {
        if (!open || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("invFrom")) return self.Inventory.InMainBag(d["invFrom"].AsInt32());
        return d.ContainsKey("companionFrom") && d["companionFrom"].AsInt32() != vipIdx;
    }

    private void DropOnVault(int vipIdx, Variant data)
    {
        var d = data.AsGodotDictionary();
        int? amount = DragLayer.ChosenAmount(d);
        if (d.ContainsKey("invFrom"))
        {
            int abs = d["invFrom"].AsInt32();
            if (IsVaultKey(self.Inventory[abs].ItemId)) AskKey(abs);
            else DepositSlot(abs, vipIdx, amount);
        }
        else if (d.ContainsKey("companionFrom")) MoveInsideVault(d["companionFrom"].AsInt32(), vipIdx, amount);
    }

    private void DepositSlot(int abs, int target, int? amount = null)
    {
        if (warehouse.StorageTransferBusy || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty) return;
        if (!open) { RefuseLockedVault(); return; }
        var slot = self.Inventory[abs];
        if (ItemStacks.UseRefusal(ItemUse.VipVault, slot) is not ItemUseRules.Allowed and var vaultRefusal)
        {
            Status.Status(ItemData.Text(vaultRefusal, "This item is non-storable"), bad: true);
            return;
        }
        if (amount == null && UiKit.AsksStackAmount(slot))
            amountPrompt.Open(ItemIcons.Get(slot.ItemId), $"Store {ItemData.DisplayName(slot.ItemId)}",
                $"You carry {slot.Count:n0}", slot.Count, slot.Count, n => { if (shown && self.Inventory[abs].Equals(slot)) DepositCount(abs, (int)n, target); }, "Store");
        else
            DepositCount(abs, amount ?? Mathf.Max(1, (int)slot.Count), target);
    }

    private void DepositCount(int abs, int count, int target)
    {
        if (warehouse.StorageTransferBusy || !self.Inventory.InMainBag(abs) || self.Inventory[abs].IsEmpty || count <= 0) return;
        var slot = self.Inventory[abs];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int vipIdx;
        bool merge;
        if (target >= 0)
        {
            if (!FitsInVault(target, slot, count)) { Status.Status("That slot is taken.", bad: true); return; }
            vipIdx = target;
            merge = !StoredItems[target].IsEmpty;
        }
        else
        {
            vipIdx = VaultDestination(slot, count, out merge);
            if (vipIdx < 0) { Status.Status("The vault is full.", bad: true); return; }
        }
        pending = new WhPending { Op = VipWarehouseSubOpcode.Input, Merge = merge, InvAbs = abs, VipIdx = vipIdx, Count = count };
        inFlight = true;
        requests.Input(slot.ItemId, (byte)(abs - Inventory.GridStart), (byte)vipIdx, count);
    }

    internal void AskWithdraw(int vipIdx, int target, int? amount = null)
    {
        if (warehouse.StorageTransferBusy || vipIdx < 0 || vipIdx >= Slots || StoredItems[vipIdx].IsEmpty) return;
        if (!open) { RefuseLockedVault(); return; }
        var slot = StoredItems[vipIdx];
        if (amount == null && UiKit.AsksStackAmount(slot))
            amountPrompt.Open(ItemIcons.Get(slot.ItemId), $"Take out {ItemData.DisplayName(slot.ItemId)}",
                $"Stored {slot.Count:n0}", slot.Count, slot.Count, n => { if (shown && StoredItems[vipIdx].Equals(slot)) WithdrawCount(vipIdx, (int)n, target); }, "Take out");
        else
            WithdrawCount(vipIdx, amount ?? Mathf.Max(1, (int)slot.Count), target);
    }

    private void WithdrawCount(int vipIdx, int count, int target)
    {
        if (warehouse.StorageTransferBusy || vipIdx < 0 || vipIdx >= Slots || StoredItems[vipIdx].IsEmpty || count <= 0) return;
        var slot = StoredItems[vipIdx];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int dest;
        bool merge;
        if (target >= 0)
        {
            if (!warehouse.FitsInBag(target, slot, count)) { Status.Status("That slot is taken.", bad: true); return; }
            dest = target;
            merge = !self.Inventory[target].IsEmpty;
        }
        else
        {
            dest = warehouse.BagDestination(slot, count, out merge);
            if (dest < 0) { Status.Status("Your bags are full.", bad: true); return; }
        }
        pending = new WhPending { Op = VipWarehouseSubOpcode.Output, Merge = merge, InvAbs = dest, VipIdx = vipIdx, Count = count };
        inFlight = true;
        requests.Output(slot.ItemId, (byte)vipIdx, (byte)(dest - Inventory.GridStart), count);
    }

    private void MoveInsideVault(int from, int to, int? amount)
    {
        if (warehouse.StorageTransferBusy || from == to || from < 0 || to < 0 || from >= Slots || to >= Slots || StoredItems[from].IsEmpty) return;
        if (!open) { RefuseLockedVault(); return; }
        if (ItemStacks.IsPartOfStack(amount, StoredItems[from])) { Status.Status(GameText.StorageWholeStack, bad: true); return; }
        if (!StoredItems[to].IsEmpty) { Status.Status("That slot is taken.", bad: true); return; }
        pending = new WhPending { Op = VipWarehouseSubOpcode.Store, VipIdx = from, VipTo = to, Count = StoredItems[from].Count };
        inFlight = true;
        requests.MoveInside(StoredItems[from].ItemId, (byte)from, (byte)to);
    }

    private void OnResult(VipWarehouseSubOpcode op, VipWarehouseResult result)
    {
        if (!inFlight) return;
        inFlight = false;
        if (result == VipWarehouseResult.Expired)
        {
            open = false;
            if (shown) Refresh();
            return;
        }
        if (result != VipWarehouseResult.Succeeded)
        {
            Status.Status(op == VipWarehouseSubOpcode.Output ? ItemData.Text(WarehouseFeature.NoRoomText, "Your bags are full.") : "Transfer failed.", bad: true);
            if (shown) Refresh();
            return;
        }

        var p = pending;
        if (p.Op == VipWarehouseSubOpcode.Store)
        {
            int itemId = StoredItems[p.VipIdx].ItemId;
            StoredItems[p.VipTo] = StoredItems[p.VipIdx];
            StoredItems[p.VipIdx] = default;
            Status.Status($"Moved {ItemData.DisplayName(itemId)}.", bad: false);
        }
        else if (p.Op == VipWarehouseSubOpcode.Input)
        {
            var bag = self.Inventory[p.InvAbs];
            int itemId = bag.ItemId;
            ItemStacks.MoveStack(ref bag, ref StoredItems[p.VipIdx], p.Count, p.Merge);
            self.SetInventorySlot(p.InvAbs, bag);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            Status.Status($"Stored {WarehouseFeature.ItemLine(itemId, p.Count)}.", bad: false);
        }
        else
        {
            var bag = self.Inventory[p.InvAbs];
            int itemId = StoredItems[p.VipIdx].ItemId;
            ItemStacks.MoveStack(ref StoredItems[p.VipIdx], ref bag, p.Count, p.Merge);
            self.SetInventorySlot(p.InvAbs, bag);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            Status.Status($"Took out {WarehouseFeature.ItemLine(itemId, p.Count)}.", bad: false);
        }
        if (shown) Refresh();
    }

    private void OnPinPrompt()
    {
        ShowPinDialog(VipWarehouseSubOpcode.EnterPassword, ItemData.Text(EnterPinText, "Please insert 4 digits for Password."));
    }

    private void ShowPinDialog(VipWarehouseSubOpcode sub, string prompt)
    {
        pinSub = sub;
        pinFirst = null;
        RepromptPin(prompt);
    }

    private void RepromptPin(string prompt)
    {
        pinPrompt.Text = prompt;
        pinEdit.Text = "";
        pinDlg.PopupCentered();
        pinEdit.GrabFocus();
    }

    private void SubmitPin()
    {
        string pin = pinEdit.Text.Trim();
        if (pin.Length != 4 || !IsAllDigits(pin))
        {
            RepromptPin(ItemData.Text(PinDigitsText, "You have to insert 4 digits Password."));
            return;
        }
        if (pinSub is VipWarehouseSubOpcode.SetPassword or VipWarehouseSubOpcode.ChangePassword)
        {
            if (pinFirst == null)
            {
                pinFirst = pin;
                RepromptPin(ItemData.Text(ConfirmPinText, "Please reenter your password to confirm."));
                return;
            }
            if (pinFirst != pin)
            {
                pinFirst = null;
                RepromptPin(ItemData.Text(PinMismatchText, "The password is not same as initial one."));
                return;
            }
        }
        switch (pinSub)
        {
            case VipWarehouseSubOpcode.SetPassword:   requests.SetPin(pin); break;
            case VipWarehouseSubOpcode.ChangePassword: requests.ChangePin(pin); break;
            default:                   requests.EnterPin(pin); break;
        }
        pinFirst = null;
        pinEdit.Text = "";
        pinDlg.Hide();
    }

    private void OnPinRejected(int failures)
    {
        if (failures < PinTriesAllowed)
        {
            ShowPinDialog(VipWarehouseSubOpcode.EnterPassword,
                TextTemplate.Fill(ItemData.Text(WrongPinText, "You have inserted wrong Password. (%d times)"), failures));
            return;
        }
        pinDlg.Hide();
        Close();
        context.Log.Add(ItemData.Text(PinLockedText,
            "You have inserted wrong Password more than 3 times."), CombatLogKind.Status, UiKit.WhRefusalTint);
        context.Log.Notice("The VIP vault stays locked until you log in again.");
    }

    private void OnPinResult(VipWarehouseSubOpcode sub, bool ok)
    {
        switch (sub)
        {
            case VipWarehouseSubOpcode.EnterPassword:
                if (!ok) ShowPinDialog(VipWarehouseSubOpcode.EnterPassword, ItemData.Text(EnterPinText, "Please insert 4 digits for Password."));
                return;
            case VipWarehouseSubOpcode.SetPassword:
                context.Log.Notice(ok ? ItemData.Text(PinSetText, "Password has set.\nVIP Storage will be closed.")
                    : ItemData.Text(PinSetFailedText, "Password setting has failed."));
                break;
            case VipWarehouseSubOpcode.ChangePassword:
                context.Log.Notice(ok ? ItemData.Text(PinChangedText, "Password change has set.\nVIP Storage will be closed.")
                    : ItemData.Text(PinChangeFailedText, "Password change has failed."));
                break;
            case VipWarehouseSubOpcode.CancelPassword:
                context.Log.Notice(ok ? ItemData.Text(PinClearedText, "Password cancellation has set.")
                    : ItemData.Text(PinClearFailedText, "Password cancellation has failed."));
                if (ok) { pinSet = false; if (shown) Refresh(); }
                return;
        }
        if (!ok) return;
        pinSet = true;
        Close();
    }

    private static bool IsAllDigits(string s)
    {
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }
}
