using Godot;

namespace LibreKO.Features.Windows;

internal sealed class RepairFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ItemsSession items;
    private readonly NpcQuestSession npcQuest;
    private readonly ProgressSession progress;
    private readonly RepairSession repair;
    private readonly SelfSession self;
    private readonly IRepairRequests requests;
    private readonly VendorFeature vendor;
    private readonly IInteractionDialogs interactionDialogs;

    public RepairFeature(IWorldContext context, ItemsSession items, NpcQuestSession npcQuest, ProgressSession progress, RepairSession repair, SelfSession self, IRepairRequests requests, VendorFeature vendor, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.items = items;
        this.npcQuest = npcQuest;
        this.progress = progress;
        this.repair = repair;
        this.self = self;
        this.requests = requests;
        this.vendor = vendor;
        this.interactionDialogs = interactionDialogs;
    }

    private const int Columns = 8;
    private const int PageSize = 24;
    private const float CellSize = 44f;
    private const float DetailSlotSize = 40f;
    private const float WornShare = 0.25f;
    private const float UsedShare = 0.6f;
    private const int BarSteps = 1000;
    private const float BarWidth = 96f;
    private const string Hint = "Right-click gear to repair it";
    private const string ChoiceText = "A great blacksmith like me is hard to find. Tell me if you need anything…";
    private static readonly Color WornColour = new("a0522d");

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private Label note = null!;
    private ServicePager pager = null!;
    private Label empty = null!;
    private readonly ItemSlotView[] cells = new ItemSlotView[PageSize];
    private readonly int[] cellSlots = new int[PageSize];
    private DetailStrip detail = null!;
    private Control box = null!;
    private ProgressBar bar = null!;
    private Label durability = null!;
    private Label costLabel = null!;
    private Button oneBtn = null!;
    private Button allBtn = null!;
    private FooterBand footer = null!;
    private MoneyPlaque wallet = null!;
    private BagCompanion? companion;
    private bool shown;

    public bool IsOpen => shown;

    private int page;
    private readonly Queue<int> queue = new();
    internal bool InFlight { get; private set; }
    private int cur = -1;
    private int itemId;
    private int selected = -1;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        repair.RepairNpcEvent += OnOpen;
        repair.ItemRepairResultEvent += OnResult;
        items.InventorySlotEvent += OnInventorySlot;
        items.InventoryGridRefreshEvent += OnInventoryGrid;
        progress.GoldChangeEvent += OnGold;
    }

    public void Dispose()
    {
        repair.RepairNpcEvent -= OnOpen;
        repair.ItemRepairResultEvent -= OnResult;
        items.InventorySlotEvent -= OnInventorySlot;
        items.InventoryGridRefreshEvent -= OnInventoryGrid;
        progress.GoldChangeEvent -= OnGold;
    }

    internal void OnOpen(int sellingGroup)
    {
        context.NpcDialog.ShowServiceChoice(ChoiceText,
            ("Buy / Sell", () => vendor.Open(sellingGroup)),
            ("Repair", Open));
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("repair", "Repair") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);

        var head = ServiceKit.Section("Damaged gear", UiIcons.Get("system/hammer"), out note);
        pager = new ServicePager();
        pager.PageChanged += page => { this.page = page; RefreshWindow(); };
        head.AddChild(pager);
        root.AddChild(head);

        var well = new DropWell { CanDrop = CanDrop, Dropped = Drop };
        well.Wheeled += pager.Step;
        root.AddChild(well);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        well.AddChild(stack);
        var grid = new GridContainer { Columns = Columns, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        stack.AddChild(grid);
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new ItemSlotView(CellSize) { Index = i, Veiled = true };
            cell.Clicked += c => { if (cellSlots[c.Index] >= 0) Select(cellSlots[c.Index]); };
            cell.DoubleClicked += c => One(cellSlots[c.Index]);
            cell.RightClicked += c => One(cellSlots[c.Index]);
            cell.Hovered += c =>
            {
                int abs = cellSlots[c.Index];
                if (abs >= 0) context.Tooltips.Show(abs, self.Inventory[abs], RepairNote(abs));
            };
            cell.Unhovered += _ => context.Tooltips.Hide();
            cell.Wheeled += (_, step) => pager.Step(step);
            cell.CanDrop = (_, data) => CanDrop(data);
            cell.Dropped = (_, data) => Drop(data);
            cells[i] = cell;
            grid.AddChild(cell);
        }
        empty = UiTheme.Text("Nothing needs repair.", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        empty.Visible = false;
        stack.AddChild(empty);

        detail = new DetailStrip(DetailSlotSize);
        root.AddChild(detail);
        BuildBox(root);

        footer = new FooterBand { Hint = Hint };
        wallet = new MoneyPlaque("");
        footer.Left.AddChild(wallet);
        allBtn = UiTheme.ActionButton("Repair All", "Repair every damaged item you carry and wear");
        allBtn.Pressed += All;
        footer.Right.AddChild(allBtn);
        root.AddChild(footer);
    }

    private void BuildBox(Control parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var gauge = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        gauge.AddThemeConstantOverride("separation", 2);
        bar = new ProgressBar
        {
            MinValue = 0, MaxValue = BarSteps, ShowPercentage = false,
            CustomMinimumSize = new Vector2(BarWidth, 6),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        gauge.AddChild(bar);
        durability = UiTheme.Text("", 11, UiTheme.TextLo);
        gauge.AddChild(durability);
        row.AddChild(gauge);
        costLabel = UiTheme.Text("", 12, UiTheme.Gold, HorizontalAlignment.Right);
        costLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        costLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(costLabel);
        oneBtn = UiTheme.SmallButton("Repair", "Repair this item");
        oneBtn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        oneBtn.Pressed += () => One(selected);
        row.AddChild(oneBtn);
        row.Visible = false;
        box = row;
        parent.AddChild(row);
    }

    internal void Open()
    {
        context.NpcDialog.Close();
        queue.Clear();
        page = 0;
        selected = -1;
        Panel.Title = npcQuest.LastNpcName;
        footer.ResetStatus();
        wallet.Value = self.Sheet.Gold;
        Panel.Visible = true;
        shown = true;
        companion ??= new BagCompanion(TakeFromBag, _ => BagFit.Normal, BagNote, Close);
        context.BagCompanions.Attach(companion);
        RefreshWindow();
    }

    public void Close()
    {
        context.Tooltips.Hide();
        if (!shown) return;
        shown = false;
        queue.Clear();
        Panel.Visible = false;
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    private void OnGold(GoldChange change)
    {
        if (!shown) return;
        wallet.Value = self.Sheet.Gold;
        RefreshWindow();
    }

    private static Color Tint(float share) =>
        share < WornShare ? WornColour : share < UsedShare ? UiTheme.Gold : UiTheme.Good;

    private bool IsRepairable(int abs)
    {
        if (abs < 0 || abs >= self.Inventory.Length || abs >= Inventory.GridStart + Inventory.GridCount || self.Inventory[abs].IsEmpty) return false;
        var def = ItemData.Get(self.Inventory[abs].ItemId);
        int max = Max(abs);
        return def != null && RepairPrice.Listed(def.IsChargeItem, def.SaleType == ItemData.SaleTypeLowNoRepair,
            max, self.Inventory[abs].Durability, CostAt(abs));
    }

    private int Max(int abs) => ItemData.MaxDurability(ItemData.Get(self.Inventory[abs].ItemId), ItemData.ExtFor(self.Inventory[abs].ItemId));

    private IEnumerable<int> RepairableSlots()
    {
        for (int abs = 0; abs < self.Inventory.Length && abs < Inventory.GridStart + Inventory.GridCount; abs++)
            if (IsRepairable(abs)) yield return abs;
    }

    private int CostAt(int abs)
    {
        var def = ItemData.Get(self.Inventory[abs].ItemId);
        return def == null ? 0 : RepairPrice.Cost(ItemData.BuyPrice(self.Inventory[abs].ItemId), Max(abs), self.Inventory[abs].Durability);
    }

    private string RepairNote(int abs) => $"Repair cost: {CostAt(abs):n0} gold";

    private string BagNote(int abs) => IsRepairable(abs) ? RepairNote(abs) : "";

    private float Share(int abs)
    {
        int max = Max(abs);
        return max > 0 ? Mathf.Clamp((float)self.Inventory[abs].Durability / max, 0f, 1f) : 0f;
    }

    private bool TakeFromBag(int abs)
    {
        if (abs < 0 || abs >= self.Inventory.Length || self.Inventory[abs].IsEmpty) return false;
        if (IsRepairable(abs)) One(abs);
        else footer.Status("That doesn't need repair.", bad: true);
        return true;
    }

    private bool CanDrop(Variant data) =>
        data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary().ContainsKey("invFrom")
    && IsRepairable(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void Drop(Variant data) => One(data.AsGodotDictionary()["invFrom"].AsInt32());

    private void RefreshWindow()
    {
        var slots = RepairableSlots().ToList();
        int pages = Mathf.Max(1, (slots.Count + PageSize - 1) / PageSize);
        page = Paging.Step(page, 0, pages);
        pager.Set(page, pages);
        pager.Visible = pages > 1;
        int first = page * PageSize;
        for (int i = 0; i < cells.Length; i++)
        {
            int at = first + i;
            cellSlots[i] = at < slots.Count ? slots[at] : -1;
            if (at < slots.Count)
            {
                cells[i].Set(self.Inventory[slots[at]]);
                float share = Share(slots[at]);
                cells[i].SetWear(share, Tint(share));
            }
            else
            {
                cells[i].Clear();
                cells[i].SetWear(null, default);
            }
        }
        empty.Visible = slots.Count == 0;
        long total = slots.Sum(abs => (long)CostAt(abs));
        note.Text = slots.Count == 0 ? "" : $"{(slots.Count == 1 ? "1 item" : $"{slots.Count} items")} · {total:n0} gold";
        note.AddThemeColorOverride("font_color", total > self.Sheet.Gold ? UiTheme.Bad : UiTheme.TextDim);
        allBtn.Disabled = slots.Count == 0 || InFlight;
        Select(selected >= 0 && cellSlots.Contains(selected) ? selected : cellSlots[0]);
    }

    private void Select(int abs)
    {
        selected = abs;
        for (int i = 0; i < cells.Length; i++)
            cells[i].Look = abs >= 0 && cellSlots[i] == abs ? SlotLook.Selected : SlotLook.Normal;
        if (abs < 0 || !IsRepairable(abs))
        {
            detail.Slot.Clear();
            detail.Title.Text = "Nothing selected";
            detail.Title.AddThemeColorOverride("font_color", UiTheme.TextLo);
            detail.Sub.Text = "";
            box.Visible = false;
            return;
        }
        float share = Share(abs);
        int cost = CostAt(abs);
        detail.Slot.Set(self.Inventory[abs]);
        detail.Title.Text = ItemData.DisplayName(self.Inventory[abs].ItemId);
        detail.Title.AddThemeColorOverride("font_color", ItemGrade.Tint(self.Inventory[abs].ItemId));
        detail.Sub.Text = abs < Inventory.GridStart ? "Worn" : "In your bag";
        detail.Sub.AddThemeColorOverride("font_color", UiTheme.TextLo);
        bar.Value = share * BarSteps;
        bar.AddThemeStyleboxOverride("fill", UiKit.CachedMeterFill(Tint(share)));
        durability.Text = $"{self.Inventory[abs].Durability:n0} / {Max(abs):n0}";
        costLabel.Text = $"{cost:n0} gold";
        costLabel.AddThemeColorOverride("font_color", cost > self.Sheet.Gold ? UiTheme.Bad : UiTheme.Gold);
        oneBtn.Disabled = InFlight;
        box.Visible = true;
    }

    private void One(int abs)
    {
        if (InFlight || !IsRepairable(abs)) return;
        SendFor(abs);
    }

    private void All()
    {
        if (InFlight) return;
        queue.Clear();
        foreach (int abs in RepairableSlots()) queue.Enqueue(abs);
        Pump();
    }

    private void Pump()
    {
        if (InFlight || queue.Count == 0) return;
        SendFor(queue.Dequeue());
    }

    private void SendFor(int abs)
    {
        if (!IsRepairable(abs)) { Pump(); return; }
        byte posType = (byte)(abs < Inventory.GridStart ? 1 : 2);
        byte slot = (byte)(abs < Inventory.GridStart ? abs : abs - Inventory.GridStart);
        cur = abs;
        itemId = self.Inventory[abs].ItemId;
        InFlight = true;
        oneBtn.Disabled = true;
        allBtn.Disabled = true;
        requests.Send(posType, slot, npcQuest.LastNpcId, self.Inventory[abs].ItemId);
    }

    private void OnResult(bool ok, int money)
    {
        if (!InFlight) return;
        InFlight = false;
        if (ok && self.Inventory.Holds(cur, itemId))
        {
            var repaired = self.Inventory[cur];
            repaired.Durability = ItemData.MaxDurabilityOf(repaired.ItemId);
            self.SetInventorySlot(cur, repaired);
            if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
            footer.Status($"Repaired {ItemData.DisplayName(self.Inventory[cur].ItemId)}.", bad: false);
            Audio.PlayUi(Sfx.UiRepair);
        }
        else if (!ok)
        {
            footer.Status("Repair failed (not enough gold?).", bad: true);
            queue.Clear();
        }
        cur = -1;
        itemId = 0;
        if (shown) RefreshWindow();
        Pump();
    }

    private void OnInventorySlot(int absSlot, ItemSlot item)
    {
        if (shown) RefreshWindow();
    }

    private void OnInventoryGrid(ItemSlot[] itemSlots)
    {
        if (shown) RefreshWindow();
    }
}
