using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LibreKO;

public partial class World
{
    private const int GenieSkillGroupSlots = 8;
    private const int GenieSkillGroups = 3;
    private const int GenieSkillSlots = GenieSkillGroupSlots * GenieSkillGroups;
    private const int GenieModeCount = 8;
    private const double GenieStartReplyTimeout = 8;
    private const double GeniePollInterval = 0.3;
    private const float GenieRangeSlack = 2;
    private const double GenieLeaderTargetMemory = 8;
    private const double GenieSupportRetryInterval = 2;
    private const double GeniePotionUiInterval = 1;

    private readonly int[] _genieSkills = new int[GenieSkillSlots];
    private readonly Button[] _genieSlots = new Button[GenieSkillSlots];
    private readonly CheckButton[] _genieModes = new CheckButton[GenieModeCount];
    private readonly HashSet<string> _genieMonsters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Skill, int Target), double> _genieSupportAt = new();
    private ItemList _genieMonsterList = null!;
    private SpinBox _genieHp = null!, _genieMp = null!, _genieRange = null!;
    private CheckButton _genieAutoParty = null!;
    private LineEdit _geniePtCode = null!;
    private readonly GeniePartyCodeGate _geniePartyCodes = new();
    private int _geniePendingInviterId = -1;
    private string _geniePendingInviterName = "";
    private Label _genieRunLabel = null!;
    private Button _genieStart = null!;
    private PopupMenu _genieSkillPicker = null!;
    private int _genieEditingSlot, _genieAttackCursor, _genieLeaderTarget = -1, _genieLeaderId = -1;
    private double _geniePollAt, _genieDeadline, _genieLeaderSeenAt, _genieRequestAt;
    private bool _genieWasRunning, _genieMoving, _genieRequestPending, _genieWantsRunning;
    private Vector3 _genieOrigin;
    private string GenieSettingsPath => $"user://genie-{Net.I.MyCharId}.cfg";

    // Flags: attacks, self skills, party skills, basic attack, assist leader,
    // sequential combo, HP potion, MP potion. Extra options persist locally.
    private VBoxContainer BuildAdvancedGenie(VBoxContainer root)
    {
        Net.I.ResetGenieSystem();
        var tabs = new TabContainer();
        root.AddChild(tabs);
        var main = new VBoxContainer { Name = "Main", CustomMinimumSize = new Vector2(510, 0) };
        var misc = new VBoxContainer { Name = "Misc" };
        tabs.AddChild(main);
        tabs.AddChild(misc);
        main.AddThemeConstantOverride("separation", 8);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        main.AddChild(columns);
        string[] headings = { "Attack Skills", "Self Skills", "Party Skills" };
        for (int group = 0; group < GenieSkillGroups; group++)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            columns.AddChild(column);
            column.AddChild(UiTheme.SectionTitle(headings[group]));
            var grid = new GridContainer { Columns = 4 };
            column.AddChild(grid);
            for (int cell = 0; cell < GenieSkillGroupSlots; cell++)
            {
                int slot = group * GenieSkillGroupSlots + cell;
                var button = new Button { Text = "+", CustomMinimumSize = new Vector2(36, 36),
                    ExpandIcon = true, FocusMode = Control.FocusModeEnum.None };
                var border = new[] { new Color("a98632"), new Color("9348bc"), new Color("346675") }[group];
                var slotStyle = new StyleBoxFlat { BgColor = new Color("091719"), BorderColor = border };
                slotStyle.SetBorderWidthAll(1);
                button.AddThemeStyleboxOverride("normal", slotStyle);
                var hoverStyle = (StyleBoxFlat)slotStyle.Duplicate();
                hoverStyle.BgColor = new Color("234044");
                button.AddThemeStyleboxOverride("hover", hoverStyle);
                button.AddThemeConstantOverride("icon_max_width", 30);
                button.Pressed += () => OpenGenieSkillPicker(slot);
                grid.AddChild(button);
                _genieSlots[slot] = button;
            }
            GenieMode(column, group, headings[group].Replace("Skills", "Mode"), group < 2);
            GenieMode(column, group + 3, new[] { "R Attack", "Party Leader Target", "3 - 5 Combo" }[group], group == 0);
        }
        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 12);
        main.AddChild(lower);
        var potions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lower.AddChild(potions);
        potions.AddChild(UiTheme.SectionTitle("Potion Options"));
        GenieMode(potions, 6, "Use HP Potion", true);
        BuildGeniePotionSelector(potions, HealTarget.Hp);
        _genieHp = GeniePercent(potions, "HP Threshold (%)", 70, 1, 99);
        GenieMode(potions, 7, "Use MP Potion", true);
        BuildGeniePotionSelector(potions, HealTarget.Mp);
        _genieMp = GeniePercent(potions, "MP Threshold (%)", 35, 1, 99);
        var monsters = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lower.AddChild(monsters);
        monsters.AddChild(UiTheme.SectionTitle("Monster Attack List"));
        _genieMonsterList = new ItemList { CustomMinimumSize = new Vector2(245, 104) };
        monsters.AddChild(_genieMonsterList);
        var actions = new HBoxContainer();
        monsters.AddChild(actions);
        GenieButton(actions, "+ Add Target", () =>
        {
            if (_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var e) && e.IsMonster)
            { _genieMonsters.Add(e.Name); RefreshGenieMonsters(); }
            else Chat.Info("Select a monster first.");
        });
        GenieButton(actions, "Remove", () =>
        {
            foreach (int i in _genieMonsterList.GetSelectedItems()) _genieMonsters.Remove(_genieMonsterList.GetItemText(i));
            RefreshGenieMonsters();
        });
        main.AddChild(UiTheme.Text("Empty list: all monsters in range. Click a slot to select a skill.", 11, UiTheme.TextLo));
        _genieRunLabel = UiTheme.Text("Genie stopped.", 13, UiTheme.Gold);
        main.AddChild(_genieRunLabel);
        var footer = new HBoxContainer();
        main.AddChild(footer);
        _genieStart = GenieButton(footer, "Start", () =>
        {
            if (_genieRequestPending) return;
            if (Net.I.GenieRunning) { StopAdvancedGenie(); return; }
            if (_selfDead || MerchantBlocksMove() || !Net.I.Connected || Net.I.ReconnectBlocking) return;
            SaveAdvancedGenie();
            _genieWantsRunning = true;
            _genieRequestPending = true;
            _genieRequestAt = Now();
            _genieStart.Disabled = true;
            _genieRunLabel.Text = "Waiting for server response…";
            Net.I.SendGenieSystem(Net.GenieStart);
        });
        GenieButton(footer, "Save Settings", SaveAdvancedGenie);
        GenieButton(footer, "Use Spirit of Genie", () => Net.I.SendGenieSystem(Net.GenieUseSpiritPotion));
        misc.AddChild(UiTheme.SectionTitle("Genie Settings"));
        _genieRange = GeniePercent(misc, "Range from Start Position", 25, 3, 80);
        _genieAutoParty = new CheckButton { Text = "Auto Party" };
        misc.AddChild(_genieAutoParty);
        _geniePtCode = new LineEdit { PlaceholderText = "PT CODE — code to send in a private message", Secret = true, MaxLength = 64 };
        misc.AddChild(UiTheme.SectionTitle("PT CODE"));
        misc.AddChild(_geniePtCode);
        _geniePtCode.TextChanged += _ => ConfigureGeniePartyCode();
        _genieAutoParty.Toggled += _ => ConfigureGeniePartyCode();
        misc.AddChild(UiTheme.Text("Senders who PM the code can have their party invite accepted within 2 minutes.", 11, UiTheme.TextLo));
        misc.AddChild(UiTheme.Text("3 - 5 Combo: cycles through selected 3-arrow and 5-arrow skills.", 11, UiTheme.TextLo));
        misc.AddChild(UiTheme.Text("Leader Target uses the nearby party leader's most recent attack.", 11, UiTheme.TextLo));
        BuildGenieScrollOptions(misc);
        BuildGenieHammerOptions(misc);
        misc.AddChild(new HSeparator());
        _genieSkillPicker = new PopupMenu();
        _genieLayer.AddChild(_genieSkillPicker);
        _genieSkillPicker.IdPressed += id =>
        {
            _genieSkills[_genieEditingSlot] = (int)id;
            RefreshGenieSlots();
        };
        LoadAdvancedGenie();
        Net.I.GenieSystemState += OnGenieSystemState;
        Net.I.GenieOptionsReceived += OnGenieOptions;
        Net.I.ChatEvent += OnGeniePrivateMessage;
        Net.I.AttackEvent += GenieObserveAttack;
        Net.I.MagicEvent += GenieObserveMagic;
        return misc;
    }

    private static Button GenieButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void GenieMode(Node parent, int index, string text, bool enabled)
    {
        var button = new CheckButton { Text = text, ButtonPressed = enabled };
        button.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(button);
        _genieModes[index] = button;
    }

    private static SpinBox GeniePercent(Node parent, string title, int value, int min, int max)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        row.AddChild(UiTheme.Text(title, 12, UiTheme.TextLo));
        var input = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = value };
        row.AddChild(input);
        return input;
    }

    private void OpenGenieSkillPicker(int slot)
    {
        _genieEditingSlot = slot;
        _genieSkillPicker.Clear();
        _genieSkillPicker.AddItem("Clear Slot", 0);
        foreach (var s in SkillData.ForClass(_selfClass).Where(SkillRequirementMet).OrderBy(s => s.Level))
        {
            if (s.IsPotion || s.IsDeadFriend || s.IsBlink || s.IsGroundArea) continue;
            bool allowed = slot < GenieSkillGroupSlots ? s.IsEnemy : slot < GenieSkillGroupSlots * 2
                ? s.Moral is SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party or SkillTarget.PartyAll
                : s.IsFriendly;
            if (allowed) _genieSkillPicker.AddIconItem(SkillData.Icon(s.Id), s.Name, s.Id);
        }
        _genieSkillPicker.Position = (Vector2I)(_genieSlots[slot].GetGlobalRect().End);
        _genieSkillPicker.Popup();
    }

    private void RefreshGenieSlots()
    {
        for (int i = 0; i < GenieSkillSlots; i++)
        {
            var skill = SkillData.Get(_genieSkills[i]);
            _genieSlots[i].Icon = skill == null ? null : SkillData.Icon(skill.Id);
            _genieSlots[i].Text = skill == null ? "+" : _genieSlots[i].Icon == null ? (i % GenieSkillGroupSlots + 1).ToString() : "";
            _genieSlots[i].TooltipText = skill == null ? "Select Skill" : SkillTooltip(skill);
        }
    }

    private void RefreshGenieMonsters()
    {
        _genieMonsterList.Clear();
        foreach (string name in _genieMonsters.OrderBy(n => n)) _genieMonsterList.AddItem(name);
    }

    private byte[] GenieOptionBytes()
    {
        var bytes = new byte[Net.GenieOptionBytes];
        for (int i = 0; i < GenieModeCount; i++) if (_genieModes[i].ButtonPressed) bytes[0] |= (byte)(1 << i);
        bytes[1] = (byte)_genieHp.Value;
        bytes[2] = (byte)_genieMp.Value;
        bytes[3] = (byte)_genieRange.Value;
        for (int i = 0; i < GenieSkillSlots; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + i * 4, 4), _genieSkills[i]);
        return bytes;
    }

    private void OnGenieOptions(byte[] bytes)
    {
        // A new character's zero-filled options must not erase usable defaults.
        if (bytes.Length != Net.GenieOptionBytes || bytes[1] == 0 || _genieRequestPending || Net.I.GenieRunning) return;
        for (int i = 0; i < GenieModeCount; i++) _genieModes[i].ButtonPressed = (bytes[0] & (1 << i)) != 0;
        _genieHp.Value = bytes[1]; _genieMp.Value = bytes[2]; _genieRange.Value = bytes[3];
        for (int i = 0; i < GenieSkillSlots; i++)
            _genieSkills[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4 + i * 4, 4));
        RefreshGenieSlots();
    }

    private void SaveAdvancedGenie()
    {
        var config = new ConfigFile();
        config.SetValue("genie", "options", GenieOptionBytes());
        config.SetValue("genie", "monsters", _genieMonsters.ToArray());
        config.SetValue("genie", "auto_party", _genieAutoParty.ButtonPressed);
        config.SetValue("genie", "pt_code", _geniePtCode.Text.Trim());
        config.SetValue("genie", "hp_potion_item", _genieHpItem);
        config.SetValue("genie", "mp_potion_item", _genieMpItem);
        SaveGenieScrollSettings(config);
        config.SetValue("genie", "auto_hammer", _genieAutoHammer.ButtonPressed);
        config.SetValue("genie", "hammer_threshold", _genieHammerThreshold.Value);
        if (config.Save(GenieSettingsPath) != Error.Ok) Chat.Info("Could not save Genie settings to disk.");
        Net.I.SendGenieSystem(Net.GenieSaveOptions, GenieOptionBytes());
    }

    private void LoadAdvancedGenie()
    {
        var config = new ConfigFile();
        if (config.Load(GenieSettingsPath) == Error.Ok)
        {
            _genieHpItem = config.GetValue("genie", "hp_potion_item", 0).AsInt32();
            _genieMpItem = config.GetValue("genie", "mp_potion_item", 0).AsInt32();
            LoadGenieScrollSettings(config);
            _genieAutoHammer.ButtonPressed = config.GetValue("genie", "auto_hammer", false).AsBool();
            _genieHammerThreshold.Value = config.GetValue("genie", "hammer_threshold", 10).AsDouble();
            OnGenieOptions(config.GetValue("genie", "options", new byte[Net.GenieOptionBytes]).AsByteArray());
            foreach (string name in config.GetValue("genie", "monsters", Array.Empty<string>()).AsStringArray())
                _genieMonsters.Add(name);
            _genieAutoParty.ButtonPressed = config.GetValue("genie", "auto_party", false).AsBool();
            _geniePtCode.Text = config.GetValue("genie", "pt_code", "").AsString();
        }
        RefreshGenieSlots(); RefreshGenieMonsters();
        RefreshGeniePotionSelectors();
    }

    private void OnGenieSystemState(bool running, int minutes)
    {
        if (running && !_genieWantsRunning) { StopAdvancedGenie(); return; }
        _genieRequestPending = false;
        _genieStart.Disabled = false;
        if (running && !_genieWasRunning)
        {
            _genieOrigin = _self.GlobalPosition;
            _genieSupportAt.Clear();
            _genieScrollRetryAt.Clear();
            _genieNextScrollAt = 0;
            _geniePollAt = 0;
            _genieAttackCursor = 0;
        }
        if (!running && _genieWasRunning) HaltGenieActions();
        _genieWasRunning = running;
        if (!running) _genieWantsRunning = false;
        _genieDeadline = Now() + minutes * 60;
        _genieStart.Text = running ? "Stop" : "Start";
        _genieRunLabel.Text = running ? $"Genie active • {minutes} minutes" :
            minutes > 0 ? $"Genie stopped • {minutes} minutes" : "No time remaining. Spirit of Genie required.";
    }

    private void HaltGenieActions()
    {
        _geniePartyCodes.Clear();
        StopAutoAttack();
        if (_genieMoving) _hasMoveTarget = false;
        _genieMoving = false;
        foreach (int skill in _pendingCasts.Select(c => c.SkillId).Distinct().ToArray()) CancelSelfCast(skill);
        if (_castingSkillId != 0) CancelSelfCast(_castingSkillId);
    }

    private void StopAdvancedGenie()
    {
        HaltGenieActions();
        Net.I.ResetGenieSystem();
        _genieWasRunning = false;
        _genieWantsRunning = false;
        _genieRequestPending = false;
        _genieStart.Disabled = false;
        _genieStart.Text = "Start";
        _genieRunLabel.Text = "Genie stopped.";
        if (Net.I.Connected) Net.I.SendGenieSystem(Net.GenieStop);
    }

    private void ConfigureGeniePartyCode() => _geniePartyCodes.Configure(
        Net.I.GenieRunning && Net.I.Connected && !Net.I.ReconnectBlocking && !_selfDead
        && _genieAutoParty != null && _genieAutoParty.ButtonPressed,
        _geniePtCode?.Text ?? "");

    private bool GenieAcceptInvite(int inviterId, string name)
    {
        ConfigureGeniePartyCode();
        return _geniePartyCodes.TryAccept(inviterId, name, Now());
    }

    private void OnGeniePrivateMessage(ChatLine line)
    {
        ConfigureGeniePartyCode();
        if (!_geniePartyCodes.Receive(line.Type, line.CharId, line.Name, line.Message, _myId, Now())) return;
        // Also support an invitation arriving before the private message.
        if (_invitePending && GenieAcceptInvite(_geniePendingInviterId, _geniePendingInviterName))
        {
            AnswerInvite(true);
            _inviteAskDialog.Hide();
        }
    }

    private void GenieObserveAttack(int type, int result, int attacker, int target) => GenieObserveLeader(attacker, target);
    private void GenieObserveMagic(int stage, int skill, int caster, int target, short[] data)
    {
        if (stage is 1 or 3) GenieObserveLeader(caster, target);
        if (stage == 3 && caster == _myId && Net.I.GenieRunning
            && _genieSkills.Skip(GenieSkillGroupSlots).Contains(skill) && SkillData.Get(skill) is { } support)
            _genieSupportAt[(skill, target)] = Now() + Math.Max(GenieSupportRetryInterval, support.Duration > 0 ? support.Duration - GenieSupportRetryInterval : support.RecastSeconds);
    }
    private void GenieObserveLeader(int source, int target)
    {
        if (InParty && !AmLeader && PartyMembers[0].CharId == source)
        { _genieLeaderTarget = target; _genieLeaderId = source; _genieLeaderSeenAt = Now(); }
    }

    private void AdvancedGenieDispose()
    {
        if (Net.I.GenieRunning || _genieRequestPending) StopAdvancedGenie();
        Net.I.ResetGenieSystem();
        Net.I.GenieHammerResult -= OnGenieHammerResult;
        Net.I.GenieSystemState -= OnGenieSystemState;
        Net.I.GenieOptionsReceived -= OnGenieOptions;
        _geniePartyCodes.Clear();
        Net.I.ChatEvent -= OnGeniePrivateMessage;
        Net.I.AttackEvent -= GenieObserveAttack;
        Net.I.MagicEvent -= GenieObserveMagic;
    }

    private bool GenieValidMonster(int id) => _ents.TryGetValue(id, out var e)
        && e.IsMonster && e.Attackable && !e.Dead
        && e.Body.GlobalPosition.DistanceTo(_genieOrigin) <= (float)_genieRange.Value
        && (_genieMonsters.Count == 0 || _genieMonsters.Contains(e.Name));

    private void AdvancedGenieTick(double now)
    {
        if (_genieRequestPending && now - _genieRequestAt > GenieStartReplyTimeout) StopAdvancedGenie();
        if (!Net.I.GenieRunning)
        { if (_genieWasRunning) StopAdvancedGenie(); return; }
        if (_selfDead || !Net.I.Connected || Net.I.ReconnectBlocking || MerchantBlocksMove() || now >= _genieDeadline
            || _self.GlobalPosition.DistanceTo(_genieOrigin) > (float)_genieRange.Value + GenieRangeSlack)
        { StopAdvancedGenie(); return; }
        if (now < _geniePollAt) return;
        _geniePollAt = now + GeniePollInterval;
        GenieHammerTick(now);
        if (now >= _geniePotionUiAt)
        { _geniePotionUiAt = now + GeniePotionUiInterval; RefreshGeniePotionSelectors(); }
        if (_genieModes[6].ButtonPressed && Vitals.MaxHp > 0 && Vitals.Hp * 100.0 / Vitals.MaxHp <= _genieHp.Value)
            UseGeniePotion(HealTarget.Hp);
        if (_genieModes[7].ButtonPressed && Vitals.MaxMp > 0 && Vitals.Mp * 100.0 / Vitals.MaxMp <= _genieMp.Value)
            UseGeniePotion(HealTarget.Mp);
        if (SelfCasting(now)) return;
        if (GenieScrollTick(now)) return;
        if (_genieModes[1].ButtonPressed && GenieSupport(GenieSkillGroupSlots, _myId, Vitals.Hp, Vitals.MaxHp, now)) return;
        if (_genieModes[2].ButtonPressed)
            foreach (var member in PartyMembers)
                if (member.CharId != _myId && member.Hp > 0 && _ents.TryGetValue(member.CharId, out var ally)
                    && !ally.Dead && !ally.Attackable && GenieSupport(GenieSkillGroupSlots * 2, member.CharId, member.Hp, member.MaxHp, now)) return;
        if (!_genieModes[0].ButtonPressed)
        { StopAutoAttack(); if (_genieMoving) _hasMoveTarget = false; _genieMoving = false; return; }
        int target = _selectedId;
        if (_genieModes[4].ButtonPressed && InParty && !AmLeader)
            target = _genieLeaderId == PartyMembers[0].CharId && now - _genieLeaderSeenAt < GenieLeaderTargetMemory && GenieValidMonster(_genieLeaderTarget) ? _genieLeaderTarget : -1;
        else if (!GenieValidMonster(target))
            target = _ents.Where(pair => GenieValidMonster(pair.Key))
                .OrderBy(pair => pair.Value.Body.GlobalPosition.DistanceSquaredTo(_self.GlobalPosition))
                .Select(pair => pair.Key).DefaultIfEmpty(-1).First();
        if (target < 0)
        { StopAutoAttack(); if (_genieMoving) _hasMoveTarget = false; _genieMoving = false; return; }
        if (_selectedId != target) Select(target, _ents[target]);
        bool basic = _genieModes[3].ButtonPressed;
        bool basicInRange = basic && InBasicAttackRange(target);
        if (basicInRange && (!_autoAttack || _autoTargetId != target)) StartAutoAttack(target);
        if (!basicInRange && _autoAttack) StopAutoAttack();
        bool skillInRange = false;
        bool hasAttackSkill = false;
        for (int j = 0; j < GenieSkillGroupSlots; j++)
        {
            int index = _genieModes[5].ButtonPressed ? (_genieAttackCursor + j) % GenieSkillGroupSlots : j;
            var skill = SkillData.Get(_genieSkills[index]);
            if (skill == null || !skill.IsEnemy || !SkillRequirementMet(skill)) continue;
            if (_genieModes[5].ButtonPressed && !(skill.IsRanged && skill.NeedArrow is 3 or 5)) continue;
            hasAttackSkill = true;
            if (!InSkillRange(target, skill)) continue;
            skillInRange = true;
            if (!SkillReady(skill, now) || !CanCastWithGear(skill)) continue;
            _hasMoveTarget = false; _genieMoving = false;
            CastSkill(skill.Id);
            _genieAttackCursor = (index + 1) % GenieSkillGroupSlots;
            return;
        }
        // Use the normal movement path; never teleport or bypass server range checks.
        bool shouldMove = (basic || hasAttackSkill) && !skillInRange && !(basic && InBasicAttackRange(target));
        if (shouldMove)
        {
            _moveTarget = _ents[target].Body.GlobalPosition;
            _hasMoveTarget = true; _genieMoving = true;
        }
        else if (_genieMoving) { _hasMoveTarget = false; _genieMoving = false; }
    }

    private bool GenieSupport(int start, int target, int hp, int maxHp, double now)
    {
        for (int i = start; i < start + GenieSkillGroupSlots; i++)
        {
            var skill = SkillData.Get(_genieSkills[i]);
            if (skill == null || skill.IsEnemy || skill.IsDeadFriend || skill.IsGroundArea || skill.IsBlink
                || !SkillRequirementMet(skill) || !SkillReady(skill, now)) continue;
            if (target == _myId && skill.Moral is not (SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party or SkillTarget.PartyAll)) continue;
            if (target != _myId && (!skill.IsFriendly || !InSkillRange(target, skill))) continue;
            if (skill.FirstDamage > 0 && (maxHp <= 0 || hp * 100.0 / maxHp > _genieHp.Value)) continue;
            if (target == _myId && GenieHasActiveBuff(skill, now)) continue;
            if (_genieSupportAt.TryGetValue((skill.Id, target), out var next) && now < next) continue;
            if (!CanCastWithGear(skill)) continue;
            int previous = _selectedId;
            _selectedId = target;
            try { CastSkill(skill.Id); }
            finally { _selectedId = previous; }
            // Only a server effect reply installs the full buff duration. Failed casts retry later.
            _genieSupportAt[(skill.Id, target)] = now + GenieSupportRetryInterval;
            return true;
        }
        return false;
    }
}
