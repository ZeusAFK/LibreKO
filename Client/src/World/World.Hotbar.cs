using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LibreKO;

public partial class World
{
    private const int HotPages = HotbarLayout.Pages;
    private const int HotSlotsPerPage = HotbarLayout.SlotsPerPage;
    private const int HotTotal = HotbarLayout.Total;
    private const int HotToolSize = 21;
    private const int HotBarGap = 4;
    private const int HotVerticalGap = 3;
    private const int HotGripThickness = 10;
    private const int HotPageArrowWidth = 11;
    private const int HotPageArrowGap = 1;
    private const string HotbarLayoutId = "hotbar";
    private const string HotbarVerticalLayoutId = "hotbar_vertical";
    private const int HotSlotSize = 46;
    private const int HotPageBtnGap = 2;
    private const int HotPageLblHeight = 14;
    private const int HotPageBtnHeight = (HotSlotSize - HotPageBtnGap * 2 - HotPageLblHeight) / 2;

    private readonly int[] _hotbar = new int[HotTotal];
    private int _hotPage;
    private int _hotSelected = -1;
    private bool _hotLoadedFromServer;

    private CanvasLayer _hotbarLayer = null!;
    private Control _hotbarBox = null!;
    private readonly List<HotSlotCell> _hotCells = new();
    private Label _hotPageLbl = null!;
    private readonly List<HotStrip> _extraBars = new();

    private sealed class HotStrip
    {
        public int Extra;
        public int Page;
        public Label PageLbl = null!;
        public readonly List<HotSlotCell> Cells = new();
    }

    private void HotbarInit()
    {
        BuildHotbar();
        Net.I.SkillDataEvent += OnSkillData;
        Net.I.SkillBarClearEvent += OnSkillBarClear;

        LoadLocalHotbar();
        RefreshHotbar();

        Net.I.SendSkillDataLoad();
    }

    private void HotbarDispose()
    {
        Net.I.SkillDataEvent -= OnSkillData;
        Net.I.SkillBarClearEvent -= OnSkillBarClear;
    }

    private void BuildHotbar() => BuildHotbar(Config.HotbarVertical, Config.HotbarExtraBars);

    private void BuildHotbar(bool vertical, int extraBars)
    {
        _hotbarLayer = new CanvasLayer { Layer = 64 };
        AddChild(_hotbarLayer);
        PluginHudSeam(_hotbarLayer, LibreKO.Plugins.HudPart.Hotbar);
        LayoutHotbars(vertical, extraBars);
    }

    private void LayoutHotbars() => LayoutHotbars(Config.HotbarVertical, Config.HotbarExtraBars);

    private void LayoutHotbars(bool vertical, int extraBars)
    {
        bool hidden = false;
        if (_hotbarBox != null && GodotObject.IsInstanceValid(_hotbarBox))
        {
            hidden = !_hotbarBox.Visible;
            _hotbarBox.QueueFree();
        }
        _hotCells.Clear();
        _extraBars.Clear();

        var stack = new BoxContainer { Vertical = !vertical, Visible = !hidden };
        stack.AddThemeConstantOverride("separation", HotBarGap);
        _hotbarLayer.AddChild(stack);
        _hotbarBox = stack;

        var extras = new List<Control>();
        for (int extra = 0; extra < extraBars; extra++)
        {
            var strip = new HotStrip { Extra = extra, Page = Config.HotbarExtraPage(extra) };
            _extraBars.Add(strip);
            extras.Add(BuildHotStrip(strip, vertical, extraBars));
        }
        if (!vertical) foreach (var strip in extras) stack.AddChild(strip);
        var grip = new HotGrip
        {
            Vertical = vertical,
            TooltipText = "Drag to move the skill bar",
            CustomMinimumSize = HotGripSize(vertical),
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        var main = BuildHotStrip(null, vertical, extraBars, grip);
        stack.AddChild(main);
        if (vertical) foreach (var strip in extras) stack.AddChild(strip);

        HudLayout.Attach(stack, HotbarLayoutIdFor(vertical), grip,
            () => HotbarDefaultPosition(stack, main));
    }

    private static Vector2 HotGripSize(bool vertical) =>
        vertical ? new Vector2(HotSlotSize, HotGripThickness) : new Vector2(HotGripThickness, HotSlotSize);

    private static string HotbarLayoutIdFor(bool vertical) => vertical ? HotbarVerticalLayoutId : HotbarLayoutId;

    private Vector2 HotbarDefaultPosition(Control stack, Control main)
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        return new Vector2((viewport.X - main.GetCombinedMinimumSize().X) * 0.5f,
            viewport.Y - stack.GetCombinedMinimumSize().Y);
    }

    private void KeepMainBarInPlace(Vector2 sizeBefore)
    {
        string id = HotbarLayoutIdFor(Config.HotbarVertical);
        if (Config.HotbarVertical || !GodotObject.IsInstanceValid(_hotbarBox) || !Config.HasWindowPos(id)) return;

        var grown = _hotbarBox.GetCombinedMinimumSize() - sizeBefore;
        var pos = Config.GetWindowPos(id, _hotbarBox.Position) - new Vector2(0f, grown.Y);
        var room = GetViewport().GetVisibleRect().Size - _hotbarBox.GetCombinedMinimumSize();
        pos = new Vector2(Mathf.Clamp(pos.X, 0f, Mathf.Max(0f, room.X)), Mathf.Clamp(pos.Y, 0f, Mathf.Max(0f, room.Y)));
        _hotbarBox.Position = pos;
        Config.SaveWindowPos(id, pos);
    }

    private Control BuildHotStrip(HotStrip? strip, bool vertical, int extraBars, Control? grip = null)
    {
        var row = new BoxContainer
        {
            Vertical = vertical,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        row.AddThemeConstantOverride("separation", vertical ? HotVerticalGap : 6);

        var pageLbl = HudStyle.Label(11, HorizontalAlignment.Center);
        pageLbl.CustomMinimumSize = new Vector2(22, HotPageLblHeight);
        if (strip == null) _hotPageLbl = pageLbl;
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

        var cells = strip == null ? _hotCells : strip.Cells;
        for (int i = 0; i < HotSlotsPerPage; i++)
        {
            var cell = new HotSlotCell(i, showKey: strip == null, keyInside: vertical)
            {
                PageOf = strip == null ? () => _hotPage : () => strip.Page,
                OnActivate = abs => FireHotSlot(abs),
                OnSelect = SelectHotAbs,
                OnClear = ClearHotAbs,
                OnDrop = DropOntoHotAbs,
                OnHover = HotAbsHover,
            };
            cells.Add(cell);
            row.AddChild(cell);
        }

        if (strip == null) row.AddChild(BuildHotTools(vertical, extraBars));
        if (grip != null) row.AddChild(grip);
        return row;
    }

    private Control BuildHotTools(bool vertical, int extraBars)
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
        lockBtn.Pressed += () => ApplyHotbarLayout(!Config.HotbarLocked, vertical, extraBars);
        grid.AddChild(lockBtn);

        var turn = MakeHotTool(HotToolGlyph.Turn, vertical ? "Lay the skill bar flat" : "Stand the skill bar up");
        turn.Pressed += () => ApplyHotbarLayout(Config.HotbarLocked, !vertical, extraBars);
        grid.AddChild(turn);

        var add = MakeHotTool(HotToolGlyph.Add, "Add a skill bar");
        add.Disabled = extraBars >= HotbarLayout.MaxBars - 1;
        add.Pressed += () => ApplyHotbarLayout(Config.HotbarLocked, vertical, extraBars + 1);
        grid.AddChild(add);

        var remove = MakeHotTool(HotToolGlyph.Remove, "Remove a skill bar");
        remove.Disabled = extraBars <= 0;
        remove.Pressed += () => ApplyHotbarLayout(Config.HotbarLocked, vertical, extraBars - 1);
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

    private void ApplyHotbarLayout(bool locked, bool vertical, int extraBars)
    {
        bool regrow = vertical == Config.HotbarVertical && extraBars != Config.HotbarExtraBars;
        var sizeBefore = GodotObject.IsInstanceValid(_hotbarBox) ? _hotbarBox.GetCombinedMinimumSize() : Vector2.Zero;
        Config.SetHotbarLayout(locked, vertical, extraBars);
        LayoutHotbars();
        RefreshHotbar();
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

    private void ChangeHotPage(int delta)
    {
        int pages = ReachablePages;
        _hotPage = ((_hotPage + delta) % pages + pages) % pages;
        RefreshHotbar();
    }

    private void ChangeStripPage(HotStrip strip, int delta)
    {
        strip.Page = ((strip.Page + delta) % HotPages + HotPages) % HotPages;
        Config.SetHotbarExtraPage(strip.Extra, strip.Page);
        RefreshHotbar();
    }

    private void SetHotPage(int page)
    {
        _hotPage = Mathf.Clamp(page, 0, ReachablePages - 1);
        RefreshHotbar();
    }

    private void ActivateHotSlot(int slotInPage) => FireHotSlot(_hotPage * HotSlotsPerPage + slotInPage);

    private bool FireHotSlot(int abs)
    {
        if (abs < 0 || abs >= HotTotal || _selfDead) return false;
        int id = _hotbar[abs];
        if (id == 0) return false;
        if (SkillData.IsSkill(id)) CastSkill(id);
        else UseHotItem(id);
        return true;
    }

    private void SelectHotAbs(int abs)
    {
        if (abs < 0 || abs >= HotTotal) return;
        _hotSelected = _hotSelected == abs || _hotbar[abs] == 0 ? -1 : abs;
        RefreshHotbar();
    }

    private bool CastSelectedHotSlot() => FireHotSlot(_hotSelected);

    private void HotAbsHover(int abs, bool entered)
    {
        if (!entered)
        {
            HideItemTooltip();
            return;
        }
        if (abs < 0 || abs >= HotTotal) return;
        int id = _hotbar[abs];
        if (id == 0 || SkillData.IsSkill(id) || ItemData.Get(id) == null) return;

        for (int slot = GridStart; slot < Inv.Length; slot++)
            if (Inv[slot].ItemId == id && !Inv[slot].IsEmpty)
            {
                ShowItemTooltip(slot, Inv[slot]);
                return;
            }
        ShowItemTooltip(-1, TooltipItem(id));
    }

    private bool UseHotItem(int itemId)
    {
        var def = ItemData.Get(itemId);
        if (def == null) return false;
        if (def.Effect1 != 0 && SkillData.IsSkill(def.Effect1))
        {
            if (!ItemUseAllowed(def, itemId, out string problem))
            {
                CombatNotice(problem);
                return true;
            }
            CastSkill(def.Effect1);
            return true;
        }
        if (ItemData.EquipSlotFor(def) >= 0)
        {
            for (int abs = GridStart; abs < Inv.Length; abs++)
                if (Inv[abs].ItemId == itemId) { InventoryActivate(abs); return true; }
        }
        // Effect1 naming a magic row the bake never produced is what made quest potions inert.
        if (def.Effect1 != 0)
        {
            CombatNotice($"{ItemData.DisplayName(itemId)} has no usable effect.");
            return true;
        }
        return false;
    }

    // Mirrors the server's MagicItemUsageService.CanUseItem so a refused consumable says why.
    private bool ItemUseAllowed(ItemData.Item def, int itemId, out string problem)
    {
        problem = "";
        string name = ItemData.DisplayName(itemId);
        if (!HasItemInBackpack(itemId))
        {
            problem = $"You have no {name} left.";
            return false;
        }
        if (Sheet.Level > 0 && def.ReqLevel > 0 && Sheet.Level < def.ReqLevel)
        {
            problem = $"{name} needs level {def.ReqLevel}.";
            return false;
        }
        if (Sheet.Level > 0 && def.ReqLevelMax > 0 && Sheet.Level > def.ReqLevelMax)
        {
            problem = $"{name} can only be used up to level {def.ReqLevelMax}.";
            return false;
        }
        if (def.Class != 0 && _selfClass != def.Class && _selfClass / 100 != def.Class)
        {
            problem = $"Your class cannot use {name}.";
            return false;
        }
        return true;
    }

    private bool HasItemInBackpack(int itemId)
    {
        for (int abs = GridStart; abs < Inv.Length; abs++)
            if (Inv[abs].ItemId == itemId && !Inv[abs].IsEmpty) return true;
        return false;
    }

    private void ClearHotAbs(int abs)
    {
        if (!Config.HotbarLocked) SetHotSlot(abs, 0);
    }

    private void DropOntoHotAbs(int dest, int id, int fromAbs)
    {
        if (dest < 0 || dest >= HotTotal) return;
        if (fromAbs >= 0 && fromAbs < HotTotal)
        {
            if (Config.HotbarLocked) return;
            (_hotbar[fromAbs], _hotbar[dest]) = (_hotbar[dest], _hotbar[fromAbs]);
        }
        else
        {
            if (!SkillAssignable(id)) return;
            _hotbar[dest] = id;
        }
        RefreshHotbar();
        SaveHotbar();
    }

    private void DropOntoHotSlot(int slotInPage, int id, int fromSlotInPage)
    {
        int dest = _hotPage * HotSlotsPerPage + slotInPage;
        if (dest < 0 || dest >= HotTotal) return;
        if (fromSlotInPage >= 0)
        {
            int src = _hotPage * HotSlotsPerPage + fromSlotInPage;
            (_hotbar[src], _hotbar[dest]) = (_hotbar[dest], _hotbar[src]);
        }
        else
        {
            if (!SkillAssignable(id)) return;
            _hotbar[dest] = id;
        }
        RefreshHotbar();
        SaveHotbar();
    }

    private void AddToHotbar(int id)
    {
        if (!SkillAssignable(id)) return;
        for (int p = 0; p < HotPages; p++)
        {
            int page = (_hotPage + p) % HotPages;
            for (int i = 0; i < HotSlotsPerPage; i++)
            {
                int abs = page * HotSlotsPerPage + i;
                if (_hotbar[abs] != 0) continue;
                _hotbar[abs] = id;
                if (page != _hotPage) SetHotPage(page);
                else RefreshHotbar();
                SaveHotbar();
                return;
            }
        }
    }

    private void SetHotSlot(int abs, int id)
    {
        if (abs < 0 || abs >= HotTotal) return;
        _hotbar[abs] = id;
        RefreshHotbar();
        SaveHotbar();
    }

    private void ClearHotbar(bool notifyServer = true)
    {
        System.Array.Clear(_hotbar, 0, HotTotal);
        _hotSelected = -1;
        _hotPage = 0;
        RefreshHotbar();
        if (notifyServer) SaveHotbar();
        else WriteLocalHotbar();
    }

    private void OnSkillBarClear()
    {
        if (!IsInsideTree() || _hotbarBox == null || !GodotObject.IsInstanceValid(_hotbarBox)) return;
        _hotLoadedFromServer = true;
        ClearHotbar(notifyServer: false);
    }

    private void RefreshHotbar()
    {
        PluginNotifyHotbar();
        _touchActions?.Refresh(_hotPage);
        if (_hotbarBox == null || !GodotObject.IsInstanceValid(_hotbarBox)) return;
        HideItemTooltip();
        if (_hotSelected >= 0 && (_hotSelected >= HotTotal || _hotbar[_hotSelected] == 0)) _hotSelected = -1;
        int page = _hotPage * HotSlotsPerPage;
        for (int i = 0; i < _hotCells.Count; i++)
            if (GodotObject.IsInstanceValid(_hotCells[i]))
            {
                _hotCells[i].Set(_hotbar[page + i]);
                _hotCells[i].SetSelected(_hotSelected == page + i);
            }
        if (_hotPageLbl != null && GodotObject.IsInstanceValid(_hotPageLbl))
            _hotPageLbl.Text = $"{_hotPage + 1}/{HotPages}";

        foreach (var strip in _extraBars)
        {
            int first = strip.Page * HotSlotsPerPage;
            for (int i = 0; i < strip.Cells.Count; i++)
                if (GodotObject.IsInstanceValid(strip.Cells[i]))
                {
                    strip.Cells[i].Set(_hotbar[first + i]);
                    strip.Cells[i].SetSelected(_hotSelected == first + i);
                }
            if (GodotObject.IsInstanceValid(strip.PageLbl))
                strip.PageLbl.Text = $"{strip.Page + 1}/{HotPages}";
        }
    }

    private double _readyCueCheckedAt;
    private readonly List<int> _readyCued = new();

    private void CueReadySkills(double now)
    {
        double since = _readyCueCheckedAt;
        _readyCueCheckedAt = now;
        CooldownCue.Collect(_skillReady, since, now, _hotbar,
            id => SkillData.Get(id)?.RecastSeconds ?? 0f, _readyCued);
        if (_readyCued.Count == 0) return;

        Audio.PlayUiFile(Sfx.SkillReadyFile);
        FlashReady(_hotCells, _hotPage);
        foreach (var strip in _extraBars) FlashReady(strip.Cells, strip.Page);
    }

    private void FlashReady(List<HotSlotCell> cells, int page)
    {
        int first = page * HotSlotsPerPage;
        for (int i = 0; i < cells.Count; i++)
            if (GodotObject.IsInstanceValid(cells[i]) && _readyCued.Contains(_hotbar[first + i]))
                cells[i].Flash();
    }

    private void UpdateHotbarReady(double now)
    {
        if (_hotbarBox == null) return;

        UpdateStripReady(_hotCells, _hotPage, now, touch: true);
        foreach (var strip in _extraBars) UpdateStripReady(strip.Cells, strip.Page, now, touch: false);
    }

    private void UpdateStripReady(List<HotSlotCell> cells, int page, double now, bool touch)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!GodotObject.IsInstanceValid(cell)) continue;
            int id = _hotbar[page * HotSlotsPerPage + i];
            if (id == 0) { cell.SetDim(false); cell.SetCooldown(0f); cell.SetCount(-1, true); continue; }

            var s = SkillData.Get(id);
            if (s == null && ItemData.Get(id) is { Effect1: not 0 } def) s = SkillData.Get(def.Effect1);

            float cd = s == null ? 0f : SkillCooldown(s, now);
            cell.SetCooldown(cd);
            if (touch && i < TouchControls.ActionSlots) _touchActions?.SetCooldown(i, cd);
            cell.SetDim(s != null && cd <= 0f && (!SkillReady(s, now) || !SkillRequirementMet(s)));

            int needId = SkillData.IsSkill(id) ? s?.ConsumedItem ?? 0 : id;
            if (needId != 0 && ItemData.Get(needId) != null)
            {
                int need = s is { IsRanged: true } ? Mathf.Max(1, s.NeedArrow) : 1;
                int have = CountInBackpack(needId);
                cell.SetCount(have, have >= need);
            }
            else cell.SetCount(-1, true);
        }
    }

    private void SaveHotbar()
    {
        Net.I.SendSkillDataSave(_hotbar);
        WriteLocalHotbar();
    }

    private void OnSkillData(int[] ids)
    {
        if (!IsInsideTree() || _hotbarBox == null || !GodotObject.IsInstanceValid(_hotbarBox)) return;

        bool any = false;
        foreach (int id in ids) if (id != 0) { any = true; break; }
        if (!any)
        {
            if (!_hotLoadedFromServer) SaveHotbar();
            return;
        }

        System.Array.Clear(_hotbar, 0, HotTotal);
        bool dropped = ids.Length == HotbarLayout.LegacyTotal;
        var slots = HotbarLayout.Normalize(ids);
        for (int i = 0; i < HotTotal; i++)
        {
            if (slots[i] != 0 && !SkillData.IsSkill(slots[i]) && ItemData.Get(slots[i]) == null)
            {
                dropped = true;
                continue;
            }
            _hotbar[i] = slots[i];
        }
        _hotLoadedFromServer = true;
        RefreshHotbar();
        if (dropped) SaveHotbar();
        else WriteLocalHotbar();
    }

    private string LocalHotbarPath()
    {
        string key = $"{Config.ServerHost}_{Config.GamePort}_{LoginNet.I.Account}_{Net.I.MyCharId}";
        foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
            key = key.Replace(bad, '_');
        return $"user://hotbar_{key}.json";
    }

    private void WriteLocalHotbar()
    {
        var arr = new Godot.Collections.Array();
        foreach (int id in _hotbar) arr.Add(id);
        using var f = Godot.FileAccess.Open(LocalHotbarPath(), Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(Json.Stringify(arr));
    }

    private bool LoadLocalHotbar()
    {
        using var f = Godot.FileAccess.Open(LocalHotbarPath(), Godot.FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Array) return false;
        var arr = parsed.AsGodotArray();
        var saved = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++) saved[i] = arr[i].AsInt32();
        var slots = HotbarLayout.Normalize(saved);
        bool any = false;
        for (int i = 0; i < HotTotal; i++)
        {
            _hotbar[i] = slots[i];
            if (_hotbar[i] != 0) any = true;
        }
        return any;
    }

    private partial class HotSlotCell : VBoxContainer
    {
        public readonly int SlotInPage;
        public System.Func<int>? PageOf;
        public System.Action<int>? OnActivate;
        public System.Action<int>? OnSelect;
        public System.Action<int>? OnClear;
        public System.Action<int, int, int>? OnDrop;
        public System.Action<int, bool>? OnHover;
        private int _id;
        private readonly PanelContainer _slot;
        private readonly TextureRect _icon;
        private readonly Label _name;
        private readonly StyleBoxFlat _bg;
        private readonly ColorRect _cool;
        private readonly ShaderMaterial _coolMat;
        private readonly Label _count;
        private readonly UpgradeBadge _plus;
        private float _coolShown = -1f;
        private string _countText = "";
        private bool? _countOk;
        private bool _dim;
        private bool _selected;
        private bool _pressed;
        private bool _dragging;

        private int Abs => (PageOf?.Invoke() ?? 0) * HotSlotsPerPage + SlotInPage;

        public HotSlotCell(int slotInPage, bool showKey = true, bool keyInside = false)
        {
            SlotInPage = slotInPage;
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeConstantOverride("separation", 1);

            var key = HudStyle.Label(10, HorizontalAlignment.Center);
            key.Text = showKey ? HotbarLayout.KeyLabel(slotInPage) : "";
            key.AddThemeColorOverride("font_color", KeyColor);
            key.MouseFilter = MouseFilterEnum.Ignore;
            if (!keyInside) AddChild(key);

            _slot = new PanelContainer
            {
                CustomMinimumSize = new Vector2(HotSlotSize, HotSlotSize),
                MouseFilter = MouseFilterEnum.Ignore,
                ClipContents = true,
            };
            AddChild(_slot);

            _bg = new StyleBoxFlat { BgColor = EmptyColor, BorderColor = SlotBorder };
            _bg.SetBorderWidthAll(1);
            _slot.AddThemeStyleboxOverride("panel", _bg);

            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _icon.SetAnchorsPreset(LayoutPreset.FullRect);
            _slot.AddChild(_icon);

            _name = HudStyle.Label(11, HorizontalAlignment.Center);
            _name.MouseFilter = MouseFilterEnum.Ignore;
            _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _name.SetAnchorsPreset(LayoutPreset.FullRect);
            _name.VerticalAlignment = VerticalAlignment.Center;
            _slot.AddChild(_name);

            _coolMat = new ShaderMaterial { Shader = CooldownShader };
            _cool = new ColorRect { Material = _coolMat, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            _cool.SetAnchorsPreset(LayoutPreset.FullRect);
            _slot.AddChild(_cool);

            _count = HudStyle.Label(11, HorizontalAlignment.Right);
            _count.VerticalAlignment = VerticalAlignment.Bottom;
            _count.MouseFilter = MouseFilterEnum.Ignore;
            _count.SizeFlagsVertical = _count.SizeFlagsHorizontal = SizeFlags.Fill;
            _count.SetAnchorsPreset(LayoutPreset.FullRect);
            _count.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginRight = 4, ContentMarginBottom = 2 });
            _slot.AddChild(_count);

            _plus = UpgradeBadge.Attach(_slot);

            if (keyInside && showKey)
            {
                key.HorizontalAlignment = HorizontalAlignment.Left;
                key.VerticalAlignment = VerticalAlignment.Top;
                key.SizeFlagsVertical = key.SizeFlagsHorizontal = SizeFlags.Fill;
                key.AddThemeColorOverride("font_outline_color", Colors.Black);
                key.AddThemeConstantOverride("outline_size", 3);
                key.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginTop = 1 });
                _slot.AddChild(key);
            }

            MouseEntered += () => OnHover?.Invoke(Abs, true);
            MouseExited += () => OnHover?.Invoke(Abs, false);
        }

        private static readonly Color EmptyColor = new(0.07f, 0.08f, 0.10f, 0.28f);
        private static readonly Color FilledColor = new(0.07f, 0.08f, 0.10f, 0.92f);
        private static readonly Color SlotBorder = new(0.32f, 0.34f, 0.40f);
        private static readonly Color EmptyBorder = new(0.32f, 0.34f, 0.40f, 0.35f);
        private static readonly Color DimColor = new(0.5f, 0.5f, 0.5f);
        private static readonly Color ShortColor = new(1f, 0.42f, 0.36f);
        private static readonly Color KeyColor = new("6cff6c");

        public void SetDim(bool dim)
        {
            if (_dim == dim || !GodotObject.IsInstanceValid(_icon)) return;
            _dim = dim;
            _icon.Modulate = _name.Modulate = dim ? DimColor : Colors.White;
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected || !GodotObject.IsInstanceValid(_slot)) return;
            _selected = selected;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            _bg.BgColor = _id == 0 ? EmptyColor : FilledColor;
            _bg.BorderColor = _selected ? UiTheme.Good : _id == 0 ? EmptyBorder : SlotBorder;
            _bg.SetBorderWidthAll(_selected ? 2 : 1);
        }

        public void SetCooldown(float frac)
        {
            if (!GodotObject.IsInstanceValid(_cool)) return;
            bool on = frac > 0.001f;
            if (_cool.Visible != on) _cool.Visible = on;
            if (on && Mathf.Abs(frac - _coolShown) > 0.002f)
            {
                _coolMat.SetShaderParameter("remain", frac);
                _coolShown = frac;
            }
        }

        private static readonly Color FlashColor = new(1.9f, 1.9f, 1.6f);
        private const double FlashStep = 0.12;
        private Tween? _flash;

        public void Flash()
        {
            if (!GodotObject.IsInstanceValid(_slot)) return;
            _flash?.Kill();
            _slot.Modulate = FlashColor;
            _flash = CreateTween();
            _flash.TweenProperty(_slot, "modulate", Colors.White, FlashStep);
            _flash.TweenProperty(_slot, "modulate", FlashColor, FlashStep);
            _flash.TweenProperty(_slot, "modulate", Colors.White, FlashStep);
        }

        public void SetCount(int have, bool enough)
        {
            if (!GodotObject.IsInstanceValid(_count)) return;
            string txt = have < 0 ? "" : have.ToString(CultureInfo.InvariantCulture);
            if (txt != _countText) { _countText = txt; _count.Text = txt; }
            if (_countOk != enough)
            {
                _countOk = enough;
                _count.AddThemeColorOverride("font_color", enough ? Colors.White : ShortColor);
            }
        }

        private static Shader CooldownShader => Shaders.Get("cooldown");

        // Icon-first like retail KO: the real skillicon/itemicon fills the slot; the name only shows as a
        // fallback caption for the (~11% of) skills with no shipped icon. Tooltip always carries the name.
        public void Set(int id)
        {
            if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(_icon) || !GodotObject.IsInstanceValid(_name))
                return;
            _id = id;
            ApplyFrame();
            if (id == 0)
            {
                _icon.Texture = null; _name.Text = ""; TooltipText = "";
                _plus.Clear();
                return;
            }
            if (SkillData.Get(id) is { } s)
            {
                var icon = SkillData.Icon(s.Id);
                _icon.Texture = icon;
                _name.Text = icon == null ? s.Name : "";
                TooltipText = SkillTooltip(s);
                _plus.Clear();
            }
            else
            {
                _icon.Texture = ItemData.Icon(id);
                _name.Text = "";
                TooltipText = "";
                _plus.Set(id);
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (_id == 0 || Config.HotbarLocked) return default;
            _pressed = false;
            _dragging = true;
            var preview = new PanelContainer { CustomMinimumSize = new Vector2(48, 40) };
            var icon = SkillData.IsSkill(_id) ? SkillData.Icon(_id) : ItemData.Icon(_id);
            if (icon != null)
                preview.AddChild(new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            else { var lbl = HudStyle.Label(11, HorizontalAlignment.Center); lbl.Text = _name.Text; preview.AddChild(lbl); }
            SetDragPreview(preview);
            return new Godot.Collections.Dictionary { { "id", _id }, { "barAbs", Abs } };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("id");

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            int id = d["id"].AsInt32();
            int fromAbs = d.ContainsKey("barAbs") ? d["barAbs"].AsInt32() : -1;
            OnDrop?.Invoke(Abs, id, fromAbs);
        }

        public override void _Notification(int what)
        {
            switch ((long)what)
            {
                case NotificationDragEnd:
                    if (!_dragging) return;
                    _dragging = false;
                    if (!IsDragSuccessful()) OnClear?.Invoke(Abs);
                    return;
                case NotificationMouseExit:
                    _pressed = false;
                    return;
            }
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton mb) return;
            if (mb.ButtonIndex == MouseButton.Right)
            {
                if (mb.Pressed) OnSelect?.Invoke(Abs);
                AcceptEvent();
                return;
            }
            if (mb.ButtonIndex != MouseButton.Left) return;
            if (mb.Pressed) _pressed = true;
            else if (_pressed) { _pressed = false; OnActivate?.Invoke(Abs); }
        }
    }

    private enum HotToolGlyph { Locked, Unlocked, Turn, Add, Remove }

    private sealed partial class HotGrip : Control
    {
        public bool Vertical;
        private static readonly Color Dot = new(UiTheme.TextLo, 0.75f);
        private const float DotStep = 6f;
        private const float DotRadius = 1.2f;

        public override void _Draw()
        {
            var style = new StyleBoxFlat { BgColor = new Color(UiTheme.Glass, 0.6f) };
            style.SetCornerRadiusAll(3);
            DrawStyleBox(style, new Rect2(Vector2.Zero, Size));
            float along = Vertical ? Size.X : Size.Y;
            float across = (Vertical ? Size.Y : Size.X) * 0.5f;
            for (float t = DotStep; t < along - DotStep * 0.5f; t += DotStep)
                DrawCircle(Vertical ? new Vector2(t, across) : new Vector2(across, t), DotRadius, Dot);
        }
    }

    private sealed partial class HotToolButton : Button
    {
        public HotToolGlyph Glyph;

        public override void _Draw()
        {
            var tint = Disabled ? new Color(UiTheme.TextLo, 0.4f)
                : Glyph == HotToolGlyph.Locked ? UiTheme.Bad
                : Glyph == HotToolGlyph.Unlocked ? UiTheme.Good
                : UiTheme.GoldBright;
            float w = Size.X, h = Size.Y, cx = w * 0.5f, cy = h * 0.5f;
            switch (Glyph)
            {
                case HotToolGlyph.Locked:
                case HotToolGlyph.Unlocked:
                {
                    var body = new Rect2(w * 0.28f, h * 0.48f, w * 0.44f, h * 0.30f);
                    DrawRect(body, tint, Glyph == HotToolGlyph.Locked);
                    if (Glyph == HotToolGlyph.Unlocked) DrawRect(body, tint, false, 1f);
                    float r = w * 0.15f, arcCy = h * 0.40f;
                    float lift = Glyph == HotToolGlyph.Unlocked ? h * 0.08f : 0f;
                    DrawArc(new Vector2(cx, arcCy - lift), r, Mathf.Pi, Mathf.Tau, 12, tint, 1.5f);
                    DrawLine(new Vector2(cx - r, arcCy - lift), new Vector2(cx - r, body.Position.Y - lift), tint, 1.5f);
                    DrawLine(new Vector2(cx + r, arcCy), new Vector2(cx + r, body.Position.Y), tint, 1.5f);
                    break;
                }
                case HotToolGlyph.Turn:
                {
                    float r = w * 0.26f;
                    DrawArc(new Vector2(cx, cy), r, -Mathf.Pi * 0.9f, Mathf.Pi * 0.4f, 16, tint, 1.5f);
                    var tip = new Vector2(cx + r * Mathf.Cos(Mathf.Pi * 0.4f), cy + r * Mathf.Sin(Mathf.Pi * 0.4f));
                    DrawLine(tip, tip + new Vector2(w * 0.14f, 0f), tint, 1.5f);
                    DrawLine(tip, tip + new Vector2(0f, -h * 0.14f), tint, 1.5f);
                    break;
                }
                case HotToolGlyph.Add:
                    DrawLine(new Vector2(w * 0.28f, cy), new Vector2(w * 0.72f, cy), tint, 2f);
                    DrawLine(new Vector2(cx, h * 0.28f), new Vector2(cx, h * 0.72f), tint, 2f);
                    break;
                case HotToolGlyph.Remove:
                    DrawLine(new Vector2(w * 0.28f, cy), new Vector2(w * 0.72f, cy), tint, 2f);
                    break;
            }
        }
    }
}
