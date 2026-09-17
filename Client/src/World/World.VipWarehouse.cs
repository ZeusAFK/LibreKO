using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int VipWhSlots = Net.VipWarehouseSlots;
    private const int VipWhPageSize = Net.VipWarehousePageSize;
    private const int VipWhPages = VipWhSlots / VipWhPageSize;

    private readonly ItemSlot[] _vipWh = new ItemSlot[VipWhSlots];
    private int _vipWhPage;
    private int _vipWhExpirySec;

    private CanvasLayer _vipWhLayer = null!;
    private HudWindow _vipWhPanel = null!;
    private bool _vipWhShown;
    private readonly WarehouseCell[] _vipWhCells = new WarehouseCell[VipWhPageSize];
    private readonly WarehouseCell[] _vipWhBagCells = new WarehouseCell[28];
    private Label _vipWhPageLbl = null!, _vipWhStatus = null!, _vipWhExpiryLbl = null!;

    private AcceptDialog _vipWhPinDlg = null!;
    private LineEdit _vipWhPinEdit = null!;
    private Label _vipWhPinPrompt = null!;
    private byte _vipWhPinSub;

    private struct VipWhPending { public byte Op; public int InvAbs; public int VipIdx; }
    private VipWhPending _vipWhPending;
    private bool _vipWhInFlight;

    private void VipWarehouseInit()
    {
        BuildVipWarehousePanel();
        Net.I.VipWarehouseContentsEvent += OnVipWarehouseContents;
        Net.I.VipWarehouseResultEvent += OnVipWarehouseResult;
        Net.I.VipWarehouseExpiredEvent += OnVipWarehouseExpired;
        Net.I.VipWarehousePinPromptEvent += OnVipWarehousePinPrompt;
        Net.I.VipWarehousePinResultEvent += OnVipWarehousePinResult;
    }

    private void VipWarehouseDispose()
    {
        Net.I.VipWarehouseContentsEvent -= OnVipWarehouseContents;
        Net.I.VipWarehouseResultEvent -= OnVipWarehouseResult;
        Net.I.VipWarehouseExpiredEvent -= OnVipWarehouseExpired;
        Net.I.VipWarehousePinPromptEvent -= OnVipWarehousePinPrompt;
        Net.I.VipWarehousePinResultEvent -= OnVipWarehousePinResult;
    }

    private void BuildVipWarehousePanel()
    {
        _vipWhLayer = new CanvasLayer { Layer = 74 };
        AddChild(_vipWhLayer);

        _vipWhPanel = new HudWindow("vipwarehouse", "VIP Vault", new Vector2(170, 100)) { Visible = false };
        _vipWhPanel.Closed += CloseVipWarehouse;
        _vipWhLayer.AddChild(_vipWhPanel);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        _vipWhPanel.Body.AddChild(body);

        var vipCol = new VBoxContainer();
        vipCol.AddThemeConstantOverride("separation", 6);
        body.AddChild(vipCol);
        vipCol.AddChild(UiTheme.SectionTitle("VIP Vault"));

        var vipGrid = new GridContainer { Columns = 4 };
        vipGrid.AddThemeConstantOverride("h_separation", 4);
        vipGrid.AddThemeConstantOverride("v_separation", 4);
        vipCol.AddChild(vipGrid);
        for (int i = 0; i < VipWhPageSize; i++)
        {
            var cell = new WarehouseCell(i)
            {
                OnActivate = VipWithdrawSlot,
                OnHover = HoverWarehouseCell,
                OnHoverEnd = HideItemTooltip,
            };
            _vipWhCells[i] = cell;
            vipGrid.AddChild(cell);
        }

        var pageRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pageRow.AddThemeConstantOverride("separation", 8);
        var prev = new Button { Text = "◀", FocusMode = Control.FocusModeEnum.None };
        prev.Pressed += () => ChangeVipWhPage(-1);
        _vipWhPageLbl = UiTheme.Text($"1 / {VipWhPages}", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _vipWhPageLbl.CustomMinimumSize = new Vector2(60, 0);
        var next = new Button { Text = "▶", FocusMode = Control.FocusModeEnum.None };
        next.Pressed += () => ChangeVipWhPage(1);
        pageRow.AddChild(prev); pageRow.AddChild(_vipWhPageLbl); pageRow.AddChild(next);
        vipCol.AddChild(pageRow);

        vipCol.AddChild(new HSeparator());
        var expiryRow = new HBoxContainer();
        var el = UiTheme.Text("Expires in", 12, UiTheme.TextLo); el.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        expiryRow.AddChild(el);
        _vipWhExpiryLbl = UiTheme.Text("—", 13, UiTheme.Gold, HorizontalAlignment.Right);
        expiryRow.AddChild(_vipWhExpiryLbl);
        vipCol.AddChild(expiryRow);

        var pinRow = new HBoxContainer();
        pinRow.AddThemeConstantOverride("separation", 6);
        var setPinBtn = new Button { Text = "Set / change PIN", FocusMode = Control.FocusModeEnum.None };
        setPinBtn.Pressed += () => ShowVipPinDialog(Net.VipWhSetPinSub, "Choose a new 4-digit PIN:");
        var clrPinBtn = new Button { Text = "Clear PIN", FocusMode = Control.FocusModeEnum.None };
        clrPinBtn.Pressed += () => Net.I.SendVipWarehouseCancelPin();
        pinRow.AddChild(setPinBtn); pinRow.AddChild(clrPinBtn);
        vipCol.AddChild(pinRow);

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
                OnActivate = VipDepositSlot,
                OnHover = HoverWarehouseCell,
                OnHoverEnd = HideItemTooltip,
            };
            _vipWhBagCells[i] = cell;
            bagGrid.AddChild(cell);
        }
        bagCol.AddChild(new HSeparator());
        _vipWhStatus = UiTheme.Text("Right-click to store / withdraw", 11, new Color(UiTheme.TextLo, 0.7f));
        bagCol.AddChild(_vipWhStatus);

        _vipWhPinDlg = new AcceptDialog { Title = "VIP Vault PIN", Unresizable = true };
        _vipWhPinDlg.GetOkButton().Text = "Confirm";
        var pinBox = new VBoxContainer();
        pinBox.AddThemeConstantOverride("separation", 8);
        _vipWhPinPrompt = UiTheme.Text("Enter your 4-digit PIN:", 13, UiTheme.TextHi);
        pinBox.AddChild(_vipWhPinPrompt);
        _vipWhPinEdit = new LineEdit
        {
            PlaceholderText = "1234",
            MaxLength = 4,
            Secret = true,
            CustomMinimumSize = new Vector2(160, 0),
        };
        _vipWhPinEdit.TextSubmitted += _ => SubmitVipPin();
        pinBox.AddChild(_vipWhPinEdit);
        _vipWhPinDlg.AddChild(pinBox);
        _vipWhPinDlg.Confirmed += SubmitVipPin;
        _vipWhLayer.AddChild(_vipWhPinDlg);
    }

    private void ToggleVipWarehouse()
    {
        if (_vipWhShown) { CloseVipWarehouse(); return; }
        _vipWhInFlight = false;
        _vipWhPage = 0;
        Net.I.SendVipWarehouseOpen();
    }

    private void OpenVipWarehouse()
    {
        _vipWhStatus.Text = "Right-click to store / withdraw";
        _vipWhPanel.Visible = true;
        _vipWhShown = true;
        RefreshVipWarehouse();
    }

    private void CloseVipWarehouse()
    {
        if (!_vipWhShown) return;
        _vipWhShown = false;
        _vipWhPanel.Visible = false;
        HideItemTooltip();
    }

    private void OnVipWarehouseContents(int remainingSec, ItemSlot[] slots)
    {
        _vipWhExpirySec = remainingSec;
        for (int i = 0; i < VipWhSlots && i < slots.Length; i++) _vipWh[i] = slots[i];
        if (!_vipWhShown) OpenVipWarehouse();
        else RefreshVipWarehouse();
    }

    private void OnVipWarehouseExpired()
    {
        Chat.Info("Your VIP vault rental has expired. Renew it with a vault key.");
        if (_vipWhShown) { _vipWhStatus.Text = "Vault rental expired."; RefreshVipWarehouse(); }
    }

    private void ChangeVipWhPage(int d)
    {
        _vipWhPage = ((_vipWhPage + d) % VipWhPages + VipWhPages) % VipWhPages;
        RefreshVipWarehouse();
    }

    private void RefreshVipWarehouse()
    {
        for (int i = 0; i < VipWhPageSize; i++)
            _vipWhCells[i].Set(_vipWh[_vipWhPage * VipWhPageSize + i]);
        for (int i = 0; i < 28; i++)
            _vipWhBagCells[i].Set(GridStart + i < Inv.Length ? Inv[GridStart + i] : default);
        _vipWhPageLbl.Text = $"{_vipWhPage + 1} / {VipWhPages}";
        _vipWhExpiryLbl.Text = FormatVipExpiry(_vipWhExpirySec);
    }

    private static string FormatVipExpiry(int seconds)
    {
        if (seconds <= 0) return "expired";
        int days = seconds / 86400;
        if (days >= 1) return $"{days}d {(seconds % 86400) / 3600}h";
        int hours = seconds / 3600;
        if (hours >= 1) return $"{hours}h {(seconds % 3600) / 60}m";
        return $"{seconds / 60}m";
    }

    private int FirstFreeVipWarehouse()
    {
        for (int i = 0; i < VipWhSlots; i++) if (_vipWh[i].IsEmpty) return i;
        return -1;
    }

    private void VipDepositSlot(int abs)
    {
        if (_vipWhInFlight || abs < 0 || abs >= Inv.Length || Inv[abs].IsEmpty) return;
        int vipIdx = FirstFreeVipWarehouse();
        if (vipIdx < 0) { _vipWhStatus.Text = "The vault is full."; return; }
        var slot = Inv[abs];
        _vipWhPending = new VipWhPending { Op = 2, InvAbs = abs, VipIdx = vipIdx };
        _vipWhInFlight = true;
        Net.I.SendVipWarehouseInput(slot.ItemId, (byte)(vipIdx / VipWhPageSize),
            (byte)(abs - GridStart), (byte)(vipIdx % VipWhPageSize), slot.Count);
    }

    private void VipWithdrawSlot(int vipIdx)
    {
        if (_vipWhInFlight) return;
        int absVip = _vipWhPage * VipWhPageSize + vipIdx;
        if (absVip < 0 || absVip >= VipWhSlots || _vipWh[absVip].IsEmpty) return;
        int free = Inv.FirstFreeGridSlot();
        if (free < 0) { _vipWhStatus.Text = "Your bags are full."; return; }
        var slot = _vipWh[absVip];
        _vipWhPending = new VipWhPending { Op = 3, InvAbs = free, VipIdx = absVip };
        _vipWhInFlight = true;
        Net.I.SendVipWarehouseOutput(slot.ItemId, (byte)(absVip / VipWhPageSize),
            (byte)(absVip % VipWhPageSize), (byte)(free - GridStart), slot.Count);
    }

    private void OnVipWarehouseResult(byte op, bool ok)
    {
        if (!_vipWhInFlight) return;
        _vipWhInFlight = false;
        if (!ok) { _vipWhStatus.Text = "Transfer failed."; if (_vipWhShown) RefreshVipWarehouse(); return; }

        var p = _vipWhPending;
        if (p.Op == 2)
        {
            _vipWh[p.VipIdx] = Inv[p.InvAbs];
            Inv[p.InvAbs] = default;
            Net.I.MirrorInventorySlot(p.InvAbs, Inv[p.InvAbs]);
            if (CharTabOpen()) RefreshInventoryUI();
        }
        else
        {
            Inv[p.InvAbs] = _vipWh[p.VipIdx];
            _vipWh[p.VipIdx] = default;
            Net.I.MirrorInventorySlot(p.InvAbs, Inv[p.InvAbs]);
            if (CharTabOpen()) RefreshInventoryUI();
        }
        if (_vipWhShown) RefreshVipWarehouse();
    }

    private void OnVipWarehousePinPrompt()
    {
        ShowVipPinDialog(Net.VipWhEnterPinSub, "This vault is PIN-protected. Enter your 4-digit PIN:");
    }

    private void ShowVipPinDialog(byte sub, string prompt)
    {
        _vipWhPinSub = sub;
        _vipWhPinPrompt.Text = prompt;
        _vipWhPinEdit.Text = "";
        _vipWhPinDlg.PopupCentered();
        _vipWhPinEdit.GrabFocus();
    }

    private void SubmitVipPin()
    {
        string pin = _vipWhPinEdit.Text.Trim();
        if (pin.Length != 4 || !IsAllDigits(pin))
        {
            _vipWhPinPrompt.Text = "PIN must be exactly 4 digits.";
            _vipWhPinDlg.PopupCentered();
            _vipWhPinEdit.GrabFocus();
            return;
        }
        switch (_vipWhPinSub)
        {
            case Net.VipWhSetPinSub:   Net.I.SendVipWarehouseSetPin(pin); break;
            case Net.VipWhChangePinSub: Net.I.SendVipWarehouseChangePin(pin); break;
            default:                   Net.I.SendVipWarehouseEnterPin(pin); break;
        }
        _vipWhPinEdit.Text = "";
        _vipWhPinDlg.Hide();
    }

    private void OnVipWarehousePinResult(byte sub, bool ok)
    {
        if (sub == Net.VipWhEnterPinSub)
        {
            if (ok) Chat.Info("Vault unlocked.");
            else { ShowVipPinDialog(Net.VipWhEnterPinSub, "Wrong PIN. Try again:"); }
            return;
        }
        if (sub == Net.VipWhSetPinSub || sub == Net.VipWhChangePinSub)
        {
            Chat.Info(ok ? "Vault PIN updated." : "Couldn't set the PIN (must be 4 digits).");
            return;
        }
        if (sub == Net.VipWhCancelPinSub)
            Chat.Info(ok ? "Vault PIN cleared." : "Couldn't clear the PIN.");
    }

    private static bool IsAllDigits(string s)
    {
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }
}
