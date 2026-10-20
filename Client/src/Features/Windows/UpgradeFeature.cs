using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class UpgradeFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ItemUpgradeSession item;
    private readonly ItemsSession items;
    private readonly SelfSession self;
    private readonly IItemUpgradeRequests itemRequests;
    private readonly VendorFeature vendor;
    private readonly IInteractionDialogs interactionDialogs;

    public UpgradeFeature(IWorldContext context, ItemUpgradeSession itemUpgrade, ItemsSession items, SelfSession self, IItemUpgradeRequests itemUpgradeRequests, VendorFeature vendor, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        item = itemUpgrade;
        this.items = items;
        this.self = self;
        itemRequests = itemUpgradeRequests;
        this.vendor = vendor;
        this.interactionDialogs = interactionDialogs;
    }

    private const int SlotCount = ItemUpgradeWire.SlotCount;
    private const int ScrollHigh = 379016000;
    internal const int ScrollHighBlessed = 379021000;
    private const int ScrollClass = 379152000;
    private const int ScrollMiddle = 379205000;
    private const int ScrollLow = 379221000;
    private const int ScrollTraining = 379255000;
    private const int BonusScrollHighLast = 379035000;
    private const int BonusScrollMiddleLast = 379220000;
    private const int BonusScrollLowLast = 379235000;
    private const int DispelScrollFirst = 379138000;
    private const int DispelScrollLast = 379141000;
    private const int ReverseStrengthenScroll = 379257000;
    private const int KarivdisPiece = 379258000;
    private const int TrinaPieceMiddle = 352900000;
    private const int TrinaPieceLow = 353000000;
    private const int TrinaPieceAccessory = 354000000;
    internal const int TrinaPiece = 700002000;
    private const int RebirthRestorationScroll = 810322000;
    private const int BlessingLogos = 890092000;
    private const int EquipSlotFirst = 0;
    private const int EquipSlotLast = 14;
    private const int EtcKindFirst = 95;
    private const int EtcKindLast = 99;
    private const int AccessorySocketCount = 3;
    private const int AccessoryBenchSlots = AccessorySocketCount + 2;
    private const float AnvilItemSocketSize = 56f;
    private const float AnvilMaterialSocketSize = 44f;

    private const int AnvilBodyMinWidth = 470;
    private const int AnvilLidEdge = 2;
    private const float AnvilSeamHalf = 5f;
    private const double AnvilLidSeconds = 0.25;
    private const double AnvilScanSeconds = 1.9;
    private const double AnvilRevealSeconds = 0.8;
    private const string AnvilHint = "Right-click a bag item to place it";
    private const int TextRented = 10748;
    private const int TextSealed = 15103;
    private const string SelectionChangedText = "The selection changed. Check it and press Upgrade again.";
    private static readonly Color AnvilLidColour = new(0.045f, 0.045f, 0.055f);

    private const string AnvilSelectText =
    "You can upgrade your item at the magic anvil. Please select what you want to upgrade.";

    internal enum AnvilBench { Item, Accessory }

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    internal ServiceTabs AnvilTabs { get; private set; } = null!;
    private VBoxContainer anvilBenches = null!;
    internal DetailStrip AnvilStrip { get; private set; } = null!;
    private FooterBand anvilFooter = null!;
    private Button btn = null!;
    private ItemSlotView resultSocket = null!;
    private ItemSlotView itemResultSocket = null!;
    private ItemSlotView accessoryResultSocket = null!;
    private Control itemBench = null!;
    private Control accessoryBench = null!;
    internal Control AnvilLids { get; private set; } = null!;
    internal Panel AnvilLidTop { get; private set; } = null!;
    internal Panel AnvilLidBottom { get; private set; } = null!;
    internal TextureRect AnvilSeam { get; private set; } = null!;
    private Tween? scanTween;
    private UpgradeResult? pendingResult;
    private ProgressBar compoundGauge = null!;
    private Gradient anvilSeamGlow = null!;
    private const float CompoundGaugeHeight = 18f;
    private BagCompanion? anvilCompanion;
    private AnvilBench anvilBench = AnvilBench.Item;

    private bool shown;

    public bool IsOpen => shown;

    internal UpgradeSession Session { get; } = new();
    private readonly UpgradePreviewGate previewGate = new();

    private bool InteractionLocked => Session.Awaiting || pendingResult != null || scanTween != null;

    internal int AnvilId { get; set; }
    private int previewId;
    private Notice? confirm;
    private readonly int[] itemIds = new int[SlotCount];
    private readonly int[] positions = new int[SlotCount];
    private readonly ItemSlotView?[] itemSockets = new ItemSlotView?[SlotCount];
    private readonly ItemSlotView?[] accessorySockets = new ItemSlotView?[SlotCount];
    private ItemSlotView?[] sockets = null!;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        item.UpgradeOpenEvent += OnOpen;
        item.UpgradeResultEvent += OnResult;
        items.InventorySlotEvent += OnInventorySlot;
        items.InventoryGridRefreshEvent += OnInventoryGrid;
    }

    public void Dispose()
    {
        item.UpgradeOpenEvent -= OnOpen;
        item.UpgradeResultEvent -= OnResult;
        items.InventorySlotEvent -= OnInventorySlot;
        items.InventoryGridRefreshEvent -= OnInventoryGrid;
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("anvil", "Magic Anvil", bodyMinWidth: AnvilBodyMinWidth) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 6);

        AnvilTabs = new ServiceTabs();
        AnvilTabs.SetTabs(new[] { "Upgrade Item", "Compound Accessory" }, (int)AnvilBench.Item);
        AnvilTabs.Selected += OnAnvilTab;
        root.AddChild(AnvilTabs);

        var well = ServiceKit.Well();
        root.AddChild(well);
        anvilBenches = new VBoxContainer();
        well.AddChild(anvilBenches);
        sockets = itemSockets;
        itemBench = BuildBench();
        anvilBenches.AddChild(itemBench);
        accessoryBench = BuildAccessoryBench();
        accessoryBench.Visible = false;
        anvilBenches.AddChild(accessoryBench);
        resultSocket = itemResultSocket;
        well.AddChild(BuildAnvilLids());

        AnvilStrip = new DetailStrip(AnvilItemSocketSize);
        AnvilStrip.Slot.Visible = false;
        btn = UiTheme.ActionButton("Upgrade", "Upgrade the item on the anvil");
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        btn.Pressed += Confirm;
        AnvilStrip.Right.AddChild(btn);
        root.AddChild(AnvilStrip);

        anvilFooter = new FooterBand { Hint = AnvilHint };
        var back = UiTheme.SmallButton("Back", "Back to the anvil menu");
        back.Pressed += BackToAnvilMenu;
        anvilFooter.Right.AddChild(back);
        root.AddChild(anvilFooter);

        ClearSockets();
        ShowAnvilPrompt();
    }

    private Control BuildBench()
    {
        var bench = BenchRow();
        bench.AddChild(UiKit.UpgradeColumn("Item", BenchSocket(itemSockets, 0, AnvilItemSocketSize)));
        bench.AddChild(UiKit.BenchSign("+"));

        var materials = new GridContainer { Columns = 3 };
        materials.AddThemeConstantOverride("h_separation", UiKit.AnvilSocketGap);
        materials.AddThemeConstantOverride("v_separation", UiKit.AnvilSocketGap);
        for (int i = 1; i < SlotCount; i++)
            materials.AddChild(BenchSocket(itemSockets, i, AnvilMaterialSocketSize));
        bench.AddChild(UiKit.UpgradeColumn("Materials", materials));

        bench.AddChild(UiKit.BenchSign("="));
        itemResultSocket = BuildResultSocket();
        bench.AddChild(UiKit.UpgradeColumn("Result", itemResultSocket));
        return bench;
    }

    private Control BuildAccessoryBench()
    {
        var bench = BenchRow();
        var accessories = new HBoxContainer();
        accessories.AddThemeConstantOverride("separation", UiKit.AnvilSocketGap);
        for (int i = 0; i < AccessorySocketCount; i++)
            accessories.AddChild(BenchSocket(accessorySockets, i, AnvilItemSocketSize));
        bench.AddChild(UiKit.UpgradeColumn("Accessories", accessories));
        bench.AddChild(UiKit.BenchSign("+"));

        var materials = new HBoxContainer();
        materials.AddThemeConstantOverride("separation", UiKit.AnvilSocketGap);
        for (int i = AccessorySocketCount; i < AccessoryBenchSlots; i++)
            materials.AddChild(BenchSocket(accessorySockets, i, AnvilMaterialSocketSize));
        bench.AddChild(UiKit.UpgradeColumn("Materials", materials));

        bench.AddChild(UiKit.BenchSign("="));
        accessoryResultSocket = BuildResultSocket();
        bench.AddChild(UiKit.UpgradeColumn("Result", accessoryResultSocket));

        compoundGauge = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false, Visible = false,
            CustomMinimumSize = new Vector2(0, CompoundGaugeHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        compoundGauge.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        compoundGauge.AddThemeStyleboxOverride("fill", UiTheme.MeterFill(UiTheme.Gold));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", UiKit.AnvilSocketGap);
        column.AddChild(bench);
        column.AddChild(compoundGauge);
        return column;
    }

    private static HBoxContainer BenchRow()
    {
        var bench = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        bench.AddThemeConstantOverride("separation", 14);
        return bench;
    }

    private ItemSlotView BenchSocket(ItemSlotView?[] bench, int index, float size)
    {
        var socket = new ItemSlotView(size) { Index = index, Veiled = true };
        socket.RightClicked += _ => ClearSocket(index);
        socket.DoubleClicked += _ => ClearSocket(index);
        socket.Hovered += s => context.Tooltips.Show(SocketSlot(index), s.Item);
        socket.Unhovered += _ => context.Tooltips.Hide();
        socket.DragOut = _ => InteractionLocked ? default(Variant) : new Godot.Collections.Dictionary { { "companionFrom", index } };
        socket.CanDrop = (_, data) => CanStageDrop(index, data);
        socket.Dropped = (_, data) => StageDrop(index, data);
        bench[index] = socket;
        return socket;
    }

    private ItemSlotView BuildResultSocket()
    {
        var socket = new ItemSlotView(AnvilItemSocketSize) { Veiled = true };
        socket.Hovered += s => context.Tooltips.Show(-1, s.Item);
        socket.Unhovered += _ => context.Tooltips.Hide();
        return socket;
    }

    private Control BuildAnvilLids()
    {
        AnvilLids = new Control { MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true };
        AnvilLidTop = AnvilLid(top: true);
        AnvilLids.AddChild(AnvilLidTop);
        AnvilLidBottom = AnvilLid(top: false);
        AnvilLids.AddChild(AnvilLidBottom);

        anvilSeamGlow = new Gradient
        {
            Offsets = new[] { 0f, 0.5f, 1f },
            Colors = SeamColours(fails: false),
        };
        AnvilSeam = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = anvilSeamGlow, Width = 1, Height = 16,
                FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetTop = -AnvilSeamHalf, OffsetBottom = AnvilSeamHalf,
        };
        AnvilLids.AddChild(AnvilSeam);
        ResetAnvilLids();
        return AnvilLids;
    }

    private static Panel AnvilLid(bool top)
    {
        var style = new StyleBoxFlat { BgColor = AnvilLidColour, BorderColor = UiTheme.Gold };
        if (top) style.BorderWidthBottom = AnvilLidEdge;
        else style.BorderWidthTop = AnvilLidEdge;
        var lid = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1f };
        lid.AddThemeStyleboxOverride("panel", style);
        return lid;
    }

    internal void SetAnvilBench(AnvilBench bench)
    {
        ClearSockets();
        anvilBench = bench;
        itemBench.Visible = bench == AnvilBench.Item;
        accessoryBench.Visible = bench == AnvilBench.Accessory;
        sockets = bench == AnvilBench.Item ? itemSockets : accessorySockets;
        resultSocket = bench == AnvilBench.Item ? itemResultSocket : accessoryResultSocket;
        btn.Text = bench == AnvilBench.Item ? "Upgrade" : "Compound";
        ShowAnvilPrompt();
    }

    private void OnAnvilTab(int index)
    {
        var bench = (AnvilBench)index;
        if (InteractionLocked)
        {
            AnvilTabs.Select((int)anvilBench, notify: false);
            return;
        }
        if (bench == anvilBench) return;
        SetAnvilBench(bench);
        context.BagCompanions.RefreshFit();
    }

    internal void HoldBenchHeight()
    {
        if (anvilBenches.CustomMinimumSize.Y > 0 || !itemBench.IsInsideTree()) return;
        anvilBenches.CustomMinimumSize = new Vector2(0, itemBench.GetCombinedMinimumSize().Y);
    }

    private string BenchPrompt => anvilBench == AnvilBench.Item
    ? "Place the item to upgrade."
    : "Place three identical accessories.";

    private string BenchFollowUp => anvilBench == AnvilBench.Item
    ? "Then add the scrolls or materials it needs."
    : "Then add a compound scroll.";

    private void ShowAnvilPrompt() => SetAnvilStrip(BenchPrompt, BenchFollowUp, UiTheme.TextLo);

    internal void SetAnvilStrip(string title, string sub, Color tone)
    {
        AnvilStrip.Title.Text = title;
        AnvilStrip.Sub.Text = sub;
        AnvilStrip.Sub.AddThemeColorOverride("font_color", tone);
    }

    private void OnOpen(int anvilId)
    {
        this.AnvilId = anvilId;
        vendor.Close();
        ShowAnvilChoice();
    }

    internal void ShowAnvilChoice()
    {
        context.NpcDialog.Begin("Magic Anvil", AnvilSelectText);
        context.NpcDialog.AddMenuButton("1.   Upgrade Item", () => OpenAnvilBench(AnvilBench.Item));
        context.NpcDialog.AddMenuButton("2.   Compound Accessory", () => OpenAnvilBench(AnvilBench.Accessory));
        context.NpcDialog.AddMenuButton("3.   Walk away", context.NpcDialog.Close);
        context.NpcDialog.End(3);
    }

    private void BackToAnvilMenu()
    {
        if (!Session.CanSend || pendingResult != null) return;
        Close();
        ShowAnvilChoice();
    }

    private void OpenAnvilBench(AnvilBench bench)
    {
        context.NpcDialog.Close();
        AnvilTabs.Select((int)bench, notify: false);
        SetAnvilBench(bench);
        anvilFooter.ResetStatus();
        Show();
        anvilCompanion ??= new BagCompanion(AnvilTakeFromBag, AnvilBagFit, _ => "", Close, (from, to, _) => AnvilIntoBag(from, to));
        context.BagCompanions.Attach(anvilCompanion);
        HoldBenchHeight();
        RefreshActions();
    }

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Session.Closed();
        scanTween?.Kill();
        scanTween = null;
        if (pendingResult != null) FinishScan();
        ResetAnvilLids();
        if (!Session.Awaiting) context.BagCompanions.ReleaseHold();
        Panel.Visible = false;
        context.Tooltips.Hide();
        DismissConfirm();
        ClearSockets();
        if (anvilCompanion != null) context.BagCompanions.Detach(anvilCompanion);
    }

    private void DismissConfirm()
    {
        if (confirm != null && GodotObject.IsInstanceValid(confirm))
            confirm.Close();
        confirm = null;
    }

    private bool AnvilTakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        PlaceItem(abs);
        return true;
    }

    private BagFit AnvilBagFit(int abs)
    {
        if (abs < Inventory.GridStart) return BagFit.Normal;
        if (IsSlotStaged(abs)) return BagFit.Staged;
        return self.Inventory.InMainBag(abs) && UsableOnBench(self.Inventory[abs].ItemId) ? BagFit.Normal : BagFit.Unfit;
    }

    private bool AnvilIntoBag(int socket, int abs)
    {
        ClearSocket(socket);
        return true;
    }

    private bool UsableOnBench(int itemId) => anvilBench == AnvilBench.Item
    ? IsTarget(itemId) || IsMaterial(itemId)
    : IsAccessory(itemId) || IsAccessoryMaterial(itemId);

    private bool IsSlotStaged(int absSlot)
    {
        if (!self.Inventory.InMainBag(absSlot)) return false;
        int rel = absSlot - Inventory.GridStart;
        foreach (int pos in positions)
            if (pos == rel) return true;
        return false;
    }

    internal void PlaceItem(int absSlot)
    {
        if (InteractionLocked || !self.Inventory.InMainBag(absSlot) || self.Inventory[absSlot].IsEmpty) return;
        if (!self.Inventory[absSlot].IsTradable)
        {
            anvilFooter.Status(RefusalFor(self.Inventory[absSlot]), bad: true);
            return;
        }

        int rel = absSlot - Inventory.GridStart;
        for (int i = 0; i < positions.Length; i++)
            if (positions[i] == rel)
            {
                ClearSocket(i);
                return;
            }

        int itemId = self.Inventory[absSlot].ItemId;
        string problem;
        int target = anvilBench == AnvilBench.Accessory
            ? AccessorySocketFor(itemId, out problem)
            : ItemSocketFor(itemId, out problem);
        if (target < 0)
        {
            anvilFooter.Status(problem, bad: true);
            return;
        }
        StageInSocket(target, absSlot);
    }

    private int ItemSocketFor(int itemId, out string problem)
    {
        problem = "";
        if (itemIds[0] == 0 && IsTarget(itemId)) return 0;
        if (IsMaterial(itemId) || (itemIds[0] != 0 && itemId == itemIds[0]))
        {
            int free = FirstEmptySocket(1, SlotCount);
            if (free < 0) problem = "All material sockets are full.";
            return free;
        }
        problem = PlacementError(itemId);
        return -1;
    }

    private int AccessorySocketFor(int itemId, out string problem)
    {
        problem = "";
        if (IsAccessory(itemId))
        {
            if (!AccessoryMatches(itemId, -1))
            {
                problem = "Compounding needs three identical accessories.";
                return -1;
            }
            int free = FirstEmptySocket(0, AccessorySocketCount);
            if (free < 0) problem = "All three accessory slots are full.";
            return free;
        }
        if (IsAccessoryMaterial(itemId))
        {
            int free = FirstEmptySocket(AccessorySocketCount, AccessoryBenchSlots);
            if (free < 0) problem = "All material sockets are full.";
            return free;
        }
        problem = "Only accessories can be compounded.";
        return -1;
    }

    private bool AccessoryMatches(int itemId, int except)
    {
        for (int i = 0; i < AccessorySocketCount; i++)
            if (i != except && itemIds[i] != 0 && itemIds[i] != itemId) return false;
        return true;
    }

    private bool SocketAccepts(int socket, int itemId)
    {
        if (anvilBench == AnvilBench.Accessory)
            return socket < AccessorySocketCount
                ? IsAccessory(itemId) && AccessoryMatches(itemId, socket)
                : socket < AccessoryBenchSlots && IsAccessoryMaterial(itemId);
        if (socket == 0) return IsTarget(itemId) && !IsAccessory(itemId);
        return IsMaterial(itemId) || (itemIds[0] != 0 && itemId == itemIds[0]);
    }

    private bool CanStageDrop(int socket, Variant data)
    {
        if (InteractionLocked || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("invFrom")) return false;
        int abs = d["invFrom"].AsInt32();
        return self.Inventory.InMainBag(abs) && !self.Inventory[abs].IsEmpty && self.Inventory[abs].IsTradable && !IsSlotStaged(abs)
               && SocketAccepts(socket, self.Inventory[abs].ItemId);
    }

    private void StageDrop(int socket, Variant data)
    {
        if (!CanStageDrop(socket, data)) return;
        StageInSocket(socket, data.AsGodotDictionary()["invFrom"].AsInt32());
    }

    private void StageInSocket(int socket, int absSlot)
    {
        var itemSlot = self.Inventory[absSlot];
        if (!itemSlot.IsTradable)
        {
            anvilFooter.Status(RefusalFor(itemSlot), bad: true);
            return;
        }
        if (!SocketAccepts(socket, itemSlot.ItemId))
        {
            anvilFooter.Status(ItemData.Text(NoMatchText, "The items required for upgrade does not match."), bad: true);
            return;
        }
        itemIds[socket] = itemSlot.ItemId;
        positions[socket] = absSlot - Inventory.GridStart;
        itemSlot.Count = 1;
        sockets[socket]?.Set(itemSlot);
        context.BagCompanions.RefreshFit();
        OnBenchChanged();
    }

    private int FirstEmptySocket(int from, int to)
    {
        for (int i = from; i < to; i++)
            if (itemIds[i] == 0) return i;
        return -1;
    }

    private static bool IsAccessory(int itemId) =>
        ItemData.Get(itemId) is { Countable: 0 } def
        && def.Slot is ItemData.SlotType.Earring or ItemData.SlotType.Necklace or ItemData.SlotType.Ring
            or ItemData.SlotType.Belt;

    private static bool IsAccessoryMaterial(int itemId) =>
        itemId is >= UpgradeScrolls.AccessoryCompoundFirst and <= UpgradeScrolls.AccessoryCompoundLast or TrinaPieceAccessory;

    private int MaterialFirst => anvilBench == AnvilBench.Item ? 1 : AccessorySocketCount;

    private int MaterialEnd => anvilBench == AnvilBench.Item ? SlotCount : AccessoryBenchSlots;

    private void ClearSocket(int index)
    {
        if (index < 0 || index >= itemIds.Length || InteractionLocked) return;
        if (itemIds[index] == 0) return;
        itemIds[index] = 0;
        positions[index] = -1;
        sockets[index]?.Clear();
        context.BagCompanions.RefreshFit();
        OnBenchChanged();
    }

    private void ClearSockets()
    {
        for (int i = 0; i < itemIds.Length; i++)
        {
            itemIds[i] = 0;
            positions[i] = -1;
            sockets[i]?.Clear();
        }
        previewId = 0;
        ClearResultSocket();
        RefreshActions();
    }

    private void ClearResultSocket()
    {
        resultSocket.Clear();
        resultSocket.Look = SlotLook.Normal;
    }

    private void ShowResultItem(int itemId, SlotLook look)
    {
        resultSocket.Set(new ItemSlot { ItemId = itemId, Count = 1, Durability = ItemData.MaxDurabilityOf(itemId) });
        resultSocket.Look = look;
    }

    private void OnBenchChanged()
    {
        previewId = 0;
        ClearResultSocket();

        if (itemIds[0] == 0)
        {
            ShowAnvilPrompt();
            RefreshActions();
            return;
        }

        string name = ItemData.DisplayName(itemIds[0]);
        if (anvilBench == AnvilBench.Accessory && FirstEmptySocket(0, AccessorySocketCount) >= 0)
            SetAnvilStrip(name, "Compounding needs three identical accessories.", UiTheme.TextLo);
        else if (!HasMaterial())
            SetAnvilStrip(name, anvilBench == AnvilBench.Item ? "Add the upgrade materials." : "Add the compound materials.", UiTheme.TextLo);
        else
        {
            SetAnvilStrip(name, "Checking the recipe...", UiTheme.TextLo);
            if (previewGate.Begin(itemIds, positions))
                itemRequests.UpgradeRequest(AnvilId, itemIds, positions, preview: true);
        }
        RefreshActions();
    }

    private bool HasMaterial()
    {
        for (int i = MaterialFirst; i < MaterialEnd; i++)
            if (itemIds[i] != 0) return true;
        return false;
    }

    private string OperationName()
    {
        for (int i = 1; i < itemIds.Length; i++)
        {
            int id = itemIds[i];
            if (id >= UpgradeScrolls.AccessoryCompoundFirst && id <= UpgradeScrolls.AccessoryCompoundLast)
                return "Accessory compound";
            if (id == UpgradeScrolls.Reverse)
                return "Reverse conversion";
            if (id == ReverseStrengthenScroll)
                return "Reverse upgrade";
            if (id is ScrollHigh or ScrollHighBlessed or ScrollClass
                or ScrollMiddle or ScrollLow or ScrollTraining)
                return "Upgrade";
            if (IsBonusScroll(id))
                return "Bonus";
        }
        return "Upgrade";
    }

    private static bool IsMaterial(int itemId) => itemId switch
    {
        >= ScrollHigh and <= BonusScrollHighLast => true,
        >= DispelScrollFirst and <= DispelScrollLast => true,
        ScrollClass => true,
        >= UpgradeScrolls.AccessoryCompoundFirst and <= UpgradeScrolls.AccessoryCompoundLast => true,
        >= ScrollMiddle and <= BonusScrollMiddleLast => true,
        >= ScrollLow and <= BonusScrollLowLast => true,
        ScrollTraining or UpgradeScrolls.Reverse or ReverseStrengthenScroll or KarivdisPiece => true,
        TrinaPiece or TrinaPieceMiddle or TrinaPieceLow or TrinaPieceAccessory => true,
        RebirthRestorationScroll or BlessingLogos => true,
        _ => false
    };

    private static bool IsTarget(int itemId)
    {
        if (IsMaterial(itemId)) return false;
        var def = ItemData.Get(itemId);
        return def != null && def.Countable == 0
               && def.Slot >= EquipSlotFirst && def.Slot <= EquipSlotLast
               && (def.Kind < EtcKindFirst || def.Kind > EtcKindLast);
    }

    public string PlacementReport(params int[] itemIds)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (int id in itemIds)
        {
            string role = IsMaterial(id) ? "material" : IsTarget(id) ? "item" : "REJECT";
            parts.Add($"{ItemData.DisplayName(id)}={role}");
        }
        return string.Join(" | ", parts);
    }

    private string PlacementError(int itemId)
    {
        if (IsTarget(itemId))
            return $"The item socket already holds {ItemData.DisplayName(itemIds[0])}. Clear it first.";
        if (itemIds[0] == 0)
            return $"{ItemData.DisplayName(itemId)} cannot be upgraded.";
        return $"{ItemData.DisplayName(itemId)} is not an upgrade material.";
    }

    private static string RefusalFor(ItemSlot itemSlot) => itemSlot.State switch
    {
        ItemFlag.Sealed or ItemFlag.Bound => ItemData.Text(TextSealed, "Sealed items cannot be upgraded."),
        ItemFlag.Rented => ItemData.Text(TextRented, "You cannot upgrade a rented item."),
        ItemFlag.CharacterSeal or ItemFlag.Duplicate =>
            ItemData.Text(GameText.DuplicateSerial, "Items with duplicated serial number cannot be upgraded."),
        _ => ItemData.Text(RefusedText, "Cannot perform item upgrade."),
    };

    private static bool IsBonusScroll(int itemId)
    => (itemId > ScrollHigh && itemId <= BonusScrollHighLast)
       || (itemId >= DispelScrollFirst && itemId <= DispelScrollLast)
       || (itemId > ScrollMiddle && itemId <= BonusScrollMiddleLast)
       || (itemId > ScrollLow && itemId <= BonusScrollLowLast);

    private void Confirm()
    {
        if (!CanSend()) return;
        var sentItems = (int[])itemIds.Clone();
        var sentPositions = (int[])positions.Clone();
        int preview = previewId;
        DismissConfirm();
        confirm = Notice.Confirm(
            context.Root,
            "The item might be destroyed while performing the upgrade. Will you continue?",
            "Upgrade", "Cancel",
            () =>
            {
                if (SelectionMatches(sentItems, sentPositions) && preview == previewId) Send();
                else
                {
                    confirm = null;
                    anvilFooter.Status(SelectionChangedText, bad: true);
                }
            },
            () => confirm = null,
            "Magic Anvil");
    }

    private void Send()
    {
        confirm = null;
        if (!CanSend()) return;
        Session.Sent();
        foreach (int pos in positions)
            if (pos >= 0) context.BagCompanions.Hold.Hold(Inventory.GridStart + pos, self.Inventory[Inventory.GridStart + pos]);
        RefreshActions();
        SetAnvilStrip(AnvilStrip.Title.Text, $"{OperationName()} in progress...", UiTheme.TextLo);
        itemRequests.UpgradeRequest(AnvilId, itemIds, positions, preview: false);
    }

    private bool CanSend()
    => shown && Session.CanSend && !InteractionLocked && AnvilId != 0
       && itemIds[0] != 0 && previewId != 0;

    internal void OnResult(UpgradeResult result)
    {
        if (result.UpgradeType == ItemUpgradeKind.Preview)
        {
            OnPreviewResult(result);
            return;
        }
        bool watched = Session.Answered() == UpgradeAnswerView.Reveal;
        if (watched && shown && result.ResultCode is ItemUpgradeResult.Succeeded or ItemUpgradeResult.Failed)
        {
            StartScan(result);
            return;
        }
        ApplyResult(result, onBench: watched);
    }

    internal static bool ScanFails(ItemUpgradeResult code) => code == ItemUpgradeResult.Failed;

    private static Color[] SeamColours(bool fails)
    {
        Color edge = fails ? UiTheme.Bad : UiTheme.GoldVivid;
        Color core = fails ? UiTheme.Bad : UiTheme.GoldBright;
        return new[] { new Color(edge, 0f), core, new Color(edge, 0f) };
    }

    private void StartScan(UpgradeResult result)
    {
        pendingResult = result;
        scanTween?.Kill();
        ResetAnvilLids();
        bool fails = ScanFails(result.ResultCode);
        if (anvilBench == AnvilBench.Accessory)
        {
            compoundGauge.AddThemeStyleboxOverride("fill", UiKit.CachedMeterFill(fails ? UiTheme.Bad : UiTheme.Gold));
            compoundGauge.Value = 0;
            compoundGauge.Visible = true;
            var fill = context.Root.CreateTween();
            fill.TweenProperty(compoundGauge, "value", 1.0, AnvilScanSeconds);
            fill.TweenCallback(Callable.From(FinishCompoundGauge));
            scanTween = fill;
            return;
        }
        anvilSeamGlow.Colors = SeamColours(fails);
        AnvilLids.Visible = true;
        var tween = context.Root.CreateTween();
        tween.TweenProperty(AnvilLidTop, "anchor_bottom", 0.5f, AnvilLidSeconds);
        tween.Parallel().TweenProperty(AnvilLidBottom, "anchor_top", 0.5f, AnvilLidSeconds);
        tween.TweenProperty(AnvilSeam, "anchor_right", 1f, AnvilScanSeconds);
        tween.TweenCallback(Callable.From(FinishScan));
        scanTween = tween;
        RefreshActions();
    }

    private void FinishScan()
    {
        scanTween = null;
        if (pendingResult is not { } result) return;
        pendingResult = null;
        ApplyResult(result, onBench: true);
        if (shown) OpenAnvilLids();
    }

    private void FinishCompoundGauge()
    {
        scanTween = null;
        compoundGauge.Visible = false;
        if (pendingResult is not { } result) return;
        pendingResult = null;
        ApplyResult(result, onBench: true);
    }

    private void OpenAnvilLids()
    {
        AnvilSeam.AnchorRight = 0f;
        var tween = context.Root.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(AnvilLidTop, "anchor_bottom", 0f, AnvilRevealSeconds);
        tween.Parallel().TweenProperty(AnvilLidBottom, "anchor_top", 1f, AnvilRevealSeconds);
        tween.TweenCallback(Callable.From(ResetAnvilLids));
        scanTween = tween;
        RefreshActions();
    }

    private void ResetAnvilLids()
    {
        scanTween = null;
        AnvilLids.Visible = false;
        if (compoundGauge != null) compoundGauge.Visible = false;
        AnvilLidTop.AnchorTop = 0f;
        AnvilLidTop.AnchorBottom = 0f;
        AnvilLidBottom.AnchorTop = 1f;
        AnvilLidBottom.AnchorBottom = 1f;
        AnvilSeam.AnchorRight = 0f;
        RefreshActions();
    }

    internal void ApplyResult(UpgradeResult result, bool onBench)
    {
        context.BagCompanions.Hold.Release();
        int resultItemId = result.Slots.Length > 0 ? result.Slots[0].ItemId : 0;
        if (onBench) ShowOutcome(result.ResultCode, resultItemId);

        switch (result.ResultCode)
        {
            case ItemUpgradeResult.Succeeded:
                context.Log.Notice(ItemData.Text(SucceededText, "Item upgrade succeeded."), SucceededTint);
                break;
            case ItemUpgradeResult.Failed:
                context.Log.Notice(resultItemId == 0
                    ? ItemData.Text(FailedText, "Item upgrade failed.")
                    : $"Upgrade failed. Retained item: {ItemData.DisplayName(resultItemId)}", RefusedTint);
                break;
            default:
                context.Log.Notice(Error(result.ResultCode), RefusedTint);
                break;
        }

        context.BagCompanions.RefreshFit();
        RefreshActions();
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
    }

    private void ShowOutcome(ItemUpgradeResult code, int resultItemId)
    {
        string before = itemIds[0] != 0 ? ItemData.DisplayName(itemIds[0]) : BenchPrompt;
        string operation = OperationName();
        if (code is ItemUpgradeResult.Succeeded or ItemUpgradeResult.Failed) ClearSockets();

        switch (code)
        {
            case ItemUpgradeResult.Succeeded when resultItemId != 0:
                ShowResultItem(resultItemId, SlotLook.Normal);
                SetAnvilStrip(ItemData.DisplayName(resultItemId), $"{operation} succeeded.", UiTheme.Good);
                break;
            case ItemUpgradeResult.Succeeded:
                SetAnvilStrip(before, $"{operation} succeeded.", UiTheme.Good);
                break;
            case ItemUpgradeResult.Failed when resultItemId != 0:
                ShowResultItem(resultItemId, SlotLook.Normal);
                SetAnvilStrip(before, $"{operation} failed — retained {ItemData.DisplayName(resultItemId)}.", UiTheme.Bad);
                break;
            case ItemUpgradeResult.Failed:
                SetAnvilStrip(before, $"{operation} failed — the item was destroyed.", UiTheme.Bad);
                break;
            default:
                SetAnvilStrip(before, Error(code), UiTheme.Bad);
                break;
        }
    }

    private void OnPreviewResult(UpgradeResult result)
    {
        if (!previewGate.Complete(itemIds, positions))
        {
            if (shown) OnBenchChanged();
            return;
        }
        if (itemIds[0] == 0) return;
        int previewId = result.Slots.Length > 0 ? result.Slots[0].ItemId : 0;
        string name = ItemData.DisplayName(itemIds[0]);
        if (result.ResultCode == ItemUpgradeResult.Succeeded && previewId != 0)
        {
            this.previewId = previewId;
            ShowResultItem(previewId, SlotLook.Ghost);
            SetAnvilStrip($"{name} → {ItemData.DisplayName(previewId)}",
                $"{OperationName()} · the item may be destroyed", UiTheme.Warning);
        }
        else
        {
            this.previewId = 0;
            ClearResultSocket();
            SetAnvilStrip(name, Error(result.ResultCode), UiTheme.Bad);
        }
        RefreshActions();
    }

    private const int SucceededText = 6700;
    private const int FailedText = 6701;
    private const int RefusedText = 6702;
    private const int NoCoinsText = 6703;
    private const int NoMatchText = 6704;
    private static readonly Color SucceededTint = Color.Color8(128, 128, 255);
    private static readonly Color RefusedTint = Color.Color8(255, 0, 255);

    private static string Error(ItemUpgradeResult code) => code switch
    {
        ItemUpgradeResult.NeedCoins => ItemData.Text(NoCoinsText, "You don't have enough Coins."),
        ItemUpgradeResult.NoMatch => ItemData.Text(NoMatchText, "The items required for upgrade does not match."),
        _ => ItemData.Text(RefusedText, "Cannot perform item upgrade."),
    };

    internal void RefreshActions()
    {
        if (btn == null) return;
        btn.Disabled = !CanSend();
    }

    public bool StageAnvil(int originItemId, int materialItemId, int extraItemId = 0)
    {
        if (!shown) return false;
        int origin = FindBackpackSlot(originItemId);
        int material = FindBackpackSlot(materialItemId);
        int extra = extraItemId == 0 ? -1 : FindBackpackSlot(extraItemId);
        if (origin < 0 || material < 0 || (extraItemId != 0 && extra < 0))
        {
            GD.Print($"[anvil] stage miss origin={originItemId}@{origin} material={materialItemId}@{material} extra={extraItemId}@{extra} bag={BagReport()}");
            return false;
        }
        PlaceItem(origin);
        PlaceItem(material);
        if (extra >= 0) PlaceItem(extra);
        return itemIds[0] == originItemId && itemIds[1] == materialItemId
               && (extraItemId == 0 || itemIds[2] == extraItemId);
    }

    public void CommitAnvil() => Send();

    public Vector2 HoverPoint()
    {
        if (itemIds[0] != 0 && sockets[0] is { } staged)
            return staged.GetGlobalRect().GetCenter();
        if (previewId != 0)
            return resultSocket.GetGlobalRect().GetCenter();
        return Vector2.Zero;
    }

    private int SocketSlot(int index)
    => positions[index] >= 0 ? Inventory.GridStart + positions[index] : -1;

    private string BagReport()
    {
        var parts = new System.Collections.Generic.List<string>();
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].ItemId != 0) parts.Add($"{abs}:{self.Inventory[abs].ItemId}x{self.Inventory[abs].Count}");
        return parts.Count == 0 ? "empty" : string.Join(" ", parts);
    }

    internal int FindBackpackSlot(int itemId)
    {
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].ItemId == itemId && !IsSlotStaged(abs)) return abs;
        return -1;
    }

    private void OnInventorySlot(int absSlot, ItemSlot itemSlot)
    {
        if (shown && self.Inventory.InMainBag(absSlot)) DropStaleSockets();
    }

    private void OnInventoryGrid(ItemSlot[] itemSlots)
    {
        if (shown) DropStaleSockets();
    }

    private void DropStaleSockets()
    {
        if (InteractionLocked) return;
        bool dropped = false;
        for (int i = 0; i < positions.Length; i++)
        {
            int pos = positions[i];
            if (pos < 0) continue;
            int abs = Inventory.GridStart + pos;
            var held = abs < self.Inventory.Length ? self.Inventory[abs] : default;
            if (!held.IsEmpty && held.IsTradable && held.ItemId == itemIds[i])
            {
                held.Count = 1;
                sockets[i]?.Set(held);
                continue;
            }
            itemIds[i] = 0;
            positions[i] = -1;
            sockets[i]?.Clear();
            dropped = true;
        }
        if (!dropped) return;
        context.BagCompanions.RefreshFit();
        OnBenchChanged();
    }

    private bool SelectionMatches(int[] sentItems, int[] sentPositions)
    => sentItems.AsSpan().SequenceEqual(itemIds) && sentPositions.AsSpan().SequenceEqual(positions);
}
