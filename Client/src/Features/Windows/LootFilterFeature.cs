using Godot;

namespace LibreKO.Features.Windows;

internal sealed class LootFilterFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly TargetStateSession targetState;
    private readonly ITargetStateRequests targetStateRequests;

    public LootFilterFeature(IWorldContext context, SelfSession self, TargetStateSession targetState, ITargetStateRequests targetStateRequests)
    {
        this.context = context;
        this.self = self;
        this.targetState = targetState;
        this.targetStateRequests = targetStateRequests;
    }

    public void Dispose()
    {
        DropListDispose();
    }

    private const int RuleAlways = 1;
    private const int RuleNever = 2;
    private const int RuleClear = 3;
    private const int PriceStep = 100;
    private const int PriceMax = 100_000_000;
    private const float RuleNameWidth = 200f;
    private const float RuleKindWidth = 56f;

    private static readonly string[] ToggleColourStates =
    { "font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color", "font_focus_color" };

    private static readonly (int Rarity, string Name, Color Colour)[] GradeRows =
    {
        (ItemData.Rarity.Regular, "Normal", new Color("ffffff")),
        (ItemData.Rarity.Magic, "Magic", new Color("0080ff")),
        (ItemData.Rarity.Rare, "Rare", new Color("ffff00")),
        (ItemData.Rarity.Craft, "Craft", new Color("00ff00")),
        (ItemData.Rarity.Unique, "Unique", new Color("dcc77c")),
        (ItemData.Rarity.Upgrade, "Upgrade", new Color("ff80ff")),
    };

    private static readonly (LootGroup Group, string Name)[] GroupRows =
    {
        (LootGroup.Coins, "Coins"),
        (LootGroup.Equipment, "Equipment"),
        (LootGroup.Quest, "Quest items"),
        (LootGroup.Other, "Other items (potions, scrolls, materials)"),
    };

    private LootFilter lootFilter = new();
    private string owner = "";
    internal HudWindow? Window { get; private set; }
    private VBoxContainer ruleList = null!;
    private PopupMenu? ruleMenu;
    private int ruleItem;

    internal LootFilter Current()
    {
        string owner = self.LastEnter.Name ?? "";
        if (owner != this.owner)
        {
            this.owner = owner;
            lootFilter = owner.Length > 0 ? Config.LoadLootFilter(owner) : new LootFilter();
        }
        return lootFilter;
    }

    private void Save()
    {
        if (owner.Length > 0) Config.SaveLootFilter(owner, lootFilter);
        RefreshDropListTicks();
    }

    private static string GroupName(LootGroup group)
    {
        foreach (var (g, name) in GroupRows)
            if (g == group) return name;
        return group.ToString();
    }

    private static string GradeName(int rarity)
    {
        foreach (var (r, name, _) in GradeRows)
            if (r == rarity) return name;
        return rarity.ToString();
    }

    internal bool Takes(int itemId) =>
        Current().Takes(itemId, LootFilter.GroupOf(itemId, LootWire.GoldItemId, ItemData.Get(itemId)),
            ItemData.ExtFor(itemId)?.MagicOrRare ?? ItemData.Rarity.Regular, ItemData.SellPrice(itemId));

    internal void Open()
    {
        if (Window == null || !GodotObject.IsInstanceValid(Window)) Build();
        RefreshRules();
        Window!.Visible = true;
    }

    public void Build()
    {
        var filter = Current();
        var layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        Window = new HudWindow("loot_filter", "Loot Filter") { Visible = false };
        Window.Closed += () => Window.Visible = false;
        layer.AddChild(Window);
        var body = Window.Body;
        body.AddThemeConstantOverride("separation", 6);

        body.AddChild(Toggle("Filter what auto-loot picks up", filter.Enabled, on => filter.Enabled = on));
        body.AddChild(UiTheme.SectionTitle("Item groups"));
        foreach (var (group, name) in GroupRows)
        {
            var g = group;
            body.AddChild(Toggle(name, filter.Groups.Contains(g), on => { if (on) filter.Groups.Add(g); else filter.Groups.Remove(g); }));
        }

        body.AddChild(UiTheme.SectionTitle("Equipment grades"));
        var grades = new GridContainer { Columns = 3 };
        grades.AddThemeConstantOverride("h_separation", 12);
        foreach (var (rarity, name, colour) in GradeRows)
        {
            int r = rarity;
            var toggle = Toggle(name, filter.Grades.Contains(r), on => { if (on) filter.Grades.Add(r); else filter.Grades.Remove(r); });
            foreach (var state in ToggleColourStates) toggle.AddThemeColorOverride(state, colour);
            grades.AddChild(toggle);
        }
        body.AddChild(grades);

        var priceRow = new HBoxContainer();
        priceRow.AddThemeConstantOverride("separation", 8);
        var priceLabel = HudStyle.Label(13);
        priceLabel.Text = "Minimum sell price";
        priceRow.AddChild(priceLabel);
        var price = new SpinBox
        {
            MinValue = 0, MaxValue = PriceMax, Step = PriceStep,
            Value = filter.MinSellPrice, Suffix = "coins",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        price.ValueChanged += v => { filter.MinSellPrice = (int)v; Save(); };
        priceRow.AddChild(price);
        body.AddChild(priceRow);

        body.AddChild(UiTheme.SectionTitle("Item rules (right-click an item in a loot box)"));
        ruleList = new VBoxContainer();
        ruleList.AddThemeConstantOverride("separation", 2);
        body.AddChild(ruleList);
        DropListInit();
    }

    private CheckBox Toggle(string text, bool pressed, System.Action<bool> changed)
    {
        var box = new CheckBox { Text = text, ButtonPressed = pressed, FocusMode = Control.FocusModeEnum.None };
        box.AddThemeFontSizeOverride("font_size", 13);
        foreach (var state in ToggleColourStates) box.AddThemeColorOverride(state, UiTheme.TextHi);
        box.Toggled += on => { changed(on); Save(); };
        return box;
    }

    internal void RefreshRules()
    {
        foreach (var child in ruleList.GetChildren()) child.QueueFree();
        var filter = Current();
        int shown = 0;
        foreach (var (ids, take) in new[] { (filter.Always, true), (filter.Never, false) })
            foreach (int id in new List<int>(ids))
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                var name = HudStyle.Label(12);
                name.Text = ItemData.DisplayName(id);
                name.CustomMinimumSize = new Vector2(RuleNameWidth, 0);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                name.AddThemeColorOverride("font_color", ItemGrade.Tint(id));
                row.AddChild(name);
                var rule = HudStyle.Label(12);
                rule.Text = take ? "always" : "never";
                rule.CustomMinimumSize = new Vector2(RuleKindWidth, 0);
                rule.AddThemeColorOverride("font_color", take ? UiTheme.Good : UiTheme.Bad);
                row.AddChild(rule);
                int itemId = id;
                var remove = UiTheme.SmallButton("Remove", "");
                remove.Pressed += () => { filter.SetRule(itemId, null); Save(); RefreshRules(); };
                row.AddChild(remove);
                ruleList.AddChild(row);
                shown++;
            }
        if (shown == 0)
        {
            var none = HudStyle.Label(12);
            none.Text = "No item rules yet.";
            ruleList.AddChild(none);
        }
    }

    internal void ShowRuleMenu(int itemId)
    {
        if (itemId == LootWire.GoldItemId) return;
        if (ruleMenu == null || !GodotObject.IsInstanceValid(ruleMenu))
        {
            ruleMenu = new PopupMenu();
            ruleMenu.IdPressed += OnRuleMenu;
            context.Root.AddChild(ruleMenu);
        }
        ruleItem = itemId;
        ruleMenu.Clear();
        ruleMenu.AddItem("Always auto-loot this item", RuleAlways);
        ruleMenu.AddItem("Never auto-loot this item", RuleNever);
        ruleMenu.AddItem("Clear the rule for this item", RuleClear);
        UiKit.PopupAtCursor(context.Root, ruleMenu, context.Root.GetViewport().GetMousePosition());
    }

    private void OnRuleMenu(long id)
    {
        Current().SetRule(ruleItem, id switch
        {
            RuleAlways => true,
            RuleNever => false,
            _ => null,
        });
        Save();
        if (Window != null && GodotObject.IsInstanceValid(Window) && Window.Visible) RefreshRules();
    }

    private const double DropListCacheSeconds = 180;
    private const float DropListIconSize = 24f;
    private const float DropListMaxHeight = 220f;
    private const float DropListButtonSize = 22f;
    private const string DropListIcon = "system/bag";

    internal Button DropListBtn { get; private set; } = null!;
    internal PanelContainer DropPanel { get; private set; } = null!;
    private Label dropHeader = null!;
    private VBoxContainer dropRows = null!;

    private readonly Dictionary<int, (double At, DropListReply List)> dropCache = new();

    private int dropTargetId = -1;
    private int dropPendingId = -1;
    private DropListReply? dropShown;
    private Label dropLegend = null!;
    private const string DropLegendText = "Ticked items are picked up by auto-loot. Click a tick to change it for that item.";
    private const string DropLegendOffText = "Auto-loot filter is off: everything is picked up. Click a tick to set a rule.";
    private const string DropRulesFullText = "You already have the most item rules. Remove one first.";
    private static readonly Color DropRuleAlwaysColour = new("ffd75a");
    private static readonly Color DropRuleNeverColour = new("ff6a6a");

    private static readonly string[] DropTickStyleStates = { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" };

    internal void BuildDropListUi(VBoxContainer box)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        box.AddChild(row);
        DropListBtn = UiTheme.IconButton(UiIcons.Get(DropListIcon), "Drop list");
        DropListBtn.CustomMinimumSize = new Vector2(DropListButtonSize, DropListButtonSize);
        DropListBtn.Visible = false;
        DropListBtn.Pressed += ToggleDropList;
        row.AddChild(DropListBtn);

        DropPanel = UiTheme.Section();
        DropPanel.Visible = false;
        box.AddChild(DropPanel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        DropPanel.AddChild(body);
        dropHeader = UiTheme.Text("", 12, UiTheme.TextHi);
        dropHeader.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(dropHeader);
        dropLegend = UiTheme.Text("", 11, UiTheme.TextLo);
        dropLegend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(dropLegend);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0f, 0f),
        };
        body.AddChild(scroll);
        dropRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        dropRows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(dropRows);
        dropRows.Resized += () => scroll.CustomMinimumSize = new Vector2(0f, Mathf.Min(dropRows.Size.Y, DropListMaxHeight));
    }

    private void DropListInit() => targetState.DropListEvent += OnDropList;

    private void DropListDispose() => targetState.DropListEvent -= OnDropList;

    private int shownTargetId = -1;

    internal void SyncDropListButton(int shownId)
    {
        shownTargetId = shownId;
        if (DropListBtn == null || !GodotObject.IsInstanceValid(DropListBtn)) return;
        bool monster = shownId >= 0 && context.Entities.Find(shownId) is { IsMonster: true, Attackable: true };
        if (DropListBtn.Visible != monster) DropListBtn.Visible = monster;
        if (shownId != dropTargetId && DropPanel.Visible) DropPanel.Visible = false;
    }

    private void ToggleDropList()
    {
        if (DropPanel.Visible) { DropPanel.Visible = false; return; }
        int target = shownTargetId;
        if (target < 0 || context.Entities.Find(target) is not { } e) return;
        dropTargetId = target;
        DropPanel.Visible = true;
        if (dropCache.TryGetValue(e.NpcId, out var cached) && context.Now - cached.At < DropListCacheSeconds)
        {
            ShowDropList(cached.List);
            return;
        }
        dropHeader.Text = "Loading...";
        ClearDropRows();
        dropPendingId = target;
        targetStateRequests.DropListRequest(target);
    }

    internal DropListReply? CachedDrops(int targetId) =>
        targetId >= 0 && context.Entities.Find(targetId) is { } e && dropCache.TryGetValue(e.NpcId, out var cached)
    && context.Now - cached.At < DropListCacheSeconds ? cached.List with { TargetId = targetId } : null;

    internal void RequestDropsFor(int targetId)
    {
        if (targetId < 0 || context.Entities.Find(targetId) is not { IsMonster: true }) return;
        if (CachedDrops(targetId) != null) { context.Plugins.Drops(targetId); return; }
        targetStateRequests.DropListRequest(targetId);
    }

    private void OnDropList(DropListReply list)
    {
        if (context.Entities.Find(list.TargetId) is { } e && list.Shown)
            dropCache[e.NpcId] = (context.Now, list);
        context.Plugins.Drops(list.TargetId);
        if (list.TargetId != dropPendingId) return;
        dropPendingId = -1;
        if (DropPanel.Visible && dropTargetId == list.TargetId) ShowDropList(list);
    }

    private void RefreshDropListTicks()
    {
        if (dropShown is { } shown && DropPanel != null && GodotObject.IsInstanceValid(DropPanel) && DropPanel.Visible)
            ShowDropList(shown);
    }

    internal void ShowDropList(DropListReply list)
    {
        ClearDropRows();
        dropShown = list;
        dropLegend.Text = Current().Enabled ? DropLegendText : DropLegendOffText;
        dropLegend.Visible = list.Shown && list.Items.Count > 0;
        if (!list.Shown)
        {
            dropHeader.Text = "No drop list for this target.";
            return;
        }
        string coins = list.CoinMax > 0 ? $"Coins {list.CoinMin:n0}-{list.CoinMax:n0}" : "No coins";
        dropHeader.Text = list.Experience > 0 ? $"{coins}  ·  EXP {list.Experience:n0}" : coins;
        if (list.Items.Count == 0)
        {
            dropRows.AddChild(UiTheme.Text("No items.", 12, UiTheme.TextLo));
            return;
        }
        foreach (var line in list.Items) dropRows.AddChild(DropRow(line));
    }

    private Control DropRow(DropLine line)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(DropTick(line.ItemId));
        var icon = new TextureRect
        {
            Texture = ItemIcons.Get(line.ItemId),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(DropListIconSize, DropListIconSize),
        };
        row.AddChild(icon);
        var name = UiTheme.Text(ItemData.DisplayName(line.ItemId), 12, UiTheme.TextHi);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        row.AddChild(name);
        row.AddChild(UiTheme.Text(BandText(line.Band), 11, BandColour(line.Band), HorizontalAlignment.Right));
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.MouseEntered += () => context.Tooltips.Show(-1, UiKit.TooltipItem(line.ItemId));
        row.MouseExited += context.Tooltips.Hide;
        return row;
    }

    private CheckBox DropTick(int itemId)
    {
        var filter = Current();
        var group = LootFilter.GroupOf(itemId, LootWire.GoldItemId, ItemData.Get(itemId));
        int rarity = ItemData.ExtFor(itemId)?.MagicOrRare ?? ItemData.Rarity.Regular;
        int price = ItemData.SellPrice(itemId);
        var reason = filter.Verdict(itemId, group, rarity, price);
        bool taken = reason is LootReason.Taken or LootReason.AlwaysRule;
        var tick = new CheckBox { ButtonPressed = taken, FocusMode = Control.FocusModeEnum.None };
        foreach (var state in DropTickStyleStates) tick.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        tick.TooltipText = DropReasonText(reason, group, rarity, price);
        if (reason == LootReason.AlwaysRule) tick.Modulate = DropRuleAlwaysColour;
        else if (reason == LootReason.NeverRule) tick.Modulate = DropRuleNeverColour;
        tick.Toggled += _ =>
        {
            if (!filter.Flip(itemId, group, rarity, price)) context.Log.Notice(DropRulesFullText);
            Save();
            Callable.From(RefreshDropListTicks).CallDeferred();
        };
        return tick;
    }

    private static string DropReasonText(LootReason reason, LootGroup group, int rarity, int price) => reason switch
    {
        LootReason.Taken => $"Auto-loot picks this up ({GroupName(group)}).",
        LootReason.AlwaysRule => "Auto-loot picks this up: your rule.",
        LootReason.NeverRule => "Auto-loot leaves this: your rule.",
        LootReason.GroupOff => $"Auto-loot leaves this: the {GroupName(group)} group is off.",
        LootReason.GradeOff => $"Auto-loot leaves this: {GradeName(rarity)} items are off.",
        _ => $"Auto-loot leaves this: it sells for {price:n0}.",
    };

    private void ClearDropRows()
    {
        foreach (Node child in dropRows.GetChildren()) child.QueueFree();
    }

    private static string BandText(DropBand band) => band switch
    {
        DropBand.Common => "Common",
        DropBand.Uncommon => "Uncommon",
        DropBand.Rare => "Rare",
        _ => "Very rare",
    };

    private static Color BandColour(DropBand band) => band switch
    {
        DropBand.Common => UiTheme.TextLo,
        DropBand.Uncommon => UiTheme.Good,
        DropBand.Rare => UiTheme.Premium,
        _ => UiTheme.GoldVivid,
    };
}
