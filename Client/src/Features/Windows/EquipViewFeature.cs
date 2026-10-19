using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class EquipViewFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly PlayerInspectSession playerInspect;
    private readonly IPlayerInspectRequests playerInspectRequests;

    public EquipViewFeature(IWorldContext context, PlayerInspectSession playerInspect, IPlayerInspectRequests playerInspectRequests)
    {
        this.context = context;
        this.playerInspect = playerInspect;
        this.playerInspectRequests = playerInspectRequests;
    }

    private const int TitleText = 44765;
    private const int LevelText = 44766;
    private const int AttackText = 44768;
    private const int DefenceText = 44769;
    private const int StrText = 44771;
    private const int HpText = 44772;
    private const int DexText = 44773;
    private const int MpText = 44774;
    private const int IntText = 44775;
    private const int ResistText = 44776;
    private const int FireText = 44777;
    private const int IceText = 44778;
    private const int LightningText = 44779;
    private const int MagicText = 44780;
    private const int CurseText = 44781;
    private const int PoisonText = 44782;
    private const int NoteText = 44783;
    private const int NotHereText = 44785;
    private const int NoItemText = 44787;
    private const int SelfText = 44789;
    private const int Width = 540;
    private const int CostumeDrop = 55;
    private const float Cell = 44f;
    private const int CellGap = 8;
    private const float IdentityWidth = 170f;
    private const float IconInset = 3f;
    private const string NoValue = "-";

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private Label nameLabel = null!;
    private Label equipViewClass = null!;
    private Label nation = null!;
    private Label status = null!;

    private readonly Dictionary<int, TextureRect> icons = new();

    private readonly Dictionary<int, ItemSlot> items = new();

    private readonly Dictionary<string, Label> values = new();

    private bool shown;

    public bool IsOpen => shown;

    private LibreKO.Plugins.GameEquipView? model;
    private readonly List<LibreKO.Plugins.GameEquipValue> modelValues = new();
    private string pending = "";

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.TopWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("equipview", ItemData.Text(TitleText, "Equipment/Ability"),
            new Vector2(360, 120), Width) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 10);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 18);
        root.AddChild(top);

        var identity = new VBoxContainer { CustomMinimumSize = new Vector2(IdentityWidth, 0) };
        identity.AddThemeConstantOverride("separation", 4);
        nation = UiTheme.Text("", 12, UiTheme.TextLo);
        nameLabel = UiTheme.Text("", 15, UiTheme.TextHi);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        equipViewClass = UiTheme.Text("", 12, UiTheme.TextLo);
        identity.AddChild(nation);
        identity.AddChild(nameLabel);
        identity.AddChild(equipViewClass);
        top.AddChild(identity);

        var gear = Grid();
        for (int slot = 0; slot < InventoryConstants.SlotMax; slot++) gear.AddChild(CellFor(slot));
        top.AddChild(gear);

        var costume = Grid();
        foreach (int slot in EquipViewLayout.CostumeCells) costume.AddChild(CellFor(slot));
        var costumeColumn = new MarginContainer();
        costumeColumn.AddThemeConstantOverride("margin_top", CostumeDrop);
        costumeColumn.AddChild(costume);
        top.AddChild(costumeColumn);

        var stats = new GridContainer { Columns = EquipViewLayout.Columns * 2 };
        stats.AddThemeConstantOverride("h_separation", 14);
        stats.AddThemeConstantOverride("v_separation", 4);
        StatRow(stats, (LevelText, "Level"), (AttackText, "Attack"), (DexText, "DEX"));
        StatRow(stats, (HpText, "HP"), (DefenceText, "Defence"), (0, "MAG"));
        StatRow(stats, (MpText, "MP"), (StrText, "STR"), (IntText, "INT"));
        StatRow(stats, (0, "SP"), (0, "STA"), (-1, ""));
        root.AddChild(stats);

        root.AddChild(UiTheme.SectionTitle(ItemData.Text(ResistText, "Resistance")));
        var resist = new GridContainer { Columns = EquipViewLayout.Columns * 2 };
        resist.AddThemeConstantOverride("h_separation", 14);
        resist.AddThemeConstantOverride("v_separation", 4);
        StatRow(resist, (FireText, "Fire"), (IceText, "Ice"), (LightningText, "Lightning"));
        StatRow(resist, (MagicText, "Magic"), (CurseText, "Curse"), (PoisonText, "Poison"));
        root.AddChild(resist);

        var note = UiTheme.Text(ItemData.Text(NoteText, "The corresponding ability value is the value with items and buffs applied."),
            11, UiTheme.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(note);

        status = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(status);

        playerInspect.EquipmentViewEvent += OnEquipmentView;
        AttachPlugin();
    }

    private static GridContainer Grid()
    {
        var grid = new GridContainer { Columns = EquipViewLayout.Columns };
        grid.AddThemeConstantOverride("h_separation", CellGap);
        grid.AddThemeConstantOverride("v_separation", CellGap);
        return grid;
    }

    private Control CellFor(int slot)
    {
        var cell = new Panel { CustomMinimumSize = new Vector2(Cell, Cell) };
        cell.AddThemeStyleboxOverride("panel", UiTheme.Slot());
        var icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, (int)IconInset);
        cell.AddChild(icon);
        cell.MouseEntered += () =>
        {
            if (items.TryGetValue(slot, out var item)) context.Tooltips.Show(-1, item);
        };
        cell.MouseExited += context.Tooltips.Hide;
        icons[slot] = icon;
        return cell;
    }

    private void StatRow(GridContainer grid, params (int Text, string Fallback)[] cells)
    {
        foreach (var (text, fallback) in cells)
        {
            var key = UiTheme.Text(text > 0 ? ItemData.Text(text, fallback) : fallback, 12, UiTheme.TextLo);
            var value = UiTheme.Text("", 12, UiTheme.TextHi);
            value.CustomMinimumSize = new Vector2(72, 0);
            grid.AddChild(key);
            grid.AddChild(value);
            if (fallback.Length > 0) values[fallback] = value;
        }
    }

    private void PluginNotify() => Callable.From(LibreKO.Plugins.PluginHost.Game.RaiseEquipView).CallDeferred();

    private void SetValue(string key, string value)
    {
        int at = modelValues.FindIndex(v => v.Key == key);
        var entry = new LibreKO.Plugins.GameEquipValue(key, Caption(key), value);
        if (at >= 0) modelValues[at] = entry;
        else modelValues.Add(entry);
        if (values.TryGetValue(key, out var label)) label.Text = value;
    }

    public void Dispose()
    {
        playerInspect.EquipmentViewEvent -= OnEquipmentView;
    }

    private void AttachPlugin() => LibreKO.Plugins.PluginHost.Game.AttachEquipView(new PluginBridge(this));

    internal void RequestEquipmentView(string name)
    {
        pending = name;
        Clear();
        nameLabel.Text = name;
        status.Text = "Requesting…";
        status.AddThemeColorOverride("font_color", UiTheme.TextLo);
        Panel.Visible = true;
        shown = true;
        PluginNotify();
        playerInspectRequests.EquipmentViewRequest(name);
    }

    public void Close()
    {
        context.Tooltips.Hide();
        shown = false;
        Panel.Visible = false;
        PluginNotify();
    }

    private void Clear()
    {
        items.Clear();
        foreach (var icon in icons.Values)
        {
            icon.Texture = null;
            foreach (var child in icon.GetChildren())
                if (child is UpgradeBadge badge) badge.QueueFree();
        }
        foreach (var value in values.Values) value.Text = "";
        nameLabel.Text = "";
        equipViewClass.Text = "";
        nation.Text = "";
        modelValues.Clear();
        model = null;
    }

    private static string Caption(string key) => key switch
    {
        "Level" => ItemData.Text(LevelText, "Level"),
        "HP" => ItemData.Text(HpText, "HP"),
        "MP" => ItemData.Text(MpText, "MP"),
        "SP" => "SP",
        "Attack" => ItemData.Text(AttackText, "Attack"),
        "Defence" => ItemData.Text(DefenceText, "Defence"),
        "STR" => ItemData.Text(StrText, "STR"),
        "STA" => "STA",
        "DEX" => ItemData.Text(DexText, "DEX"),
        "MAG" => "MAG",
        "INT" => ItemData.Text(IntText, "INT"),
        "Fire" => ItemData.Text(FireText, "Fire"),
        "Ice" => ItemData.Text(IceText, "Ice"),
        "Lightning" => ItemData.Text(LightningText, "Lightning"),
        "Magic" => ItemData.Text(MagicText, "Magic"),
        "Curse" => ItemData.Text(CurseText, "Curse"),
        "Poison" => ItemData.Text(PoisonText, "Poison"),
        _ => key,
    };

    private sealed class PluginBridge : LibreKO.Plugins.IGameEquipView
    {
        private readonly EquipViewFeature equipView;

        public PluginBridge(EquipViewFeature equipView) => this.equipView = equipView;

        public bool IsOpen => equipView.shown;
        public LibreKO.Plugins.GameEquipView? View => equipView.model;
        public string Title => ItemData.Text(TitleText, "Equipment/Ability");
        public string Note => ItemData.Text(NoteText, "These values include items and buffs.");
        public string ResistTitle => ItemData.Text(ResistText, "Resistance");

        public void ShowTooltip(int slot)
        {
            if (equipView.items.TryGetValue(slot, out var item)) equipView.context.Tooltips.Show(-1, item);
        }

        public void HideTooltip() => equipView.context.Tooltips.Hide();
        public void Close() => equipView.Close();

        event System.Action? LibreKO.Plugins.IGameEquipView.Changed { add { } remove { } }
    }

    internal void OnEquipmentView(EquipmentViewResult result, EquipmentView view)
    {
        if (!shown) return;

        if (result != EquipmentViewResult.Accepted)
        {
            Clear();
            nameLabel.Text = pending;
            status.Text = result switch
            {
                EquipmentViewResult.NotInSameRegion => ItemData.Text(NotHereText, "The user does not exist in the same region."),
                EquipmentViewResult.CannotChooseYourself => ItemData.Text(SelfText, "You cannot choose yourself"),
                EquipmentViewResult.NoViewEquipmentItem => ItemData.Text(NoItemText, "There is not enough View Equipment Item."),
                _ => $"{pending} could not be found.",
            };
            status.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }

        Clear();
        status.Text = "";
        nameLabel.Text = view.Name;
        nameLabel.AddThemeColorOverride("font_color", UiKit.NationColor(view.Nation));
        equipViewClass.Text = CharacterClassCatalog.DisplayName(view.Class);
        nation.Text = UiKit.NationName(view.Nation);

        foreach (var w in view.Worn)
        {
            if (!icons.TryGetValue(w.Slot, out var icon)) continue;
            items[w.Slot] = new ItemSlot { ItemId = w.ItemId, Count = 1, Durability = w.Durability };
            icon.Texture = ItemIcons.Get(w.ItemId);
            UpgradeBadge.Show(icon, w.ItemId);
        }

        SetValue("Level", EquipViewLayout.LevelLine(view.Level, view.RebirthLevel));
        SetValue("HP", view.MaxHp.ToString("N0"));
        SetValue("MP", view.MaxMp.ToString("N0"));
        SetValue("SP", NoValue);
        SetValue("Attack", view.Attack.ToString());
        SetValue("Defence", view.Defence.ToString());
        SetValue("STR", EquipViewLayout.StatLine(view.Str, view.StrBonus));
        SetValue("STA", EquipViewLayout.StatLine(view.Sta, view.StaBonus));
        SetValue("DEX", EquipViewLayout.StatLine(view.Dex, view.DexBonus));
        SetValue("MAG", EquipViewLayout.StatLine(view.Magic, view.MagicBonus));
        SetValue("INT", EquipViewLayout.StatLine(view.Intel, view.IntelBonus));
        SetValue("Fire", view.FireR.ToString());
        SetValue("Ice", view.IceR.ToString());
        SetValue("Lightning", view.LightningR.ToString());
        SetValue("Magic", view.MagicR.ToString());
        SetValue("Curse", view.CurseR.ToString());
        SetValue("Poison", view.PoisonR.ToString());
        var itemIcons = new Dictionary<int, Texture2D?>();
        foreach (var (slot, item) in items) itemIcons[slot] = ItemIcons.Get(item.ItemId);
        model = new LibreKO.Plugins.GameEquipView(view.Name, UiKit.NationColor(view.Nation),
            CharacterClassCatalog.DisplayName(view.Class), view.Nation, modelValues.ToArray(), itemIcons, "");
        PluginNotify();
    }
}
