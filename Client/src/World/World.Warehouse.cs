using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int WhSlots = 192;
    private const int WhPageSize = 24;
    private const int WhPages = WhSlots / WhPageSize;

    private readonly ItemSlot[] _warehouse = new ItemSlot[WhSlots];
    private int _whMoney;
    private int _whPage;

    private CanvasLayer _whLayer = null!;
    private HudWindow _whPanel = null!;
    private bool _whShown;
    private readonly WarehouseCell[] _whCells = new WarehouseCell[WhPageSize];
    private readonly WarehouseCell[] _whBagCells = new WarehouseCell[28];
    private Label _whPageLbl = null!, _whStoredGold = null!, _whCarriedGold = null!, _whStatus = null!;
    private LineEdit _whGoldInput = null!;

    private struct WhPending { public byte Op; public bool Gold; public int InvAbs; public int WhIdx; public int Count; }
    private WhPending _whPending;
    private bool _whInFlight;

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

    private void OnWarehouseGold(int g) { if (_whShown) _whCarriedGold.Text = $"{g:n0}"; }

    private void BuildWarehousePanel()
    {
        _whLayer = new CanvasLayer { Layer = 74 };
        AddChild(_whLayer);

        _whPanel = new HudWindow("warehouse", "Warehouse", new Vector2(150, 90)) { Visible = false };
        _whPanel.Closed += CloseWarehouse;
        _whLayer.AddChild(_whPanel);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        _whPanel.Body.AddChild(body);

        var whCol = new VBoxContainer();
        whCol.AddThemeConstantOverride("separation", 6);
        body.AddChild(whCol);
        whCol.AddChild(UiTheme.SectionTitle("Warehouse"));

        var whGrid = new GridContainer { Columns = 4 };
        whGrid.AddThemeConstantOverride("h_separation", 4);
        whGrid.AddThemeConstantOverride("v_separation", 4);
        whCol.AddChild(whGrid);
        for (int i = 0; i < WhPageSize; i++)
        {
            var cell = new WarehouseCell(i)
            {
                OnActivate = WithdrawSlot,
                OnHover = HoverWarehouseCell,
                OnHoverEnd = HideItemTooltip,
            };
            _whCells[i] = cell;
            whGrid.AddChild(cell);
        }

        var pageRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pageRow.AddThemeConstantOverride("separation", 8);
        var prev = new Button { Text = "◀", FocusMode = Control.FocusModeEnum.None };
        prev.Pressed += () => ChangeWhPage(-1);
        _whPageLbl = UiTheme.Text("1 / 8", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _whPageLbl.CustomMinimumSize = new Vector2(60, 0);
        var next = new Button { Text = "▶", FocusMode = Control.FocusModeEnum.None };
        next.Pressed += () => ChangeWhPage(1);
        pageRow.AddChild(prev); pageRow.AddChild(_whPageLbl); pageRow.AddChild(next);
        whCol.AddChild(pageRow);

        whCol.AddChild(new HSeparator());
        var storedRow = new HBoxContainer();
        var sl = UiTheme.Text("Stored gold", 12, UiTheme.TextLo); sl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        storedRow.AddChild(sl);
        _whStoredGold = UiTheme.Text("0", 13, UiTheme.Gold, HorizontalAlignment.Right);
        storedRow.AddChild(_whStoredGold);
        whCol.AddChild(storedRow);

        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 6);
        _whGoldInput = new LineEdit { PlaceholderText = "amount", CustomMinimumSize = new Vector2(110, 0) };
        goldRow.AddChild(_whGoldInput);
        var depBtn = new Button { Text = "Deposit", FocusMode = Control.FocusModeEnum.None };
        depBtn.Pressed += () => GoldTransfer(deposit: true);
        var wdrBtn = new Button { Text = "Withdraw", FocusMode = Control.FocusModeEnum.None };
        wdrBtn.Pressed += () => GoldTransfer(deposit: false);
        goldRow.AddChild(depBtn); goldRow.AddChild(wdrBtn);
        whCol.AddChild(goldRow);

        var bagCol = new VBoxContainer();
        bagCol.AddThemeConstantOverride("separation", 6);
        body.AddChild(bagCol);
        bagCol.AddChild(UiTheme.SectionTitle("Inventory"));

        var bagGrid = new GridContainer { Columns = 5 };
        bagGrid.AddThemeConstantOverride("h_separation", 4);
        bagGrid.AddThemeConstantOverride("v_separation", 4);
        bagCol.AddChild(bagGrid);
        for (int i = 0; i < 28; i++)
        {
            var cell = new WarehouseCell(GridStart + i, bag: true)
            {
                OnActivate = DepositSlot,
                OnHover = HoverWarehouseCell,
                OnHoverEnd = HideItemTooltip,
            };
            _whBagCells[i] = cell;
            bagGrid.AddChild(cell);
        }
        bagCol.AddChild(new HSeparator());
        var carriedRow = new HBoxContainer();
        var cl = UiTheme.Text("Carried gold", 12, UiTheme.TextLo); cl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        carriedRow.AddChild(cl);
        _whCarriedGold = UiTheme.Text("0", 13, UiTheme.Gold, HorizontalAlignment.Right);
        carriedRow.AddChild(_whCarriedGold);
        bagCol.AddChild(carriedRow);
        _whStatus = UiTheme.Text("Right-click to store / withdraw", 11, new Color(UiTheme.TextLo, 0.7f));
        bagCol.AddChild(_whStatus);
    }

    private void OnWarehouseOpen()
    {
        ShowNpcServiceChoice(
            "Shall we start the day at our inn and end it at our inn as well?",
            ("Use storage", OpenWarehouseStorage),
            ("Use [VIP] storage", ToggleVipWarehouse),
            ("Seal / Cancel (anti-theft)", () => OpenSealWindow(SealMode.Secret)),
            ("Seal / Cancel", () => OpenSealWindow(SealMode.Bind)));
    }

    private void OpenWarehouseStorage()
    {
        CloseNpcDialog();
        _whInFlight = false;
        _whPage = 0;
        _whStatus.Text = "Right-click to store / withdraw";
        _whPanel.Visible = true;
        _whShown = true;
        System.Array.Clear(_warehouse, 0, _warehouse.Length);
        _whMoney = 0;
        Net.I.SendWarehouseOpen();
        RefreshWarehouse();
    }

    private void CloseWarehouse()
    {
        if (!_whShown) return;
        _whShown = false;
        _whPanel.Visible = false;
        HideItemTooltip();
    }

    private void OnWarehouseContents(int money, ItemSlot[] slots)
    {
        _whMoney = money;
        for (int i = 0; i < WhSlots && i < slots.Length; i++) _warehouse[i] = slots[i];
        if (_whShown) RefreshWarehouse();
    }

    private void ChangeWhPage(int d)
    {
        _whPage = ((_whPage + d) % WhPages + WhPages) % WhPages;
        RefreshWarehouse();
    }

    private void RefreshWarehouse()
    {
        for (int i = 0; i < WhPageSize; i++)
            _whCells[i].Set(_warehouse[_whPage * WhPageSize + i]);
        for (int i = 0; i < 28; i++)
            _whBagCells[i].Set(GridStart + i < Inv.Length ? Inv[GridStart + i] : default);
        _whPageLbl.Text = $"{_whPage + 1} / {WhPages}";
        _whStoredGold.Text = $"{_whMoney:n0}";
        _whCarriedGold.Text = $"{Sheet.Gold:n0}";
    }

    private int FirstFreeWarehouse()
    {
        for (int i = 0; i < WhSlots; i++) if (_warehouse[i].IsEmpty) return i;
        return -1;
    }

    private void DepositSlot(int abs)
    {
        if (_whInFlight || abs < 0 || abs >= Inv.Length || Inv[abs].IsEmpty) return;
        int whIdx = FirstFreeWarehouse();
        if (whIdx < 0) { _whStatus.Text = "Warehouse is full."; return; }
        var slot = Inv[abs];
        _whPending = new WhPending { Op = 2, InvAbs = abs, WhIdx = whIdx, Count = slot.Count };
        _whInFlight = true;
        Net.I.SendWarehouseInput(_vendorNpcId, slot.ItemId, (byte)(whIdx / WhPageSize),
            (byte)(abs - GridStart), (byte)(whIdx % WhPageSize), slot.Count);
    }

    private void WithdrawSlot(int whIdx)
    {
        if (_whInFlight) return;
        int absWh = _whPage * WhPageSize + whIdx;
        if (absWh < 0 || absWh >= WhSlots || _warehouse[absWh].IsEmpty) return;
        int free = Inv.FirstFreeGridSlot();
        if (free < 0) { _whStatus.Text = "Your bags are full."; return; }
        var slot = _warehouse[absWh];
        _whPending = new WhPending { Op = 3, InvAbs = free, WhIdx = absWh, Count = slot.Count };
        _whInFlight = true;
        Net.I.SendWarehouseOutput(_vendorNpcId, slot.ItemId, (byte)(absWh / WhPageSize),
            (byte)(absWh % WhPageSize), (byte)(free - GridStart), slot.Count);
    }

    private void GoldTransfer(bool deposit)
    {
        if (_whInFlight) return;
        if (!int.TryParse(_whGoldInput.Text.Replace(",", "").Trim(), out int amount) || amount <= 0)
        { _whStatus.Text = "Enter an amount."; return; }
        if (deposit && amount > Sheet.Gold) { _whStatus.Text = "Not enough carried gold."; return; }
        if (!deposit && amount > _whMoney) { _whStatus.Text = "Not enough stored gold."; return; }

        _whPending = new WhPending { Op = (byte)(deposit ? 2 : 3), Gold = true, Count = amount };
        _whInFlight = true;
        if (deposit) Net.I.SendWarehouseInput(_vendorNpcId, Net.GoldItemId, 0, 0, 0, amount);
        else Net.I.SendWarehouseOutput(_vendorNpcId, Net.GoldItemId, 0, 0, 0, amount);
    }

    private void OnWarehouseResult(byte op, bool ok)
    {
        if (!_whInFlight) return;
        _whInFlight = false;
        if (!ok) { _whStatus.Text = "Transfer failed."; if (_whShown) RefreshWarehouse(); return; }

        var p = _whPending;
        if (p.Gold)
        {
            if (p.Op == 2) { Sheet.Spend(p.Count); _whMoney += p.Count; }
            else { _whMoney -= p.Count; Sheet.Receive(p.Count); }
            _whGoldInput.Clear();
            Net.I.RaiseGold(Sheet.Gold);
        }
        else if (p.Op == 2)
        {
            _warehouse[p.WhIdx] = Inv[p.InvAbs];
            Inv[p.InvAbs] = default;
            Net.I.MirrorInventorySlot(p.InvAbs, Inv[p.InvAbs]);
            if (CharTabOpen()) RefreshInventoryUI();
        }
        else
        {
            Inv[p.InvAbs] = _warehouse[p.WhIdx];
            _warehouse[p.WhIdx] = default;
            Net.I.MirrorInventorySlot(p.InvAbs, Inv[p.InvAbs]);
            if (CharTabOpen()) RefreshInventoryUI();
        }
        if (_whShown) RefreshWarehouse();
    }
}
