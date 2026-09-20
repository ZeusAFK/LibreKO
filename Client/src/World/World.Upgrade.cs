using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const int UpgradeSlotCount = 10;
    private const byte UpgradeTypeNormal = 1;
    private const byte UpgradeTypePreview = 2;
    private const byte UpgradeResultFailed = 0;
    private const byte UpgradeResultSucceeded = 1;
    private const byte UpgradeResultTrading = 2;
    private const byte UpgradeResultNeedCoins = 3;
    private const byte UpgradeResultNoMatch = 4;
    private const byte UpgradeResultSealed = 5;
    private const int AccessoryCompoundScrollFirst = 379159000;
    private const int AccessoryCompoundScrollLast = 379164000;
    private const int UpgradeScrollHigh = 379016000;
    private const int UpgradeScrollHighBlessed = 379021000;
    private const int UpgradeScrollClass = 379152000;
    private const int UpgradeScrollMiddle = 379205000;
    private const int UpgradeScrollLow = 379221000;
    private const int UpgradeScrollTraining = 379255000;
    private const int BonusScrollHighLast = 379035000;
    private const int BonusScrollMiddleLast = 379220000;
    private const int BonusScrollLowLast = 379235000;
    private const int DispelScrollFirst = 379138000;
    private const int DispelScrollLast = 379141000;
    private const int ReverseScroll = 379256000;
    private const int ReverseStrengthenScroll = 379257000;
    private const int KarivdisPiece = 379258000;
    private const int TrinaPieceMiddle = 352900000;
    private const int TrinaPieceLow = 353000000;
    private const int TrinaPieceAccessory = 354000000;
    private const int TrinaPiece = 700002000;
    private const int RebirthRestorationScroll = 810322000;
    private const int BlessingLogos = 890092000;
    private const int EquipSlotFirst = 0;
    private const int EquipSlotLast = 14;
    private const int EtcKindFirst = 95;
    private const int EtcKindLast = 99;

    private CanvasLayer _upgradeLayer = null!;
    private HudWindow _upgradePanel = null!;
    private VBoxContainer _upgradeBackpackGrid = null!;
    private Label _upgradeStatus = null!;
    private Label _upgradeTarget = null!;
    private Button _upgradeBtn = null!;
    private UpgradeSocket _upgradeResultSocket = null!;
    private bool _upgradeShown;
    private bool _upgradeBusy;
    private int _upgradeAnvilId;
    private int _upgradePreviewId;
    private Notice? _upgradeConfirm;

    private readonly int[] _upgradeItemIds = new int[UpgradeSlotCount];
    private readonly int[] _upgradePositions = new int[UpgradeSlotCount];
    private readonly UpgradeSocket[] _upgradeSockets = new UpgradeSocket[UpgradeSlotCount];
    private readonly System.Collections.Generic.List<UpgradeBackpackCell> _upgradeBackpackCells = new();

    private void UpgradeInit()
    {
        BuildUpgradePanel();
        Net.I.UpgradeOpenEvent += OnUpgradeOpen;
        Net.I.UpgradeResultEvent += OnUpgradeResult;
        Net.I.InventorySlotEvent += OnUpgradeInventorySlot;
        Net.I.InventoryGridRefreshEvent += OnUpgradeInventoryGrid;
    }

    private void UpgradeDispose()
    {
        Net.I.UpgradeOpenEvent -= OnUpgradeOpen;
        Net.I.UpgradeResultEvent -= OnUpgradeResult;
        Net.I.InventorySlotEvent -= OnUpgradeInventorySlot;
        Net.I.InventoryGridRefreshEvent -= OnUpgradeInventoryGrid;
    }

    private void BuildUpgradePanel()
    {
        _upgradeLayer = new CanvasLayer { Layer = 75 };
        AddChild(_upgradeLayer);

        _upgradePanel = new HudWindow("anvil", "Magic Anvil", new Vector2(120, 105), 800) { Visible = false };
        _upgradePanel.Closed += CloseUpgrade;
        _upgradeLayer.AddChild(_upgradePanel);

        var root = _upgradePanel.Body;
        root.AddThemeConstantOverride("separation", 10);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 14);
        root.AddChild(cols);

        var ritualPanel = UiTheme.Section();
        ritualPanel.CustomMinimumSize = new Vector2(500, 0);
        cols.AddChild(ritualPanel);

        var ritual = new VBoxContainer();
        ritual.AddThemeConstantOverride("separation", 10);
        ritualPanel.AddChild(ritual);
        ritual.AddChild(BuildUpgradeBench());

        _upgradeTarget = UiTheme.Text("Place the item to upgrade.", 14, UiTheme.TextHi, HorizontalAlignment.Center);
        _upgradeTarget.CustomMinimumSize = new Vector2(0, 30);
        ritual.AddChild(_upgradeTarget);

        _upgradeStatus = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _upgradeStatus.CustomMinimumSize = new Vector2(0, 22);
        _upgradeStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ritual.AddChild(_upgradeStatus);

        _upgradeBtn = new Button
        {
            Text = "Upgrade",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _upgradeBtn.Pressed += ConfirmUpgrade;
        ritual.AddChild(_upgradeBtn);

        var bagPanel = UiTheme.Section();
        bagPanel.CustomMinimumSize = new Vector2(280, 0);
        cols.AddChild(bagPanel);
        var bag = new VBoxContainer();
        bag.AddThemeConstantOverride("separation", 7);
        bagPanel.AddChild(bag);
        bag.AddChild(UiTheme.SectionTitle("Inventory"));
        _upgradeBackpackGrid = new VBoxContainer();
        _upgradeBackpackGrid.AddThemeConstantOverride("separation", 4);
        bag.AddChild(_upgradeBackpackGrid);

        ClearUpgradeSockets();
        RefreshUpgradeBackpack();
        RefreshUpgradeActions();
    }

    private Control BuildUpgradeBench()
    {
        var bench = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bench.AddThemeConstantOverride("separation", 16);

        bench.AddChild(BuildUpgradeColumn("Item", BuildUpgradeTargetSocket()));
        bench.AddChild(UiTheme.Text("+", 26, UiTheme.GoldDark));

        var materials = new GridContainer { Columns = 3 };
        materials.AddThemeConstantOverride("h_separation", 5);
        materials.AddThemeConstantOverride("v_separation", 5);
        for (int i = 1; i < _upgradeSockets.Length; i++)
        {
            int idx = i;
            var socket = new UpgradeSocket("", 50);
            socket.Cleared += () => ClearUpgradeSocket(idx);
            socket.Hovered += held => ShowItemTooltip(UpgradeSocketSlot(idx), held);
            socket.Unhovered += HideItemTooltip;
            _upgradeSockets[i] = socket;
            materials.AddChild(socket);
        }
        bench.AddChild(BuildUpgradeColumn("Materials", materials));

        bench.AddChild(UiTheme.Text("=", 26, UiTheme.GoldDark));
        _upgradeResultSocket = new UpgradeSocket("?", 64, interactive: false);
        _upgradeResultSocket.Hovered += held => ShowItemTooltip(-1, held);
        _upgradeResultSocket.Unhovered += HideItemTooltip;
        bench.AddChild(BuildUpgradeColumn("Result", _upgradeResultSocket));
        return bench;
    }

    private Control BuildUpgradeTargetSocket()
    {
        var socket = new UpgradeSocket("", 64);
        socket.Cleared += () => ClearUpgradeSocket(0);
        socket.Hovered += held => ShowItemTooltip(UpgradeSocketSlot(0), held);
        socket.Unhovered += HideItemTooltip;
        _upgradeSockets[0] = socket;
        return socket;
    }

    private static Control BuildUpgradeColumn(string caption, Control body)
    {
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 6);
        col.AddChild(UiTheme.Text(caption, 11, UiTheme.TextDim, HorizontalAlignment.Center));
        var centre = new CenterContainer();
        centre.AddChild(body);
        col.AddChild(centre);
        return col;
    }

    private void OnUpgradeOpen(int anvilId)
    {
        _upgradeAnvilId = anvilId;
        _upgradeBusy = false;
        ClearUpgradeSockets();
        RefreshUpgradeBackpack();
        _upgradeTarget.Text = "Place the item to upgrade.";
        SetUpgradeStatus("", false);
        _upgradePanel.Visible = true;
        _upgradeShown = true;
        CloseNpcDialog();
        CloseVendor();
    }

    private void CloseUpgrade()
    {
        if (!_upgradeShown) return;
        _upgradeShown = false;
        _upgradePanel.Visible = false;
        _upgradeBusy = false;
        HideItemTooltip();
        DismissUpgradeConfirm();
    }

    private void DismissUpgradeConfirm()
    {
        if (_upgradeConfirm != null && GodotObject.IsInstanceValid(_upgradeConfirm))
            _upgradeConfirm.Close();
        _upgradeConfirm = null;
    }

    private void RefreshUpgradeBackpack()
    {
        if (_upgradeBackpackGrid == null) return;
        HideItemTooltip();
        _upgradeBackpackCells.Clear();
        foreach (var c in _upgradeBackpackGrid.GetChildren()) c.QueueFree();

        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        _upgradeBackpackGrid.AddChild(grid);

        var usable = UsableBackpackSlots(id => IsUpgradeTarget(id) || IsUpgradeMaterial(id));
        foreach (int abs in usable)
        {
            int slot = abs;
            var cell = new UpgradeBackpackCell(slot, Inv[slot], IsUpgradeSlotStaged(slot));
            _upgradeBackpackCells.Add(cell);
            cell.Pressed += () => PlaceUpgradeItem(slot);
            cell.Hovered += (slot, item) => ShowItemTooltip(slot, item);
            cell.Unhovered += HideItemTooltip;
            grid.AddChild(cell);
        }
        for (int i = usable.Count; i < FilteredBackpackMinCells; i++)
            grid.AddChild(new UpgradeBackpackCell(-1, default, false));

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 6);
        footer.AddChild(UiTheme.Pill($"{usable.Count} usable", UiTheme.Gold));
        footer.AddChild(UiTheme.Pill($"{BackpackUsedCount()}/{GridCount}", UiTheme.Edge));
        footer.AddChild(UiTheme.Text($"{Sheet.Gold:n0} gold", 12, UiTheme.Gold, HorizontalAlignment.Right));
        _upgradeBackpackGrid.AddChild(footer);
    }

    private bool IsUpgradeSlotStaged(int absSlot)
    {
        int rel = absSlot - GridStart;
        foreach (int pos in _upgradePositions)
            if (pos == rel) return true;
        return false;
    }

    private const int FilteredBackpackMinCells = 10;

    private List<int> UsableBackpackSlots(System.Func<int, bool> usable)
    {
        var slots = new List<int>();
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (!Inv[abs].IsEmpty && usable(Inv[abs].ItemId)) slots.Add(abs);
        return slots;
    }

    private int BackpackUsedCount()
    {
        int used = 0;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (!Inv[abs].IsEmpty) used++;
        return used;
    }

    private void PlaceUpgradeItem(int absSlot)
    {
        if (_upgradeBusy || absSlot < GridStart || absSlot >= Inv.Length || Inv[absSlot].IsEmpty)
            return;

        int rel = absSlot - GridStart;
        for (int i = 0; i < _upgradePositions.Length; i++)
            if (_upgradePositions[i] == rel)
            {
                ClearUpgradeSocket(i);
                return;
            }

        var item = Inv[absSlot];
        int target;
        if (_upgradeItemIds[0] == 0 && IsUpgradeTarget(item.ItemId))
        {
            target = 0;
        }
        else if (IsUpgradeMaterial(item.ItemId)
                 || (_upgradeItemIds[0] != 0 && item.ItemId == _upgradeItemIds[0]))
        {
            target = -1;
            for (int i = 1; i < _upgradeItemIds.Length; i++)
                if (_upgradeItemIds[i] == 0) { target = i; break; }

            if (target < 0)
            {
                SetUpgradeStatus("All material sockets are full.", true);
                return;
            }
        }
        else
        {
            SetUpgradeStatus(UpgradePlacementError(item.ItemId), true);
            return;
        }

        _upgradeItemIds[target] = item.ItemId;
        _upgradePositions[target] = rel;
        _upgradeSockets[target].Set(item.ItemId, item.Count, item.Durability);
        RefreshUpgradeBackpack();
        OnUpgradeBenchChanged();
    }

    private void ClearUpgradeSocket(int index)
    {
        if (index < 0 || index >= _upgradeItemIds.Length || _upgradeBusy) return;
        if (_upgradeItemIds[index] == 0) return;
        _upgradeItemIds[index] = 0;
        _upgradePositions[index] = -1;
        _upgradeSockets[index]?.Clear();
        RefreshUpgradeBackpack();
        OnUpgradeBenchChanged();
    }

    private void ClearUpgradeSockets()
    {
        for (int i = 0; i < _upgradeItemIds.Length; i++)
        {
            _upgradeItemIds[i] = 0;
            _upgradePositions[i] = -1;
            _upgradeSockets[i]?.Clear();
        }
        _upgradePreviewId = 0;
        _upgradeResultSocket?.Clear();
        RefreshUpgradeActions();
    }

    private void OnUpgradeBenchChanged()
    {
        _upgradePreviewId = 0;
        _upgradeResultSocket.Clear();

        if (_upgradeItemIds[0] == 0)
        {
            _upgradeTarget.Text = "Place the item to upgrade.";
            SetUpgradeStatus("", false);
            RefreshUpgradeActions();
            return;
        }

        _upgradeTarget.Text = ItemData.DisplayName(_upgradeItemIds[0]);
        if (!HasUpgradeMaterial())
        {
            SetUpgradeStatus("Add the upgrade materials.", false);
            RefreshUpgradeActions();
            return;
        }

        SetUpgradeStatus($"Checking {UpgradeOperationName()} recipe...", false);
        RefreshUpgradeActions();
        Net.I.SendUpgradeRequest(_upgradeAnvilId, _upgradeItemIds, _upgradePositions, preview: true);
    }

    private bool HasUpgradeMaterial()
    {
        for (int i = 1; i < _upgradeItemIds.Length; i++)
            if (_upgradeItemIds[i] != 0) return true;
        return false;
    }

    private string UpgradeOperationName()
    {
        for (int i = 1; i < _upgradeItemIds.Length; i++)
        {
            int id = _upgradeItemIds[i];
            if (id >= AccessoryCompoundScrollFirst && id <= AccessoryCompoundScrollLast)
                return "Accessory compound";
            if (id == ReverseScroll)
                return "Reverse conversion";
            if (id == ReverseStrengthenScroll)
                return "Reverse upgrade";
            if (id is UpgradeScrollHigh or UpgradeScrollHighBlessed or UpgradeScrollClass
                or UpgradeScrollMiddle or UpgradeScrollLow or UpgradeScrollTraining)
                return "Upgrade";
            if (IsBonusScroll(id))
                return "Bonus";
        }
        return "Upgrade";
    }

    private static bool IsUpgradeMaterial(int itemId) => itemId switch
    {
        >= UpgradeScrollHigh and <= BonusScrollHighLast => true,
        >= DispelScrollFirst and <= DispelScrollLast => true,
        UpgradeScrollClass => true,
        >= AccessoryCompoundScrollFirst and <= AccessoryCompoundScrollLast => true,
        >= UpgradeScrollMiddle and <= BonusScrollMiddleLast => true,
        >= UpgradeScrollLow and <= BonusScrollLowLast => true,
        UpgradeScrollTraining or ReverseScroll or ReverseStrengthenScroll or KarivdisPiece => true,
        TrinaPiece or TrinaPieceMiddle or TrinaPieceLow or TrinaPieceAccessory => true,
        RebirthRestorationScroll or BlessingLogos => true,
        _ => false
    };

    private static bool IsUpgradeTarget(int itemId)
    {
        if (IsUpgradeMaterial(itemId)) return false;
        var def = ItemData.Get(itemId);
        return def != null && def.Countable == 0
               && def.Slot >= EquipSlotFirst && def.Slot <= EquipSlotLast
               && (def.Kind < EtcKindFirst || def.Kind > EtcKindLast);
    }

    public string UpgradePlacementReport(params int[] itemIds)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (int id in itemIds)
        {
            string role = IsUpgradeMaterial(id) ? "material" : IsUpgradeTarget(id) ? "item" : "REJECT";
            parts.Add($"{ItemData.DisplayName(id)}={role}");
        }
        return string.Join(" | ", parts);
    }

    private string UpgradePlacementError(int itemId)
    {
        if (IsUpgradeTarget(itemId))
            return $"The item socket already holds {ItemData.DisplayName(_upgradeItemIds[0])}. Clear it first.";
        if (_upgradeItemIds[0] == 0)
            return $"{ItemData.DisplayName(itemId)} cannot be upgraded.";
        return $"{ItemData.DisplayName(itemId)} is not an upgrade material.";
    }

    private static bool IsBonusScroll(int itemId)
        => (itemId > UpgradeScrollHigh && itemId <= BonusScrollHighLast)
           || (itemId >= DispelScrollFirst && itemId <= DispelScrollLast)
           || (itemId > UpgradeScrollMiddle && itemId <= BonusScrollMiddleLast)
           || (itemId > UpgradeScrollLow && itemId <= BonusScrollLowLast);

    private void ConfirmUpgrade()
    {
        if (!CanSendUpgrade()) return;
        DismissUpgradeConfirm();
        _upgradeConfirm = Notice.Confirm(
            this,
            "The item might be destroyed while performing the upgrade. Will you continue?",
            "Upgrade", "Cancel",
            SendUpgrade,
            () => _upgradeConfirm = null,
            "Magic Anvil");
    }

    private void SendUpgrade()
    {
        _upgradeConfirm = null;
        if (!CanSendUpgrade()) return;
        _upgradeBusy = true;
        RefreshUpgradeActions();
        SetUpgradeStatus($"{UpgradeOperationName()} in progress...", false);
        Net.I.SendUpgradeRequest(_upgradeAnvilId, _upgradeItemIds, _upgradePositions, preview: false);
    }

    private bool CanSendUpgrade()
        => _upgradeShown && !_upgradeBusy && _upgradeAnvilId != 0
           && _upgradeItemIds[0] != 0 && _upgradePreviewId != 0;

    private void OnUpgradeResult(UpgradeResult result)
    {
        if (result.UpgradeType == UpgradeTypePreview)
        {
            OnUpgradePreviewResult(result);
            return;
        }

        _upgradeBusy = false;
        int resultItemId = result.Slots.Length > 0 ? result.Slots[0].ItemId : 0;

        switch (result.ResultCode)
        {
            case UpgradeResultSucceeded when resultItemId != 0:
                _upgradeTarget.Text = ItemData.DisplayName(resultItemId);
                _upgradeResultSocket.Set(resultItemId, 1, ResultDurability(resultItemId));
                SetUpgradeStatus("Upgrade succeeded.", false);
                CombatNotice($"Upgrade succeeded: {ItemData.DisplayName(resultItemId)}");
                break;
            case UpgradeResultFailed:
                SetUpgradeStatus("Upgrade failed — the item was destroyed.", true);
                CombatNotice("The upgrade failed and the item was destroyed.");
                break;
            default:
                SetUpgradeStatus(UpgradeError(result.ResultCode), true);
                break;
        }

        if (result.ResultCode is UpgradeResultSucceeded or UpgradeResultFailed)
        {
            ClearUpgradeSockets();
            _upgradeTarget.Text = "Place the item to upgrade.";
            if (result.ResultCode == UpgradeResultSucceeded && resultItemId != 0)
                _upgradeResultSocket.Set(resultItemId, 1, ResultDurability(resultItemId));
        }

        RefreshUpgradeBackpack();
        RefreshUpgradeActions();
        if (CharTabOpen()) RefreshInventoryUI();
    }

    private void OnUpgradePreviewResult(UpgradeResult result)
    {
        int previewId = result.Slots.Length > 0 ? result.Slots[0].ItemId : 0;
        if (result.ResultCode == UpgradeResultSucceeded && previewId != 0)
        {
            _upgradePreviewId = previewId;
            _upgradeResultSocket.Set(previewId, 1, ResultDurability(previewId));
            SetUpgradeStatus($"Upgrades to {ItemData.DisplayName(previewId)}.", false);
        }
        else
        {
            _upgradePreviewId = 0;
            _upgradeResultSocket.Clear();
            SetUpgradeStatus(UpgradeError(result.ResultCode), true);
        }
        RefreshUpgradeActions();
    }

    private static string UpgradeError(byte code) => code switch
    {
        UpgradeResultTrading => "Cannot upgrade while trading.",
        UpgradeResultNeedCoins => "You don't have enough coins.",
        UpgradeResultNoMatch => "The items required for upgrade do not match.",
        UpgradeResultSealed => "That item is sealed or rented.",
        _ => "Cannot perform item upgrade.",
    };

    private void SetUpgradeStatus(string text, bool warn)
    {
        _upgradeStatus.Text = text;
        _upgradeStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void RefreshUpgradeActions()
    {
        if (_upgradeBtn == null) return;
        _upgradeBtn.Disabled = !CanSendUpgrade();
    }

    public bool StageAnvilUpgrade(int originItemId, int materialItemId, int extraItemId = 0)
    {
        if (!_upgradeShown) return false;
        int origin = FindBackpackSlot(originItemId);
        int material = FindBackpackSlot(materialItemId);
        int extra = extraItemId == 0 ? -1 : FindBackpackSlot(extraItemId);
        if (origin < 0 || material < 0 || (extraItemId != 0 && extra < 0))
        {
            GD.Print($"[anvil] stage miss origin={originItemId}@{origin} material={materialItemId}@{material} extra={extraItemId}@{extra} bag={UpgradeBagReport()}");
            return false;
        }
        PlaceUpgradeItem(origin);
        PlaceUpgradeItem(material);
        if (extra >= 0) PlaceUpgradeItem(extra);
        return _upgradeItemIds[0] == originItemId && _upgradeItemIds[1] == materialItemId
               && (extraItemId == 0 || _upgradeItemIds[2] == extraItemId);
    }

    public void CommitAnvilUpgrade() => SendUpgrade();

    public Vector2 UpgradeHoverPoint()
    {
        if (_upgradeItemIds[0] != 0 && _upgradeSockets[0] is { } staged)
            return staged.GetGlobalRect().GetCenter();
        if (_upgradePreviewId != 0 && _upgradeResultSocket != null)
            return _upgradeResultSocket.GetGlobalRect().GetCenter();
        foreach (var cell in _upgradeBackpackCells)
            if (GodotObject.IsInstanceValid(cell) && cell.HasItem)
                return cell.GetGlobalRect().GetCenter();
        return Vector2.Zero;
    }

    private int UpgradeSocketSlot(int index)
        => _upgradePositions[index] >= 0 ? GridStart + _upgradePositions[index] : -1;

    private static short ResultDurability(int itemId)
    {
        var def = ItemData.Get(itemId);
        int durability = (def?.Duration ?? 0) + (ItemData.ExtFor(itemId)?.DurationBonus ?? 0);
        return (short)Mathf.Min(durability, short.MaxValue);
    }

    private string UpgradeBagReport()
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (Inv[abs].ItemId != 0) parts.Add($"{abs}:{Inv[abs].ItemId}x{Inv[abs].Count}");
        return parts.Count == 0 ? "empty" : string.Join(" ", parts);
    }

    private int FindBackpackSlot(int itemId)
    {
        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (Inv[abs].ItemId == itemId && !IsUpgradeSlotStaged(abs)) return abs;
        return -1;
    }

    private void OnUpgradeInventorySlot(int absSlot, ItemSlot item)
    {
        if (!_upgradeShown) return;
        if (absSlot < GridStart || absSlot >= GridStart + GridCount) return;

        int rel = absSlot - GridStart;
        bool dropped = false;
        for (int i = 0; i < _upgradePositions.Length; i++)
        {
            if (_upgradePositions[i] != rel) continue;
            if (item.IsEmpty || item.ItemId != _upgradeItemIds[i])
            {
                _upgradeItemIds[i] = 0;
                _upgradePositions[i] = -1;
                _upgradeSockets[i]?.Clear();
                dropped = true;
            }
        }
        RefreshUpgradeBackpack();
        if (dropped && !_upgradeBusy) OnUpgradeBenchChanged();
    }

    private void OnUpgradeInventoryGrid(ItemSlot[] items)
    {
        if (!_upgradeShown) return;
        RefreshUpgradeBackpack();
    }

    private sealed partial class UpgradeSocket : PanelContainer
    {
        public event System.Action? Cleared;
        public event System.Action<ItemSlot>? Hovered;
        public event System.Action? Unhovered;
        private readonly TextureRect _icon;
        private readonly Label _label;
        private readonly Label _count;
        private readonly UpgradeBadge _plus;
        private readonly bool _interactive;
        private ItemSlot _held;

        public UpgradeSocket(string label, float size, bool interactive = true)
        {
            _interactive = interactive;
            CustomMinimumSize = new Vector2(size, size);
            AddThemeStyleboxOverride("panel", UiTheme.Slot());

            _label = UiTheme.Text(label, 11, UiTheme.TextDim, HorizontalAlignment.Center);
            _label.SetAnchorsPreset(LayoutPreset.FullRect);
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_label);

            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_icon);

            _count = HudStyle.Label(11, HorizontalAlignment.Right);
            _count.SetAnchorsPreset(LayoutPreset.BottomRight);
            _count.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(_count);

            _plus = UpgradeBadge.Attach(this);

            MouseEntered += () => { if (!_held.IsEmpty) Hovered?.Invoke(_held); };
            MouseExited += () => Unhovered?.Invoke();
        }

        public void Set(int itemId, short count, short durability)
        {
            _held = new ItemSlot { ItemId = itemId, Count = count, Durability = durability };
            _icon.Texture = ItemData.Icon(itemId);
            _label.Visible = false;
            _count.Text = count > 1 ? count.ToString() : "";
            _plus.Set(itemId);
            AddThemeStyleboxOverride("panel", UiTheme.Slot(UiTheme.Gold));
        }

        public void Clear()
        {
            _held = default;
            _icon.Texture = null;
            _label.Visible = true;
            _count.Text = "";
            _plus.Clear();
            AddThemeStyleboxOverride("panel", UiTheme.Slot());
            Unhovered?.Invoke();
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (!_interactive) return;
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
                or InputEventMouseButton { Pressed: true, DoubleClick: true, ButtonIndex: MouseButton.Left })
                Cleared?.Invoke();
        }
    }

    private sealed partial class UpgradeBackpackCell : PanelContainer
    {
        public event System.Action? Pressed;
        public event System.Action<int, ItemSlot>? Hovered;
        public event System.Action? Unhovered;
        private readonly ItemSlot _item;

        public bool HasItem => !_item.IsEmpty;

        public UpgradeBackpackCell(int absSlot, ItemSlot item, bool staged)
        {
            _item = item;
            MouseEntered += () => { if (!_item.IsEmpty) Hovered?.Invoke(absSlot, _item); };
            MouseExited += () => Unhovered?.Invoke();
            CustomMinimumSize = new Vector2(48, 48);
            AddThemeStyleboxOverride("panel",
                item.IsEmpty ? UiTheme.Slot() : UiTheme.Slot(staged ? UiTheme.Gold : UiTheme.Edge));

            if (item.IsEmpty)
                return;

            var icon = new TextureRect
            {
                Texture = ItemData.Icon(item.ItemId),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = staged ? new Color(1f, 1f, 1f, 0.4f) : Colors.White,
            };
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(icon);
            if (UpgradeBadge.Show(this, item.ItemId) is { } badge) badge.Modulate = icon.Modulate;

            if (item.Count > 1)
            {
                var count = HudStyle.Label(11, HorizontalAlignment.Right);
                count.Text = item.Count.ToString();
                count.SetAnchorsPreset(LayoutPreset.BottomRight);
                count.MouseFilter = MouseFilterEnum.Ignore;
                AddChild(count);
            }
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (!_item.IsEmpty && ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Pressed?.Invoke();
        }
    }
}
