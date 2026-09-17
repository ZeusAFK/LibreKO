using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LibreKO;

public partial class World : Node3D
{
    private const int GridStart = Inventory.GridStart;
    private const int GridCount = Inventory.GridCount;

    internal Inventory Inv { get; } = new();
    private int _selfRace, _selfFace, _selfClass, _selfHair;
    private readonly Dictionary<int, (Mesh? Mesh, Skin? Skin)> _selfDefaultParts = new();

    private const int InvCols = 7;
    private const float InvCellSize = 50f;
    private const float InvCellGap = 5f;
    private const string InvTabGeneral = "General";
    private const string InvTabQuest = "Quest";
    private const string InvTabBag = "Bag";

    private CanvasLayer _invLayer = null!;
    private Control _invContent = null!;
    private PanelContainer _itemTipPanel = null!;
    private VBoxContainer _itemTipLines = null!;
    private ItemCell? _hoverCell;
    private readonly Dictionary<int, ItemCell> _invCells = new();
    private readonly List<ItemCell> _invBagCells = new();
    private readonly List<ItemCell> _invQuestCells = new();
    private readonly List<ItemCell> _invMagicBagCells = new();
    private readonly List<Button> _invBagButtons = new();
    private Label? _invBagEmptyHint;
    private int _invSelectedBag;
    private Label? _invGoldLbl, _invWeightLbl, _invSlotLbl;
    private Control _invBag = null!;
    private LineEdit _invSearch = null!;
    private ProgressBar _invWeightBar = null!, _invSlotBar = null!;
    private TrashSlot _invTrash = null!;
    private readonly Dictionary<string, Control> _invTabPages = new();
    private readonly Dictionary<string, Button> _invTabBtns = new();
    private string _invTab = InvTabGeneral;

    private PanelContainer _invDelPanel = null!;
    private TextureRect _invDelIcon = null!;
    private Label _invDelName = null!;
    private int _invDelSlot = -1;
    private int _invDelItemId;

    private const int NoSlot = -1;

    private static readonly (int Slot, string Label)[] DollLeftColumn =
    {
        (InventoryConstants.CosEmblem, "Emblem"),
        (InventoryConstants.CosWing, "Wings"),
        (InventoryConstants.CosGloveRight, "Glv R"),
        (InventoryConstants.CosGloveLeft, "Glv L"),
        (InventoryConstants.Pet, "Pet"),
        (InventoryConstants.CosFairy, "Fairy"),
    };

    private static readonly (int Slot, string Label)[] DollArmourGrid =
    {
        (NoSlot, ""), (InventoryConstants.Head, "Head"), (NoSlot, ""),
        (InventoryConstants.Glove, "Glove"), (InventoryConstants.Breast, "Chest"), (InventoryConstants.Foot, "Boot"),
        (NoSlot, ""), (InventoryConstants.Leg, "Leg"), (NoSlot, ""),
        (InventoryConstants.RightHand, "Main"), (NoSlot, ""), (InventoryConstants.LeftHand, "Off"),
    };

    private static readonly (int Slot, string Label)[] DollRightColumn =
    {
        (InventoryConstants.RightEar, "Ear"),
        (InventoryConstants.LeftEar, "Ear"),
        (InventoryConstants.Neck, "Neck"),
        (InventoryConstants.Waist, "Belt"),
        (InventoryConstants.RightRing, "Ring"),
        (InventoryConstants.LeftRing, "Ring"),
    };

    private static readonly (int Slot, string Label)[] DollBottomRow =
    {
        (InventoryConstants.CosPauldron, "Top"),
        (InventoryConstants.CosHelmet, "Mask"),
        (InventoryConstants.CosTattoo, "Tattoo"),
        (InventoryConstants.CosTalisman, "Talis"),
    };

    private static int TooltipFontHeight => Config.TooltipHeight;
    private static bool TooltipBold => Config.TooltipBold;
    private static bool TooltipBack => Config.TooltipBack;

    private readonly struct TooltipLine
    {
        public readonly string Text;
        public readonly int Color;
        public readonly HorizontalAlignment Align;
        public readonly bool Divider;

        public TooltipLine(string text, int color,
            HorizontalAlignment align = HorizontalAlignment.Left, bool divider = false)
        {
            Text = text; Color = color; Align = align; Divider = divider;
        }

        public static TooltipLine Rule() => new("", 0, HorizontalAlignment.Left, true);
    }

    private struct MoveStep
    {
        public byte Dir;
        public int ItemId;
        public byte Src, Dst;
        public int From, To;
    }
    private readonly Queue<MoveStep> _moveQueue = new();
    private bool _moveInFlight;
    private MoveStep _moveCur;

    private void CaptureSelfDefaults(Node3D selfVisual)
    {
        _selfDefaultParts.Clear();
        foreach (var (idx, part) in CapturePartDefaults(selfVisual))
            _selfDefaultParts[idx] = part;
    }

    private Dictionary<int, (Mesh? Mesh, Skin? Skin)> CapturePartDefaults(Node3D visual)
    {
        var defaults = new Dictionary<int, (Mesh? Mesh, Skin? Skin)>();
        foreach (var (idx, mi) in BodyParts(visual))
            defaults[idx] = (mi.Mesh, mi.Skin);
        return defaults;
    }

    private void RestorePartDefaults(Node3D visual, Dictionary<int, (Mesh? Mesh, Skin? Skin)> defaults)
    {
        if (defaults.Count == 0) return;
        var parts = BodyParts(visual);
        foreach (var (idx, def) in defaults)
            if (parts.TryGetValue(idx, out var mi))
            {
                mi.Mesh = def.Mesh;
                mi.Skin = def.Skin;
                mi.MaterialOverlay = null;
            }
    }

    private void InventoryInit()
    {
        ItemData.EnsureLoaded();
        var info = Net.I.LastEnter;
        _selfRace = info.Race; _selfFace = info.Face; _selfHair = info.Hair;
        _selfClass = info.Class;
        Inv.Reset(info.Inventory);
        BuildInventoryPanel();
        RefreshInventoryUI();
        Net.I.ItemMoveResultEvent += OnItemMoveResult;
        Net.I.ItemRemoveResultEvent += OnItemRemoveResult;
        Net.I.InventorySlotEvent += OnInventorySlotUpdate;
        Net.I.ItemGainedEvent += OnItemGained;
        Net.I.InventoryGridRefreshEvent += OnInventoryGridRefresh;
        Net.I.GoldChangeEvent += OnInventoryGoldChange;
        RerenderSelfEquipment();
    }

    private void BuildInventoryPanel()
    {
        if (_invLayer != null && GodotObject.IsInstanceValid(_invLayer))
        {
            RemoveChild(_invLayer);
            _invLayer.QueueFree();
        }
        _invCells.Clear();
        _invBagCells.Clear();
        _invQuestCells.Clear();
        _invMagicBagCells.Clear();
        _invTabPages.Clear();
        _invTabBtns.Clear();

        _invLayer = new CanvasLayer { Layer = 76 };
        AddChild(_invLayer);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        _invContent = body;

        var gearPanel = UiTheme.Section();
        body.AddChild(gearPanel);
        var gearBox = new VBoxContainer();
        gearBox.AddThemeConstantOverride("separation", 6);
        gearPanel.AddChild(gearBox);
        gearBox.AddChild(UiTheme.SectionTitle("Equipment", UiIcons.Get("game/chest")));
        gearBox.AddChild(BuildEquipDoll());
        gearBox.AddChild(BuildCospreRow());
        gearBox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var bagPanel = UiTheme.Section();
        body.AddChild(bagPanel);
        _invBag = bagPanel;
        var bag = new VBoxContainer();
        bag.AddThemeConstantOverride("separation", 6);
        bagPanel.AddChild(bag);

        bag.AddChild(BuildInventoryTabBar());
        bag.AddChild(BuildInventoryToolRow());

        var pages = new MarginContainer();
        bag.AddChild(pages);
        pages.AddChild(BuildInventoryGridPage(InvTabGeneral, _invBagCells, GridCount));
        pages.AddChild(BuildInventoryGridPage(InvTabQuest, _invQuestCells, GridCount));
        pages.AddChild(BuildMagicBagPage());
        SelectInventoryTab(InvTabGeneral);

        bag.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 5);
        footer.AddChild(UiIcons.Image(
            "system/coins",
            new Vector2(15, 15),
            new Color(UiTheme.Gold, 0.9f),
            "Noah"));
        _invGoldLbl = UiTheme.Text("", 13, UiTheme.Gold);
        _invGoldLbl.AddThemeColorOverride("font_color", UiTheme.Gold);
        footer.AddChild(_invGoldLbl);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        bag.AddChild(footer);

        bag.AddChild(BuildMeterRow("system/bag", "Inventory Slot", out _invSlotLbl, out _invSlotBar));
        bag.AddChild(BuildMeterRow("system/weight", "Weight", out _invWeightLbl, out _invWeightBar));

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        bag.AddChild(bottom);
        _invTrash = new TrashSlot { OnDropItem = AskDeleteItem };
        bottom.AddChild(_invTrash);

        BuildItemTooltip();
        BuildDeletePrompt();
        RefreshInventoryFooter();
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

    private Control BuildInventoryTabBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 1);
        var group = new ButtonGroup();
        foreach (string tab in new[] { InvTabGeneral, InvTabQuest, InvTabBag })
        {
            string name = tab;
            var button = UiTheme.TopTabButton(name, 13);
            button.ButtonGroup = group;
            button.Pressed += () => SelectInventoryTab(name);
            _invTabBtns[name] = button;
            bar.AddChild(button);
        }
        return bar;
    }

    private Control BuildInventoryToolRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var pack = UiTheme.SmallButton("Arrange", "Sort the bag and close up the gaps.");
        pack.Pressed += () => Net.I.SendInventoryArrange();
        row.AddChild(pack);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var field = new PanelContainer { CustomMinimumSize = new Vector2(152, 26) };
        field.AddThemeStyleboxOverride("panel", InvSearchStyle(false));
        var fieldRow = new HBoxContainer();
        fieldRow.AddThemeConstantOverride("separation", 4);
        field.AddChild(fieldRow);

        _invSearch = new LineEdit
        {
            PlaceholderText = "Search",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _invSearch.AddThemeFontSizeOverride("font_size", 12);
        _invSearch.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _invSearch.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _invSearch.TextChanged += _ => RefreshInventoryUI();
        fieldRow.AddChild(_invSearch);

        fieldRow.AddChild(UiIcons.Image(
            "system/search", new Vector2(13, 13), new Color(UiTheme.TextLo, 0.75f)));
        row.AddChild(field);
        return row;
    }

    private static StyleBoxFlat InvSearchStyle(bool focused)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.043f, 0.045f, 0.055f, 0.96f),
            BorderColor = focused ? new Color(UiTheme.Gold, 0.75f) : new Color(UiTheme.EdgeSoft, 0.55f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(3);
        sb.ContentMarginLeft = 8;
        sb.ContentMarginRight = 6;
        sb.ContentMarginTop = sb.ContentMarginBottom = 3;
        return sb;
    }

    private static Vector2 InvPageSize(int cellCount)
    {
        int rows = Mathf.CeilToInt(cellCount / (float)InvCols);
        return new Vector2(InvCols * (InvCellSize + InvCellGap), rows * (InvCellSize + InvCellGap));
    }

    private GridContainer NewInventoryGrid(string tab, int cellCount)
    {
        var grid = new GridContainer
        {
            Columns = InvCols,
            Visible = false,
            CustomMinimumSize = InvPageSize(cellCount),
        };
        grid.AddThemeConstantOverride("h_separation", (int)InvCellGap);
        grid.AddThemeConstantOverride("v_separation", (int)InvCellGap);
        _invTabPages[tab] = grid;
        return grid;
    }

    private Control BuildInventoryGridPage(string tab, List<ItemCell> cells, int cellCount)
    {
        var grid = NewInventoryGrid(tab, cellCount);
        for (int i = 0; i < cellCount; i++)
        {
            var cell = new ItemCell(GridStart + i, InvCellSize)
            {
                OnContext = InventoryContext,
                OnHoverChanged = InventoryHover,
                OnDropItem = MoveBetween,
            };
            cells.Add(cell);
            grid.AddChild(cell);
        }
        return grid;
    }

    private Control BuildMagicBagPage()
    {
        var page = new VBoxContainer { Visible = false, CustomMinimumSize = InvPageSize(GridCount) };
        page.AddThemeConstantOverride("separation", 8);
        _invTabPages[InvTabBag] = page;

        var worn = new HBoxContainer();
        worn.AddThemeConstantOverride("separation", 6);
        page.AddChild(worn);
        worn.AddChild(UiTheme.Text("Worn bags", 12, UiTheme.TextLo));
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
        {
            int abs = InventoryConstants.BagSlotFor(i);
            var cell = new ItemCell(abs, 44f)
            {
                OnContext = InventoryContext,
                OnHoverChanged = InventoryHover,
                OnDropItem = MoveBetween,
                EmptyHint = "Bag",
            };
            _invCells[abs] = cell;
            worn.AddChild(cell);
        }

        page.AddChild(new HSeparator());

        var grid = new GridContainer { Columns = InvCols };
        grid.AddThemeConstantOverride("h_separation", (int)InvCellGap);
        grid.AddThemeConstantOverride("v_separation", (int)InvCellGap);
        page.AddChild(grid);
        for (int i = 0; i < InventoryConstants.MagicBagTotal; i++)
        {
            var cell = new ItemCell(-1, InvCellSize)
            {
                OnContext = InventoryContext,
                OnHoverChanged = InventoryHover,
                OnDropItem = MoveBetween,
            };
            _invMagicBagCells.Add(cell);
            grid.AddChild(cell);
        }
        return page;
    }

    private List<int> InventoryDisplayOrder(bool questOnly)
    {
        var order = new List<int>(GridCount);
        AppendRegionOrder(order, GridStart, GridCount, questOnly);
        return order;
    }

    private List<int> MagicBagDisplayOrder()
    {
        var order = new List<int>(InventoryConstants.MagicBagTotal);
        for (int bag = 0; bag < InventoryConstants.BagSlotMax; bag++)
            AppendRegionOrder(order, InventoryConstants.MagicBagPageStart(bag),
                              InventoryConstants.MagicBagMax, false);
        return order;
    }

    private void AppendRegionOrder(List<int> into, int start, int count, bool questOnly)
    {
        if (!questOnly)
        {
            for (int i = 0; i < count; i++) into.Add(start + i);
            return;
        }

        var filled = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            int abs = start + i;
            if (abs >= Inv.Length || Inv[abs].IsEmpty) continue;
            if (!QuestData.IsQuestItem(Inv[abs].ItemId)) continue;
            filled.Add(abs);
        }
        filled.Sort(CompareArrangeSlots);
        into.AddRange(filled);
    }

    private void SelectInventoryTab(string tab)
    {
        _invTab = tab;
        foreach (var (key, page) in _invTabPages) page.Visible = key == tab;
        if (_invTabBtns.TryGetValue(tab, out var button)) button.ButtonPressed = true;
        HideDeletePrompt();
        RefreshInventoryUI();
    }

    private const float DollCell = 52f;

    private Control BuildEquipDoll()
    {
        var board = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        board.AddThemeConstantOverride("separation", 36);

        board.AddChild(DollColumn(DollLeftColumn));

        var armour = new GridContainer { Columns = 3, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        armour.AddThemeConstantOverride("h_separation", 26);
        armour.AddThemeConstantOverride("v_separation", 16);
        foreach (var (slot, label) in DollArmourGrid)
        {
            if (slot == NoSlot)
            {
                armour.AddChild(new Control { CustomMinimumSize = new Vector2(DollCell, DollCell) });
                continue;
            }
            armour.AddChild(DollCellFor(slot, label));
        }
        board.AddChild(armour);

        board.AddChild(DollColumn(DollRightColumn));
        return board;
    }

    private Control DollColumn((int Slot, string Label)[] slots)
    {
        var column = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        column.AddThemeConstantOverride("separation", 8);
        foreach (var (slot, label) in slots)
            column.AddChild(DollCellFor(slot, label));
        return column;
    }

    private ItemCell DollCellFor(int slot, string label)
    {
        var cell = new ItemCell(slot, DollCell)
        {
            OnContext = InventoryContext,
            OnHoverChanged = InventoryHover,
            OnDropItem = MoveBetween,
            EmptyHint = label,
        };
        _invCells[slot] = cell;
        return cell;
    }

    private Control BuildCospreRow()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        foreach (var (slot, label) in DollBottomRow)
            row.AddChild(DollCellFor(slot, label));
        return row;
    }

    private void RefreshInventoryFooter()
    {
        if (!GodotObject.IsInstanceValid(_invGoldLbl) || !GodotObject.IsInstanceValid(_invWeightLbl))
        {
            _invGoldLbl = null;
            _invWeightLbl = null;
            return;
        }
        int wt = CarriedWeight();
        _invWeightLbl.Text = Sheet.MaxWeight > 0
            ? $"{wt / 10f:0.0} / {Sheet.MaxWeight / 10f:0.0} LT"
            : $"{wt / 10f:0.0} LT";
        _invGoldLbl.Text = $"{Sheet.Gold:n0}";

        float load = Sheet.MaxWeight > 0 ? Mathf.Clamp((float)wt / Sheet.MaxWeight, 0f, 1f) : 0f;
        SetMeter(_invWeightBar, load);

        (int used, int total) = InventorySlotUsage();
        if (GodotObject.IsInstanceValid(_invSlotLbl)) _invSlotLbl!.Text = $"{used}/{total}";
        SetMeter(_invSlotBar, total > 0 ? Mathf.Clamp((float)used / total, 0f, 1f) : 0f);
    }

    private static void SetMeter(ProgressBar bar, float load)
    {
        if (!GodotObject.IsInstanceValid(bar)) return;
        bar.Value = load * bar.MaxValue;
        bar.AddThemeStyleboxOverride("fill", UiTheme.MeterFill(MeterTint(load)));
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

    private int CarriedWeight()
    {
        int wt = 0;
        for (int abs = 0; abs < Inv.Length; abs++)
        {
            if (Inv[abs].IsEmpty) continue;
            var d = ItemData.Get(Inv[abs].ItemId);
            if (d != null) wt += d.Weight * Inv[abs].Count;
        }
        return wt;
    }

    private void OnInventoryGoldChange(int total)
    {
        if (!GodotObject.IsInstanceValid(_invGoldLbl))
        {
            _invGoldLbl = null;
            return;
        }
        _invGoldLbl.Text = $"{total:n0} gold";
    }

    private void RefreshInventoryUI()
    {
        string needle = GodotObject.IsInstanceValid(_invSearch) ? _invSearch.Text.Trim() : "";
        foreach (var (slot, cell) in _invCells)
        {
            cell.Bind(slot, slot < Inv.Length ? Inv[slot] : default);
            cell.SetMatch(needle.Length == 0 || MatchesInventorySearch(cell.Current, needle));
        }
        BindBagCells(_invBagCells, InventoryDisplayOrder(questOnly: false), needle);
        BindBagCells(_invQuestCells, InventoryDisplayOrder(questOnly: true), needle);
        BindBagCells(_invMagicBagCells, MagicBagDisplayOrder(), needle);
        RefreshInventoryFooter();
        RefreshTracker();

        if (_invDelSlot >= 0
            && (_invDelSlot >= Inv.Length || Inv[_invDelSlot].ItemId != _invDelItemId))
            HideDeletePrompt();

        if (_hoverCell != null)
        {
            if (!CharTabOpen() || _hoverCell.Current.IsEmpty)
                HideItemTooltip();
            else
                ShowItemTooltip(_hoverCell.Slot, _hoverCell.Current);
        }

    }

    private ItemSlot SlotAt(int abs) => abs >= 0 && abs < Inv.Length ? Inv[abs] : default;

    private bool IsMagicBagSlotUnlocked(int abs)
    {
        if (!InventoryConstants.IsMagicBagSlot(abs)) return true;
        int bag = InventoryConstants.BagIndexForMagicBagPosition(abs - InventoryConstants.MagicBagStart);
        return !SlotAt(InventoryConstants.BagSlotFor(bag)).IsEmpty;
    }

    private void BindBagCells(List<ItemCell> cells, List<int> order, string needle)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            int slot = i < order.Count ? order[i] : -1;
            var item = SlotAt(slot);
            bool locked = slot >= 0 && !IsMagicBagSlotUnlocked(slot);
            cells[i].Locked = locked;
            cells[i].Bind(locked ? -1 : slot, locked ? default : item);
            cells[i].SetMatch(needle.Length == 0 || MatchesInventorySearch(cells[i].Current, needle));
        }
    }

    private static bool MatchesInventorySearch(ItemSlot item, string needle) =>
        !item.IsEmpty
        && ItemData.DisplayName(item.ItemId)
            .Contains(needle, System.StringComparison.OrdinalIgnoreCase);

    private void BuildDeletePrompt()
    {
        _invDelPanel = new PanelContainer { Visible = false, ZIndex = 240 };
        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.045f, 0.045f, 0.985f),
            BorderColor = new Color(0.62f, 0.24f, 0.20f, 0.95f),
            ShadowColor = new Color(0, 0, 0, 0.65f),
            ShadowSize = 10,
        };
        bg.SetBorderWidthAll(1);
        bg.SetCornerRadiusAll(3);
        _invDelPanel.AddThemeStyleboxOverride("panel", bg);
        _invLayer.AddChild(_invDelPanel);

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 12, 10, 12, 11);
        _invDelPanel.AddChild(margin);

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(214, 0) };
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        root.AddChild(head);
        _invDelIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(30, 30),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        head.AddChild(_invDelIcon);
        var headText = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        headText.AddThemeConstantOverride("separation", 1);
        head.AddChild(headText);
        headText.AddChild(UiTheme.Text("Destroy this item?", 13, UiTheme.TextHi));
        _invDelName = UiTheme.Text("", 12, new Color(1f, 0.62f, 0.55f));
        _invDelName.AddThemeColorOverride("font_color", new Color(1f, 0.62f, 0.55f));
        _invDelName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _invDelName.CustomMinimumSize = new Vector2(158, 0);
        headText.AddChild(_invDelName);

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

    private void AskDeleteItem(int absSlot)
    {
        if (absSlot < 0 || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        if (absSlot >= InventoryConstants.CospreStart) return;

        _invDelSlot = absSlot;
        _invDelItemId = Inv[absSlot].ItemId;
        _invDelIcon.Texture = ItemData.Icon(_invDelItemId);
        int count = Inv[absSlot].Count;
        _invDelName.Text = count > 1
            ? $"{ItemData.DisplayName(_invDelItemId)} ×{count}"
            : ItemData.DisplayName(_invDelItemId);
        _invDelPanel.Visible = true;
        UpdateDeletePrompt();
        Audio.PlayUi(Sfx.MsgBoxPop);
    }

    private void HideDeletePrompt()
    {
        _invDelSlot = -1;
        _invDelItemId = 0;
        if (_invDelPanel != null && GodotObject.IsInstanceValid(_invDelPanel))
            _invDelPanel.Visible = false;
    }

    private void UpdateDeletePrompt()
    {
        if (_invDelPanel == null || !_invDelPanel.Visible) return;
        if (!CharTabOpen()) { HideDeletePrompt(); return; }

        if (GetViewport() is not { } vp) return;
        var size = _invDelPanel.Size;
        if (size.X <= 1 || size.Y <= 1) size = _invDelPanel.GetCombinedMinimumSize();
        var bag = _invBag.GetGlobalRect();
        var trash = _invTrash.GetGlobalRect();
        var viewport = vp.GetVisibleRect().Size;
        var p = new Vector2(
            bag.Position.X + (bag.Size.X - size.X) * 0.5f,
            trash.Position.Y - size.Y - 10f);
        p.X = Mathf.Clamp(p.X, 8f, Mathf.Max(8f, viewport.X - size.X - 8f));
        p.Y = Mathf.Clamp(p.Y, 8f, Mathf.Max(8f, viewport.Y - size.Y - 8f));
        _invDelPanel.Position = p;
    }

    private void ConfirmDeleteItem()
    {
        int slot = _invDelSlot;
        int itemId = _invDelItemId;
        HideDeletePrompt();
        if (slot < 0 || slot >= Inv.Length || Inv[slot].ItemId != itemId) return;
        if (_moveInFlight || _moveQueue.Count > 0 || _selfDead) return;

        if (slot < GridStart) Net.I.SendItemRemove(1, (byte)slot, itemId);
        else Net.I.SendItemRemove(0, (byte)(slot - GridStart), itemId);
        Audio.PlayUi(Sfx.UiButton);
    }

    private void OnItemRemoveResult(bool ok)
    {
        if (!ok) { Chat.Info("That item could not be destroyed."); return; }
        RefreshInventoryUI();
    }

    private sealed partial class LockGlyph : Control
    {
        public override void _Draw()
        {
            var tint = new Color(UiTheme.TextLo, 0.32f);
            float w = Size.X, h = Size.Y;
            float cx = Mathf.Round(w * 0.5f);
            float bodyTop = Mathf.Round(h * 0.44f);
            float inset = Mathf.Round(w * 0.125f);

            DrawRect(new Rect2(inset, bodyTop, w - inset * 2f, h - bodyTop - 1f), tint, false, 1f);

            float sr = Mathf.Round(w * 0.22f);
            float arcCy = bodyTop - Mathf.Round(h * 0.14f);
            DrawArc(new Vector2(cx, arcCy), sr, Mathf.Pi, Mathf.Tau, 16, tint, 1f);
            DrawLine(new Vector2(cx - sr, arcCy), new Vector2(cx - sr, bodyTop), tint, 1f);
            DrawLine(new Vector2(cx + sr, arcCy), new Vector2(cx + sr, bodyTop), tint, 1f);
        }
    }

    private int[] SelfGear()
    {
        var gear = new int[InventoryConstants.VisualSlots.Length];
        for (int i = 0; i < gear.Length; i++)
        {
            int s = InventoryConstants.VisualSlots[i];
            gear[i] = s < Inv.Length ? Inv[s].ItemId : 0;
        }
        return gear;
    }

    private void RerenderSelfEquipment()
    {
        if (_self == null) return;
        var gear = SelfGear();
        RestorePartDefaults(_self, _selfDefaultParts);
        GraftEquipment(_self, _selfRace, _selfFace, gear, _selfHair, Net.I.HelmetHidden);
        AttachWeapons(_self, gear);
        _selfWingAnims = AttachWings(_self, gear, _selfRace, _zone);
        System.Array.Clear(_selfWingClips);
        AttachHandFx(_self, gear, _selfRace, _zone);
        RearmWornLook(_self, gear);
    }

    private partial class ItemCell : PanelContainer
    {
        public int Slot { get; private set; }
        public System.Action<int>? OnContext;
        public System.Action<ItemCell, bool>? OnHoverChanged;
        public System.Action<int, int>? OnDropItem;
        public ItemSlot Current { get; private set; }
        private readonly TextureRect _icon;
        private readonly LockGlyph _lockIcon;
        private readonly TextureRect _emptyIcon;
        private bool _locked;

        public bool Locked
        {
            get => _locked;
            set
            {
                if (_locked == value) return;
                _locked = value;
                _lockIcon.Visible = value;
                AddThemeStyleboxOverride("panel", value ? _lockedStyle : _normal);
            }
        }
        private readonly Label _count;
        private readonly Label _hint;
        private readonly UpgradeBadge _plus;
        private string _emptyHint = "";
        private readonly StyleBoxFlat _normal;
        private readonly StyleBoxFlat _hover;
        private readonly StyleBoxFlat _lockedStyle = LockedStyle();

        public ItemCell(int slot, float size = 46f)
        {
            Slot = slot;
            CustomMinimumSize = new Vector2(size, size);

            _normal = UiTheme.Slot();
            _hover = UiTheme.Slot(hover: true);
            AddThemeStyleboxOverride("panel", _normal);

            _hint = UiTheme.Text("", 9, new Color(UiTheme.TextLo, 0.42f), HorizontalAlignment.Center);
            _hint.AddThemeColorOverride("font_color", new Color(UiTheme.TextLo, 0.42f));
            _hint.MouseFilter = MouseFilterEnum.Ignore;
            _hint.VerticalAlignment = VerticalAlignment.Center;
            _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hint.CustomMinimumSize = Vector2.Zero;
            _hint.Visible = false;
            AddChild(_hint);

            _emptyIcon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                SelfModulate = new Color(UiTheme.TextLo, 0.34f),
                Visible = false,
            };
            _emptyIcon.SetAnchorsPreset(LayoutPreset.FullRect);
            _emptyIcon.OffsetLeft = 9;
            _emptyIcon.OffsetTop = 9;
            _emptyIcon.OffsetRight = -9;
            _emptyIcon.OffsetBottom = -9;
            AddChild(_emptyIcon);

            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                CustomMinimumSize = Vector2.Zero,
                SizeFlagsHorizontal = SizeFlags.Fill,
                SizeFlagsVertical = SizeFlags.Fill,
            };
            _icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_icon);

            _lockIcon = new LockGlyph
            {
                CustomMinimumSize = new Vector2(16, 18),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            AddChild(_lockIcon);

            _plus = UpgradeBadge.Attach(this);

            _count = HudStyle.Label(12, HorizontalAlignment.Right);
            _count.MouseFilter = MouseFilterEnum.Ignore;
            _count.VerticalAlignment = VerticalAlignment.Bottom;
            _count.SizeFlagsHorizontal = _count.SizeFlagsVertical = SizeFlags.Fill;
            _count.SetAnchorsPreset(LayoutPreset.FullRect);
            _count.AddThemeStyleboxOverride("normal",
                new StyleBoxEmpty { ContentMarginRight = 3, ContentMarginBottom = 1 });
            _count.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.92f));
            _count.AddThemeConstantOverride("shadow_offset_x", 1);
            _count.AddThemeConstantOverride("shadow_offset_y", 1);
            _count.AddThemeConstantOverride("outline_size", 3);
            _count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            AddChild(_count);

            MouseEntered += () =>
            {
                if (_locked) return;
                AddThemeStyleboxOverride("panel", _hover);
                OnHoverChanged?.Invoke(this, true);
            };
            MouseExited += () =>
            {
                if (_locked) return;
                AddThemeStyleboxOverride("panel", _normal);
                OnHoverChanged?.Invoke(this, false);
            };
        }

        private static StyleBoxFlat LockedStyle()
        {
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.055f, 0.057f, 0.066f, 0.92f),
                BorderColor = new Color(UiTheme.EdgeSoft, 0.3f),
            };
            sb.SetBorderWidthAll(1);
            sb.SetCornerRadiusAll(3);
            return sb;
        }

        public string EmptyHint
        {
            set
            {
                _emptyHint = value;
                _hint.Text = value;
                _emptyIcon.Texture = UiIcons.Equipment(Slot);
                if (!Current.IsEmpty) return;
                _emptyIcon.Visible = _emptyIcon.Texture != null;
                _hint.Visible = _emptyIcon.Texture == null && value.Length > 0;
                TooltipText = value;
            }
        }

        public void Bind(int slot, ItemSlot it)
        {
            Slot = slot;
            Set(it);
        }

        public void Set(ItemSlot it)
        {
            Current = it;
            if (it.IsEmpty)
            {
                _icon.Texture = null;
                _count.Text = "";
                _plus.Clear();
                _emptyIcon.Visible = _emptyIcon.Texture != null;
                _hint.Visible = _emptyIcon.Texture == null && _hint.Text.Length > 0;
                _icon.SelfModulate = Colors.White;
                if (!_locked) AddThemeStyleboxOverride("panel", _normal);
                TooltipText = _emptyHint;
                return;
            }
            _hint.Visible = false;
            _emptyIcon.Visible = false;
            _icon.Texture = ItemData.Icon(it.ItemId);
            _count.Text = it.Count > 1 ? it.Count.ToString() : "";
            TooltipText = "";
            _plus.Set(it.ItemId);
            if (!_locked) SealLook.Apply(it.State, _icon, this, _normal);
        }

        public void SetMatch(bool match)
        {
            var wanted = match ? Colors.White : DimmedBySearch;
            if (Modulate != wanted) Modulate = wanted;
        }

        private static readonly Color DimmedBySearch = new(0.42f, 0.42f, 0.45f, 0.85f);

        public override void _GuiInput(InputEvent ev)
        {
            if (Locked) return;
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                OnContext?.Invoke(Slot);
                AcceptEvent();
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (Locked || Current.IsEmpty) return default;
            var preview = new TextureRect
            {
                Texture = ItemData.Icon(Current.ItemId),
                CustomMinimumSize = new Vector2(40, 40),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            UpgradeBadge.Show(preview, Current.ItemId);
            SetDragPreview(preview);
            return new Godot.Collections.Dictionary
            {
                { "id", Current.ItemId },
                { "invFrom", Slot },
            };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (Locked || Slot < 0 || OnDropItem == null) return false;
            if (data.VariantType != Variant.Type.Dictionary) return false;
            var d = data.AsGodotDictionary();
            if (!d.ContainsKey("invFrom")) return false;
            int from = d["invFrom"].AsInt32();
            return from >= 0 && from != Slot;
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            AddThemeStyleboxOverride("panel", _normal);
            OnDropItem?.Invoke(data.AsGodotDictionary()["invFrom"].AsInt32(), Slot);
        }
    }

    private partial class TrashSlot : PanelContainer
    {
        public System.Action<int>? OnDropItem;
        private readonly StyleBoxFlat _normal;
        private readonly StyleBoxFlat _hover;

        public TrashSlot()
        {
            CustomMinimumSize = new Vector2(62, 40);
            TooltipText = "Drag an item here to destroy it";
            MouseFilter = MouseFilterEnum.Stop;

            _normal = TrashStyle(new Color(0.36f, 0.11f, 0.10f, 0.94f), new Color(0.55f, 0.19f, 0.16f));
            _hover = TrashStyle(new Color(0.54f, 0.15f, 0.13f, 0.97f), new Color(0.86f, 0.33f, 0.27f));
            AddThemeStyleboxOverride("panel", _normal);

            var icon = new TextureRect
            {
                Texture = UiIcons.Get("system/trash"),
                CustomMinimumSize = new Vector2(17, 17),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                SelfModulate = new Color(0.97f, 0.91f, 0.89f),
            };
            AddChild(icon);

            MouseEntered += () => AddThemeStyleboxOverride("panel", _hover);
            MouseExited += () => AddThemeStyleboxOverride("panel", _normal);
        }

        private static StyleBoxFlat TrashStyle(Color bg, Color border)
        {
            var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            sb.SetBorderWidthAll(1);
            sb.SetCornerRadiusAll(4);
            sb.SetContentMarginAll(0);
            return sb;
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("invFrom");

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            AddThemeStyleboxOverride("panel", _normal);
            OnDropItem?.Invoke(data.AsGodotDictionary()["invFrom"].AsInt32());
        }
    }
}

