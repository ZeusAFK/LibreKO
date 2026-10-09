using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int WhSlots = WarehouseView.Slots;
    private const int WhPageSize = WarehouseView.UiPageSize;
    private const int WhColumns = 8;
    private const float WhCellSize = 44f;
    private const string WhHint = "Right-click to store or take out · drag to place";
    private const string WhHitBadge = "•";

    private readonly ItemSlot[] _warehouse = new ItemSlot[WhSlots];
    private int _whMoney;
    private int _whPage;

    private CanvasLayer _whLayer = null!;
    private HudWindow _whPanel = null!;
    private bool _whShown;
    private readonly ItemSlotView[] _whCells = new ItemSlotView[WhPageSize];
    private LineEdit _whSearch = null!;
    private ServiceTabs _whPageTabs = null!;
    private Label _whSlotsNote = null!;
    private StatusLabel _whStatus = null!;
    private MoneyPlaque _whStored = null!;
    private Button _whDepositBtn = null!, _whWithdrawBtn = null!;
    private QuantityPrompt _whAmount = null!;
    private HashSet<int> _whHits = new();
    private BagCompanion? _whCompanion;

    private const byte WhOpInput = 2;
    private const byte WhOpOutput = 3;
    private const byte WhOpMove = 4;

    private struct WhPending { public byte Op; public bool Gold; public bool Merge; public int InvAbs; public int WhIdx; public int WhTo; public int Count; }
    private WhPending _whPending;
    private bool _whInFlight;

    private bool StorageTransferBusy => _whInFlight || _vipWhInFlight || _clanWhInFlight || _moveInFlight || _moveQueue.Count > 0;

    private void WarehouseInit()
    {
        BuildWarehousePanel();
        Net.I.WarehouseNpcEvent += OnWarehouseOpen;
        Net.I.WarehouseContentsEvent += OnWarehouseContents;
        Net.I.WarehouseResultEvent += OnWarehouseResult;
        Net.I.GoldChangeEvent += OnWarehouseGold;
    }

    private void WarehouseDispose()
    {
        Net.I.WarehouseNpcEvent -= OnWarehouseOpen;
        Net.I.WarehouseContentsEvent -= OnWarehouseContents;
        Net.I.WarehouseResultEvent -= OnWarehouseResult;
        Net.I.GoldChangeEvent -= OnWarehouseGold;
    }

    private void OnWarehouseGold(int g) { if (_whShown) RefreshWarehouseGold(); }

    private void BuildWarehousePanel()
    {
        _whLayer = new CanvasLayer { Layer = 74 };
        AddChild(_whLayer);

        _whPanel = new HudWindow("warehouse", "Warehouse") { Visible = false };
        _whPanel.Closed += CloseWarehouse;
        _whLayer.AddChild(_whPanel);

        _whAmount = new QuantityPrompt(76);
        AddChild(_whAmount);

        var root = _whPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        _whSearch = new LineEdit { PlaceholderText = "Search stored items", ClearButtonEnabled = true };
        _whSearch.AddThemeFontSizeOverride("font_size", 13);
        _whSearch.TextChanged += _ => RefreshWarehouse();
        root.AddChild(_whSearch);

        root.AddChild(ServiceKit.Section("Stored items", UiIcons.Get("system/package"), out _whSlotsNote));

        var well = ServiceKit.Well();
        root.AddChild(well);
        var grid = new GridContainer { Columns = WhColumns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        well.AddChild(grid);
        for (int i = 0; i < WhPageSize; i++)
        {
            var cell = new ItemSlotView(WhCellSize) { Index = i };
            cell.RightClicked += c => WithdrawSlot(WhAbs(c.Index));
            cell.Hovered += c => ShowItemTooltip(-1, c.Item);
            cell.Unhovered += _ => HideItemTooltip();
            cell.Wheeled += (_, step) => TurnWhPage(step);
            cell.DragOut = c => new Godot.Collections.Dictionary { { "companionFrom", WhAbs(c.Index) } };
            cell.CanDrop = (c, data) => CanDropOnWarehouse(WhAbs(c.Index), data);
            cell.Dropped = (c, data) => DropOnWarehouse(WhAbs(c.Index), data);
            _whCells[i] = cell;
            grid.AddChild(cell);
        }

        _whPageTabs = new ServiceTabs();
        var pages = new List<string>();
        for (int p = 0; p < WarehouseView.UiPages; p++) pages.Add($"{p + 1}");
        _whPageTabs.SetTabs(pages, 0);
        _whPageTabs.Selected += page =>
        {
            _whPage = page;
            RefreshWarehouse();
        };
        root.AddChild(_whPageTabs);

        _whStatus = new StatusLabel { Hint = WhHint };
        root.AddChild(_whStatus);

        var footer = new FooterBand();
        _whStored = new MoneyPlaque("Stored gold");
        footer.Left.AddChild(_whStored);
        _whDepositBtn = UiTheme.SmallButton("Deposit", "Put gold into the warehouse");
        _whDepositBtn.Pressed += () => AskGoldTransfer(deposit: true);
        _whWithdrawBtn = UiTheme.SmallButton("Withdraw", "Take gold out of the warehouse");
        _whWithdrawBtn.Pressed += () => AskGoldTransfer(deposit: false);
        footer.Right.AddChild(_whDepositBtn);
        footer.Right.AddChild(_whWithdrawBtn);
        root.AddChild(footer);
    }

    private int WhAbs(int cell) => _whPage * WhPageSize + cell;

    private void OnWarehouseOpen()
    {
        ShowNpcServiceChoice(
            "Shall we start the day at our inn and end it at our inn as well?",
            ("Use storage", OpenWarehouseStorage),
            ("Use [VIP] storage", ToggleVipWarehouse),
            ("Create a Clan", CreateClanFromInn),
            ("Seal / Cancel (anti-theft)", () => OpenSealWindow(SealMode.Secret)),
            ("Seal / Cancel", () => OpenSealWindow(SealMode.Bind)));
    }

    private void OpenWarehouseStorage()
    {
        CloseNpcDialog();
        _whPage = 0;
        _whPageTabs.Select(0, notify: false);
        _whSearch.Text = "";
        _whStatus.ResetStatus();
        _whPanel.Visible = true;
        _whShown = true;
        if (!_whInFlight)
        {
            System.Array.Clear(_warehouse, 0, _warehouse.Length);
            _whMoney = 0;
        }
        _whCompanion ??= new BagCompanion(WhTakeFromBag, WhBagFit, _ => "", CloseWarehouse, WithdrawInto);
        AttachBagCompanion(_whCompanion);
        Net.I.SendWarehouseOpen();
        RefreshWarehouse();
    }

    private void CloseWarehouse()
    {
        _whAmount.Close();
        if (!_whShown) return;
        _whShown = false;
        _whPanel.Visible = false;
        HideItemTooltip();
        if (_whCompanion != null) DetachBagCompanion(_whCompanion);
    }

    private void OnWarehouseContents(int money, ItemSlot[] slots)
    {
        _whMoney = money;
        for (int i = 0; i < WhSlots && i < slots.Length; i++) _warehouse[i] = slots[i];
        if (_whShown) RefreshWarehouse();
    }

    private void TurnWhPage(int step)
    {
        int next = Mathf.Clamp(_whPage + step, 0, WarehouseView.UiPages - 1);
        if (next == _whPage) return;
        _whPage = next;
        _whPageTabs.Select(next, notify: false);
        RefreshWarehouse();
    }

    private void RefreshWarehouse()
    {
        var ids = _warehouse.Select(s => s.ItemId).ToArray();
        _whHits = WarehouseView.Matches(ids, _whSearch.Text, ItemData.DisplayName);
        bool searching = !new ItemQuery(_whSearch.Text).IsEmpty;
        var pages = WarehouseView.PagesWithHits(_whHits);
        for (int p = 0; p < pages.Length; p++) _whPageTabs.SetBadge(p, searching && pages[p] ? WhHitBadge : "");

        for (int i = 0; i < WhPageSize; i++)
        {
            int abs = WhAbs(i);
            _whCells[i].Set(_warehouse[abs]);
            _whCells[i].Look = !searching || _warehouse[abs].IsEmpty ? SlotLook.Normal
                : _whHits.Contains(abs) ? SlotLook.Selected : SlotLook.Dimmed;
        }

        int used = _warehouse.Count(s => !s.IsEmpty);
        _whSlotsNote.Text = $"Slots used {used} / {WhSlots}";
        _whSlotsNote.AddThemeColorOverride("font_color", used >= WhSlots ? UiTheme.Bad : UiTheme.TextDim);
        RefreshWarehouseGold();
        RefreshBagFit();
    }

    private void RefreshWarehouseGold()
    {
        _whStored.Value = _whMoney;
        _whDepositBtn.Disabled = Sheet.Gold <= 0;
        _whWithdrawBtn.Disabled = _whMoney <= 0;
    }

    private static bool WarehouseStorable(int itemId) =>
        ItemData.Get(itemId) is { } def && WarehouseRules.Storable(itemId, def.Race);

    private static bool IsStackable(int itemId) => ItemData.Get(itemId) is { Countable: not 0 };

    private static int WholeStack(ItemSlot slot) => IsStackable(slot.ItemId) ? Mathf.Max(1, (int)slot.Count) : 1;

    private bool WhTakeFromBag(int abs)
    {
        if (!InMainBag(abs)) return false;
        DepositSlot(abs, -1);
        return true;
    }

    private BagFit WhBagFit(int abs)
    {
        if (abs < GridStart) return BagFit.Normal;
        return InMainBag(abs) && WarehouseStorable(Inv[abs].ItemId) ? BagFit.Normal : BagFit.Unfit;
    }

    private bool FitsInWarehouse(int whIdx, int itemId, int count) =>
        StorageDestination.Fits(_warehouse[whIdx], itemId, count, IsStackable(itemId));

    private bool FitsInBag(int abs, int itemId, int count) =>
        InMainBag(abs) && (Inv[abs].IsEmpty
        || (Inv[abs].ItemId == itemId && IsStackable(itemId) && Inv[abs].Count + count <= Inventory.StackMax));

    private int WarehouseDestination(int itemId, int count, out bool merge)
    {
        int whIdx = StorageDestination.Find(_warehouse, itemId, count, IsStackable(itemId));
        merge = whIdx >= 0 && !_warehouse[whIdx].IsEmpty;
        return whIdx;
    }

    private int BagDestination(int itemId, int count, out bool merge)
    {
        int abs = Inv.GridSlotFor(itemId, count, IsStackable(itemId));
        merge = abs >= 0 && !Inv[abs].IsEmpty;
        return abs;
    }

    private bool CanDropOnWarehouse(int whIdx, Variant data)
    {
        if (StorageTransferBusy || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("invFrom"))
        {
            int abs = d["invFrom"].AsInt32();
            return InMainBag(abs) && WarehouseStorable(Inv[abs].ItemId);
        }
        if (!d.ContainsKey("companionFrom")) return false;
        int from = d["companionFrom"].AsInt32();
        return from is >= 0 and < WhSlots && from != whIdx && !_warehouse[from].IsEmpty && _warehouse[whIdx].IsEmpty;
    }

    private void DropOnWarehouse(int whIdx, Variant data)
    {
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("invFrom")) DepositSlot(d["invFrom"].AsInt32(), whIdx);
        else if (d.ContainsKey("companionFrom")) MoveInsideWarehouse(d["companionFrom"].AsInt32(), whIdx);
    }

    private void DepositSlot(int abs, int target)
    {
        if (StorageTransferBusy || !InMainBag(abs) || Inv[abs].IsEmpty) return;
        var slot = Inv[abs];
        if (!WarehouseStorable(slot.ItemId))
        {
            _whStatus.Status(ItemData.Text(WarehouseRules.NonStorableText, "This item is non-storable"), bad: true);
            return;
        }
        if (IsStackable(slot.ItemId) && slot.Count > 1)
            _whAmount.Open(ItemData.Icon(slot.ItemId), $"Store {ItemData.DisplayName(slot.ItemId)}",
                $"You carry {slot.Count:n0}", slot.Count, slot.Count, n => { if (_whShown && Inv[abs].Equals(slot)) DepositCount(abs, (int)n, target); }, "Store");
        else
            DepositCount(abs, Mathf.Max(1, (int)slot.Count), target);
    }

    private void DepositCount(int abs, int count, int target)
    {
        if (StorageTransferBusy || !InMainBag(abs) || Inv[abs].IsEmpty || count <= 0 || target >= WhSlots) return;
        var slot = Inv[abs];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int whIdx;
        bool merge;
        if (target >= 0)
        {
            if (!FitsInWarehouse(target, slot.ItemId, count)) { _whStatus.Status("That slot is taken.", bad: true); return; }
            whIdx = target;
            merge = !_warehouse[target].IsEmpty;
        }
        else
        {
            whIdx = WarehouseDestination(slot.ItemId, count, out merge);
            if (whIdx < 0) { _whStatus.Status("Warehouse is full.", bad: true); return; }
        }
        _whPending = new WhPending { Op = WhOpInput, Merge = merge, InvAbs = abs, WhIdx = whIdx, Count = count };
        _whInFlight = true;
        Net.I.SendWarehouseInput(_vendorNpcId, slot.ItemId, WarehouseView.ServerPage(whIdx),
            (byte)(abs - GridStart), WarehouseView.ServerCell(whIdx), count);
    }

    private void WithdrawSlot(int whIdx) => AskWithdraw(whIdx, -1);

    private bool WithdrawInto(int whIdx, int bagAbs)
    {
        AskWithdraw(whIdx, bagAbs);
        return true;
    }

    private void AskWithdraw(int whIdx, int target)
    {
        if (StorageTransferBusy || whIdx < 0 || whIdx >= WhSlots || _warehouse[whIdx].IsEmpty) return;
        var slot = _warehouse[whIdx];
        if (IsStackable(slot.ItemId) && slot.Count > 1)
            _whAmount.Open(ItemData.Icon(slot.ItemId), $"Take out {ItemData.DisplayName(slot.ItemId)}",
                $"Stored {slot.Count:n0}", slot.Count, slot.Count, n => { if (_whShown && _warehouse[whIdx].Equals(slot)) WithdrawCount(whIdx, (int)n, target); }, "Take out");
        else
            WithdrawCount(whIdx, Mathf.Max(1, (int)slot.Count), target);
    }

    private void WithdrawCount(int whIdx, int count, int target)
    {
        if (StorageTransferBusy || whIdx < 0 || whIdx >= WhSlots || _warehouse[whIdx].IsEmpty || count <= 0) return;
        var slot = _warehouse[whIdx];
        count = Mathf.Min(count, slot.Count > 0 ? slot.Count : 1);
        int dest;
        bool merge;
        if (target >= 0)
        {
            if (!FitsInBag(target, slot.ItemId, count)) { _whStatus.Status("That slot is taken.", bad: true); return; }
            dest = target;
            merge = !Inv[target].IsEmpty;
        }
        else
        {
            dest = BagDestination(slot.ItemId, count, out merge);
            if (dest < 0) { _whStatus.Status("Your bags are full.", bad: true); return; }
        }
        _whPending = new WhPending { Op = WhOpOutput, Merge = merge, InvAbs = dest, WhIdx = whIdx, Count = count };
        _whInFlight = true;
        Net.I.SendWarehouseOutput(_vendorNpcId, slot.ItemId, WarehouseView.ServerPage(whIdx),
            WarehouseView.ServerCell(whIdx), (byte)(dest - GridStart), count);
    }

    private void MoveInsideWarehouse(int from, int to)
    {
        if (StorageTransferBusy || from == to || from < 0 || to < 0 || from >= WhSlots || to >= WhSlots || _warehouse[from].IsEmpty) return;
        if (!_warehouse[to].IsEmpty) { _whStatus.Status("That slot is taken.", bad: true); return; }
        _whPending = new WhPending { Op = WhOpMove, WhIdx = from, WhTo = to, Count = _warehouse[from].Count };
        _whInFlight = true;
        Net.I.SendWarehouseMoveInside(_vendorNpcId, _warehouse[from].ItemId, WarehouseView.ServerPage(from),
            WarehouseView.ServerCell(from), WarehouseView.ServerPage(to), WarehouseView.ServerCell(to));
    }

    private void AskGoldTransfer(bool deposit)
    {
        if (StorageTransferBusy) return;
        long max = System.Math.Min(deposit ? Sheet.Gold : _whMoney, WarehouseRules.CoinMax - (long)(deposit ? _whMoney : Sheet.Gold));
        if (max <= 0) return;
        _whAmount.Open(null, deposit ? "Deposit gold" : "Withdraw gold",
            deposit ? $"You carry {max:n0}" : $"Stored {max:n0}", max, max, n => GoldTransfer(deposit, (int)n),
            deposit ? "Deposit" : "Withdraw");
    }

    private void GoldTransfer(bool deposit, int amount)
    {
        if (StorageTransferBusy || amount <= 0) return;
        if (deposit && amount > Sheet.Gold) { _whStatus.Status("Not enough carried gold.", bad: true); return; }
        if (!deposit && amount > _whMoney) { _whStatus.Status("Not enough stored gold.", bad: true); return; }

        _whPending = new WhPending { Op = deposit ? WhOpInput : WhOpOutput, Gold = true, Count = amount };
        _whInFlight = true;
        if (deposit) Net.I.SendWarehouseInput(_vendorNpcId, Net.GoldItemId, 0, 0, 0, amount);
        else Net.I.SendWarehouseOutput(_vendorNpcId, Net.GoldItemId, 0, 0, 0, amount);
    }

    private void OnWarehouseResult(byte op, bool ok)
    {
        if (!_whInFlight || op != _whPending.Op) return;
        _whInFlight = false;
        if (!ok)
        {
            _whStatus.Status("Transfer failed.", bad: true);
            if (_whShown) RefreshWarehouse();
            return;
        }

        var p = _whPending;
        if (p.Gold)
        {
            if (p.Op == WhOpInput) { Sheet.Spend(p.Count); _whMoney += p.Count; }
            else { _whMoney -= p.Count; Sheet.Receive(p.Count); }
            Net.I.RaiseGold(Sheet.Gold);
            _whStatus.Status(p.Op == WhOpInput ? $"Deposited {p.Count:n0} gold." : $"Withdrew {p.Count:n0} gold.", bad: false);
        }
        else if (p.Op == WhOpMove)
        {
            int itemId = _warehouse[p.WhIdx].ItemId;
            _warehouse[p.WhTo] = _warehouse[p.WhIdx];
            _warehouse[p.WhIdx] = default;
            _whStatus.Status($"Moved {ItemData.DisplayName(itemId)}.", bad: false);
        }
        else if (p.Op == WhOpInput)
        {
            var bag = Inv[p.InvAbs];
            int itemId = bag.ItemId;
            MoveStack(ref bag, ref _warehouse[p.WhIdx], p.Count, p.Merge);
            Inv[p.InvAbs] = bag;
            Net.I.MirrorInventorySlot(p.InvAbs, Inv[p.InvAbs]);
            if (CharTabOpen()) RefreshInventoryUI();
            _whStatus.Status($"Stored {WhItemLine(itemId, p.Count)}.", bad: false);
        }
        else
        {
            var bag = Inv[p.InvAbs];
            int itemId = _warehouse[p.WhIdx].ItemId;
            MoveStack(ref _warehouse[p.WhIdx], ref bag, p.Count, p.Merge);
            Inv[p.InvAbs] = bag;
            Net.I.MirrorInventorySlot(p.InvAbs, Inv[p.InvAbs]);
            if (CharTabOpen()) RefreshInventoryUI();
            _whStatus.Status($"Took out {WhItemLine(itemId, p.Count)}.", bad: false);
        }
        if (_whShown) RefreshWarehouse();
    }

    private static void MoveStack(ref ItemSlot from, ref ItemSlot to, int count, bool merge)
    {
        bool whole = from.Count <= count || !IsStackable(from.ItemId);
        if (merge) to.Count = (short)(to.Count + count);
        else
        {
            to = from;
            if (!whole) to.Count = (short)count;
        }
        if (whole) from = default;
        else from.Count = (short)(from.Count - count);
    }

    private static string WhItemLine(int itemId, int count) =>
        count > 1 ? $"{ItemData.DisplayName(itemId)} x{count:n0}" : ItemData.DisplayName(itemId);
}
