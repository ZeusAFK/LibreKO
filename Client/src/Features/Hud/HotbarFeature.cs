using System.Globalization;
using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class HotbarFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly ChaosSession chaos;
    private readonly BifrostSession bifrost;
    private readonly CombatSession combat;
    private readonly GenieSession genie;
    private readonly ProgressSession progress;
    private readonly PvpSession pvp;
    private readonly SelfSession self;
    private readonly SkillTimers skillTimers;
    private readonly ICombatRequests combatRequests;
    private readonly MonsterStoneFeature monsterStone;
    private readonly SoccerSystem soccer;
    private readonly SkillCastSystem skillCast;

    public HotbarFeature(IWorldContext context, ChaosSession chaos, BifrostSession bifrost, CombatSession combat, GenieSession genie, ProgressSession progress, PvpSession pvp, SelfSession self, SkillTimers skillTimers, ICombatRequests combatRequests, MonsterStoneFeature monsterStone, SoccerSystem soccer, SkillCastSystem skillCast)
    {
        this.context = context;
        this.chaos = chaos;
        this.bifrost = bifrost;
        this.combat = combat;
        this.genie = genie;
        this.progress = progress;
        this.pvp = pvp;
        this.self = self;
        this.skillTimers = skillTimers;
        this.combatRequests = combatRequests;
        this.monsterStone = monsterStone;
        this.soccer = soccer;
        this.skillCast = skillCast;
    }

    internal const int HotPages = HotbarLayout.Pages;
    internal const int HotSlotsPerPage = HotbarLayout.SlotsPerPage;
    private const int HotTotal = HotbarLayout.Total;
    private const int HotToolSize = 21;
    private const int HotBarGap = 4;
    internal const int HotGripThickness = 10;
    private const int HotPageArrowWidth = 11;
    private const int HotPageArrowGap = 1;
    private const string LayoutId = "hotbar";
    private const string GenieText = "The skill bar cannot be changed while the Genie is hunting.";
    private const string VerticalLayoutId = "hotbar_vertical";
    internal const int HotSlotSize = 46;
    private const int HotPageBtnGap = 2;
    private const int HotPageLblHeight = 14;
    private const int HotPageBtnHeight = (HotSlotSize - HotPageBtnGap * 2 - HotPageLblHeight) / 2;

    internal int[] Slots { get; } = new int[HotTotal];
    internal int HotPage { get; private set; }
    private int hotSelected = -1;

    internal int HotSelected
    {
        get => hotSelected;
        set
        {
            hotSelected = value;
            combat.SelectedHotSlot = (self.MyCharId, value);
        }
    }
    private bool hotLoadedFromServer;

    private CanvasLayer layer = null!;
    internal Control Box { get; private set; } = null!;
    internal List<HotSlotCell> HotCells { get; } = new();
    private Label hotPageLbl = null!;
    private readonly List<HotStrip> extraBars = new();

    private sealed class HotStrip
    {
        public int Extra;
        public int Page;
        public Label PageLbl = null!;
        public readonly List<HotSlotCell> Cells = new();
    }

    private IMerchantSearch? merchantSearch;

    internal void AttachMerchantSearch(IMerchantSearch merchantSearch) => this.merchantSearch = merchantSearch;

    internal void DetachMerchantSearch() => merchantSearch = null;

    public void Build()
    {
        int keptSelection = combat.SelectedHotSlot.Owner == self.MyCharId ? combat.SelectedHotSlot.Slot : -1;
        BuildHotbar();
        combat.SkillDataEvent += OnSkillData;
        combat.SkillBarClearEvent += OnSkillBarClear;
        Config.HotbarLookChanged += OnLookChanged;

        LoadLocal();
        hotSelected = keptSelection;
        Refresh();
        ApplyChaos();
        ApplyFullMoon();

        if (combat.SkillBar is not { } bar)
        {
            if (!combat.SkillBarRequested)
            {
                combat.MarkSkillBarRequested();
                combatRequests.SkillDataLoad();
            }
            return;
        }
        hotLoadedFromServer = true;
        if (bar.Length == 0) OnSkillBarClear();
        else OnSkillData(bar);
    }

    public void Dispose()
    {
        combat.SkillDataEvent -= OnSkillData;
        combat.SkillBarClearEvent -= OnSkillBarClear;
        Config.HotbarLookChanged -= OnLookChanged;
    }

    public void LateTick(double delta, double nowSec)
    {
        CueReadySkills(nowSec);
        UpdateReady(nowSec);
    }

    private void OnLookChanged()
    {
        LayoutHotbars();
        Refresh();
    }

    internal void BuildHotbar() => BuildHotbar(Config.HotbarVertical, Config.HotbarExtraBars);

    internal void BuildHotbar(bool vertical, int extraBarCount)
    {
        layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        context.Hud.PluginSeam(layer, LibreKO.Plugins.HudPart.Hotbar);
        LayoutHotbars(vertical, extraBarCount);
    }

    private void LayoutHotbars() => LayoutHotbars(Config.HotbarVertical, Config.HotbarExtraBars);

    private void LayoutHotbars(bool vertical, int extraBarCount)
    {
        bool hidden = false;
        if (Box != null && GodotObject.IsInstanceValid(Box))
        {
            hidden = !Box.Visible;
            Box.QueueFree();
        }
        HotCells.Clear();
        extraBars.Clear();

        var stack = new BoxContainer { Vertical = !vertical, Visible = !hidden, Scale = Vector2.One * Config.HotbarScale };
        stack.AddThemeConstantOverride("separation", HotBarGap);
        layer.AddChild(stack);
        Box = stack;

        var extras = new List<Control>();
        var extraGrips = new List<Control>();
        for (int extra = 0; extra < extraBarCount; extra++)
        {
            var strip = new HotStrip { Extra = extra, Page = Config.HotbarExtraPage(extra) };
            extraBars.Add(strip);
            var extraGrip = NewHotGrip(vertical);
            extraGrips.Add(extraGrip);
            extras.Add(BuildHotStrip(strip, vertical, extraBarCount, extraGrip));
        }
        if (!vertical) foreach (var strip in extras) stack.AddChild(strip);
        var grip = NewHotGrip(vertical);
        var main = BuildHotStrip(null, vertical, extraBarCount, grip);
        stack.AddChild(main);
        if (vertical) foreach (var strip in extras) stack.AddChild(strip);

        var layout = HudLayout.Attach(stack, LayoutIdFor(vertical), grip,
            () => DefaultPosition(stack, main));
        foreach (var extraGrip in extraGrips) layout.AddDragHandle(extraGrip);
    }

    private static HotGrip NewHotGrip(bool vertical) => new()
    {
        Vertical = vertical,
        TooltipText = "Drag to move the skill bar",
        CustomMinimumSize = HotGripSize(vertical),
        SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
    };

    private static Vector2 HotGripSize(bool vertical) =>
        vertical ? new Vector2(HotSlotSize, HotGripThickness) : new Vector2(HotGripThickness, HotSlotSize);

    private static string LayoutIdFor(bool vertical) => vertical ? VerticalLayoutId : LayoutId;

    private Vector2 DefaultPosition(Control stack, Control main)
    {
        var viewport = context.Root.GetViewport().GetVisibleRect().Size;
        return new Vector2((viewport.X - main.GetCombinedMinimumSize().X * stack.Scale.X) * 0.5f,
            viewport.Y - stack.GetCombinedMinimumSize().Y * stack.Scale.Y);
    }

    private void KeepMainBarInPlace(Vector2 sizeBefore)
    {
        string id = LayoutIdFor(Config.HotbarVertical);
        if (Config.HotbarVertical || !GodotObject.IsInstanceValid(Box) || !Config.HasWindowPos(id)) return;

        var grown = (Box.GetCombinedMinimumSize() - sizeBefore) * Box.Scale;
        var pos = Config.GetWindowPos(id, Box.Position) - new Vector2(0f, grown.Y);
        var room = context.Root.GetViewport().GetVisibleRect().Size - Box.GetCombinedMinimumSize() * Box.Scale;
        pos = new Vector2(Mathf.Clamp(pos.X, 0f, Mathf.Max(0f, room.X)), Mathf.Clamp(pos.Y, 0f, Mathf.Max(0f, room.Y)));
        Box.Position = pos;
        Config.SaveWindowPos(id, pos);
    }

    private Control BuildHotStrip(HotStrip? strip, bool vertical, int extraBarCount, Control? grip = null)
    {
        var row = new BoxContainer
        {
            Vertical = vertical,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        row.AddThemeConstantOverride("separation", HotbarLayout.SlotGap(Config.HotbarGap, vertical));

        var pageLbl = HudStyle.Label(11, HorizontalAlignment.Center);
        pageLbl.CustomMinimumSize = new Vector2(22, HotPageLblHeight);
        if (strip == null) hotPageLbl = pageLbl;
        else strip.PageLbl = pageLbl;

        var pageBox = new BoxContainer
        {
            Vertical = !vertical,
            SizeFlagsVertical = vertical ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkEnd,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        pageBox.AddThemeConstantOverride("separation", vertical ? HotPageArrowGap : HotPageBtnGap);
        var arrowSize = vertical ? new Vector2(HotPageArrowWidth, HotPageLblHeight) : new Vector2(22, HotPageBtnHeight);
        pageBox.AddChild(MakeHotPageButton(vertical ? "◀" : "▲", -1, strip, arrowSize));
        pageBox.AddChild(pageLbl);
        pageBox.AddChild(MakeHotPageButton(vertical ? "▶" : "▼", 1, strip, arrowSize));
        row.AddChild(pageBox);

        var cells = strip == null ? HotCells : strip.Cells;
        for (int i = 0; i < HotSlotsPerPage; i++)
        {
            var cell = new HotSlotCell(i, genie, showKey: strip == null, keyInside: vertical)
            {
                PageOf = strip == null ? () => HotPage : () => strip.Page,
                OnActivate = abs => FireHotSlot(abs),
                OnSelect = SelectHotAbs,
                OnClear = ClearHotAbs,
                OnDrop = DropOntoHotAbs,
                OnHover = HotAbsHover,
            };
            cells.Add(cell);
            row.AddChild(cell);
        }

        if (strip == null) row.AddChild(BuildHotTools(vertical, extraBarCount));
        if (grip != null) row.AddChild(grip);
        return row;
    }

    private Control BuildHotTools(bool vertical, int extraBarCount)
    {
        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        grid.AddThemeConstantOverride("h_separation", HotPageBtnGap);
        grid.AddThemeConstantOverride("v_separation", HotPageBtnGap);

        var lockBtn = MakeHotTool(Config.HotbarLocked ? HotToolGlyph.Locked : HotToolGlyph.Unlocked,
            Config.HotbarLocked ? "Unlock the skill bar" : "Lock the skill bar");
        lockBtn.Pressed += () => ApplyLayout(!Config.HotbarLocked, vertical, extraBarCount);
        grid.AddChild(lockBtn);

        var turn = MakeHotTool(HotToolGlyph.Turn, vertical ? "Lay the skill bar flat" : "Stand the skill bar up");
        turn.Pressed += () => ApplyLayout(Config.HotbarLocked, !vertical, extraBarCount);
        grid.AddChild(turn);

        var add = MakeHotTool(HotToolGlyph.Add, "Add a skill bar");
        add.Disabled = extraBarCount >= HotbarLayout.MaxBars - 1;
        add.Pressed += () => ApplyLayout(Config.HotbarLocked, vertical, extraBarCount + 1);
        grid.AddChild(add);

        var remove = MakeHotTool(HotToolGlyph.Remove, "Remove a skill bar");
        remove.Disabled = extraBarCount <= 0;
        remove.Pressed += () => ApplyLayout(Config.HotbarLocked, vertical, extraBarCount - 1);
        grid.AddChild(remove);
        return grid;
    }

    private HotToolButton MakeHotTool(HotToolGlyph glyph, string tip)
    {
        var b = new HotToolButton
        {
            Glyph = glyph,
            TooltipText = tip,
            CustomMinimumSize = new Vector2(HotToolSize, HotToolSize),
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeStyleboxOverride("normal", HotPageBtnStyle(UiTheme.Glass, new Color(UiTheme.Edge, 0.85f)));
        b.AddThemeStyleboxOverride("hover", HotPageBtnStyle(UiTheme.GlassLight, new Color(UiTheme.Gold, 0.9f)));
        b.AddThemeStyleboxOverride("pressed", HotPageBtnStyle(new Color(0.05f, 0.05f, 0.06f, 0.97f), new Color(UiTheme.GoldDark, 0.95f)));
        b.AddThemeStyleboxOverride("disabled", HotPageBtnStyle(new Color(UiTheme.Glass, 0.5f), new Color(UiTheme.Edge, 0.35f)));
        return b;
    }

    internal void ApplyLayout(bool locked, bool vertical, int extraBarCount)
    {
        bool regrow = vertical == Config.HotbarVertical && extraBarCount != Config.HotbarExtraBars;
        var sizeBefore = GodotObject.IsInstanceValid(Box) ? Box.GetCombinedMinimumSize() : Vector2.Zero;
        Config.SetHotbarLayout(locked, vertical, extraBarCount);
        LayoutHotbars();
        Refresh();
        if (regrow) Callable.From(() => KeepMainBarInPlace(sizeBefore)).CallDeferred();
    }

    // HudTheme's Button pads 5px top/bottom, so only a compact stylebox lets CustomMinimumSize govern.
    private Button MakeHotPageButton(string glyph, int delta, HotStrip? strip, Vector2 size)
    {
        var b = new Button
        {
            Text = glyph,
            CustomMinimumSize = size,
            FocusMode = Control.FocusModeEnum.None,
            ClipContents = true,
        };
        b.AddThemeFontSizeOverride("font_size", 10);
        b.AddThemeStyleboxOverride("normal", HotPageBtnStyle(UiTheme.Glass, new Color(UiTheme.Edge, 0.85f)));
        b.AddThemeStyleboxOverride("hover", HotPageBtnStyle(UiTheme.GlassLight, new Color(UiTheme.Gold, 0.9f)));
        b.AddThemeStyleboxOverride("pressed", HotPageBtnStyle(new Color(0.05f, 0.05f, 0.06f, 0.97f), new Color(UiTheme.GoldDark, 0.95f)));
        b.Pressed += () =>
        {
            if (strip == null) ChangeHotPage(delta);
            else ChangeStripPage(strip, delta);
        };
        return b;
    }

    private static StyleBoxFlat HotPageBtnStyle(Color bg, Color border)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(3);
        sb.SetContentMarginAll(0);
        return sb;
    }

    private static int ReachablePages =>
        Platform.Pick(HotPages, TouchControls.ActionPages);

    internal void ChangeHotPage(int delta)
    {
        int pages = ReachablePages;
        HotPage = ((HotPage + delta) % pages + pages) % pages;
        Refresh();
    }

    private void ChangeStripPage(HotStrip strip, int delta)
    {
        strip.Page = ((strip.Page + delta) % HotPages + HotPages) % HotPages;
        Config.SetHotbarExtraPage(strip.Extra, strip.Page);
        Refresh();
    }

    internal void SetHotPage(int page)
    {
        HotPage = Mathf.Clamp(page, 0, ReachablePages - 1);
        Refresh();
    }

    internal void ActivateHotSlot(int slotInPage) => FireHotSlot(HotPage * HotSlotsPerPage + slotInPage);

    internal bool FireHotSlot(int abs)
    {
        if (abs < 0 || abs >= HotTotal || context.Self.Dead) return false;
        int id = Slots[abs];
        if (id == 0) return false;
        if (SkillData.IsSkill(id))
        {
            var skill = SkillData.Get(id)!;
            if (!Keeps(id) || !ZoneSkillTrees.Allowed(context.Zone, skill.Tree, skill.Id)) return false;
            skillCast.CastSkill(id);
        }
        else
        {
            if (!ChaosBattle.ItemUsable(context.Zone, ItemData.Get(id)?.Kind ?? 0)) return false;
            UseHotItem(id);
        }
        return true;
    }

    internal static bool Takes(int id, bool dragged) =>
        SkillData.Get(id) is not { } s || (dragged ? HotbarAssign.CanDrag(s.Type1, s.Type2) : HotbarAssign.CanAdd(s.Id, s.Type1));

    private static bool Keeps(int id) => SkillData.Get(id) is not { } s || HotbarAssign.Holds(s.Id, s.Type1, s.Type2);

    internal void SelectHotAbs(int abs)
    {
        if (abs < 0 || abs >= HotTotal) return;
        HotSelected = HotSelected == abs || Slots[abs] == 0 ? -1 : abs;
        Refresh();
    }

    internal bool CastSelectedHotSlot() => FireHotSlot(HotSelected);

    internal void HotAbsHover(int abs, bool entered)
    {
        if (!entered)
        {
            context.Tooltips.Hide();
            return;
        }
        if (abs < 0 || abs >= HotTotal) return;
        int id = Slots[abs];
        if (id == 0 || SkillData.IsSkill(id) || ItemData.Get(id) == null) return;

        for (int slot = Inventory.GridStart; slot < self.Inventory.Length; slot++)
            if (self.Inventory[slot].ItemId == id && !self.Inventory[slot].IsEmpty)
            {
                context.Tooltips.Show(slot, self.Inventory[slot]);
                return;
            }
        context.Tooltips.Show(-1, UiKit.TooltipItem(id));
    }

    internal bool UseHotItem(int itemId)
    {
        var def = ItemData.Get(itemId);
        if (def == null) return false;
        if (def.Effect1 != 0 && SkillData.IsSkill(def.Effect1))
        {
            if (!ItemUseAllowed(def, itemId, out string problem))
            {
                context.Log.Notice(problem);
                return true;
            }
            if (MerchantSearch.OpensWith(def.Effect1))
                return merchantSearch?.RequestSearch(itemId) ?? false;
            if (SkillData.Get(def.Effect1) is { } skill && MonsterStoneFeature.OpensNestDungeon(skill))
            {
                monsterStone.OpenNestDungeon(itemId);
                return true;
            }
            skillCast.CastSkill(def.Effect1);
            return true;
        }
        if (ItemData.EquipSlotFor(def) >= 0)
        {
            for (int abs = Inventory.GridStart; abs < self.Inventory.Length; abs++)
                if (self.Inventory[abs].ItemId == itemId) { context.Inventory.Activate(abs); return true; }
        }
        // Effect1 naming a magic row the bake never produced is what made quest potions inert.
        if (def.Effect1 != 0)
        {
            context.Log.Notice($"{ItemData.DisplayName(itemId)} has no usable effect.");
            return true;
        }
        return false;
    }

    // Mirrors the server's MagicItemUsageService.CanUseItem so a refused consumable says why.
    internal bool ItemUseAllowed(ItemData.Item def, int itemId, out string problem)
    {
        problem = "";
        string name = ItemData.DisplayName(itemId);
        if (!HasItemInBackpack(itemId))
        {
            problem = $"You have no {name} left.";
            return false;
        }
        if (self.Sheet.Level > 0 && def.ReqLevel > 0 && self.Sheet.Level < def.ReqLevel)
        {
            problem = $"{name} needs level {def.ReqLevel}.";
            return false;
        }
        if (self.Sheet.Level > 0 && def.ReqLevelMax > 0 && self.Sheet.Level > def.ReqLevelMax)
        {
            problem = $"{name} can only be used up to level {def.ReqLevelMax}.";
            return false;
        }
        if (!EquipRules.UseAllows(self.Class, def.Class))
        {
            problem = $"Your class cannot use {name}.";
            return false;
        }
        if (!MaestroPotions.CanUse(itemId, self.Sheet.Gold))
        {
            problem = $"{name} needs at least {MaestroPotions.MinimumCoins:n0} gold.";
            return false;
        }
        return true;
    }

    private bool HasItemInBackpack(int itemId)
    {
        for (int abs = Inventory.GridStart; abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].ItemId == itemId && !self.Inventory[abs].IsEmpty) return true;
        return false;
    }

    private bool HeldByGenie => genie.GenieRunning;

    private bool RefuseEdit()
    {
        if (!HeldByGenie) return false;
        context.Log.Notice(GenieText);
        return true;
    }

    internal void ClearHotAbs(int abs)
    {
        if (Config.HotbarLocked || RefuseEdit()) return;
        SetHotSlot(abs, 0);
    }

    internal void DropOntoHotAbs(int dest, int id, int fromAbs)
    {
        if (dest < 0 || dest >= HotTotal || RefuseEdit()) return;
        if (fromAbs >= 0 && fromAbs < HotTotal)
        {
            if (Config.HotbarLocked) return;
            (Slots[fromAbs], Slots[dest]) = (Slots[dest], Slots[fromAbs]);
        }
        else
        {
            if (!Assignable(id) || !Takes(id, dragged: true)) return;
            Slots[dest] = id;
        }
        Refresh();
        Save();
    }

    internal void DropOntoHotSlot(int slotInPage, int id, int fromSlotInPage)
    {
        int dest = HotPage * HotSlotsPerPage + slotInPage;
        if (dest < 0 || dest >= HotTotal || RefuseEdit()) return;
        if (fromSlotInPage >= 0)
        {
            int src = HotPage * HotSlotsPerPage + fromSlotInPage;
            (Slots[src], Slots[dest]) = (Slots[dest], Slots[src]);
        }
        else
        {
            if (!Assignable(id) || !Takes(id, dragged: true)) return;
            Slots[dest] = id;
        }
        Refresh();
        Save();
    }

    internal void AddTo(int id)
    {
        if (!Assignable(id) || !Takes(id, dragged: false) || RefuseEdit()) return;
        for (int p = 0; p < HotPages; p++)
        {
            int page = (HotPage + p) % HotPages;
            for (int i = 0; i < HotSlotsPerPage; i++)
            {
                int abs = page * HotSlotsPerPage + i;
                if (Slots[abs] != 0) continue;
                Slots[abs] = id;
                if (page != HotPage) SetHotPage(page);
                else Refresh();
                Save();
                return;
            }
        }
    }

    private void SetHotSlot(int abs, int id)
    {
        if (abs < 0 || abs >= HotTotal) return;
        Slots[abs] = id;
        Refresh();
        Save();
    }

    internal void Clear(bool notifyServer = true)
    {
        System.Array.Clear(Slots, 0, HotTotal);
        HotSelected = -1;
        HotPage = 0;
        Refresh();
        if (notifyServer) Save();
        else WriteLocal();
    }

    private void OnSkillBarClear()
    {
        if (!context.Root.IsInsideTree() || Box == null || !GodotObject.IsInstanceValid(Box)) return;
        hotLoadedFromServer = true;
        Clear(notifyServer: false);
    }

    internal void Refresh()
    {
        context.Plugins.Hotbar();
        context.Hud.TouchActions?.Refresh(HotPage);
        if (Box == null || !GodotObject.IsInstanceValid(Box)) return;
        context.Tooltips.Hide();
        if (HotSelected >= 0 && (HotSelected >= HotTotal || Slots[HotSelected] == 0)) HotSelected = -1;
        int page = HotPage * HotSlotsPerPage;
        for (int i = 0; i < HotCells.Count; i++)
            if (GodotObject.IsInstanceValid(HotCells[i]))
            {
                HotCells[i].Set(Slots[page + i]);
                HotCells[i].SetSelected(HotSelected == page + i);
            }
        if (hotPageLbl != null && GodotObject.IsInstanceValid(hotPageLbl))
            hotPageLbl.Text = $"{HotPage + 1}/{HotPages}";

        foreach (var strip in extraBars)
        {
            int first = strip.Page * HotSlotsPerPage;
            for (int i = 0; i < strip.Cells.Count; i++)
                if (GodotObject.IsInstanceValid(strip.Cells[i]))
                {
                    strip.Cells[i].Set(Slots[first + i]);
                    strip.Cells[i].SetSelected(HotSelected == first + i);
                }
            if (GodotObject.IsInstanceValid(strip.PageLbl))
                strip.PageLbl.Text = $"{strip.Page + 1}/{HotPages}";
        }
    }

    private double readyCueCheckedAt;
    private readonly List<int> readyCued = new();

    private void CueReadySkills(double now)
    {
        double since = readyCueCheckedAt;
        readyCueCheckedAt = now;
        CooldownCue.Collect(self.SkillCooldowns, since, now, Slots,
            id => SkillData.Get(id)?.RecastSeconds ?? 0f, readyCued);
        if (readyCued.Count == 0) return;

        if (CooldownCue.Sounds(readyCued, id => SkillData.Get(id)?.RecastSeconds ?? 0f))
            Audio.PlayUiFile(Sfx.SkillReadyFile);
        FlashReady(HotCells, HotPage);
        foreach (var strip in extraBars) FlashReady(strip.Cells, strip.Page);
        context.Plugins.HotbarReady(readyCued.ToArray());
    }

    private void FlashReady(List<HotSlotCell> cells, int page)
    {
        int first = page * HotSlotsPerPage;
        for (int i = 0; i < cells.Count; i++)
            if (GodotObject.IsInstanceValid(cells[i]) && readyCued.Contains(Slots[first + i]))
                cells[i].Flash();
    }

    private const double CountInterval = 0.1;

    private double countAt;

    private void UpdateReady(double now)
    {
        if (Box == null) return;

        bool counts = now >= countAt;
        if (counts) countAt = now + CountInterval;
        UpdateStripReady(HotCells, HotPage, now, touch: true, counts);
        foreach (var strip in extraBars) UpdateStripReady(strip.Cells, strip.Page, now, touch: false, counts);
    }

    private void UpdateStripReady(List<HotSlotCell> cells, int page, double now, bool touch, bool counts)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!GodotObject.IsInstanceValid(cell)) continue;
            int id = Slots[page * HotSlotsPerPage + i];
            if (id == 0) { cell.SetDim(false); cell.SetCooldown(0f); cell.SetCount(-1, true); continue; }

            var s = SkillData.Get(id);
            if (s == null && ItemData.Get(id) is { Effect1: not 0 } def) s = SkillData.Get(def.Effect1);

            float cd = s == null ? 0f : skillTimers.Fraction(s, now);
            cell.SetCooldown(cd);
            if (touch && i < TouchControls.ActionSlots) context.Hud.TouchActions?.SetCooldown(i, cd);
            cell.SetDim(s != null && cd <= 0f && !Usable(s, now));

            if (!counts) continue;
            int needId = SkillData.IsSkill(id) ? s?.ConsumedItem ?? 0 : id;
            if (needId != 0 && ItemData.Get(needId) != null)
            {
                int need = s is { IsRanged: true } ? Mathf.Max(1, s.NeedArrow) : 1;
                int have = self.Inventory.CountInBackpack(needId);
                cell.SetCount(have, have >= need);
            }
            else cell.SetCount(-1, true);
        }
    }

    private bool Usable(SkillData.Skill s, double now)
    {
        if (ChaosBattleLocked || !skillCast.SkillReady(s, now) || !self.SkillLearned(s)) return false;
        var gear = context.Self.Gear();
        if (!SkillUsable.GearAllows(s.NeedWeapon, SkillAnimation.HeldItemClass(gear, InventoryConstants.VisRightHand),
                SkillAnimation.HeldItemClass(gear, InventoryConstants.VisLeftHand))) return false;
        if (!self.Vitals.HasStamina(s.Sp) || !SkillUsable.AngerAllows(s.Id, pvp.AngerFull)) return false;
        if (!SkillUsable.BuffAllows(s.NeedBuffSkill, id => self.BuffEnds.TryGetValue(id, out double end) && end > now)) return false;
        if (s.Id is CoinSkills.A or CoinSkills.B && self.Sheet.Gold < CoinSkills.Floor) return false;
        if (!soccer.SoccerSkillAllowed(s)) return false;
        return SkillItemHeld(s);
    }

    internal bool SkillItemHeld(SkillData.Skill s) =>
        s.UseItem == 0 || s.IsResurrect || ItemData.Get(s.UseItem) == null
        || self.Inventory.CountInBackpack(s.ConsumedItem) >= (s.IsRanged ? Mathf.Max(1, s.NeedArrow) : 1);

    private void Save()
    {
        if (InChaosZone)
        {
            chaos.KeepHotbar((int[])Slots.Clone());
            return;
        }
        if (context.Zone == FullMoon.Zone)
        {
            bifrost.KeepFullMoonHotbar((int[])Slots.Clone());
            return;
        }
        combatRequests.SkillDataSave(Slots);
        WriteLocal();
    }

    private void OnSkillData(int[] ids)
    {
        if (!context.Root.IsInsideTree() || Box == null || !GodotObject.IsInstanceValid(Box)) return;
        if (InChaosZone)
        {
            hotLoadedFromServer = true;
            ApplyChaos();
            return;
        }
        if (context.Zone == FullMoon.Zone)
        {
            hotLoadedFromServer = true;
            ApplyFullMoon();
            return;
        }

        bool any = false;
        foreach (int id in ids) if (id != 0) { any = true; break; }
        if (!any)
        {
            if (!hotLoadedFromServer) Save();
            return;
        }

        System.Array.Clear(Slots, 0, HotTotal);
        bool dropped = ids.Length == HotbarLayout.LegacyTotal;
        var slots = HotbarLayout.Normalize(ids);
        for (int i = 0; i < HotTotal; i++)
        {
            if (slots[i] != 0 && !SkillData.IsSkill(slots[i]) && ItemData.Get(slots[i]) == null)
            {
                dropped = true;
                continue;
            }
            Slots[i] = slots[i];
        }
        if (HotbarAssign.ClearRefused(Slots, Keeps)) dropped = true;
        hotLoadedFromServer = true;
        Refresh();
        if (dropped) Save();
        else WriteLocal();
        WarmFxWhenLoaded();
    }

    private const float FxWarmAhead = 4f;
    private readonly HashSet<string> warmedFx = new();

    private List<string> UnwarmedFx()
    {
        var names = new List<string>();
        foreach (int id in Slots)
        {
            if (id == 0 || !SkillData.IsSkill(id) || SkillData.Get(id) is not { } skill) continue;
            foreach (var name in new[] { skill.SelfFx1, skill.SelfFx2, skill.FlyingFx, skill.TargetFx })
                if (!string.IsNullOrEmpty(name) && Fx.Has(name) && warmedFx.Add(name)) names.Add(name);
        }
        return names;
    }

    internal Node3D? WarmFxInView()
    {
        var names = UnwarmedFx();
        if (names.Count == 0 || context.Camera is not { } camera || !GodotObject.IsInstanceValid(camera)) return null;
        var warm = new Node3D { Name = "FxWarmHotbar", Position = new Vector3(0f, 0f, -FxWarmAhead) };
        camera.AddChild(warm);
        foreach (var name in names) Fx.Spawn(name, warm, Vector3.Zero);
        GD.Print($"[load] warming {names.Count} hotbar effects");
        return warm;
    }

    private void WarmFxWhenLoaded()
    {
        if (!context.Ready || context.Loading) return;
        var names = UnwarmedFx();
        if (names.Count == 0) return;
        var warm = new Node3D { Name = "FxWarmHotbar", Visible = false };
        context.Root.AddChild(warm);
        foreach (var name in names) Fx.Spawn(name, warm, Vector3.Zero);
        warm.QueueFree();
    }

    private string LocalPath()
    {
        string key = $"{Config.ServerHost}_{Config.GamePort}_{Game.LoginServer.Account}_{self.MyCharId}";
        foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
            key = key.Replace(bad, '_');
        return $"user://hotbar_{key}.json";
    }

    private void WriteLocal()
    {
        if (InChaosZone || context.Zone == FullMoon.Zone) return;
        var arr = new Godot.Collections.Array();
        foreach (int id in Slots) arr.Add(id);
        using var f = Godot.FileAccess.Open(LocalPath(), Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(arr));
    }

    private bool LoadLocal()
    {
        using var f = Godot.FileAccess.Open(LocalPath(), Godot.FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Array) return false;
        var arr = parsed.AsGodotArray();
        var saved = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++) saved[i] = arr[i].AsInt32();
        var slots = HotbarLayout.Normalize(saved);
        HotbarAssign.ClearRefused(slots, Keeps);
        bool any = false;
        for (int i = 0; i < HotTotal; i++)
        {
            Slots[i] = slots[i];
            if (Slots[i] != 0) any = true;
        }
        return any;
    }

    internal bool InChaosZone => ChaosBattle.IsZone(context.Zone);

    private bool Assignable(int id) =>
        SkillData.Get(id) is not { } s
        || (ChaosBattle.IsZone(context.Zone) ? s.Tree == ChaosBattle.SkillTree : self.SkillLearned(s));

    internal bool ChaosBattleLocked => ChaosBattle.BattleLocked(context.Zone, progress.SelfAbnormal);

    private void ApplyChaos()
    {
        if (!InChaosZone) return;
        var kept = chaos.ChaosHotbar;
        System.Array.Clear(Slots, 0, HotTotal);
        bool any = false;
        if (kept != null)
            for (int i = 0; i < kept.Length && i < HotTotal; i++)
                if (ChaosBattle.KeptOnEntry(kept[i]))
                {
                    Slots[i] = kept[i];
                    any = true;
                }
        if (!any)
            for (int i = 0; i < ChaosBattle.DefaultBar.Length; i++)
                if (SkillData.Get(ChaosBattle.DefaultBar[i]) is { Tree: ChaosBattle.SkillTree })
                    Slots[i] = ChaosBattle.DefaultBar[i];
        HotPage = 0;
        chaos.KeepHotbar((int[])Slots.Clone());
        Refresh();
    }

    private const int FullMoonBarPages = 2;

    private void ApplyFullMoon()
    {
        if (context.Zone != FullMoon.Zone) return;
        var kept = bifrost.FullMoonHotbar;
        if (kept != null)
        {
            System.Array.Copy(kept, Slots, Math.Min(kept.Length, HotTotal));
            HotPage = 0;
            Refresh();
            return;
        }

        int slots = FullMoonBarPages * HotSlotsPerPage;
        bool any = false;
        for (int i = 0; i < slots && i < HotTotal; i++)
        {
            if (Slots[i] != 0 && SkillData.Get(Slots[i]) is { } skill)
            {
                if (skill.Tree != FullMoon.SkillTree)
                    Slots[i] = 0;
                else
                    any = true;
            }
        }
        if (!any)
        {
            for (int i = 0; i < FullMoon.FreeSkills.Length && i < slots && i < HotTotal; i++)
                Slots[i] = FullMoon.FreeSkills[i];
        }
        bifrost.KeepFullMoonHotbar((int[])Slots.Clone());
        HotPage = 0;
        Refresh();
    }

    internal void ScaleVitals(Control status)
    {
        status.Scale = Vector2.One * HudPlacement.VitalsScale;
        status.PivotOffset = new Vector2(context.Hud.VitalsSize.X * 0.5f, context.Hud.VitalsSize.Y);
    }

}
