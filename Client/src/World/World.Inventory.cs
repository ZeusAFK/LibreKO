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
    private const int InvMinVisibleRows = 4;
    private const float InvWindowFixedHeight = 540f;
    private const int ArrangeIconSize = 16;
    private const float InvCellSize = 40f;
    private const float InvCellGap = 4f;
    private const float InvScrollbarWidth = 12f;
    private const float BagCellSize = 36f;
    private static readonly Color[] BagFrameColors = { UiTheme.Bad, UiTheme.Warning, UiTheme.Good };

    private CanvasLayer _invLayer = null!;
    private Control _invContent = null!;
    private PanelContainer _itemTipPanel = null!;
    private VBoxContainer _itemTipLines = null!;
    private ItemCell? _hoverCell;
    private readonly Dictionary<int, ItemCell> _invCells = new();
    private readonly List<ItemCell> _invBagCells = new();
    private readonly List<ItemCell> _invMagicBagCells = new();
    private readonly bool[] _bagCollapsed = new bool[InventoryConstants.BagSlotMax];
    private VBoxContainer _invLower = null!;
    private ScrollContainer _invScroll = null!;
    private int _invRowsShown;
    private bool _overweightWarned;
    private Label? _invGoldLbl, _invWeightLbl, _invSlotLbl;
    private ProgressBar _invWeightBar = null!, _invSlotBar = null!;
    private TrashSlot _invTrash = null!;

    private PanelContainer _invDelPanel = null!;
    private TextureRect _invDelIcon = null!;
    private Label _invDelName = null!;
    private int _invDelSlot = -1;
    private int _invDelItemId;

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
        GetViewport().SizeChanged += RefreshInventoryUI;
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
        _invMagicBagCells.Clear();

        _invLayer = new CanvasLayer { Layer = 76 };
        AddChild(_invLayer);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        _invContent = body;

        body.AddChild(BuildEquipDoll());
        _invLower = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(InvGridWidth + InvScrollbarWidth, 0),
        };
        _invLower.AddThemeConstantOverride("separation", 8);
        body.AddChild(_invLower);
        _invLower.AddChild(BuildBagRow());
        _invLower.AddChild(BuildInventoryGrid());
        _invLower.AddChild(BuildInventoryFooter());

        BuildItemTooltip();
        BuildDeletePrompt();
        RefreshInventoryFooter();
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
                OnContext = InventoryContext,
                OnClick = ToggleBag,
                OnHoverChanged = InventoryHover,
                OnDropItem = MoveBetween,
                EmptyHint = "Bag",
            };
            _invCells[abs] = cell;
            row.AddChild(cell);
        }

        var pack = UiTheme.IconButton(UiIcons.Get("system/arrange"), "Arrange: sort the bag and close up the gaps");
        pack.CustomMinimumSize = new Vector2(BagCellSize, BagCellSize);
        pack.AddThemeConstantOverride("icon_max_width", ArrangeIconSize);
        pack.Pressed += () => Net.I.SendInventoryArrange();
        row.AddChild(pack);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(UiIcons.Image("system/coins", new Vector2(15, 15), new Color(UiTheme.Gold, 0.9f), "Noah"));
        _invGoldLbl = UiTheme.Text("", 13, UiTheme.Gold, HorizontalAlignment.Right);
        _invGoldLbl.AddThemeColorOverride("font_color", UiTheme.Gold);
        row.AddChild(_invGoldLbl);
        return row;
    }

    private static float InvGridWidth => InvCols * (InvCellSize + InvCellGap) - InvCellGap;

    private int InvVisibleRows()
    {
        float pitch = InvCellSize + InvCellGap;
        float height = IsInsideTree() ? GetViewport().GetVisibleRect().Size.Y : DisplayServer.WindowGetSize().Y;
        int totalRows = Mathf.CeilToInt((GridCount + InventoryConstants.MagicBagTotal) / (float)InvCols);
        return Mathf.Clamp(Mathf.FloorToInt((height - InvWindowFixedHeight) / pitch), InvMinVisibleRows, totalRows);
    }

    private Control BuildInventoryGrid()
    {
        float pitch = InvCellSize + InvCellGap;
        _invRowsShown = InvVisibleRows();
        _invScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(InvGridWidth + InvScrollbarWidth, _invRowsShown * pitch - InvCellGap),
        };
        var scroll = _invScroll;
        var grid = new GridContainer { Columns = InvCols };
        grid.AddThemeConstantOverride("h_separation", (int)InvCellGap);
        grid.AddThemeConstantOverride("v_separation", (int)InvCellGap);
        scroll.AddChild(grid);

        for (int i = 0; i < GridCount; i++)
        {
            var cell = new ItemCell(GridStart + i, InvCellSize)
            {
                OnContext = InventoryContext,
                OnHoverChanged = InventoryHover,
                OnDropItem = MoveBetween,
            };
            _invBagCells.Add(cell);
            grid.AddChild(cell);
        }
        for (int i = 0; i < InventoryConstants.MagicBagTotal; i++)
        {
            var cell = new ItemCell(InventoryConstants.MagicBagStart + i, InvCellSize)
            {
                OnContext = InventoryContext,
                OnHoverChanged = InventoryHover,
                OnDropItem = MoveBetween,
                Frame = BagFrameColors[InventoryConstants.BagIndexForMagicBagPosition(i)],
                Visible = false,
            };
            _invMagicBagCells.Add(cell);
            grid.AddChild(cell);
        }
        return scroll;
    }

    private Control BuildInventoryFooter()
    {
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        var meters = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        meters.AddThemeConstantOverride("separation", 4);
        meters.AddChild(BuildMeterRow("system/weight", "Weight", out _invWeightLbl, out _invWeightBar));
        meters.AddChild(BuildMeterRow("system/bag", "Inventory Slot", out _invSlotLbl, out _invSlotBar));
        footer.AddChild(meters);
        _invTrash = new TrashSlot { OnDropItem = AskDeleteItem };
        footer.AddChild(_invTrash);
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
            OnContext = InventoryContext,
            OnHoverChanged = InventoryHover,
            OnDropItem = MoveBetween,
            EmptyHint = label,
        };
        _invCells[slot] = cell;
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
        WarnOverweight(load);

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

    private void WarnOverweight(float load)
    {
        bool over = load >= 1f;
        if (over && !_overweightWarned)
            CombatNotice(SystemText(TextOverweight, "You've exceeded your possible carrying weight."));
        _overweightWarned = over;
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
        foreach (var (slot, cell) in _invCells)
            cell.Bind(slot, SlotAt(slot));
        GhostOtherHand(InventoryConstants.RightHand, InventoryConstants.LeftHand);
        GhostOtherHand(InventoryConstants.LeftHand, InventoryConstants.RightHand);
        for (int i = 0; i < _invBagCells.Count; i++)
            _invBagCells[i].Bind(GridStart + i, SlotAt(GridStart + i));
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
            _invCells[InventoryConstants.BagSlotFor(i)].Frame = BagShown(i) ? BagFrameColors[i] : null;
        int shownCells = GridCount;
        for (int i = 0; i < _invMagicBagCells.Count; i++)
        {
            int slot = InventoryConstants.MagicBagStart + i;
            bool open = BagShown(InventoryConstants.BagIndexForMagicBagPosition(i));
            _invMagicBagCells[i].Visible = open;
            _invMagicBagCells[i].Bind(open ? slot : -1, open ? SlotAt(slot) : default);
            if (open) shownCells++;
        }
        int contentRows = Mathf.CeilToInt(shownCells / (float)InvCols);
        _invRowsShown = InvVisibleRows();
        bool scrollbar = contentRows > _invRowsShown;
        float lowerWidth = InvGridWidth + (scrollbar ? InvScrollbarWidth : 0f);
        float gridHeight = Mathf.Min(contentRows, _invRowsShown) * (InvCellSize + InvCellGap) - InvCellGap;
        _invScroll.CustomMinimumSize = new Vector2(lowerWidth, gridHeight);
        _invLower.CustomMinimumSize = new Vector2(lowerWidth, 0f);
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

    private void GhostOtherHand(int held, int other)
    {
        var weapon = SlotAt(held);
        if (weapon.IsEmpty || !IsTwoHanded(weapon.ItemId) || !SlotAt(other).IsEmpty) return;
        if (_invCells.TryGetValue(other, out var cell)) cell.ShowGhost(weapon.ItemId);
    }

    private bool BagShown(int bagIndex) =>
        !_bagCollapsed[bagIndex] && !SlotAt(InventoryConstants.BagSlotFor(bagIndex)).IsEmpty;

    private void ToggleBag(int bagSlotAbs)
    {
        if (SlotAt(bagSlotAbs).IsEmpty) return;
        int bag = bagSlotAbs - InventoryConstants.BagSlotStart;
        _bagCollapsed[bag] = !_bagCollapsed[bag];
        RefreshInventoryUI();
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
        var bag = _invContent.GetGlobalRect();
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
        public System.Action<int>? OnClick;
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
        private StyleBoxFlat _normal;
        private StyleBoxFlat _hover;
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

        public Color? Frame
        {
            set
            {
                _normal = UiTheme.Slot(value);
                _hover = UiTheme.Slot(value, hover: true);
                AddThemeStyleboxOverride("panel", _locked ? _lockedStyle : _normal);
            }
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

        public void ShowGhost(int itemId)
        {
            if (!Current.IsEmpty) return;
            _icon.Texture = ItemData.Icon(itemId);
            _icon.SelfModulate = GhostTint;
            _emptyIcon.Visible = false;
            _hint.Visible = false;
        }

        private static readonly Color GhostTint = new(1f, 1f, 1f, 0.32f);

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

        public override void _GuiInput(InputEvent ev)
        {
            if (Locked) return;
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                OnContext?.Invoke(Slot);
                AcceptEvent();
            }
            else if (OnClick != null && ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
            {
                OnClick(Slot);
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

    private partial class TrashSlot : Control
    {
        public System.Action<int>? OnDropItem;
        private bool _hot;
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
            TooltipText = "Drop an item here to destroy it";
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

            MouseEntered += () => { _hot = true; QueueRedraw(); };
            MouseExited += () => { _hot = false; QueueRedraw(); };
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size).Grow(-1f);
            DrawRect(rect, _hot ? FillHot : Fill);
            var edge = _hot ? EdgeHot : Edge;
            var tl = rect.Position;
            var tr = rect.Position + new Vector2(rect.Size.X, 0f);
            var bl = rect.Position + new Vector2(0f, rect.Size.Y);
            var br = rect.End;
            DrawDashedLine(tl, tr, edge, 1f, DashLength);
            DrawDashedLine(tr, br, edge, 1f, DashLength);
            DrawDashedLine(br, bl, edge, 1f, DashLength);
            DrawDashedLine(bl, tl, edge, 1f, DashLength);
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("invFrom");

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            _hot = false;
            QueueRedraw();
            OnDropItem?.Invoke(data.AsGodotDictionary()["invFrom"].AsInt32());
        }
    }
}

