using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private static readonly (int Slot, string Label)[] EquipViewSlots =
    {
        (InventoryConstants.Head, "Head"),
        (InventoryConstants.Breast, "Chest"),
        (InventoryConstants.Pet, "Pet"),
        (InventoryConstants.Glove, "Gloves"),
        (InventoryConstants.Leg, "Legs"),
        (InventoryConstants.Foot, "Boots"),
        (InventoryConstants.RightHand, "Right hand"),
        (InventoryConstants.LeftHand, "Left hand"),
        (InventoryConstants.Waist, "Belt"),
        (InventoryConstants.Neck, "Necklace"),
        (InventoryConstants.RightEar, "Right earring"),
        (InventoryConstants.LeftEar, "Left earring"),
        (InventoryConstants.RightRing, "Right ring"),
        (InventoryConstants.LeftRing, "Left ring"),
    };

    private const int EquipViewGearHeight = 494;
    private const int EquipViewGearWidth = 330;

    private CanvasLayer _equipViewLayer = null!;
    private HudWindow _equipViewPanel = null!;
    private VBoxContainer _equipViewGear = null!, _equipViewStats = null!;
    private Label _equipViewHeader = null!, _equipViewStatus = null!;
    private bool _equipViewShown;
    private string _equipViewPending = "";

    private void EquipViewInit()
    {
        _equipViewLayer = new CanvasLayer { Layer = 78 };
        AddChild(_equipViewLayer);

        _equipViewPanel = new HudWindow("equipview", "Equipment View", new Vector2(360, 120), 420) { Visible = false };
        _equipViewPanel.Closed += CloseEquipView;
        _equipViewLayer.AddChild(_equipViewPanel);

        var root = _equipViewPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _equipViewHeader = UiTheme.Text("", 14, UiTheme.TextHi);
        root.AddChild(_equipViewHeader);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);

        var gearCol = new VBoxContainer { CustomMinimumSize = new Vector2(EquipViewGearWidth, 0) };
        gearCol.AddThemeConstantOverride("separation", 4);
        gearCol.AddChild(UiTheme.SectionTitle("Equipment"));
        var gearScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(EquipViewGearWidth, EquipViewGearHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        gearCol.AddChild(gearScroll);
        _equipViewGear = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _equipViewGear.AddThemeConstantOverride("separation", 2);
        gearScroll.AddChild(_equipViewGear);
        cols.AddChild(gearCol);

        var statCol = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        statCol.AddThemeConstantOverride("separation", 4);
        statCol.AddChild(UiTheme.SectionTitle("State"));
        _equipViewStats = new VBoxContainer();
        _equipViewStats.AddThemeConstantOverride("separation", 3);
        statCol.AddChild(_equipViewStats);
        cols.AddChild(statCol);

        _equipViewStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(_equipViewStatus);

        Net.I.EquipmentViewEvent += OnEquipmentView;
    }

    private void EquipViewDispose()
    {
        Net.I.EquipmentViewEvent -= OnEquipmentView;
    }

    private void RequestEquipmentView(string name)
    {
        _equipViewPending = name;
        _equipViewPanel.Title = $"Equipment View — {name}";
        ClearEquipView();
        _equipViewStatus.Text = "Requesting…";
        _equipViewPanel.Visible = true;
        _equipViewShown = true;
        Net.I.SendEquipmentViewRequest(name);
    }

    private void CloseEquipView()
    {
        HideItemTooltip();
        _equipViewShown = false;
        _equipViewPanel.Visible = false;
    }

    private void ClearEquipView()
    {
        foreach (var c in _equipViewGear.GetChildren()) c.QueueFree();
        foreach (var c in _equipViewStats.GetChildren()) c.QueueFree();
        _equipViewHeader.Text = "";
    }

    private void OnEquipmentView(Net.EquipmentViewResult result, Net.EquipmentView view)
    {
        if (!_equipViewShown) return;

        if (result != Net.EquipmentViewResult.Accepted)
        {
            ClearEquipView();
            _equipViewStatus.Text = result switch
            {
                Net.EquipmentViewResult.NotInSameRegion => $"{_equipViewPending} is not in this region.",
                Net.EquipmentViewResult.CannotChooseYourself => "You cannot inspect yourself.",
                Net.EquipmentViewResult.NoViewEquipmentItem => "You need a View Equipment item.",
                _ => $"{_equipViewPending} could not be found.",
            };
            _equipViewStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }

        ClearEquipView();
        _equipViewStatus.Text = "";
        _equipViewPanel.Title = $"Equipment View — {view.Name}";
        _equipViewHeader.Text = $"{view.Name}   Lv {view.Level}   {CharacterClassCatalog.DisplayName(view.Class)}   {NationName(view.Nation)}";
        _equipViewHeader.AddThemeColorOverride("font_color", NationColor(view.Nation));

        foreach (var (slot, label) in EquipViewSlots)
        {
            int itemId = 0;
            short dura = 0;
            foreach (var w in view.Worn)
                if (w.Slot == slot) { itemId = w.ItemId; dura = w.Durability; break; }

            _equipViewGear.AddChild(BuildEquipViewRow(slot, label, itemId, dura));
        }

        AddEquipViewStat("Max HP", view.MaxHp.ToString("N0"), new Color("c0392b"));
        AddEquipViewStat("Max MP", view.MaxMp.ToString("N0"), new Color("2d6fb0"));
        _equipViewStats.AddChild(new HSeparator());
        AddEquipViewStat("Strength", StatWithBonus(view.Str, view.StrBonus));
        AddEquipViewStat("Stamina", StatWithBonus(view.Sta, view.StaBonus));
        AddEquipViewStat("Dexterity", StatWithBonus(view.Dex, view.DexBonus));
        AddEquipViewStat("Intelligence", StatWithBonus(view.Intel, view.IntelBonus));
        AddEquipViewStat("Magic attack", StatWithBonus(view.Magic, view.MagicBonus));
        _equipViewStats.AddChild(new HSeparator());
        AddEquipViewStat("Attack", view.Attack.ToString());
        AddEquipViewStat("Defence", view.Defence.ToString());
        _equipViewStats.AddChild(new HSeparator());
        AddEquipViewStat("Fire resist", view.FireR.ToString());
        AddEquipViewStat("Ice resist", view.IceR.ToString());
        AddEquipViewStat("Lightning resist", view.LightningR.ToString());
        AddEquipViewStat("Magic resist", view.MagicR.ToString());
        AddEquipViewStat("Curse resist", view.CurseR.ToString());
        AddEquipViewStat("Poison resist", view.PoisonR.ToString());
    }

    private static string StatWithBonus(int stat, int bonus) =>
        bonus > 0 ? $"{stat}  (+{bonus})" : stat.ToString();

    private Control BuildEquipViewRow(int slot, string label, int itemId, short durability)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Row(muted: itemId == 0));

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 8);
        row.AddChild(hb);

        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(22, 22),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = itemId != 0 ? ItemData.Icon(itemId) : null,
        };
        hb.AddChild(icon);

        var slotLabel = UiTheme.Text(label, 10, UiTheme.TextDim);
        slotLabel.CustomMinimumSize = new Vector2(78, 0);
        hb.AddChild(slotLabel);

        var name = UiTheme.Text(
            itemId != 0 ? ItemData.DisplayName(itemId) : "—",
            12,
            itemId != 0 ? UiTheme.TextHi : UiTheme.TextLo);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        hb.AddChild(name);

        if (itemId != 0)
        {
            var slotItem = new ItemSlot { ItemId = itemId, Count = 1, Durability = durability };
            row.MouseEntered += () => ShowItemTooltip(slot, slotItem);
            row.MouseExited += HideItemTooltip;
        }

        return row;
    }

    private void AddEquipViewStat(string label, string value, Color? color = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var key = UiTheme.Text(label, 12, UiTheme.TextLo);
        key.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(key);

        row.AddChild(UiTheme.Text(value, 12, color ?? UiTheme.TextHi));
        _equipViewStats.AddChild(row);
    }
}
