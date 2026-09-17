using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _repairLayer = null!;
    private HudWindow _repairPanel = null!;
    private VBoxContainer _repairList = null!;
    private Label _repairGoldLbl = null!, _repairStatus = null!;
    private bool _repairShown;

    private readonly Queue<int> _repairQueue = new();
    private bool _repairInFlight;
    private int _repairCur = -1;

    private void RepairInit()
    {
        BuildRepairPanel();
        Net.I.RepairNpcEvent += OnRepairOpen;
        Net.I.ItemRepairResultEvent += OnRepairResult;
        Net.I.GoldChangeEvent += OnRepairGold;
    }

    private void RepairDispose()
    {
        Net.I.RepairNpcEvent -= OnRepairOpen;
        Net.I.ItemRepairResultEvent -= OnRepairResult;
        Net.I.GoldChangeEvent -= OnRepairGold;
    }

    private void OnRepairGold(int g) { if (_repairShown) _repairGoldLbl.Text = $"{g:n0} gold"; }

    private void BuildRepairPanel()
    {
        _repairLayer = new CanvasLayer { Layer = 74 };
        AddChild(_repairLayer);

        _repairPanel = new HudWindow("repair", "Repair", new Vector2(360, 140), 340) { Visible = false };
        _repairPanel.Closed += CloseRepair;
        _repairLayer.AddChild(_repairPanel);

        var root = _repairPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(340, 340),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);
        _repairList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _repairList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_repairList);

        root.AddChild(new HSeparator());
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        var allBtn = new Button { Text = "Repair All", FocusMode = Control.FocusModeEnum.None };
        allBtn.Pressed += RepairAll;
        btnRow.AddChild(allBtn);
        _repairStatus = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        _repairStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btnRow.AddChild(_repairStatus);
        root.AddChild(btnRow);

        var footer = new HBoxContainer();
        var lbl = UiTheme.Text("Gold", 13, UiTheme.TextLo);
        lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(lbl);
        _repairGoldLbl = UiTheme.Text("", 13, UiTheme.Gold, HorizontalAlignment.Right);
        footer.AddChild(_repairGoldLbl);
        root.AddChild(footer);
    }

    private void OnRepairOpen(int sellingGroup)
    {
        ShowNpcServiceChoice(
            "A great blacksmith like me is hard to find. Tell me if you need anything…",
            ("Buy / Sell", () => OnVendorOpen(sellingGroup)),
            ("Repair", OpenRepairList));
    }

    private void OpenRepairList()
    {
        CloseNpcDialog();
        CloseVendor();
        _repairQueue.Clear();
        _repairInFlight = false;
        _repairCur = -1;
        _repairStatus.Text = "";
        _repairGoldLbl.Text = $"{Sheet.Gold:n0} gold";
        RefreshRepairList();
        _repairPanel.Visible = true;
        _repairShown = true;
    }

    private void CloseRepair()
    {
        if (!_repairShown) return;
        _repairShown = false;
        _repairPanel.Visible = false;
    }

    private static int RepairCost(int itemId, ItemData.Item def, int curDur)
    {
        int quantity = def.Duration - curDur;
        if (quantity <= 0 || def.Duration <= 0) return 0;
        int price = ItemData.BuyPrice(itemId);
        double cost = ((price - 10) / 10000.0 + System.Math.Pow(price, 0.75)) * quantity / def.Duration;
        return cost < 0 ? 0 : (int)cost;
    }

    private IEnumerable<int> RepairableSlots()
    {
        for (int abs = 0; abs < Inv.Length && abs < GridStart + GridCount; abs++)
        {
            if (Inv[abs].IsEmpty) continue;
            var def = ItemData.Get(Inv[abs].ItemId);
            if (def != null && def.Duration > 1 && Inv[abs].Durability < def.Duration)
                yield return abs;
        }
    }

    private void RefreshRepairList()
    {
        foreach (var c in _repairList.GetChildren()) c.QueueFree();
        int n = 0;
        long total = 0;
        foreach (int abs in RepairableSlots())
        {
            var slot = Inv[abs];
            var def = ItemData.Get(slot.ItemId);
            if (def == null) continue;
            int cost = RepairCost(slot.ItemId, def, slot.Durability);
            total += cost;
            int a = abs;
            _repairList.AddChild(BuildRepairRow(slot.ItemId, slot.Durability, def.Duration, cost, () => RepairOne(a)));
            n++;
        }
        if (n == 0)
        {
            _repairList.AddChild(UiTheme.Text("Nothing needs repair.", 13, UiTheme.TextLo, HorizontalAlignment.Center));
            _repairStatus.Text = "";
        }
        else
        {
            _repairStatus.Text = $"All: {total:n0} gold";
        }
    }

    private Control BuildRepairRow(int itemId, int cur, int max, int cost, System.Action onRepair)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        hb.AddChild(new TextureRect
        {
            Texture = ItemData.Icon(itemId),
            CustomMinimumSize = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", -2);
        info.AddChild(UiTheme.Text(ItemData.DisplayName(itemId), 13, UiTheme.TextHi));
        info.AddChild(UiTheme.Text($"Durability {cur} / {max}     {cost:n0} gold", 11, UiTheme.TextLo));
        hb.AddChild(info);

        var btn = new Button { Text = "Repair", FocusMode = Control.FocusModeEnum.None };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.Pressed += () => onRepair();
        hb.AddChild(btn);
        return row;
    }

    private void RepairOne(int abs)
    {
        if (_repairInFlight) return;
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
        if (abs < 0 || abs >= Inv.Length || Inv[abs].IsEmpty) { PumpRepair(); return; }
        byte posType;
        byte slot;
        if (abs < GridStart) { posType = 1; slot = (byte)abs; }
        else { posType = 2; slot = (byte)(abs - GridStart); }
        _repairCur = abs;
        _repairInFlight = true;
        Net.I.SendRepair(posType, slot, _vendorNpcId, Inv[abs].ItemId);
    }

    private void OnRepairResult(bool ok, int money)
    {
        if (!_repairInFlight) return;
        _repairInFlight = false;
        if (ok && _repairCur >= 0 && _repairCur < Inv.Length)
        {
            var def = ItemData.Get(Inv[_repairCur].ItemId);
            if (def != null)
            {
                Inv.SetDurability(_repairCur, (short)def.Duration);
                Net.I.MirrorInventorySlot(_repairCur, Inv[_repairCur]);
            }
            if (CharTabOpen()) RefreshInventoryUI();
        }
        else if (!ok)
        {
            _repairStatus.Text = "Repair failed (not enough gold?).";
            _repairQueue.Clear();
        }
        _repairCur = -1;
        if (_repairShown)
        {
            _repairGoldLbl.Text = $"{Sheet.Gold:n0} gold";
            RefreshRepairList();
        }
        PumpRepair();
    }
}
