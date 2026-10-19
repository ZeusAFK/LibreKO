using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class InventoryFeature : IFeature, IHudTickFeature, IInventoryView
{
    private readonly IWorldContext context;
    private readonly ItemsSession items;
    private readonly PetSession pet;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly VendorSession vendor;
    private readonly IItemsRequests itemsRequests;
    private readonly CosmeticsFeature cosmetics;
    private readonly ItemTooltipFeature itemTooltip;
    private readonly SealFeature seal;
    private readonly PetFeature petWindow;
    private readonly IItemActions itemActions;

    public InventoryFeature(IWorldContext context, ItemsSession items, PetSession pet, ProgressSession progress, SelfSession self, VendorSession vendor, IItemsRequests itemsRequests, CosmeticsFeature cosmetics, ItemTooltipFeature itemTooltip, SealFeature seal, PetFeature petWindow, IItemActions itemActions)
    {
        this.context = context;
        this.items = items;
        this.pet = pet;
        this.progress = progress;
        this.self = self;
        this.vendor = vendor;
        this.itemsRequests = itemsRequests;
        this.cosmetics = cosmetics;
        this.itemTooltip = itemTooltip;
        this.seal = seal;
        this.petWindow = petWindow;
        this.itemActions = itemActions;
    }

    IReadOnlyDictionary<int, ItemCell> IInventoryView.Cells => InvCells;
    event Action? IInventoryView.Refreshed { add => refreshed += value; remove => refreshed -= value; }
    bool IInventoryView.MoveBusy => MoveBusy;
    IReadOnlyList<ItemCell> IInventoryView.BagCells => InvBagCells;
    IReadOnlyList<ItemCell> IInventoryView.MagicBagCells => invMagicBagCells;
    int IInventoryView.CarriedWeight() => CarriedWeight();
    void IInventoryView.Refresh() => RefreshUI();
    void IInventoryView.RefreshCash() => RefreshCash();
    void IInventoryView.RefreshArrangeButton() => RefreshArrangeButton();
    void IInventoryView.Hover(ItemCell cell, bool entered) => itemTooltip.InventoryHover(cell, entered);
    void IInventoryView.EnqueuePetMove(ItemMoveDirection dir, int itemId, byte src, byte dst, int bagAbs, int petPos) => EnqueuePetMove(dir, itemId, src, dst, bagAbs, petPos);
    void IInventoryView.RefreshFooter() => RefreshFooter();
    void IInventoryView.Activate(int absSlot) => Activate(absSlot);

    public void Dispose()
    {
        if (context.Root.IsInsideTree()) context.Root.GetViewport().SizeChanged -= RefreshUI;
        items.ItemMoveResultEvent -= OnItemMoveResult;
        items.InventoryArrangeFailedEvent -= OnArrangeFailed;
        items.ItemRemoveResultEvent -= OnItemRemoveResult;
        items.InventorySlotEvent -= OnSlotUpdate;
        items.ItemGainedEvent -= OnItemGained;
        items.InventoryGridRefreshEvent -= OnGridRefresh;
        progress.GoldChangeEvent -= OnGoldChange;
    }

    internal const int GridStart = Inventory.GridStart;
    internal const int GridCount = Inventory.GridCount;

    private const int InvCols = 7;
    private const int InvMinVisibleRows = 4;
    private const float InvWindowFixedHeight = 576f;
    internal LineEdit? InvSearch { get; private set; }
    private const int ArrangeIconSize = 16;
    private const float InvCellSize = 40f;
    private const float InvCellGap = 4f;
    private const float InvScrollbarWidth = 12f;
    private const float BagCellSize = 36f;

    private static readonly Color[] BagFrameColors = { UiTheme.Bad, UiTheme.Warning, UiTheme.Good };

    private CanvasLayer invLayer = null!;
    internal Control InvContent { get; private set; } = null!;

    internal Dictionary<int, ItemCell> InvCells { get; } = new();

    internal List<ItemCell> InvBagCells { get; } = new();
    private readonly List<ItemCell> invMagicBagCells = new();
    private readonly bool[] bagCollapsed = new bool[InventoryConstants.BagSlotMax];
    private VBoxContainer invLower = null!;
    private ScrollContainer invScroll = null!;
    private int invRowsShown;
    private bool overweightWarned;
    private Label? invGoldLbl;
    private Label? invWeightLbl;
    private Label? invSlotLbl;
    private ProgressBar invWeightBar = null!;
    private ProgressBar invSlotBar = null!;
    private TrashSlot invTrash = null!;

    internal PanelContainer InvDelPanel { get; private set; } = null!;
    private Label invDelTitle = null!;
    private VBoxContainer invDelList = null!;
    private ScrollContainer invDelScroll = null!;
    private readonly DestroyQueue invDelQueue = new();
    private const int DestroyRowsShown = 6;
    private const float DestroyRowHeight = 30f;
    private const float DestroyIconSize = 26f;
    private static readonly Color DestroyNameTint = new(1f, 0.62f, 0.55f);

    private static readonly (int Slot, string Label, float Angle)[] DollOuterRing =
    {
        (InventoryConstants.Head, "Helmet", 90f),
        (InventoryConstants.Leg, "Pants", 56f),
        (InventoryConstants.RightRing, "Ring", 29f),
        (InventoryConstants.LeftRing, "Ring", 5f),
        (InventoryConstants.Waist, "Belt", -17f),
        (InventoryConstants.Foot, "Boots", -41f),
        (InventoryConstants.LeftHand, "L hand", -75f),
        (InventoryConstants.RightHand, "R hand", -105f),
        (InventoryConstants.Glove, "Gloves", -139f),
        (InventoryConstants.Neck, "Neck", -163f),
        (InventoryConstants.LeftEar, "Ear", 175f),
        (InventoryConstants.RightEar, "Ear", 151f),
        (InventoryConstants.Breast, "Pauldron", 124f),
    };

    private static readonly (int Slot, string Label, float Angle)[] DollInnerRing =
    {
        (InventoryConstants.CosEmblem, "Emblem", 22.5f),
        (InventoryConstants.CosPauldron, "Top", 67.5f),
        (InventoryConstants.CosHelmet, "Mask", 112.5f),
        (InventoryConstants.CosWing, "Wings", 157.5f),
        (InventoryConstants.CosTattoo, "Tattoo", -157.5f),
        (InventoryConstants.CosGloveLeft, "Pathos", -112.5f),
        (InventoryConstants.CosGloveRight, "Pathos", -67.5f),
        (InventoryConstants.CosTalisman, "Talis", -22.5f),
    };

    private static readonly (int Slot, string Label)[] DollCenter =
    {
        (InventoryConstants.CosFairy, "Fairy"),
        (InventoryConstants.Pet, "Pet"),
    };

    private struct MoveStep
    {
        public ItemMoveDirection Dir;
        public int ItemId;
        public byte Src, Dst;
        public int From, To;
        public int PetPos;
        public int PetIndex;
        public ItemSlot PetBagItem, PetItem;
        public ushort Amount;
    }

    private readonly Queue<MoveStep> moveQueue = new();
    private bool moveInFlight;
    private MoveStep moveCur;

    internal bool MoveBusy => moveInFlight || moveQueue.Count > 0 || vendor.TradeInFlight;

    public void HudTick(double delta, double nowSec) => UpdateDeletePrompt();

    public void Build()
    {
        ItemData.EnsureLoaded();
        BuildPanel();
        RefreshUI();
        context.Root.GetViewport().SizeChanged += RefreshUI;
        items.ItemMoveResultEvent += OnItemMoveResult;
        items.InventoryArrangeFailedEvent += OnArrangeFailed;
        items.ItemRemoveResultEvent += OnItemRemoveResult;
        items.InventorySlotEvent += OnSlotUpdate;
        items.ItemGainedEvent += OnItemGained;
        items.InventoryGridRefreshEvent += OnGridRefresh;
        progress.GoldChangeEvent += OnGoldChange;
        context.Self.Redress();
    }

    internal void BuildPanel()
    {
        if (invLayer != null && GodotObject.IsInstanceValid(invLayer))
        {
            context.Root.RemoveChild(invLayer);
            invLayer.QueueFree();
        }
        InvCells.Clear();
        InvBagCells.Clear();
        invMagicBagCells.Clear();

        invLayer = new CanvasLayer { Layer = UiLayers.Prompts };
        context.Root.AddChild(invLayer);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        InvContent = body;

        body.AddChild(BuildEquipDoll());
        invLower = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(InvGridWidth + InvScrollbarWidth, 0),
        };
        invLower.AddThemeConstantOverride("separation", 8);
        body.AddChild(invLower);
        invLower.AddChild(BuildBagRow());
        invLower.AddChild(BuildBagSearchRow());
        invLower.AddChild(BuildGrid());
        invLower.AddChild(BuildFooter());

        BuildDeletePrompt();
        RefreshFooter();
    }

    private Control BuildBagRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
        {
            int abs = InventoryConstants.BagSlotFor(i);
            var cell = new ItemCell(abs, BagCellSize)
            {
                OnContext = itemActions.Context,
                OnClick = ToggleBag,
                OnHoverChanged = itemTooltip.InventoryHover,
                OnDropItem = MoveBetween,
                EmptyHint = "Bag",
            };
            InvCells[abs] = cell;
            row.AddChild(cell);
        }

        var vault = UiTheme.IconButton(UiIcons.Get(VaultIcon), "VIP Vault");
        vault.CustomMinimumSize = new Vector2(BagCellSize, BagCellSize);
        vault.AddThemeConstantOverride("icon_max_width", ArrangeIconSize);
        vault.Pressed += () => itemActions.OpenVault();
        row.AddChild(vault);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(UiIcons.Image("system/coins", new Vector2(15, 15), new Color(UiTheme.Gold, 0.9f), "Gold"));
        invGoldLbl = UiTheme.Text("", 13, UiTheme.Gold, HorizontalAlignment.Right);
        invGoldLbl.AddThemeColorOverride("font_color", UiTheme.Gold);
        row.AddChild(invGoldLbl);
        return row;
    }

    private const string VaultIcon = "system/key";
    private const string PremiumCashIcon = "system/gem";
    private const int BagHeaderGap = 8;
    private static readonly Color PremiumCashColour = new("4fb8ff");
    private Button? invCashBtn;

    private Control BuildBagSearchRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", BagHeaderGap);
        InvSearch = new LineEdit
        {
            PlaceholderText = "Search the bag",
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(BagCellSize * InventoryConstants.BagSlotMax + BagHeaderGap * (InventoryConstants.BagSlotMax - 1), BagCellSize),
        };
        InvSearch.AddThemeFontSizeOverride("font_size", 13);
        InvSearch.TextChanged += _ => RefreshBagSearch();
        row.AddChild(InvSearch);

        arrangeBtn = UiTheme.IconButton(UiIcons.Get("system/arrange"), "Arrange: sort the bag and close up the gaps");
        arrangeBtn.CustomMinimumSize = new Vector2(BagCellSize, BagCellSize);
        arrangeBtn.AddThemeConstantOverride("icon_max_width", ArrangeIconSize);
        arrangeBtn.Pressed += AskArrange;
        row.AddChild(arrangeBtn);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        invCashBtn = new Button
        {
            Icon = UiIcons.Get(PremiumCashIcon),
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Right,
            TooltipText = "Premium cash - open the Power-Up Store",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        invCashBtn.AddThemeConstantOverride("icon_max_width", ArrangeIconSize);
        invCashBtn.AddThemeFontSizeOverride("font_size", 13);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            invCashBtn.AddThemeColorOverride(state, PremiumCashColour);
        foreach (string state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            invCashBtn.AddThemeColorOverride(state, PremiumCashColour);
        invCashBtn.Pressed += () => itemActions.OpenStore();
        row.AddChild(invCashBtn);
        RefreshCash();
        return row;
    }

    internal void RefreshCash()
    {
        if (invCashBtn == null || !GodotObject.IsInstanceValid(invCashBtn)) return;
        invCashBtn.Text = $"{self.Sheet.KnightCash:n0}";
    }

    internal void RefreshBagSearch()
    {
        string text = InvSearch?.Text ?? "";
        bool searching = !new ItemQuery(text).IsEmpty;
        var ids = new int[self.Inventory.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = BagSearch.Searchable(i) ? self.Inventory[i].ItemId : 0;
        var hits = searching ? WarehouseView.Matches(ids, text, ItemData.DisplayName) : new HashSet<int>();
        foreach (var cell in InvBagCells) MarkSearch(cell, searching, hits);
        foreach (var cell in invMagicBagCells) MarkSearch(cell, searching, hits);
        var bags = BagSearch.BagsHoldingHits(hits);
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
            if (InvCells.TryGetValue(InventoryConstants.BagSlotFor(i), out var bagCell))
                bagCell.SearchHit = bags[i] && !BagShown(i);
    }

    private static void MarkSearch(ItemCell cell, bool searching, HashSet<int> hits)
    {
        bool hit = searching && cell.Slot >= 0 && hits.Contains(cell.Slot);
        cell.SearchHit = hit;
        cell.SearchMiss = searching && !hit;
    }

    private static float InvGridWidth => InvCols * (InvCellSize + InvCellGap) - InvCellGap;

    private int InvVisibleRows()
    {
        float pitch = InvCellSize + InvCellGap;
        float height = context.Root.IsInsideTree() ? context.Root.GetViewport().GetVisibleRect().Size.Y : DisplayServer.WindowGetSize().Y;
        int totalRows = Mathf.CeilToInt((GridCount + InventoryConstants.MagicBagTotal) / (float)InvCols);
        return Mathf.Clamp(Mathf.FloorToInt((height - InvWindowFixedHeight) / pitch), InvMinVisibleRows, totalRows);
    }

    private Control BuildGrid()
    {
        float pitch = InvCellSize + InvCellGap;
        invRowsShown = InvVisibleRows();
        invScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(InvGridWidth + InvScrollbarWidth, invRowsShown * pitch - InvCellGap),
        };
        var scroll = invScroll;
        var grid = new GridContainer { Columns = InvCols };
        grid.AddThemeConstantOverride("h_separation", (int)InvCellGap);
        grid.AddThemeConstantOverride("v_separation", (int)InvCellGap);
        scroll.AddChild(grid);

        for (int i = 0; i < GridCount; i++)
        {
            var cell = new ItemCell(GridStart + i, InvCellSize)
            {
                OnContext = itemActions.Context,
                OnHoverChanged = itemTooltip.InventoryHover,
                OnDropItem = MoveBetween,
                OnCompanionDrop = (from, to, amount) => context.BagCompanions.CompanionInto(from, to, amount),
            };
            InvBagCells.Add(cell);
            grid.AddChild(cell);
        }
        for (int i = 0; i < InventoryConstants.MagicBagTotal; i++)
        {
            var cell = new ItemCell(InventoryConstants.MagicBagStart + i, InvCellSize)
            {
                OnContext = itemActions.Context,
                OnHoverChanged = itemTooltip.InventoryHover,
                OnDropItem = MoveBetween,
                Frame = BagFrameColors[InventoryConstants.BagIndexForMagicBagPosition(i)],
                Visible = false,
            };
            invMagicBagCells.Add(cell);
            grid.AddChild(cell);
        }
        return scroll;
    }

    private Control BuildFooter()
    {
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        var meters = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        meters.AddThemeConstantOverride("separation", 4);
        meters.AddChild(BuildMeterRow("system/weight", "Weight", out invWeightLbl, out invWeightBar));
        meters.AddChild(BuildMeterRow("system/bag", "Inventory Slot", out invSlotLbl, out invSlotBar));
        footer.AddChild(meters);
        invTrash = new TrashSlot { OnDropItem = AskDeleteItem };
        footer.AddChild(invTrash);
        return footer;
    }

    private static Control BuildMeterRow(string icon, string caption, out Label value, out ProgressBar bar)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 5);
        box.AddChild(head);
        head.AddChild(UiIcons.Image(icon, new Vector2(14, 14), new Color(UiTheme.TextLo, 0.82f), caption));
        var label = UiTheme.Text(caption, 12, UiTheme.TextLo);
        label.AddThemeColorOverride("font_color", UiTheme.TextLo);
        head.AddChild(label);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        value = UiTheme.Text("", 12, UiTheme.TextHi, HorizontalAlignment.Right);
        value.AddThemeColorOverride("font_color", UiTheme.TextHi);
        head.AddChild(value);

        bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1000, Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
        };
        bar.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        bar.AddThemeStyleboxOverride("fill", UiTheme.MeterFill(UiTheme.Gold));
        box.AddChild(bar);
        return box;
    }

    private static Color MeterTint(float load) =>
        load >= 0.95f ? UiTheme.Bad : load >= 0.8f ? UiTheme.Warning : UiTheme.Gold;

    private const float DollCell = 42f;
    private const float DollOuterRadius = 146f;
    private const float DollInnerRadius = 77f;
    private const float DollCenterOffset = 25f;
    private const float DollRingWidth = 1f;
    private const int DollRingSegments = 128;
    private static readonly Color DollRingColor = new(UiTheme.Gold, 0.4f);

    private Control BuildEquipDoll()
    {
        float span = 2f * (DollOuterRadius + DollCell / 2f) + 4f;
        var board = new DollBoard { CustomMinimumSize = new Vector2(span, span), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        var center = new Vector2(span / 2f, span / 2f);

        foreach (var (slot, label, angle) in DollOuterRing)
            board.AddChild(DollCellAt(slot, label, center + DollOffset(DollOuterRadius, angle)));
        foreach (var (slot, label, angle) in DollInnerRing)
            board.AddChild(DollCellAt(slot, label, center + DollOffset(DollInnerRadius, angle)));
        for (int i = 0; i < DollCenter.Length; i++)
        {
            var (slot, label) = DollCenter[i];
            float x = (i - (DollCenter.Length - 1) / 2f) * 2f * DollCenterOffset;
            board.AddChild(DollCellAt(slot, label, center + new Vector2(x, 0f)));
        }
        cosmetics.AddLookToggles(board, span);
        return board;
    }

    private static Vector2 DollOffset(float radius, float degrees)
    {
        float radians = Mathf.DegToRad(degrees);
        return new Vector2(radius * Mathf.Cos(radians), -radius * Mathf.Sin(radians));
    }

    private ItemCell DollCellAt(int slot, string label, Vector2 at)
    {
        var cell = DollCellFor(slot, label);
        cell.Position = at - new Vector2(DollCell / 2f, DollCell / 2f);
        return cell;
    }

    private ItemCell DollCellFor(int slot, string label)
    {
        var cell = new ItemCell(slot, DollCell)
        {
            OnContext = itemActions.Context,
            OnHoverChanged = itemTooltip.InventoryHover,
            OnDropItem = MoveBetween,
            EmptyHint = label,
        };
        InvCells[slot] = cell;
        return cell;
    }

    private sealed partial class DollBoard : Control
    {
        public override void _Draw()
        {
            var center = Size / 2f;
            DrawArc(center, DollOuterRadius, 0f, Mathf.Tau, DollRingSegments, DollRingColor, DollRingWidth, true);
            DrawArc(center, DollInnerRadius, 0f, Mathf.Tau, DollRingSegments, DollRingColor, DollRingWidth, true);
        }
    }

    internal void RefreshFooter()
    {
        if (!GodotObject.IsInstanceValid(invGoldLbl) || !GodotObject.IsInstanceValid(invWeightLbl))
        {
            invGoldLbl = null;
            invWeightLbl = null;
            return;
        }
        int wt = CarriedWeight();
        invWeightLbl.Text = self.Sheet.MaxWeight > 0
            ? $"{wt / 10f:0.0} / {self.Sheet.MaxWeight / 10f:0.0} LT"
            : $"{wt / 10f:0.0} LT";
        invGoldLbl.Text = $"{self.Sheet.Gold:n0}";

        float load = self.Sheet.MaxWeight > 0 ? Mathf.Clamp((float)wt / self.Sheet.MaxWeight, 0f, 1f) : 0f;
        SetMeter(invWeightBar, load);
        WarnOverweight(load);

        (int used, int total) = InventorySlotUsage();
        if (GodotObject.IsInstanceValid(invSlotLbl)) invSlotLbl!.Text = $"{used}/{total}";
        SetMeter(invSlotBar, total > 0 ? Mathf.Clamp((float)used / total, 0f, 1f) : 0f);
    }

    private static void SetMeter(ProgressBar bar, float load)
    {
        if (!GodotObject.IsInstanceValid(bar)) return;
        bar.Value = load * bar.MaxValue;
        bar.AddThemeStyleboxOverride("fill", UiKit.CachedMeterFill(MeterTint(load)));
    }

    private (int Used, int Total) InventorySlotUsage()
    {
        int used = 0, total = 0;
        for (int i = 0; i < GridCount; i++)
        {
            total++;
            if (!SlotAt(GridStart + i).IsEmpty) used++;
        }
        for (int bag = 0; bag < InventoryConstants.BagSlotMax; bag++)
        {
            if (SlotAt(InventoryConstants.BagSlotFor(bag)).IsEmpty) continue;
            int start = InventoryConstants.MagicBagPageStart(bag);
            for (int i = 0; i < InventoryConstants.MagicBagMax; i++)
            {
                total++;
                if (!SlotAt(start + i).IsEmpty) used++;
            }
        }
        return (used, total);
    }

    private void WarnOverweight(float load)
    {
        bool over = load >= 1f;
        if (over && !overweightWarned)
            context.Log.Notice(GameText.System(GameText.Overweight, "You've exceeded your possible carrying weight."));
        overweightWarned = over;
    }

    internal int CarriedWeight()
    {
        int wt = 0;
        for (int abs = 0; abs < self.Inventory.Length; abs++)
        {
            if (self.Inventory[abs].IsEmpty) continue;
            var d = ItemData.Get(self.Inventory[abs].ItemId);
            if (d != null) wt += d.Weight * ItemData.CarriedUnits(d, self.Inventory[abs]);
        }
        return wt;
    }

    private void OnGoldChange(GoldChange change)
    {
        if (!GodotObject.IsInstanceValid(invGoldLbl))
        {
            invGoldLbl = null;
            return;
        }
        invGoldLbl.Text = $"{change.Total:n0}";
    }

    private Action? refreshed;
    private bool refreshedQueued;

    internal void RefreshUI()
    {
        foreach (var (slot, cell) in InvCells)
            cell.Bind(slot, SlotAt(slot));
        GhostOtherHand(InventoryConstants.RightHand, InventoryConstants.LeftHand);
        GhostOtherHand(InventoryConstants.LeftHand, InventoryConstants.RightHand);
        for (int i = 0; i < InvBagCells.Count; i++)
            InvBagCells[i].Bind(GridStart + i, SlotAt(GridStart + i));
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
            InvCells[InventoryConstants.BagSlotFor(i)].Frame = BagShown(i) ? BagFrameColors[i] : null;
        int shownCells = GridCount;
        for (int i = 0; i < invMagicBagCells.Count; i++)
        {
            int slot = InventoryConstants.MagicBagStart + i;
            bool open = BagShown(InventoryConstants.BagIndexForMagicBagPosition(i));
            invMagicBagCells[i].Visible = open;
            invMagicBagCells[i].Bind(open ? slot : -1, open ? SlotAt(slot) : default);
            if (open) shownCells++;
        }
        ShadeChaosItems();
        int contentRows = Mathf.CeilToInt(shownCells / (float)InvCols);
        invRowsShown = InvVisibleRows();
        bool scrollbar = contentRows > invRowsShown;
        float lowerWidth = InvGridWidth + (scrollbar ? InvScrollbarWidth : 0f);
        float gridHeight = Mathf.Min(contentRows, invRowsShown) * (InvCellSize + InvCellGap) - InvCellGap;
        invScroll.CustomMinimumSize = new Vector2(lowerWidth, gridHeight);
        invLower.CustomMinimumSize = new Vector2(lowerWidth, 0f);
        RefreshFooter();
        RefreshBagSearch();

        if (invDelQueue.Count > 0 && !invDelQueue.Running)
        {
            invDelQueue.DropStale((slot, itemId) => slot < self.Inventory.Length && self.Inventory[slot].ItemId == itemId);
            ShowDeleteCard();
        }

        itemTooltip.RefreshHoverTooltip();
        context.BagCompanions.ApplyFit();
        if (refreshedQueued) return;
        refreshedQueued = true;
        Callable.From(RaiseRefreshed).CallDeferred();
    }

    private void RaiseRefreshed()
    {
        refreshedQueued = false;
        if (!context.Alive) return;
        context.Plugins.Inventory();
        refreshed?.Invoke();
    }

    private void ShadeChaosItems()
    {
        foreach (var cell in InvCells.Values.Concat(InvBagCells).Concat(invMagicBagCells))
        {
            var item = cell.Current.IsEmpty ? null : ItemData.Get(cell.Current.ItemId);
            cell.Shaded = ChaosBattle.IsZone(context.Zone) && item != null && !ChaosBattle.ItemUsable(context.Zone, item.Kind);
        }
    }

    private ItemSlot SlotAt(int abs) => abs >= 0 && abs < self.Inventory.Length ? context.BagCompanions.Hold.Shown(abs, self.Inventory[abs]) : default;

    private void GhostOtherHand(int held, int other)
    {
        var weapon = SlotAt(held);
        if (weapon.IsEmpty || !IsTwoHanded(weapon.ItemId) || !SlotAt(other).IsEmpty) return;
        if (InvCells.TryGetValue(other, out var cell)) cell.ShowGhost(weapon.ItemId);
    }

    private bool BagShown(int bagIndex) =>
        !bagCollapsed[bagIndex] && !SlotAt(InventoryConstants.BagSlotFor(bagIndex)).IsEmpty;

    internal void ToggleBag(int bagSlotAbs)
    {
        if (SlotAt(bagSlotAbs).IsEmpty) return;
        int bag = bagSlotAbs - InventoryConstants.BagSlotStart;
        bagCollapsed[bag] = !bagCollapsed[bag];
        RefreshUI();
    }

    private bool MagicBagHasItems(int bagSlotAbs)
    {
        int start = InventoryConstants.MagicBagPageStart(bagSlotAbs - InventoryConstants.BagSlotStart);
        for (int i = 0; i < InventoryConstants.MagicBagMax; i++)
            if (!SlotAt(start + i).IsEmpty) return true;
        return false;
    }

    private void BuildDeletePrompt()
    {
        InvDelPanel = new PanelContainer { Visible = false, ZIndex = 240 };
        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.045f, 0.045f, 0.985f),
            BorderColor = new Color(0.62f, 0.24f, 0.20f, 0.95f),
            ShadowColor = new Color(0, 0, 0, 0.65f),
            ShadowSize = 10,
        };
        bg.SetBorderWidthAll(1);
        bg.SetCornerRadiusAll(3);
        InvDelPanel.AddThemeStyleboxOverride("panel", bg);
        invLayer.AddChild(InvDelPanel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 12, 10, 12, 11);
        InvDelPanel.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(214, 0) };
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        invDelTitle = UiTheme.Text("Destroy this item?", 13, UiTheme.TextHi);
        root.AddChild(invDelTitle);
        invDelScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(invDelScroll);
        invDelList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        invDelList.AddThemeConstantOverride("separation", 4);
        invDelScroll.AddChild(invDelList);
        var more = UiTheme.Text("Drop more items on the well to add them.", 11, new Color(UiTheme.TextLo, 0.7f));
        more.AddThemeColorOverride("font_color", new Color(UiTheme.TextLo, 0.7f));
        root.AddChild(more);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        var cancel = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        cancel.AddThemeFontSizeOverride("font_size", 12);
        cancel.Pressed += HideDeletePrompt;
        buttons.AddChild(cancel);
        var destroy = new Button { Text = "Destroy", FocusMode = Control.FocusModeEnum.None };
        destroy.AddThemeFontSizeOverride("font_size", 12);
        destroy.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.66f));
        destroy.AddThemeColorOverride("font_hover_color", new Color(1f, 0.86f, 0.82f));
        destroy.Pressed += ConfirmDeleteItem;
        buttons.AddChild(destroy);
    }

    internal void AskDeleteItem(int absSlot)
    {
        if (absSlot < 0 || absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        if (ItemMove.RegionOf(absSlot) is not (ItemMove.Region.Equip or ItemMove.Region.Grid or ItemMove.Region.MagicBag)) return;
        if (context.BagCompanions.RefuseItemInUse(absSlot)) return;
        if (CharacterSealFeature.IsCypherRing(self.Inventory[absSlot]))
        {
            context.Log.Add(ItemData.Text(CharacterSeal.RingNotDestroyableText, "Sealed items cannot be destroyed."), CombatLogKind.Status, CharacterSealFeature.BadColour);
            return;
        }
        if (!invDelQueue.Add(absSlot, self.Inventory[absSlot].ItemId)) return;

        ShowDeleteCard();
        Audio.PlayUi(Sfx.MsgBoxPop);
    }

    private void ShowDeleteCard()
    {
        if (invDelQueue.Count == 0) { HideDeletePrompt(); return; }
        invDelTitle.Text = invDelQueue.Count == 1 ? "Destroy this item?" : $"Destroy these {invDelQueue.Count} items?";
        foreach (var child in invDelList.GetChildren()) child.QueueFree();
        foreach (var entry in invDelQueue.Entries) invDelList.AddChild(DestroyRow(entry));
        invDelScroll.CustomMinimumSize = new Vector2(0,
            Mathf.Min(invDelQueue.Count, DestroyRowsShown) * (DestroyRowHeight + 4f) - 4f);
        InvDelPanel.Visible = true;
        InvDelPanel.ResetSize();
        UpdateDeletePrompt();
    }

    private Control DestroyRow(DestroyQueue.Entry entry)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, DestroyRowHeight) };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new TextureRect
        {
            Texture = ItemIcons.Get(entry.ItemId),
            CustomMinimumSize = new Vector2(DestroyIconSize, DestroyIconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        int count = entry.Slot < self.Inventory.Length ? self.Inventory[entry.Slot].Count : 0;
        var name = UiTheme.Text(count > 1 ? $"{ItemData.DisplayName(entry.ItemId)} ×{count}" : ItemData.DisplayName(entry.ItemId),
            12, DestroyNameTint);
        name.AddThemeColorOverride("font_color", DestroyNameTint);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.CustomMinimumSize = new Vector2(150, 0);
        row.AddChild(name);
        var drop = new Button { Text = "✕", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Keep this item" };
        drop.AddThemeFontSizeOverride("font_size", 11);
        drop.Pressed += () =>
        {
            invDelQueue.Remove(entry.Slot);
            ShowDeleteCard();
        };
        row.AddChild(drop);
        return row;
    }

    internal void HideDeletePrompt()
    {
        if (!invDelQueue.Running) invDelQueue.Clear();
        if (InvDelPanel != null && GodotObject.IsInstanceValid(InvDelPanel))
            InvDelPanel.Visible = false;
    }

    internal void UpdateDeletePrompt()
    {
        if (InvDelPanel == null || !InvDelPanel.Visible) return;
        if (!context.MainWindows.CharTabOpen()) { HideDeletePrompt(); return; }

        if (context.Root.GetViewport() is not { } vp) return;
        var size = InvDelPanel.Size;
        if (size.X <= 1 || size.Y <= 1) size = InvDelPanel.GetCombinedMinimumSize();
        var bag = InvContent.GetGlobalRect();
        var trash = invTrash.GetGlobalRect();
        var viewport = vp.GetVisibleRect().Size;
        var p = new Vector2(
            bag.Position.X + (bag.Size.X - size.X) * 0.5f,
            trash.Position.Y - size.Y - 10f);
        p.X = Mathf.Clamp(p.X, 8f, Mathf.Max(8f, viewport.X - size.X - 8f));
        p.Y = Mathf.Clamp(p.Y, 8f, Mathf.Max(8f, viewport.Y - size.Y - 8f));
        InvDelPanel.Position = p;
    }

    private void ConfirmDeleteItem()
    {
        if (MoveBusy || context.Self.Dead) { HideDeletePrompt(); return; }
        invDelQueue.DropStale((slot, itemId) => slot < self.Inventory.Length && self.Inventory[slot].ItemId == itemId);
        var first = invDelQueue.Start();
        if (InvDelPanel != null && GodotObject.IsInstanceValid(InvDelPanel)) InvDelPanel.Visible = false;
        if (first is { } entry) SendDestroy(entry);
        Audio.PlayUi(Sfx.UiButton);
    }

    private void SendDestroy(DestroyQueue.Entry entry)
    {
        int slot = entry.Slot;
        if (slot >= self.Inventory.Length || self.Inventory[slot].ItemId != entry.ItemId)
        {
            if (invDelQueue.Advance(ok: true) is { } next) SendDestroy(next);
            return;
        }
        var region = ItemMove.RegionOf(slot);
        var source = region switch
        {
            ItemMove.Region.Equip => ItemRemoveSource.Equipment,
            ItemMove.Region.MagicBag => ItemRemoveSource.MagicBag,
            _ => ItemRemoveSource.Inventory,
        };
        itemsRequests.Remove((byte)source, (byte)ItemMove.PositionIn(region, slot), entry.ItemId);
    }

    private const int NoVipPasswordText = 32150;
    private const int VipPasswordLockedText = 33200;
    private const int WrongVipPasswordText = 15113;
    private const int SystemErrorText = 16506;

    internal void OnItemRemoveResult(bool ok, ItemRemoveReason reason)
    {
        if (invDelQueue.Running && invDelQueue.Advance(ok) is { } next) SendDestroy(next);
        if (!ok)
        {
            Notice.Show(context.Root, reason switch
            {
                ItemRemoveReason.NoVipKeyPassword => ItemData.Text(NoVipPasswordText, "Set a VIP key password first."),
                ItemRemoveReason.VipKeyLocked => ItemData.Text(VipPasswordLockedText, "The VIP key password was entered wrong three times."),
                ItemRemoveReason.WrongVipKeyPassword => ItemData.Text(WrongVipPasswordText, "Wrong VIP Key Password"),
                _ => ItemData.Text(SystemErrorText, "System Error"),
            });
            return;
        }
        RefreshUI();
    }

    private partial class TrashSlot : Control
    {
        public System.Action<int>? OnDropItem;
        private bool hot;
        private readonly TextureRect lockIcon;
        private const string OpenTip = "Drop items here to destroy them · click the lock to lock the well";
        private const string LockedTip = "The destroy well is locked. Click the lock to open it.";
        private const float LockButtonSize = 14f;
        private const float LockHitSlack = 4f;
        private static readonly Color LockedFill = new(0.16f, 0.16f, 0.18f, 0.5f);
        private static readonly Color LockTint = new(1f, 0.86f, 0.55f, 0.95f);
        private static readonly Color UnlockTint = new(0.97f, 0.78f, 0.74f, 0.55f);
        private static readonly Color Fill = new(0.42f, 0.11f, 0.09f, 0.16f);
        private static readonly Color FillHot = new(0.55f, 0.15f, 0.12f, 0.38f);
        private static readonly Color Edge = new(0.78f, 0.30f, 0.25f, 0.75f);
        private static readonly Color EdgeHot = new(1f, 0.45f, 0.38f, 1f);
        private const float DashLength = 5f;
        private const float TrashIconInsetX = 22f;
        private const float TrashIconInsetY = 17f;

        public TrashSlot()
        {
            CustomMinimumSize = new Vector2(64, 52);
            MouseFilter = MouseFilterEnum.Stop;

            var icon = new TextureRect
            {
                Texture = UiIcons.Get("system/trash"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                SelfModulate = new Color(0.97f, 0.78f, 0.74f, 0.85f),
            };
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            icon.OffsetLeft = TrashIconInsetX;
            icon.OffsetTop = TrashIconInsetY;
            icon.OffsetRight = -TrashIconInsetX;
            icon.OffsetBottom = -TrashIconInsetY;
            AddChild(icon);

            lockIcon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
                OffsetLeft = -LockButtonSize - 3f, OffsetRight = -3f, OffsetTop = 3f, OffsetBottom = LockButtonSize + 3f,
            };
            AddChild(lockIcon);
            ApplyLock();

            MouseEntered += () => { hot = true; QueueRedraw(); };
            MouseExited += () => { hot = false; QueueRedraw(); };
        }

        private void ApplyLock()
        {
            bool locked = Config.DestroyWellLocked;
            lockIcon.Texture = UiIcons.Get(locked ? "system/lock" : "system/unlock");
            lockIcon.Modulate = locked ? LockTint : UnlockTint;
            TooltipText = locked ? LockedTip : OpenTip;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size).Grow(-1f);
            DrawRect(rect, Config.DestroyWellLocked ? LockedFill : hot ? FillHot : Fill);
            var edge = hot ? EdgeHot : Edge;
            var tl = rect.Position;
            var tr = rect.Position + new Vector2(rect.Size.X, 0f);
            var bl = rect.Position + new Vector2(0f, rect.Size.Y);
            var br = rect.End;
            DrawDashedLine(tl, tr, edge, 1f, DashLength);
            DrawDashedLine(tr, br, edge, 1f, DashLength);
            DrawDashedLine(br, bl, edge, 1f, DashLength);
            DrawDashedLine(bl, tl, edge, 1f, DashLength);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            if (!lockIcon.GetRect().Grow(LockHitSlack).HasPoint(mb.Position)) return;
            Config.SetDestroyWellLocked(!Config.DestroyWellLocked);
            ApplyLock();
            AcceptEvent();
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            !Config.DestroyWellLocked
            && data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("invFrom");

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            hot = false;
            QueueRedraw();
            OnDropItem?.Invoke(data.AsGodotDictionary()["invFrom"].AsInt32());
        }
    }

    internal void Activate(int absSlot)
    {
        if (MoveBusy || context.Self.Dead) return;
        if (absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;
        if (context.BagCompanions.RefuseItemInUse(absSlot)) return;
        var def = ItemData.Get(self.Inventory[absSlot].ItemId);
        if (def == null) return;

        var region = ItemMove.RegionOf(absSlot);
        if (region != ItemMove.Region.Grid)
        {
            if (region == ItemMove.Region.BagSlot && MagicBagHasItems(absSlot))
            {
                context.Log.Notice(BagStillHoldsItems);
                return;
            }
            int free = region == ItemMove.Region.MagicBag
                ? self.Inventory.FirstStackOrFreeGridSlot(self.Inventory[absSlot], def.Countable, context.BagCompanions.IsStagedSlot) : self.Inventory.FirstFreeGridSlot();
            if (free < 0) return;
            var back = ItemMove.DirectionFor(region, ItemMove.Region.Grid);
            if (back == ItemMoveDirection.None) return;
            Enqueue(back, self.Inventory[absSlot].ItemId, (byte)ItemMove.PositionIn(region, absSlot),
                    (byte)(free - GridStart), absSlot, free);
            return;
        }

        if (CospreDestinationFor(def) is { } cos)
        {
            Enqueue(ItemMoveDirection.InventoryToCospre, self.Inventory[absSlot].ItemId,
                    (byte)(absSlot - GridStart), (byte)(cos - InventoryConstants.CospreStart), absSlot, cos);
            return;
        }

        if (def.Slot == ItemSlotCodeBag && FirstFreeBagSlot() is { } bagSlot)
        {
            Enqueue(ItemMoveDirection.InventoryToBagSlot, self.Inventory[absSlot].ItemId,
                    (byte)(absSlot - GridStart),
                    (byte)(bagSlot - InventoryConstants.BagSlotStart), absSlot, bagSlot);
            return;
        }

        int eq = self.Inventory.ResolveEquipDest(def.Slot, ItemData.EquipSlotFor(def));
        if (eq < 0) return;
        if (!CanEquipOrNotice(self.Inventory[absSlot].ItemId, def)) return;
        if (seal.AskBindBeforeEquip(absSlot, () => Activate(absSlot))) return;

        var preClear = self.Inventory.HandsToClear(eq, def.Slot, IsTwoHanded);
        var free2 = self.Inventory.FreeGridSlots();
        if (preClear.Count > free2.Count) return;
        for (int i = 0; i < preClear.Count; i++)
        {
            int handSlot = preClear[i], bag = free2[i];
            Enqueue(ItemMoveDirection.SlotToInventory, self.Inventory[handSlot].ItemId, (byte)handSlot,
                    (byte)(bag - GridStart), handSlot, bag);
        }
        Enqueue(ItemMoveDirection.InventoryToSlot, self.Inventory[absSlot].ItemId,
                (byte)(absSlot - GridStart), (byte)eq, absSlot, eq);
    }

    internal const int ItemSlotCodeBag = 25;
    private const int TextEquipRace = 3018;
    private const int TextEquipClass = 3028;
    private const int TextEquipLevelTooLow = 3022;
    private const int TextEquipLevelTooHigh = 3032;
    private const int TextEquipStrength = 3023;
    private const int TextEquipStamina = 3025;
    private const int TextEquipDexterity = 3020;
    private const int TextEquipIntelligence = 3021;
    private const int TextEquipCharisma = 3019;

    private bool CanEquipOrNotice(int itemId, ItemData.Item def)
    {
        if (ChaosBattle.IsZone(context.Zone) && ChaosBattle.IsPickaxe(def.Kind))
        {
            context.Log.Notice(ItemData.Text(ChaosBattle.PickaxeEquipText, "Pickaxe cannot be equipped in Chaos Battle."));
            return false;
        }
        var who = new EquipStats(self.Class, self.Race, self.Sheet.Level, self.Sheet.Str, self.Sheet.Sta, self.Sheet.Dex, self.Sheet.Intel, self.Sheet.Mag);
        var refusal = EquipRules.Check(who, def, ItemData.ExtFor(itemId));
        if (refusal == EquipRefusal.None) return true;
        context.Log.Notice(refusal switch
        {
            EquipRefusal.Race => GameText.System(TextEquipRace, "You cannot equip this item.  This item is designed for a different race."),
            EquipRefusal.Class => GameText.System(TextEquipClass, "You cannot equip this item.  This item is not designed for your character's specialty."),
            EquipRefusal.LevelTooLow => GameText.System(TextEquipLevelTooLow, "You cannot equip this item because your level is too low"),
            EquipRefusal.LevelTooHigh => GameText.System(TextEquipLevelTooHigh, "Cannot equip because of your high level"),
            EquipRefusal.Strength => GameText.System(TextEquipStrength, "You cannot equip this item because you don't have enough Strength stat points."),
            EquipRefusal.Stamina => GameText.System(TextEquipStamina, "You cannot equip this item because you don't have enough Health stat points."),
            EquipRefusal.Dexterity => GameText.System(TextEquipDexterity, "You cannot equip this item because you don't have enough Dexterity stat point"),
            EquipRefusal.Intelligence => GameText.System(TextEquipIntelligence, "You cannot equip this item because you don't have enough Intelligence stat point"),
            _ => GameText.System(TextEquipCharisma, "You cannot equip this item because you don't have enough Magic Power stat point"),
        });
        return false;
    }

    private const string BagStillHoldsItems = "Empty the bag before taking it off.";
    internal const int CospreCodeBase = 100;

    private static bool IsVisualSlot(int abs)
    => abs < GridStart || InventoryConstants.IsCospreSlot(abs);

    private int? FirstFreeBagSlot()
    {
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
        {
            int abs = InventoryConstants.BagSlotFor(i);
            if (abs < self.Inventory.Length && self.Inventory[abs].IsEmpty) return abs;
        }
        return null;
    }

    private int? CospreDestinationFor(ItemData.Item def)
    {
        if (def.Slot < CospreCodeBase) return null;
        int[] targets = (def.Slot % CospreCodeBase) switch
        {
            10 => new[] { InventoryConstants.CosPosWing },
            7 => new[] { InventoryConstants.CosPosHelmet },
            0 => new[] { InventoryConstants.CosPosGloveRight, InventoryConstants.CosPosGloveLeft },
            1 => new[] { InventoryConstants.CosPosGloveRight },
            2 => new[] { InventoryConstants.CosPosGloveLeft },
            5 => new[] { InventoryConstants.CosPosPauldron },
            14 => new[] { InventoryConstants.CosPosEmblem },
            11 => new[] { InventoryConstants.CosPosFairy },
            12 or 27 => new[] { InventoryConstants.CosPosTattoo },
            13 => new[] { InventoryConstants.CosPosTalisman },
            _ => System.Array.Empty<int>(),
        };
        if (targets.Length == 0) return null;
        foreach (int pos in targets)
        {
            int abs = InventoryConstants.CospreStart + pos;
            if (abs < self.Inventory.Length && self.Inventory[abs].IsEmpty) return abs;
        }
        return InventoryConstants.CospreStart + targets[0];
    }

    private static bool IsTwoHanded(int itemId) => ItemData.Get(itemId) is { } def && Inventory.IsTwoHandedSlotType(def.Slot);

    internal void MoveBetween(int from, int to) => MoveBetween(from, to, ItemMove.WholeStack);

    internal void MoveBetween(int from, int to, int amount)
    {
        if (MoveBusy || context.Self.Dead) return;
        if (from == to || from < 0 || to < 0 || from >= self.Inventory.Length || to >= self.Inventory.Length) return;
        if (self.Inventory[from].IsEmpty) return;
        if (amount < ItemMove.WholeStack || amount > self.Inventory[from].Count) return;
        if (context.BagCompanions.RefuseItemInUse(from, to)) return;

        var fromRegion = ItemMove.RegionOf(from);
        var toRegion = ItemMove.RegionOf(to);
        var dir = ItemMove.DirectionFor(fromRegion, toRegion);
        if (dir == ItemMoveDirection.None) return;
        bool splits = amount != ItemMove.WholeStack && amount != self.Inventory[from].Count;
        if (splits && !ItemMove.Splits(dir, self.Inventory[from], self.Inventory[to], ItemData.Get(self.Inventory[from].ItemId)?.Countable ?? 0, amount))
            return;
        if (fromRegion == ItemMove.Region.BagSlot && MagicBagHasItems(from))
        {
            context.Log.Notice(BagStillHoldsItems);
            return;
        }

        if (toRegion == ItemMove.Region.Equip)
        {
            var def = ItemData.Get(self.Inventory[from].ItemId);
            if (def == null) return;
            if (!CanEquipOrNotice(self.Inventory[from].ItemId, def)) return;
            if (fromRegion == ItemMove.Region.Grid && seal.AskBindBeforeEquip(from, () => MoveBetween(from, to))) return;
            var preClear = self.Inventory.HandsToClear(to, def.Slot, IsTwoHanded);
            var free = self.Inventory.FreeGridSlots();
            free.Remove(from);
            if (preClear.Count > free.Count) return;
            for (int i = 0; i < preClear.Count; i++)
            {
                int hand = preClear[i], bag = free[i];
                Enqueue(ItemMoveDirection.SlotToInventory, self.Inventory[hand].ItemId, (byte)hand,
                        (byte)(bag - GridStart), hand, bag);
            }
        }

        Enqueue(dir, self.Inventory[from].ItemId,
                (byte)ItemMove.PositionIn(fromRegion, from),
                (byte)ItemMove.PositionIn(toRegion, to),
                from, to, splits ? (ushort)amount : ItemMove.WholeStack);
    }

    internal void Enqueue(ItemMoveDirection dir, int itemId, byte src, byte dst, int from, int to, ushort amount = ItemMove.WholeStack)
    {
        moveQueue.Enqueue(new MoveStep { Dir = dir, ItemId = itemId, Src = src, Dst = dst, From = from, To = to, PetPos = NoPetSlot, Amount = amount });
        PumpMoves();
    }

    private const int NoPetSlot = -1;

    internal void EnqueuePetMove(ItemMoveDirection dir, int itemId, byte src, byte dst, int bagAbs, int petPos)
    {
        if (pet.Pet is not { } petSheet || petPos < 0 || petPos >= petSheet.Items.Length) return;
        moveQueue.Enqueue(new MoveStep { Dir = dir, ItemId = itemId, Src = src, Dst = dst, From = bagAbs, To = bagAbs,
            PetPos = petPos, PetIndex = petSheet.Index, PetBagItem = self.Inventory[bagAbs], PetItem = petSheet.Items[petPos] });
        PumpMoves();
    }

    private void PumpMoves()
    {
        if (moveInFlight || moveQueue.Count == 0) return;
        moveCur = moveQueue.Dequeue();
        moveInFlight = true;
        itemsRequests.Move(moveCur.Dir, moveCur.ItemId, moveCur.Src, moveCur.Dst, moveCur.Amount);
    }

    private const int TextArrangeFailed = 30701;
    private const int TextArrangeAsk = 30700;
    private Button? arrangeBtn;
    private Notice? arrangeAsk;

    private void AskArrange()
    {
        if (context.BagCompanions.Current != null || (arrangeAsk != null && GodotObject.IsInstanceValid(arrangeAsk))) return;
        arrangeAsk = Notice.Confirm(context.Root, ItemData.Text(TextArrangeAsk, "Do you want to arrange it?"),
            "Yes", "No", itemsRequests.InventoryArrange);
    }

    internal void RefreshArrangeButton()
    {
        if (arrangeBtn != null) arrangeBtn.Disabled = context.BagCompanions.Current != null;
    }

    internal void OnArrangeFailed() =>
        context.Log.Notice(ItemData.Text(TextArrangeFailed, "You have filed to arrange. Please try again later."));

    internal void OnItemMoveResult(bool ok)
    {
        if (!moveInFlight) return;
        moveInFlight = false;
        GD.Print($"[move] dir {moveCur.Dir} slot {moveCur.From} -> {moveCur.To} item {moveCur.ItemId} {(ok ? "ok" : "refused")}");
        if (!ok)
        {
            moveQueue.Clear();
            RefreshUI();
            return;
        }

        if (moveCur.PetPos != NoPetSlot)
        {
            if (pet.Pet?.PlaceConfirmed(moveCur.PetIndex, moveCur.PetPos, moveCur.PetBagItem) == true)
                self.SetInventorySlot(moveCur.From, moveCur.PetItem);
            PumpMoves();
            RefreshUI();
            petWindow.RefreshUI();
            return;
        }

        GD.Print($"[move] now slot {moveCur.From} = {self.Inventory[moveCur.From].ItemId} x{self.Inventory[moveCur.From].Count}, slot {moveCur.To} = {self.Inventory[moveCur.To].ItemId} x{self.Inventory[moveCur.To].Count}");
        if (IsVisualSlot(moveCur.From) || IsVisualSlot(moveCur.To)) context.Self.Redress();
        ItemSounds.Moved(moveCur.ItemId, moveCur.From, moveCur.To);
        PumpMoves();
        RefreshUI();
    }

    internal void OnItemGained(int itemId, int count)
    {
        context.Floaters?.Item(itemId, count);
        if (itemId == LootWire.GoldItemId) return;
        string name = ItemData.DisplayName(itemId);
        context.Log.Add(count > 1 ? $"You obtained {name} x{count:n0}." : $"You obtained {name}.", CombatLogKind.Resource, tab: LogTab.Item);
    }

    internal void OnSlotUpdate(int absSlot, ItemSlot item)
    {
        if (absSlot < 0) return;
        RefreshUI();
        if (IsVisualSlot(absSlot))
            context.Self.Redress();
    }

    internal void OnGridRefresh(ItemSlot[] itemSlots) => RefreshUI();
}
