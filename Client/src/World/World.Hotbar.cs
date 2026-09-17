using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LibreKO;

public partial class World
{
    private const int HotPages = 8;
    private const int HotSlotsPerPage = 8;
    private const int HotTotal = HotPages * HotSlotsPerPage;
    private const int HotSlotSize = 46;
    private const int HotPageBtnGap = 2;
    private const int HotPageLblHeight = 14;
    private const int HotPageBtnHeight = (HotSlotSize - HotPageBtnGap * 2 - HotPageLblHeight) / 2;

    private readonly int[] _hotbar = new int[HotTotal];
    private int _hotPage;
    private int _hotSelected = -1;
    private bool _hotLoadedFromServer;

    private Control _hotbarBox = null!;
    private readonly List<HotSlotCell> _hotCells = new();
    private Label _hotPageLbl = null!;

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

    private void BuildHotbar()
    {
        var layer = new CanvasLayer { Layer = 64 };
        AddChild(layer);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        layer.AddChild(col);
        _hotbarBox = col;

        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        row.AddThemeConstantOverride("separation", 6);
        col.AddChild(row);

        _hotPageLbl = HudStyle.Label(11, HorizontalAlignment.Center);
        _hotPageLbl.CustomMinimumSize = new Vector2(22, HotPageLblHeight);

        var pageBox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        pageBox.AddThemeConstantOverride("separation", HotPageBtnGap);
        pageBox.AddChild(MakeHotPageButton("▲", -1));
        pageBox.AddChild(_hotPageLbl);
        pageBox.AddChild(MakeHotPageButton("▼", 1));
        row.AddChild(pageBox);

        for (int i = 0; i < HotSlotsPerPage; i++)
        {
            var cell = new HotSlotCell(i)
            {
                OnActivate = ActivateHotSlot,
                OnSelect = SelectHotSlot,
                OnClear = ClearHotSlotInPage,
                OnDrop = DropOntoHotSlot,
                OnHover = HotSlotHover,
            };
            _hotCells.Add(cell);
            row.AddChild(cell);
        }

        HudAnchor.Pin(col, HudAnchor.Spot.BottomCenter);
    }

    // HudTheme's Button pads 5px top/bottom, so only a compact stylebox lets CustomMinimumSize govern.
    private Button MakeHotPageButton(string glyph, int delta)
    {
        var b = new Button
        {
            Text = glyph,
            CustomMinimumSize = new Vector2(22, HotPageBtnHeight),
            FocusMode = Control.FocusModeEnum.None,
            ClipContents = true,
        };
        b.AddThemeFontSizeOverride("font_size", 10);
        b.AddThemeStyleboxOverride("normal", HotPageBtnStyle(UiTheme.Glass, new Color(UiTheme.Edge, 0.85f)));
        b.AddThemeStyleboxOverride("hover", HotPageBtnStyle(UiTheme.GlassLight, new Color(UiTheme.Gold, 0.9f)));
        b.AddThemeStyleboxOverride("pressed", HotPageBtnStyle(new Color(0.05f, 0.05f, 0.06f, 0.97f), new Color(UiTheme.GoldDark, 0.95f)));
        b.Pressed += () => ChangeHotPage(delta);
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

    private void SelectHotSlot(int slotInPage)
    {
        int abs = _hotPage * HotSlotsPerPage + slotInPage;
        if (abs < 0 || abs >= HotTotal) return;
        _hotSelected = _hotSelected == abs || _hotbar[abs] == 0 ? -1 : abs;
        RefreshHotbar();
    }

    private bool CastSelectedHotSlot() => FireHotSlot(_hotSelected);

    private void HotSlotHover(int slotInPage, bool entered)
    {
        if (!entered)
        {
            HideItemTooltip();
            return;
        }
        int abs = _hotPage * HotSlotsPerPage + slotInPage;
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

    private void ClearHotSlotInPage(int slotInPage) => SetHotSlot(_hotPage * HotSlotsPerPage + slotInPage, 0);

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
    }

    private void UpdateHotbarReady(double now)
    {
        if (_hotbarBox == null) return;

        for (int i = 0; i < _hotCells.Count; i++)
        {
            var cell = _hotCells[i];
            if (!GodotObject.IsInstanceValid(cell)) continue;
            int id = _hotbar[_hotPage * HotSlotsPerPage + i];
            if (id == 0) { cell.SetDim(false); cell.SetCooldown(0f); cell.SetCount(-1, true); continue; }

            var s = SkillData.Get(id);
            if (s == null && ItemData.Get(id) is { Effect1: not 0 } def) s = SkillData.Get(def.Effect1);

            float cd = s == null ? 0f : SkillCooldown(s, now);
            cell.SetCooldown(cd);
            if (i < TouchControls.ActionSlots) _touchActions?.SetCooldown(i, cd);
            cell.SetDim(s != null && cd <= 0f && (!SkillReady(s, now) || !SkillRequirementMet(s)));

            int needId = SkillData.IsSkill(id) ? s?.UseItem ?? 0 : id;
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
        bool dropped = false;
        for (int i = 0; i < ids.Length && i < HotTotal; i++)
        {
            if (ids[i] != 0 && !SkillData.IsSkill(ids[i]) && ItemData.Get(ids[i]) == null)
            {
                dropped = true;
                continue;
            }
            _hotbar[i] = ids[i];
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
        bool any = false;
        for (int i = 0; i < arr.Count && i < HotTotal; i++)
        {
            _hotbar[i] = arr[i].AsInt32();
            if (_hotbar[i] != 0) any = true;
        }
        return any;
    }

    private partial class HotSlotCell : VBoxContainer
    {
        public readonly int SlotInPage;
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

        public HotSlotCell(int slotInPage)
        {
            SlotInPage = slotInPage;
            MouseFilter = MouseFilterEnum.Stop;
            AddThemeConstantOverride("separation", 1);

            var key = HudStyle.Label(10, HorizontalAlignment.Center);
            key.Text = (slotInPage + 1).ToString(CultureInfo.InvariantCulture);
            key.AddThemeColorOverride("font_color", KeyColor);
            key.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(key);

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

            MouseEntered += () => OnHover?.Invoke(SlotInPage, true);
            MouseExited += () => OnHover?.Invoke(SlotInPage, false);
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
            if (_id == 0) return default;
            _pressed = false;
            _dragging = true;
            var preview = new PanelContainer { CustomMinimumSize = new Vector2(48, 40) };
            var icon = SkillData.IsSkill(_id) ? SkillData.Icon(_id) : ItemData.Icon(_id);
            if (icon != null)
                preview.AddChild(new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            else { var lbl = HudStyle.Label(11, HorizontalAlignment.Center); lbl.Text = _name.Text; preview.AddChild(lbl); }
            SetDragPreview(preview);
            // "barFrom" = this bar cell's page-relative slot; the world resolves it against the active page.
            return new Godot.Collections.Dictionary { { "id", _id }, { "barFrom", SlotInPage } };
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data) =>
            data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("id");

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            var d = data.AsGodotDictionary();
            int id = d["id"].AsInt32();
            int fromSlotInPage = d.ContainsKey("barFrom") ? d["barFrom"].AsInt32() : -1;
            OnDrop?.Invoke(SlotInPage, id, fromSlotInPage);
        }

        public override void _Notification(int what)
        {
            switch ((long)what)
            {
                case NotificationDragEnd:
                    if (!_dragging) return;
                    _dragging = false;
                    if (!IsDragSuccessful()) OnClear?.Invoke(SlotInPage);
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
                if (mb.Pressed) OnSelect?.Invoke(SlotInPage);
                AcceptEvent();
                return;
            }
            if (mb.ButtonIndex != MouseButton.Left) return;
            if (mb.Pressed) _pressed = true;
            else if (_pressed) { _pressed = false; OnActivate?.Invoke(SlotInPage); }
        }
    }
}
