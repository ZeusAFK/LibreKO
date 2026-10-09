using Godot;
using LibreKO.Domain;

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
    private const int AccessorySocketCount = 3;
    private const int AccessoryBenchSlots = AccessorySocketCount + 2;
    private const int SlotEarring = 10;
    private const int SlotNecklace = 11;
    private const int SlotRing = 12;
    private const int SlotBelt = 14;
    private const float AnvilItemSocketSize = 56f;
    private const float AnvilMaterialSocketSize = 44f;
    private const int AnvilSocketGap = 5;
    private const int AnvilBodyMinWidth = 470;
    private const int AnvilLidEdge = 2;
    private const float AnvilSeamHalf = 5f;
    private const double AnvilLidSeconds = 0.25;
    private const double AnvilScanSeconds = 1.9;
    private const double AnvilRevealSeconds = 0.8;
    private const string AnvilHint = "Right-click a bag item to place it";
    private const int TextUpgradeNotPossible = 6702;
    private const int TextUpgradeNoMatch = 6704;
    private const int TextUpgradeRented = 10748;
    private const int TextUpgradeDuplicateSerial = 10760;
    private const int TextUpgradeSealed = 15103;
    private const string UpgradeSelectionChangedText = "The selection changed. Check it and press Upgrade again.";
    private static readonly Color AnvilLidColour = new(0.045f, 0.045f, 0.055f);
    private const string AnvilSelectText =
        "You can upgrade your item at the magic anvil. Please select what you want to upgrade.";

    private enum AnvilBench { Item, Accessory }

    private CanvasLayer _upgradeLayer = null!;
    private HudWindow _upgradePanel = null!;
    private ServiceTabs _anvilTabs = null!;
    private VBoxContainer _anvilBenches = null!;
    private DetailStrip _anvilStrip = null!;
    private FooterBand _anvilFooter = null!;
    private Button _upgradeBtn = null!;
    private ItemSlotView _upgradeResultSocket = null!;
    private ItemSlotView _itemResultSocket = null!;
    private ItemSlotView _accessoryResultSocket = null!;
    private Control _itemBench = null!;
    private Control _accessoryBench = null!;
    private Control _anvilLids = null!;
    private Panel _anvilLidTop = null!;
    private Panel _anvilLidBottom = null!;
    private TextureRect _anvilSeam = null!;
    private Tween? _upgradeScanTween;
    private UpgradeResult? _upgradePendingResult;
    private BagCompanion? _anvilCompanion;
    private AnvilBench _anvilBench = AnvilBench.Item;
    private bool _upgradeShown;
    private readonly UpgradeSession _upgradeSession = new();
    private readonly UpgradePreviewGate _upgradePreviewGate = new();
    private bool UpgradeInteractionLocked => _upgradeSession.Awaiting || _upgradePendingResult != null || _upgradeScanTween != null;
    private int _upgradeAnvilId;
    private int _upgradePreviewId;
    private Notice? _upgradeConfirm;

    private readonly int[] _upgradeItemIds = new int[UpgradeSlotCount];
    private readonly int[] _upgradePositions = new int[UpgradeSlotCount];
    private readonly ItemSlotView?[] _itemSockets = new ItemSlotView?[UpgradeSlotCount];
    private readonly ItemSlotView?[] _accessorySockets = new ItemSlotView?[UpgradeSlotCount];
    private ItemSlotView?[] _upgradeSockets = null!;

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

        _upgradePanel = new HudWindow("anvil", "Magic Anvil", bodyMinWidth: AnvilBodyMinWidth) { Visible = false };
        _upgradePanel.Closed += CloseUpgrade;
        _upgradeLayer.AddChild(_upgradePanel);

        var root = _upgradePanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        _anvilTabs = new ServiceTabs();
        _anvilTabs.SetTabs(new[] { "Upgrade Item", "Compound Accessory" }, (int)AnvilBench.Item);
        _anvilTabs.Selected += OnAnvilTab;
        root.AddChild(_anvilTabs);

        var well = ServiceKit.Well();
        root.AddChild(well);
        _anvilBenches = new VBoxContainer();
        well.AddChild(_anvilBenches);
        _upgradeSockets = _itemSockets;
        _itemBench = BuildUpgradeBench();
        _anvilBenches.AddChild(_itemBench);
        _accessoryBench = BuildAccessoryBench();
        _accessoryBench.Visible = false;
        _anvilBenches.AddChild(_accessoryBench);
        _upgradeResultSocket = _itemResultSocket;
        well.AddChild(BuildAnvilLids());

        _anvilStrip = new DetailStrip(AnvilItemSocketSize);
        _anvilStrip.Slot.Visible = false;
        _upgradeBtn = UiTheme.ActionButton("Upgrade", "Upgrade the item on the anvil");
        _upgradeBtn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _upgradeBtn.Pressed += ConfirmUpgrade;
        _anvilStrip.Right.AddChild(_upgradeBtn);
        root.AddChild(_anvilStrip);

        _anvilFooter = new FooterBand { Hint = AnvilHint };
        root.AddChild(_anvilFooter);

        ClearUpgradeSockets();
        ShowAnvilPrompt();
    }

    private Control BuildUpgradeBench()
    {
        var bench = BenchRow();
        bench.AddChild(BuildUpgradeColumn("Item", BenchSocket(_itemSockets, 0, AnvilItemSocketSize)));
        bench.AddChild(BenchSign("+"));

        var materials = new GridContainer { Columns = 3 };
        materials.AddThemeConstantOverride("h_separation", AnvilSocketGap);
        materials.AddThemeConstantOverride("v_separation", AnvilSocketGap);
        for (int i = 1; i < UpgradeSlotCount; i++)
            materials.AddChild(BenchSocket(_itemSockets, i, AnvilMaterialSocketSize));
        bench.AddChild(BuildUpgradeColumn("Materials", materials));

        bench.AddChild(BenchSign("="));
        _itemResultSocket = BuildResultSocket();
        bench.AddChild(BuildUpgradeColumn("Result", _itemResultSocket));
        return bench;
    }

    private Control BuildAccessoryBench()
    {
        var bench = BenchRow();
        var accessories = new HBoxContainer();
        accessories.AddThemeConstantOverride("separation", AnvilSocketGap);
        for (int i = 0; i < AccessorySocketCount; i++)
            accessories.AddChild(BenchSocket(_accessorySockets, i, AnvilItemSocketSize));
        bench.AddChild(BuildUpgradeColumn("Accessories", accessories));
        bench.AddChild(BenchSign("+"));

        var materials = new HBoxContainer();
        materials.AddThemeConstantOverride("separation", AnvilSocketGap);
        for (int i = AccessorySocketCount; i < AccessoryBenchSlots; i++)
            materials.AddChild(BenchSocket(_accessorySockets, i, AnvilMaterialSocketSize));
        bench.AddChild(BuildUpgradeColumn("Materials", materials));

        bench.AddChild(BenchSign("="));
        _accessoryResultSocket = BuildResultSocket();
        bench.AddChild(BuildUpgradeColumn("Result", _accessoryResultSocket));
        return bench;
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

    private static Control BenchSign(string sign) => BuildUpgradeColumn(" ", UiTheme.Text(sign, 22, UiTheme.GoldDark));

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

    private ItemSlotView BenchSocket(ItemSlotView?[] bench, int index, float size)
    {
        var socket = new ItemSlotView(size) { Index = index };
        socket.RightClicked += _ => ClearUpgradeSocket(index);
        socket.DoubleClicked += _ => ClearUpgradeSocket(index);
        socket.Hovered += s => ShowItemTooltip(UpgradeSocketSlot(index), s.Item);
        socket.Unhovered += _ => HideItemTooltip();
        socket.DragOut = _ => UpgradeInteractionLocked ? default(Variant) : new Godot.Collections.Dictionary { { "companionFrom", index } };
        socket.CanDrop = (_, data) => CanStageDrop(index, data);
        socket.Dropped = (_, data) => StageDrop(index, data);
        bench[index] = socket;
        return socket;
    }

    private ItemSlotView BuildResultSocket()
    {
        var socket = new ItemSlotView(AnvilItemSocketSize);
        socket.Hovered += s => ShowItemTooltip(-1, s.Item);
        socket.Unhovered += _ => HideItemTooltip();
        return socket;
    }

    private Control BuildAnvilLids()
    {
        _anvilLids = new Control { MouseFilter = Control.MouseFilterEnum.Stop, ClipContents = true };
        _anvilLidTop = AnvilLid(top: true);
        _anvilLids.AddChild(_anvilLidTop);
        _anvilLidBottom = AnvilLid(top: false);
        _anvilLids.AddChild(_anvilLidBottom);

        var glow = new Gradient
        {
            Offsets = new[] { 0f, 0.5f, 1f },
            Colors = new[] { new Color(UiTheme.GoldVivid, 0f), UiTheme.GoldBright, new Color(UiTheme.GoldVivid, 0f) },
        };
        _anvilSeam = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = glow, Width = 1, Height = 16,
                FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1),
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetTop = -AnvilSeamHalf, OffsetBottom = AnvilSeamHalf,
        };
        _anvilLids.AddChild(_anvilSeam);
        ResetAnvilLids();
        return _anvilLids;
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

    private void SetAnvilBench(AnvilBench bench)
    {
        ClearUpgradeSockets();
        _anvilBench = bench;
        _itemBench.Visible = bench == AnvilBench.Item;
        _accessoryBench.Visible = bench == AnvilBench.Accessory;
        _upgradeSockets = bench == AnvilBench.Item ? _itemSockets : _accessorySockets;
        _upgradeResultSocket = bench == AnvilBench.Item ? _itemResultSocket : _accessoryResultSocket;
        _upgradeBtn.Text = bench == AnvilBench.Item ? "Upgrade" : "Compound";
        ShowAnvilPrompt();
    }

    private void OnAnvilTab(int index)
    {
        var bench = (AnvilBench)index;
        if (UpgradeInteractionLocked)
        {
            _anvilTabs.Select((int)_anvilBench, notify: false);
            return;
        }
        if (bench == _anvilBench) return;
        SetAnvilBench(bench);
        RefreshBagFit();
    }

    private void HoldBenchHeight()
    {
        if (_anvilBenches.CustomMinimumSize.Y > 0 || !_itemBench.IsInsideTree()) return;
        _anvilBenches.CustomMinimumSize = new Vector2(0, _itemBench.GetCombinedMinimumSize().Y);
    }

    private string BenchPrompt => _anvilBench == AnvilBench.Item
        ? "Place the item to upgrade."
        : "Place three identical accessories.";

    private string BenchFollowUp => _anvilBench == AnvilBench.Item
        ? "Then add the scrolls or materials it needs."
        : "Then add a compound scroll.";

    private void ShowAnvilPrompt() => SetAnvilStrip(BenchPrompt, BenchFollowUp, UiTheme.TextLo);

    private void SetAnvilStrip(string title, string sub, Color tone)
    {
        _anvilStrip.Title.Text = title;
        _anvilStrip.Sub.Text = sub;
        _anvilStrip.Sub.AddThemeColorOverride("font_color", tone);
    }

    private void OnUpgradeOpen(int anvilId)
    {
        _upgradeAnvilId = anvilId;
        CloseVendor();
        ShowAnvilChoice();
    }

    private void ShowAnvilChoice()
    {
        BeginNpcDialog("Magic Anvil", AnvilSelectText);
        AddNpcMenuButton("1.   Upgrade Item", () => OpenAnvilBench(AnvilBench.Item));
        AddNpcMenuButton("2.   Compound Accessory", () => OpenAnvilBench(AnvilBench.Accessory));
        AddNpcMenuButton("3.   Walk away", CloseNpcDialog);
        EndNpcDialog(3);
    }

    private void OpenAnvilBench(AnvilBench bench)
    {
        CloseNpcDialog();
        _anvilTabs.Select((int)bench, notify: false);
        SetAnvilBench(bench);
        _anvilFooter.ResetStatus();
        _upgradePanel.Visible = true;
        _upgradeShown = true;
        _anvilCompanion ??= new BagCompanion(AnvilTakeFromBag, AnvilBagFit, _ => "", CloseUpgrade, AnvilIntoBag);
        AttachBagCompanion(_anvilCompanion);
        HoldBenchHeight();
        RefreshUpgradeActions();
    }

    private void CloseUpgrade()
    {
        if (!_upgradeShown) return;
        _upgradeShown = false;
        _upgradeSession.Closed();
        _upgradeScanTween?.Kill();
        _upgradeScanTween = null;
        if (_upgradePendingResult != null) FinishUpgradeScan();
        ResetAnvilLids();
        if (!_upgradeSession.Awaiting) ReleaseBagHold();
        _upgradePanel.Visible = false;
        HideItemTooltip();
        DismissUpgradeConfirm();
        ClearUpgradeSockets();
        if (_anvilCompanion != null) DetachBagCompanion(_anvilCompanion);
    }

    private void DismissUpgradeConfirm()
    {
        if (_upgradeConfirm != null && GodotObject.IsInstanceValid(_upgradeConfirm))
            _upgradeConfirm.Close();
        _upgradeConfirm = null;
    }

    private bool AnvilTakeFromBag(int abs)
    {
        if (!InMainBag(abs)) return false;
        PlaceUpgradeItem(abs);
        return true;
    }

    private BagFit AnvilBagFit(int abs)
    {
        if (abs < GridStart) return BagFit.Normal;
        if (IsUpgradeSlotStaged(abs)) return BagFit.Staged;
        return InMainBag(abs) && UsableOnBench(Inv[abs].ItemId) ? BagFit.Normal : BagFit.Unfit;
    }

    private bool AnvilIntoBag(int socket, int abs)
    {
        ClearUpgradeSocket(socket);
        return true;
    }

    private bool UsableOnBench(int itemId) => _anvilBench == AnvilBench.Item
        ? IsUpgradeTarget(itemId) || IsUpgradeMaterial(itemId)
        : IsAccessory(itemId) || IsAccessoryMaterial(itemId);

    private bool IsUpgradeSlotStaged(int absSlot)
    {
        if (!InMainBag(absSlot)) return false;
        int rel = absSlot - GridStart;
        foreach (int pos in _upgradePositions)
            if (pos == rel) return true;
        return false;
    }

    private void PlaceUpgradeItem(int absSlot)
    {
        if (UpgradeInteractionLocked || !InMainBag(absSlot) || Inv[absSlot].IsEmpty) return;
        if (!Inv[absSlot].IsTradable)
        {
            _anvilFooter.Status(UpgradeRefusalFor(Inv[absSlot]), bad: true);
            return;
        }

        int rel = absSlot - GridStart;
        for (int i = 0; i < _upgradePositions.Length; i++)
            if (_upgradePositions[i] == rel)
            {
                ClearUpgradeSocket(i);
                return;
            }

        int itemId = Inv[absSlot].ItemId;
        string problem;
        int target = _anvilBench == AnvilBench.Accessory
            ? AccessorySocketFor(itemId, out problem)
            : ItemSocketFor(itemId, out problem);
        if (target < 0)
        {
            _anvilFooter.Status(problem, bad: true);
            return;
        }
        StageInSocket(target, absSlot);
    }

    private int ItemSocketFor(int itemId, out string problem)
    {
        problem = "";
        if (_upgradeItemIds[0] == 0 && IsUpgradeTarget(itemId)) return 0;
        if (IsUpgradeMaterial(itemId) || (_upgradeItemIds[0] != 0 && itemId == _upgradeItemIds[0]))
        {
            int free = FirstEmptySocket(1, UpgradeSlotCount);
            if (free < 0) problem = "All material sockets are full.";
            return free;
        }
        problem = UpgradePlacementError(itemId);
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
            if (i != except && _upgradeItemIds[i] != 0 && _upgradeItemIds[i] != itemId) return false;
        return true;
    }

    private bool SocketAccepts(int socket, int itemId)
    {
        if (_anvilBench == AnvilBench.Accessory)
            return socket < AccessorySocketCount
                ? IsAccessory(itemId) && AccessoryMatches(itemId, socket)
                : socket < AccessoryBenchSlots && IsAccessoryMaterial(itemId);
        if (socket == 0) return IsUpgradeTarget(itemId) && !IsAccessory(itemId);
        return IsUpgradeMaterial(itemId) || (_upgradeItemIds[0] != 0 && itemId == _upgradeItemIds[0]);
    }

    private bool CanStageDrop(int socket, Variant data)
    {
        if (UpgradeInteractionLocked || data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        if (!d.ContainsKey("invFrom")) return false;
        int abs = d["invFrom"].AsInt32();
        return InMainBag(abs) && !Inv[abs].IsEmpty && Inv[abs].IsTradable && !IsUpgradeSlotStaged(abs)
               && SocketAccepts(socket, Inv[abs].ItemId);
    }

    private void StageDrop(int socket, Variant data)
    {
        if (!CanStageDrop(socket, data)) return;
        StageInSocket(socket, data.AsGodotDictionary()["invFrom"].AsInt32());
    }

    private void StageInSocket(int socket, int absSlot)
    {
        var item = Inv[absSlot];
        if (!item.IsTradable)
        {
            _anvilFooter.Status(UpgradeRefusalFor(item), bad: true);
            return;
        }
        if (!SocketAccepts(socket, item.ItemId))
        {
            _anvilFooter.Status(ItemData.Text(TextUpgradeNoMatch, "The items required for upgrade does not match."), bad: true);
            return;
        }
        _upgradeItemIds[socket] = item.ItemId;
        _upgradePositions[socket] = absSlot - GridStart;
        item.Count = 1;
        _upgradeSockets[socket]?.Set(item);
        RefreshBagFit();
        OnUpgradeBenchChanged();
    }

    private int FirstEmptySocket(int from, int to)
    {
        for (int i = from; i < to; i++)
            if (_upgradeItemIds[i] == 0) return i;
        return -1;
    }

    private static bool IsAccessory(int itemId) =>
        ItemData.Get(itemId) is { Countable: 0 } def
        && def.Slot is SlotEarring or SlotNecklace or SlotRing or SlotBelt;

    private static bool IsAccessoryMaterial(int itemId) =>
        itemId is >= AccessoryCompoundScrollFirst and <= AccessoryCompoundScrollLast or TrinaPieceAccessory;

    private int MaterialFirst => _anvilBench == AnvilBench.Item ? 1 : AccessorySocketCount;
    private int MaterialEnd => _anvilBench == AnvilBench.Item ? UpgradeSlotCount : AccessoryBenchSlots;

    private void ClearUpgradeSocket(int index)
    {
        if (index < 0 || index >= _upgradeItemIds.Length || UpgradeInteractionLocked) return;
        if (_upgradeItemIds[index] == 0) return;
        _upgradeItemIds[index] = 0;
        _upgradePositions[index] = -1;
        _upgradeSockets[index]?.Clear();
        RefreshBagFit();
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
        ClearResultSocket();
        RefreshUpgradeActions();
    }

    private void ClearResultSocket()
    {
        _upgradeResultSocket.Clear();
        _upgradeResultSocket.Look = SlotLook.Normal;
    }

    private void ShowResultItem(int itemId, SlotLook look)
    {
        _upgradeResultSocket.Set(new ItemSlot { ItemId = itemId, Count = 1, Durability = ItemData.MaxDurabilityOf(itemId) });
        _upgradeResultSocket.Look = look;
    }

    private void OnUpgradeBenchChanged()
    {
        _upgradePreviewId = 0;
        ClearResultSocket();

        if (_upgradeItemIds[0] == 0)
        {
            ShowAnvilPrompt();
            RefreshUpgradeActions();
            return;
        }

        string name = ItemData.DisplayName(_upgradeItemIds[0]);
        if (_anvilBench == AnvilBench.Accessory && FirstEmptySocket(0, AccessorySocketCount) >= 0)
            SetAnvilStrip(name, "Compounding needs three identical accessories.", UiTheme.TextLo);
        else if (!HasUpgradeMaterial())
            SetAnvilStrip(name, _anvilBench == AnvilBench.Item ? "Add the upgrade materials." : "Add the compound materials.", UiTheme.TextLo);
        else
        {
            SetAnvilStrip(name, "Checking the recipe...", UiTheme.TextLo);
            if (_upgradePreviewGate.Begin(_upgradeItemIds, _upgradePositions))
                Net.I.SendUpgradeRequest(_upgradeAnvilId, _upgradeItemIds, _upgradePositions, preview: true);
        }
        RefreshUpgradeActions();
    }

    private bool HasUpgradeMaterial()
    {
        for (int i = MaterialFirst; i < MaterialEnd; i++)
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

    private static string UpgradeRefusalFor(ItemSlot item) => item.State switch
    {
        ItemFlag.Sealed or ItemFlag.Bound => ItemData.Text(TextUpgradeSealed, "Sealed items cannot be upgraded."),
        ItemFlag.Rented => ItemData.Text(TextUpgradeRented, "You cannot upgrade a rented item."),
        ItemFlag.CharacterSeal or ItemFlag.Duplicate =>
            ItemData.Text(TextUpgradeDuplicateSerial, "Items with duplicated serial number cannot be upgraded."),
        _ => ItemData.Text(TextUpgradeNotPossible, "Cannot perform item upgrade."),
    };

    private static bool IsBonusScroll(int itemId)
        => (itemId > UpgradeScrollHigh && itemId <= BonusScrollHighLast)
           || (itemId >= DispelScrollFirst && itemId <= DispelScrollLast)
           || (itemId > UpgradeScrollMiddle && itemId <= BonusScrollMiddleLast)
           || (itemId > UpgradeScrollLow && itemId <= BonusScrollLowLast);

    private void ConfirmUpgrade()
    {
        if (!CanSendUpgrade()) return;
        var items = (int[])_upgradeItemIds.Clone();
        var positions = (int[])_upgradePositions.Clone();
        int preview = _upgradePreviewId;
        DismissUpgradeConfirm();
        _upgradeConfirm = Notice.Confirm(
            this,
            "The item might be destroyed while performing the upgrade. Will you continue?",
            "Upgrade", "Cancel",
            () =>
            {
                if (SelectionMatches(items, positions) && preview == _upgradePreviewId) SendUpgrade();
                else
                {
                    _upgradeConfirm = null;
                    _anvilFooter.Status(UpgradeSelectionChangedText, bad: true);
                }
            },
            () => _upgradeConfirm = null,
            "Magic Anvil");
    }

    private void SendUpgrade()
    {
        _upgradeConfirm = null;
        if (!CanSendUpgrade()) return;
        _upgradeSession.Sent();
        foreach (int pos in _upgradePositions)
            if (pos >= 0) _bagHold.Hold(GridStart + pos, Inv[GridStart + pos]);
        RefreshUpgradeActions();
        SetAnvilStrip(_anvilStrip.Title.Text, $"{UpgradeOperationName()} in progress...", UiTheme.TextLo);
        Net.I.SendUpgradeRequest(_upgradeAnvilId, _upgradeItemIds, _upgradePositions, preview: false);
    }

    private bool CanSendUpgrade()
        => _upgradeShown && _upgradeSession.CanSend && !UpgradeInteractionLocked && _upgradeAnvilId != 0
           && _upgradeItemIds[0] != 0 && _upgradePreviewId != 0;

    private void OnUpgradeResult(UpgradeResult result)
    {
        if (result.UpgradeType == UpgradeTypePreview)
        {
            OnUpgradePreviewResult(result);
            return;
        }
        bool watched = _upgradeSession.Answered() == UpgradeAnswerView.Reveal;
        if (watched && _upgradeShown && result.ResultCode is UpgradeResultSucceeded or UpgradeResultFailed)
        {
            StartUpgradeScan(result);
            return;
        }
        ApplyUpgradeResult(result, onBench: watched);
    }

    private void StartUpgradeScan(UpgradeResult result)
    {
        _upgradePendingResult = result;
        _upgradeScanTween?.Kill();
        ResetAnvilLids();
        _anvilLids.Visible = true;
        var tween = CreateTween();
        tween.TweenProperty(_anvilLidTop, "anchor_bottom", 0.5f, AnvilLidSeconds);
        tween.Parallel().TweenProperty(_anvilLidBottom, "anchor_top", 0.5f, AnvilLidSeconds);
        tween.TweenProperty(_anvilSeam, "anchor_right", 1f, AnvilScanSeconds);
        tween.TweenCallback(Callable.From(FinishUpgradeScan));
        _upgradeScanTween = tween;
        RefreshUpgradeActions();
    }

    private void FinishUpgradeScan()
    {
        _upgradeScanTween = null;
        if (_upgradePendingResult is not { } result) return;
        _upgradePendingResult = null;
        ApplyUpgradeResult(result, onBench: true);
        if (_upgradeShown) OpenAnvilLids();
    }

    private void OpenAnvilLids()
    {
        _anvilSeam.AnchorRight = 0f;
        var tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(_anvilLidTop, "anchor_bottom", 0f, AnvilRevealSeconds);
        tween.Parallel().TweenProperty(_anvilLidBottom, "anchor_top", 1f, AnvilRevealSeconds);
        tween.TweenCallback(Callable.From(ResetAnvilLids));
        _upgradeScanTween = tween;
        RefreshUpgradeActions();
    }

    private void ResetAnvilLids()
    {
        _upgradeScanTween = null;
        _anvilLids.Visible = false;
        _anvilLidTop.AnchorTop = 0f;
        _anvilLidTop.AnchorBottom = 0f;
        _anvilLidBottom.AnchorTop = 1f;
        _anvilLidBottom.AnchorBottom = 1f;
        _anvilSeam.AnchorRight = 0f;
        RefreshUpgradeActions();
    }

    private void ApplyUpgradeResult(UpgradeResult result, bool onBench)
    {
        _bagHold.Release();
        int resultItemId = result.Slots.Length > 0 ? result.Slots[0].ItemId : 0;
        if (onBench) ShowUpgradeOutcome(result.ResultCode, resultItemId);

        switch (result.ResultCode)
        {
            case UpgradeResultSucceeded when resultItemId != 0:
                CombatNotice($"Upgrade succeeded: {ItemData.DisplayName(resultItemId)}");
                break;
            case UpgradeResultFailed:
                CombatNotice(resultItemId == 0
                    ? "The upgrade failed and the item was destroyed."
                    : $"Upgrade failed. Retained item: {ItemData.DisplayName(resultItemId)}");
                break;
            case not UpgradeResultSucceeded when !onBench:
                CombatNotice(UpgradeError(result.ResultCode));
                break;
        }

        RefreshBagFit();
        RefreshUpgradeActions();
        if (CharTabOpen()) RefreshInventoryUI();
    }

    private void ShowUpgradeOutcome(byte code, int resultItemId)
    {
        string before = _upgradeItemIds[0] != 0 ? ItemData.DisplayName(_upgradeItemIds[0]) : BenchPrompt;
        string operation = UpgradeOperationName();
        if (code is UpgradeResultSucceeded or UpgradeResultFailed) ClearUpgradeSockets();

        switch (code)
        {
            case UpgradeResultSucceeded when resultItemId != 0:
                ShowResultItem(resultItemId, SlotLook.Normal);
                SetAnvilStrip(ItemData.DisplayName(resultItemId), $"{operation} succeeded.", UiTheme.Good);
                break;
            case UpgradeResultSucceeded:
                SetAnvilStrip(before, $"{operation} succeeded.", UiTheme.Good);
                break;
            case UpgradeResultFailed when resultItemId != 0:
                ShowResultItem(resultItemId, SlotLook.Normal);
                SetAnvilStrip(before, $"{operation} failed — retained {ItemData.DisplayName(resultItemId)}.", UiTheme.Bad);
                break;
            case UpgradeResultFailed:
                SetAnvilStrip(before, $"{operation} failed — the item was destroyed.", UiTheme.Bad);
                break;
            default:
                SetAnvilStrip(before, UpgradeError(code), UiTheme.Bad);
                break;
        }
    }

    private void OnUpgradePreviewResult(UpgradeResult result)
    {
        if (!_upgradePreviewGate.Complete(_upgradeItemIds, _upgradePositions))
        {
            if (_upgradeShown) OnUpgradeBenchChanged();
            return;
        }
        if (_upgradeItemIds[0] == 0) return;
        int previewId = result.Slots.Length > 0 ? result.Slots[0].ItemId : 0;
        string name = ItemData.DisplayName(_upgradeItemIds[0]);
        if (result.ResultCode == UpgradeResultSucceeded && previewId != 0)
        {
            _upgradePreviewId = previewId;
            ShowResultItem(previewId, SlotLook.Ghost);
            SetAnvilStrip($"{name} → {ItemData.DisplayName(previewId)}",
                $"{UpgradeOperationName()} · the item may be destroyed", UiTheme.Warning);
        }
        else
        {
            _upgradePreviewId = 0;
            ClearResultSocket();
            SetAnvilStrip(name, UpgradeError(result.ResultCode), UiTheme.Bad);
        }
        RefreshUpgradeActions();
    }

    private static string UpgradeError(byte code) => code switch
    {
        UpgradeResultTrading => "Cannot upgrade while trading.",
        UpgradeResultNeedCoins => "You don't have enough gold.",
        UpgradeResultNoMatch => "The items required for upgrade do not match.",
        UpgradeResultSealed => "That item is sealed or rented.",
        _ => "Cannot perform item upgrade.",
    };

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
        if (_upgradePreviewId != 0)
            return _upgradeResultSocket.GetGlobalRect().GetCenter();
        return Vector2.Zero;
    }

    private int UpgradeSocketSlot(int index)
        => _upgradePositions[index] >= 0 ? GridStart + _upgradePositions[index] : -1;

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
        if (_upgradeShown && InMainBag(absSlot)) DropStaleSockets();
    }

    private void OnUpgradeInventoryGrid(ItemSlot[] items)
    {
        if (_upgradeShown) DropStaleSockets();
    }

    private void DropStaleSockets()
    {
        if (UpgradeInteractionLocked) return;
        bool dropped = false;
        for (int i = 0; i < _upgradePositions.Length; i++)
        {
            int pos = _upgradePositions[i];
            if (pos < 0) continue;
            int abs = GridStart + pos;
            var held = abs < Inv.Length ? Inv[abs] : default;
            if (!held.IsEmpty && held.IsTradable && held.ItemId == _upgradeItemIds[i])
            {
                held.Count = 1;
                _upgradeSockets[i]?.Set(held);
                continue;
            }
            _upgradeItemIds[i] = 0;
            _upgradePositions[i] = -1;
            _upgradeSockets[i]?.Clear();
            dropped = true;
        }
        if (!dropped) return;
        RefreshBagFit();
        OnUpgradeBenchChanged();
    }

    private bool SelectionMatches(int[] items, int[] positions)
        => items.AsSpan().SequenceEqual(_upgradeItemIds) && positions.AsSpan().SequenceEqual(_upgradePositions);
}
