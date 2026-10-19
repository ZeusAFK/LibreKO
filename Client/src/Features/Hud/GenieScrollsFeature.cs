using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GenieScrollsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly GenieSession genieSession;
    private readonly HotbarFeature hotbar;
    private readonly SelfSession self;
    private readonly SkillCastSystem skillCast;

    public GenieScrollsFeature(IWorldContext context, GenieSession genieSession, HotbarFeature hotbar, SelfSession self, SkillCastSystem skillCast)
    {
        this.context = context;
        this.genieSession = genieSession;
        this.hotbar = hotbar;
        this.self = self;
        this.skillCast = skillCast;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private void WireScrollSlot(Button button, int slot)
    {
        button.SetDragForwarding(
            Callable.From<Vector2, Variant>(_ => GenieDrag.Begin(button, GenieDrag.ScrollSlotKey, slot, scrollItems[slot], ItemIcons.Get(scrollItems[slot]), genieSession.GenieRunning)),
            Callable.From<Vector2, Variant, bool>((_, data) => ScrollDrop(data) != 0),
            Callable.From<Vector2, Variant>((_, data) =>
            {
                int id = ScrollDrop(data);
                if (id == 0) return;
                if (GenieDrag.TryDragSlot(data, GenieDrag.ScrollSlotKey, out int from) && from != slot)
                    (scrollItems[from], scrollItems[slot]) = (scrollItems[slot], id);
                else
                    scrollItems[slot] = id;
                scrollRetryAt.Clear();
                RefreshScrolls();
            }));
        button.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
            scrollItems[slot] = 0;
            RefreshScrolls();
            button.AcceptEvent();
        };
    }

    private int ScrollDrop(Variant data)
    {
        if (genieSession.GenieRunning || !GenieDrag.DragId(data, out int id)) return 0;
        return GenieScrollSkill(id) != null ? id : 0;
    }

    private const double ScrollPollInterval = 1;
    private const double ScrollAttemptInterval = 2;
    private const double ScrollRetryInterval = 10;
    private const int ScrollSlots = 4;

    private readonly int[] scrollItems = new int[ScrollSlots];
    private readonly Button[] scrollButtons = new Button[ScrollSlots];
    private readonly Dictionary<int, double> scrollRetryAt = new();
    private CheckButton autoScrolls = null!;
    private PopupMenu scrollPicker = null!;
    private Label scrollStatus = null!;
    private int scrollEditing;
    private double nextScrollAt;

    internal void BuildScrollOptions(VBoxContainer parent, CanvasLayer layer)
    {
        parent.AddChild(UiTheme.SectionTitle("Automatic Scrolls"));
        autoScrolls = new CheckButton { Text = "Use Selected Scrolls" };
        parent.AddChild(autoScrolls);
        var row = new HBoxContainer();
        parent.AddChild(row);
        for (int i = 0; i < ScrollSlots; i++)
        {
            int slot = i;
            var button = new Button { Text = "+", ExpandIcon = true,
                CustomMinimumSize = new Vector2(48, 48), FocusMode = Control.FocusModeEnum.None };
            button.AddThemeConstantOverride("icon_max_width", 36);
            button.Pressed += () => OpenScrollPicker(slot);
            WireScrollSlot(button, slot);
            row.AddChild(button);
            scrollButtons[i] = button;
        }
        parent.AddChild(UiTheme.Text("Select inventory buff scrolls. Reapplies only after the buff expires.", 11, UiTheme.TextLo));
        scrollStatus = UiTheme.Text("Scroll automation is off.", 11, UiTheme.TextLo);
        parent.AddChild(scrollStatus);
        scrollPicker = new PopupMenu();
        layer.AddChild(scrollPicker);
        scrollPicker.IdPressed += id =>
        {
            scrollItems[scrollEditing] = (int)id;
            scrollRetryAt.Clear();
            RefreshScrolls();
        };
        autoScrolls.Toggled += _ => RefreshScrolls();
    }

    private SkillData.Skill? GenieScrollSkill(int itemId)
    {
        var item = ItemData.Get(itemId);
        var skill = item == null ? null : SkillData.Get(item.Effect1);
        // Only consumable, timed, friendly type-4 buffs; never gear, potions or attack items.
        return skill != null && skill.Type1 == MagicType.Buff && skill.Duration > 0
            && !skill.IsEnemy && skill.ConsumedItem == itemId && itemId > 0
            && skill.Moral is SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party
            ? skill : null;
    }

    private void OpenScrollPicker(int slot)
    {
        scrollEditing = slot;
        scrollPicker.Clear();
        scrollPicker.AddItem("Clear Slot", 0);
        var added = new HashSet<int>();
        int choices = 0;
        for (int i = InventoryFeature.GridStart; i < self.Inventory.Length; i++)
        {
            int id = self.Inventory[i].ItemId;
            if (id <= 0 || !added.Add(id) || GenieScrollSkill(id) == null || self.Inventory.CountInBackpack(id) <= 0) continue;
            choices++;
            scrollPicker.AddIconItem(ItemIcons.Get(id), $"{ItemData.DisplayName(id)} ({self.Inventory.CountInBackpack(id)})", id);
        }
        if (choices == 0) scrollStatus.Text = "No scrolls found in your inventory.";
        scrollPicker.Position = (Vector2I)scrollButtons[slot].GetGlobalRect().End;
        scrollPicker.Popup();
    }

    internal void RefreshScrolls()
    {
        for (int i = 0; i < ScrollSlots; i++)
        {
            int id = scrollItems[i];
            scrollButtons[i].Icon = id > 0 ? ItemIcons.Get(id) : null;
            scrollButtons[i].Text = id > 0 ? "" : "+";
            scrollButtons[i].TooltipText = id > 0
                ? $"{ItemData.DisplayName(id)} — {self.Inventory.CountInBackpack(id)} remaining"
                : "Select a buff scroll from your inventory";
        }
        scrollStatus.Text = autoScrolls.ButtonPressed
            ? "Selected scrolls will be used while Genie is running."
            : "Scroll automation is off.";
    }

    internal void ResetScrollWatch()
    {
        scrollRetryAt.Clear();
        nextScrollAt = 0;
    }

    internal void SaveScrollSettings(ConfigFile config)
    {
        config.SetValue("genie", "auto_scrolls", autoScrolls.ButtonPressed);
        config.SetValue("genie", "scroll_items", scrollItems);
    }

    internal void LoadScrollSettings(ConfigFile config)
    {
        var items = config.GetValue("genie", "scroll_items", Array.Empty<int>()).AsInt32Array();
        for (int i = 0; i < ScrollSlots; i++) scrollItems[i] = i < items.Length ? items[i] : 0;
        autoScrolls.ButtonPressed = config.GetValue("genie", "auto_scrolls", false).AsBool();
        RefreshScrolls();
    }

    internal bool ScrollTick(double now)
    {
        if (!autoScrolls.ButtonPressed || now < nextScrollAt) return false;
        nextScrollAt = now + ScrollPollInterval;
        for (int i = 0; i < ScrollSlots; i++)
        {
            int itemId = scrollItems[i];
            var skill = GenieScrollSkill(itemId);
            if (skill == null || skillCast.SelfBuffActive(skill, now)) continue;
            if (self.Inventory.CountInBackpack(itemId) <= 0)
            { scrollStatus.Text = $"Out of scrolls: {ItemData.DisplayName(itemId)}"; continue; }
            if (scrollRetryAt.TryGetValue(skill.Id, out double retry) && retry > now) continue;
            if (!self.SkillLearned(skill) || !SkillData.IsGranted(skill.Tree, context.Self.TransformModel())
                || !skillCast.SkillReady(skill, now)) continue;
            var item = ItemData.Get(itemId);
            if (item == null || !hotbar.ItemUseAllowed(item, itemId, out _) || !skillCast.CanCastWithGear(skill)) continue;
            // One item attempt at a time. Server replies, not an optimistic local timer,
            // determine whether a buff is active. Failed attempts back off for 10 seconds.
            scrollRetryAt[skill.Id] = now + ScrollRetryInterval;
            nextScrollAt = now + ScrollAttemptInterval;
            context.Selection.WithSelected(context.Self.CharId, () => hotbar.UseHotItem(itemId));
            scrollStatus.Text = $"Using {ItemData.DisplayName(itemId)}…";
            return true;
        }
        return false;
    }
}
