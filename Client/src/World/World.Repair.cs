using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int RepairColumns = 8;
    private const int RepairPageSize = 24;
    private const float RepairCellSize = 44f;
    private const float RepairDetailSlotSize = 40f;
    private const float RepairWornShare = 0.25f;
    private const float RepairUsedShare = 0.6f;
    private const int RepairBarSteps = 1000;
    private const float RepairBarWidth = 96f;
    private const string RepairHint = "Right-click gear to repair it";
    private const string RepairChoiceText = "A great blacksmith like me is hard to find. Tell me if you need anything…";
    private static readonly Color RepairWornColour = new("a0522d");

    private CanvasLayer _repairLayer = null!;
    private HudWindow _repairPanel = null!;
    private Label _repairNote = null!;
    private ServicePager _repairPager = null!;
    private Label _repairEmpty = null!;
    private readonly ItemSlotView[] _repairCells = new ItemSlotView[RepairPageSize];
    private readonly int[] _repairCellSlots = new int[RepairPageSize];
    private DetailStrip _repairDetail = null!;
    private Control _repairBox = null!;
    private ProgressBar _repairBar = null!;
    private Label _repairDurability = null!, _repairCost = null!;
    private Button _repairOneBtn = null!, _repairAllBtn = null!;
    private FooterBand _repairFooter = null!;
    private MoneyPlaque _repairWallet = null!;
    private BagCompanion? _repairCompanion;
    private bool _repairShown;
    private int _repairPage;

    private readonly Queue<int> _repairQueue = new();
    private bool _repairInFlight;
    private int _repairCur = -1;
    private int _repairItemId;
    private int _repairSelected = -1;

    private void RepairInit()
    {
        BuildRepairPanel();
        Net.I.RepairNpcEvent += OnRepairOpen;
        Net.I.ItemRepairResultEvent += OnRepairResult;
        Net.I.InventorySlotEvent += OnRepairInventorySlot;
        Net.I.InventoryGridRefreshEvent += OnRepairInventoryGrid;
        Net.I.GoldChangeEvent += OnRepairGold;
    }

    private void RepairDispose()
    {
        Net.I.RepairNpcEvent -= OnRepairOpen;
        Net.I.ItemRepairResultEvent -= OnRepairResult;
        Net.I.InventorySlotEvent -= OnRepairInventorySlot;
        Net.I.InventoryGridRefreshEvent -= OnRepairInventoryGrid;
        Net.I.GoldChangeEvent -= OnRepairGold;
    }

    private void OnRepairOpen(int sellingGroup)
    {
        ShowNpcServiceChoice(RepairChoiceText,
            ("Buy / Sell", () => OpenVendor(sellingGroup)),
            ("Repair", OpenRepair));
    }

    private void BuildRepairPanel()
    {
        _repairLayer = new CanvasLayer { Layer = 74 };
        AddChild(_repairLayer);

        _repairPanel = new HudWindow("repair", "Repair") { Visible = false };
        _repairPanel.Closed += CloseRepair;
        _repairLayer.AddChild(_repairPanel);

        var root = _repairPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        var head = ServiceKit.Section("Damaged gear", UiIcons.Get("system/hammer"), out _repairNote);
        _repairPager = new ServicePager();
        _repairPager.PageChanged += page => { _repairPage = page; RefreshRepairWindow(); };
        head.AddChild(_repairPager);
        root.AddChild(head);

        var well = new DropWell { CanDrop = CanRepairDrop, Dropped = RepairDrop };
        well.Wheeled += _repairPager.Step;
        root.AddChild(well);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        well.AddChild(stack);
        var grid = new GridContainer { Columns = RepairColumns, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        stack.AddChild(grid);
        for (int i = 0; i < _repairCells.Length; i++)
        {
            var cell = new ItemSlotView(RepairCellSize) { Index = i };
            cell.Clicked += c => { if (_repairCellSlots[c.Index] >= 0) SelectRepair(_repairCellSlots[c.Index]); };
            cell.DoubleClicked += c => RepairOne(_repairCellSlots[c.Index]);
            cell.RightClicked += c => RepairOne(_repairCellSlots[c.Index]);
            cell.Hovered += c =>
            {
                int abs = _repairCellSlots[c.Index];
                if (abs >= 0) ShowItemTooltip(abs, Inv[abs], RepairNote(abs));
            };
            cell.Unhovered += _ => HideItemTooltip();
            cell.Wheeled += (_, step) => _repairPager.Step(step);
            cell.CanDrop = (_, data) => CanRepairDrop(data);
            cell.Dropped = (_, data) => RepairDrop(data);
            _repairCells[i] = cell;
            grid.AddChild(cell);
        }
        _repairEmpty = UiTheme.Text("Nothing needs repair.", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _repairEmpty.Visible = false;
        stack.AddChild(_repairEmpty);

        _repairDetail = new DetailStrip(RepairDetailSlotSize);
        root.AddChild(_repairDetail);
        BuildRepairBox(root);

        _repairFooter = new FooterBand { Hint = RepairHint };
        _repairWallet = new MoneyPlaque("");
        _repairFooter.Left.AddChild(_repairWallet);
        _repairAllBtn = UiTheme.ActionButton("Repair All", "Repair every damaged item you carry and wear");
        _repairAllBtn.Pressed += RepairAll;
        _repairFooter.Right.AddChild(_repairAllBtn);
        root.AddChild(_repairFooter);
    }

    private void BuildRepairBox(Control parent)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var gauge = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        gauge.AddThemeConstantOverride("separation", 2);
        _repairBar = new ProgressBar
        {
            MinValue = 0, MaxValue = RepairBarSteps, ShowPercentage = false,
            CustomMinimumSize = new Vector2(RepairBarWidth, 6),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _repairBar.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        gauge.AddChild(_repairBar);
        _repairDurability = UiTheme.Text("", 11, UiTheme.TextLo);
        gauge.AddChild(_repairDurability);
        box.AddChild(gauge);
        _repairCost = UiTheme.Text("", 12, UiTheme.Gold, HorizontalAlignment.Right);
        _repairCost.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _repairCost.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        box.AddChild(_repairCost);
        _repairOneBtn = UiTheme.SmallButton("Repair", "Repair this item");
        _repairOneBtn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _repairOneBtn.Pressed += () => RepairOne(_repairSelected);
        box.AddChild(_repairOneBtn);
        box.Visible = false;
        _repairBox = box;
        parent.AddChild(box);
    }

    private void OpenRepair()
    {
        CloseNpcDialog();
        _repairQueue.Clear();
        _repairPage = 0;
        _repairSelected = -1;
        _repairPanel.Title = _vendorNpcName;
        _repairFooter.ResetStatus();
        _repairWallet.Value = Sheet.Gold;
        _repairPanel.Visible = true;
        _repairShown = true;
        _repairCompanion ??= new BagCompanion(RepairTakeFromBag, _ => BagFit.Normal, RepairBagNote, CloseRepair);
        AttachBagCompanion(_repairCompanion);
        RefreshRepairWindow();
    }

    private void CloseRepair()
    {
        HideItemTooltip();
        if (!_repairShown) return;
        _repairShown = false;
        _repairQueue.Clear();
        _repairPanel.Visible = false;
        if (_repairCompanion != null) DetachBagCompanion(_repairCompanion);
    }

    private void OnRepairGold(int total)
    {
        if (!_repairShown) return;
        _repairWallet.Value = Sheet.Gold;
        RefreshRepairWindow();
    }

    private static Color RepairTint(float share) =>
        share < RepairWornShare ? RepairWornColour : share < RepairUsedShare ? UiTheme.Gold : UiTheme.Good;

    private bool IsRepairable(int abs)
    {
        if (abs < 0 || abs >= Inv.Length || abs >= GridStart + GridCount || Inv[abs].IsEmpty) return false;
        var def = ItemData.Get(Inv[abs].ItemId);
        int max = RepairMax(abs);
        return def != null && max > 1 && def.SaleType != ItemData.SaleTypeLowNoRepair && Inv[abs].Durability < max;
    }

    private int RepairMax(int abs) => ItemData.MaxDurability(ItemData.Get(Inv[abs].ItemId), ItemData.ExtFor(Inv[abs].ItemId));

    private IEnumerable<int> RepairableSlots()
    {
        for (int abs = 0; abs < Inv.Length && abs < GridStart + GridCount; abs++)
            if (IsRepairable(abs)) yield return abs;
    }

    private int RepairCostAt(int abs)
    {
        var def = ItemData.Get(Inv[abs].ItemId);
        return def == null ? 0 : RepairPrice.Cost(ItemData.BuyPrice(Inv[abs].ItemId), RepairMax(abs), Inv[abs].Durability);
    }

    private string RepairNote(int abs) => $"Repair cost: {RepairCostAt(abs):n0} gold";

    private string RepairBagNote(int abs) => IsRepairable(abs) ? RepairNote(abs) : "";

    private float RepairShare(int abs)
    {
        int max = RepairMax(abs);
        return max > 0 ? Mathf.Clamp((float)Inv[abs].Durability / max, 0f, 1f) : 0f;
    }

    private bool RepairTakeFromBag(int abs)
    {
        if (abs < 0 || abs >= Inv.Length || Inv[abs].IsEmpty) return false;
        if (IsRepairable(abs)) RepairOne(abs);
        else _repairFooter.Status("That doesn't need repair.", bad: true);
        return true;
    }

    private bool CanRepairDrop(Variant data) =>
        data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary().ContainsKey("invFrom")
        && IsRepairable(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void RepairDrop(Variant data) => RepairOne(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void RefreshRepairWindow()
    {
        var slots = RepairableSlots().ToList();
        int pages = Mathf.Max(1, (slots.Count + RepairPageSize - 1) / RepairPageSize);
        _repairPage = Paging.Step(_repairPage, 0, pages);
        _repairPager.Set(_repairPage, pages);
        _repairPager.Visible = pages > 1;
        int first = _repairPage * RepairPageSize;
        for (int i = 0; i < _repairCells.Length; i++)
        {
            int at = first + i;
            _repairCellSlots[i] = at < slots.Count ? slots[at] : -1;
            if (at < slots.Count)
            {
                _repairCells[i].Set(Inv[slots[at]]);
                float share = RepairShare(slots[at]);
                _repairCells[i].SetWear(share, RepairTint(share));
            }
            else
            {
                _repairCells[i].Clear();
                _repairCells[i].SetWear(null, default);
            }
        }
        _repairEmpty.Visible = slots.Count == 0;
        long total = slots.Sum(abs => (long)RepairCostAt(abs));
        _repairNote.Text = slots.Count == 0 ? "" : $"{(slots.Count == 1 ? "1 item" : $"{slots.Count} items")} · {total:n0} gold";
        _repairNote.AddThemeColorOverride("font_color", total > Sheet.Gold ? UiTheme.Bad : UiTheme.TextDim);
        _repairAllBtn.Disabled = slots.Count == 0 || _repairInFlight;
        SelectRepair(_repairSelected >= 0 && _repairCellSlots.Contains(_repairSelected) ? _repairSelected : _repairCellSlots[0]);
    }

    private void SelectRepair(int abs)
    {
        _repairSelected = abs;
        for (int i = 0; i < _repairCells.Length; i++)
            _repairCells[i].Look = abs >= 0 && _repairCellSlots[i] == abs ? SlotLook.Selected : SlotLook.Normal;
        if (abs < 0 || !IsRepairable(abs))
        {
            _repairDetail.Slot.Clear();
            _repairDetail.Title.Text = "Nothing selected";
            _repairDetail.Title.AddThemeColorOverride("font_color", UiTheme.TextLo);
            _repairDetail.Sub.Text = "";
            _repairBox.Visible = false;
            return;
        }
        float share = RepairShare(abs);
        int cost = RepairCostAt(abs);
        _repairDetail.Slot.Set(Inv[abs]);
        _repairDetail.Title.Text = ItemData.DisplayName(Inv[abs].ItemId);
        _repairDetail.Title.AddThemeColorOverride("font_color", ItemGrade.Tint(Inv[abs].ItemId));
        _repairDetail.Sub.Text = abs < GridStart ? "Worn" : "In your bag";
        _repairDetail.Sub.AddThemeColorOverride("font_color", UiTheme.TextLo);
        _repairBar.Value = share * RepairBarSteps;
        _repairBar.AddThemeStyleboxOverride("fill", CachedMeterFill(RepairTint(share)));
        _repairDurability.Text = $"{Inv[abs].Durability:n0} / {RepairMax(abs):n0}";
        _repairCost.Text = $"{cost:n0} gold";
        _repairCost.AddThemeColorOverride("font_color", cost > Sheet.Gold ? UiTheme.Bad : UiTheme.Gold);
        _repairOneBtn.Disabled = _repairInFlight;
        _repairBox.Visible = true;
    }

    private void RepairOne(int abs)
    {
        if (_repairInFlight || !IsRepairable(abs)) return;
        SendRepairFor(abs);
    }

    private void RepairAll()
    {
        if (_repairInFlight) return;
        _repairQueue.Clear();
        foreach (int abs in RepairableSlots()) _repairQueue.Enqueue(abs);
        PumpRepair();
    }

    private void PumpRepair()
    {
        if (_repairInFlight || _repairQueue.Count == 0) return;
        SendRepairFor(_repairQueue.Dequeue());
    }

    private void SendRepairFor(int abs)
    {
        if (!IsRepairable(abs)) { PumpRepair(); return; }
        byte posType = (byte)(abs < GridStart ? 1 : 2);
        byte slot = (byte)(abs < GridStart ? abs : abs - GridStart);
        _repairCur = abs;
        _repairItemId = Inv[abs].ItemId;
        _repairInFlight = true;
        _repairOneBtn.Disabled = true;
        _repairAllBtn.Disabled = true;
        Net.I.SendRepair(posType, slot, _vendorNpcId, Inv[abs].ItemId);
    }

    private void OnRepairResult(bool ok, int money)
    {
        if (!_repairInFlight) return;
        _repairInFlight = false;
        if (ok && Inv.Holds(_repairCur, _repairItemId))
        {
            Inv.SetDurability(_repairCur, ItemData.MaxDurabilityOf(Inv[_repairCur].ItemId));
            Net.I.MirrorInventorySlot(_repairCur, Inv[_repairCur]);
            if (CharTabOpen()) RefreshInventoryUI();
            _repairFooter.Status($"Repaired {ItemData.DisplayName(Inv[_repairCur].ItemId)}.", bad: false);
            Audio.PlayUi(Sfx.UiRepair);
        }
        else if (!ok)
        {
            _repairFooter.Status("Repair failed (not enough gold?).", bad: true);
            _repairQueue.Clear();
        }
        _repairCur = -1;
        _repairItemId = 0;
        if (_repairShown) RefreshRepairWindow();
        PumpRepair();
    }

    private void OnRepairInventorySlot(int absSlot, ItemSlot item)
    {
        if (_repairShown) RefreshRepairWindow();
    }

    private void OnRepairInventoryGrid(ItemSlot[] items)
    {
        if (_repairShown) RefreshRepairWindow();
    }
}
