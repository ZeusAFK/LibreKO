using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const double GenieScrollPollInterval = 1;
    private const double GenieScrollAttemptInterval = 2;
    private const double GenieScrollRetryInterval = 10;
    private const int GenieScrollSlots = 4;
    private readonly int[] _genieScrollItems = new int[GenieScrollSlots];
    private readonly Button[] _genieScrollButtons = new Button[GenieScrollSlots];
    private readonly Dictionary<int, double> _genieScrollRetryAt = new();
    private CheckButton _genieAutoScrolls = null!;
    private PopupMenu _genieScrollPicker = null!;
    private Label _genieScrollStatus = null!;
    private int _genieScrollEditing;
    private double _genieNextScrollAt;

    private void BuildGenieScrollOptions(VBoxContainer parent)
    {
        parent.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Automatic Scrolls")));
        _genieAutoScrolls = new CheckButton { Text = Localization.Loc.Tr("Use Selected Scrolls") };
        parent.AddChild(_genieAutoScrolls);
        var row = new HBoxContainer();
        parent.AddChild(row);
        for (int i = 0; i < GenieScrollSlots; i++)
        {
            int slot = i;
            var button = new Button { Text = "+", ExpandIcon = true,
                CustomMinimumSize = new Vector2(48, 48), FocusMode = Control.FocusModeEnum.None };
            button.AddThemeConstantOverride("icon_max_width", 36);
            button.Pressed += () => OpenGenieScrollPicker(slot);
            row.AddChild(button);
            _genieScrollButtons[i] = button;
        }
        parent.AddChild(UiTheme.Text(Localization.Loc.Tr("Select inventory buff scrolls. Reapplies only after the buff expires."), 11, UiTheme.TextLo));
        _genieScrollStatus = UiTheme.Text(Localization.Loc.Tr("Scroll automation is off."), 11, UiTheme.TextLo);
        parent.AddChild(_genieScrollStatus);
        _genieScrollPicker = new PopupMenu();
        _genieLayer.AddChild(_genieScrollPicker);
        _genieScrollPicker.IdPressed += id =>
        {
            _genieScrollItems[_genieScrollEditing] = (int)id;
            _genieScrollRetryAt.Clear();
            RefreshGenieScrolls();
        };
        _genieAutoScrolls.Toggled += _ => RefreshGenieScrolls();
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

    private void OpenGenieScrollPicker(int slot)
    {
        _genieScrollEditing = slot;
        _genieScrollPicker.Clear();
        _genieScrollPicker.AddItem(Localization.Loc.Tr("Clear Slot"), 0);
        var added = new HashSet<int>();
        int choices = 0;
        for (int i = GridStart; i < Inv.Length; i++)
        {
            int id = Inv[i].ItemId;
            if (id <= 0 || !added.Add(id) || GenieScrollSkill(id) == null || CountInBackpack(id) <= 0) continue;
            choices++;
            _genieScrollPicker.AddIconItem(ItemData.Icon(id), $"{ItemData.DisplayName(id)} ({CountInBackpack(id)})", id);
        }
        if (choices == 0) _genieScrollStatus.Text = Localization.Loc.Tr("No scrolls found in your inventory.");
        _genieScrollPicker.Position = (Vector2I)_genieScrollButtons[slot].GetGlobalRect().End;
        _genieScrollPicker.Popup();
    }

    private void RefreshGenieScrolls()
    {
        for (int i = 0; i < GenieScrollSlots; i++)
        {
            int id = _genieScrollItems[i];
            _genieScrollButtons[i].Icon = id > 0 ? ItemData.Icon(id) : null;
            _genieScrollButtons[i].Text = id > 0 ? "" : "+";
            _genieScrollButtons[i].TooltipText = id > 0
                ? $"{ItemData.DisplayName(id)} — {CountInBackpack(id)} {Localization.Loc.Tr("remaining")}"
                : Localization.Loc.Tr("Select a buff scroll from your inventory");
        }
        _genieScrollStatus.Text = _genieAutoScrolls.ButtonPressed
            ? Localization.Loc.Tr("Selected scrolls will be used while Genie is running.")
            : Localization.Loc.Tr("Scroll automation is off.");
    }

    private void SaveGenieScrollSettings(ConfigFile config)
    {
        config.SetValue("genie", "auto_scrolls", _genieAutoScrolls.ButtonPressed);
        config.SetValue("genie", "scroll_items", _genieScrollItems);
    }

    private void LoadGenieScrollSettings(ConfigFile config)
    {
        var items = config.GetValue("genie", "scroll_items", Array.Empty<int>()).AsInt32Array();
        for (int i = 0; i < GenieScrollSlots; i++) _genieScrollItems[i] = i < items.Length ? items[i] : 0;
        _genieAutoScrolls.ButtonPressed = config.GetValue("genie", "auto_scrolls", false).AsBool();
        RefreshGenieScrolls();
    }

    private bool GenieHasActiveBuff(SkillData.Skill skill, double now)
    {
        foreach (var pending in _pendingCasts)
        {
            var casting = SkillData.Get(pending.SkillId);
            if (pending.TargetId == _myId && (pending.SkillId == skill.Id
                || (skill.BuffType > 0 && casting?.BuffType == skill.BuffType))) return true;
        }
        foreach (var (id, end) in Net.I.BuffEnds)
        {
            if (end <= now) continue;
            var active = SkillData.Get(id);
            if (id == skill.Id || (skill.BuffType > 0 && active?.BuffType == skill.BuffType)) return true;
        }
        return false;
    }

    private bool GenieScrollTick(double now)
    {
        if (!_genieAutoScrolls.ButtonPressed || now < _genieNextScrollAt) return false;
        _genieNextScrollAt = now + GenieScrollPollInterval;
        for (int i = 0; i < GenieScrollSlots; i++)
        {
            int itemId = _genieScrollItems[i];
            var skill = GenieScrollSkill(itemId);
            if (skill == null || GenieHasActiveBuff(skill, now)) continue;
            if (CountInBackpack(itemId) <= 0)
            { _genieScrollStatus.Text = $"{Localization.Loc.Tr("Out of scrolls:")} {ItemData.DisplayName(itemId)}"; continue; }
            if (_genieScrollRetryAt.TryGetValue(skill.Id, out double retry) && retry > now) continue;
            if (!SkillRequirementMet(skill) || !SkillData.IsGranted(skill.Tree, SelfTransformModel())
                || !SkillReady(skill, now)) continue;
            var item = ItemData.Get(itemId);
            if (item == null || !ItemUseAllowed(item, itemId, out _) || !CanCastWithGear(skill)) continue;
            // One item attempt at a time. Server replies, not an optimistic local timer,
            // determine whether a buff is active. Failed attempts back off for 10 seconds.
            _genieScrollRetryAt[skill.Id] = now + GenieScrollRetryInterval;
            _genieNextScrollAt = now + GenieScrollAttemptInterval;
            int selected = _selectedId;
            _selectedId = _myId;
            try { UseHotItem(itemId); }
            finally { _selectedId = selected; }
            _genieScrollStatus.Text = $"{Localization.Loc.Tr("Using")} {ItemData.DisplayName(itemId)}…";
            return true;
        }
        return false;
    }
}
