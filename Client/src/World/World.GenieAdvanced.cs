using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const int GenieCellCount = GenieSetup.AttackCells + GenieSetup.RecoveryCells + GenieSetup.SupportCells;
    private const int GenieRecoveryFirstCell = GenieSetup.AttackCells;
    private const int GenieSupportFirstCell = GenieRecoveryFirstCell + GenieSetup.RecoveryCells;
    private const int GenieGridColumns = 4;
    private const int GenieHuntInPlaceText = 31071;
    private const int GenieComboText = 31074;
    private const int GenieComboTipText = 31075;
    private const int GenieRecoveryText = 31022;
    private const int GenieRecoveryModeText = 31020;
    private const int GeniePartyText = 31032;
    private const int GeniePetText = 31027;
    private const int GenieHideRangeText = 31072;
    private const int GenieReturnHomeText = 31073;
    private const int GenieCureCurseText = 31078;
    private const int ClassSkillBase = 1000;
    private const int CureCurseSkillOffset = 525;
    private const string GenieRingTexture = "res://assets/ui/genie/genie_circle_03.png";
    private const string GenieMoreId = "genie_more";
    private const float GenieRingDepth = 12f;
    private const double GenieStartReplyTimeout = 8;
    private const double GeniePollInterval = 0.3;
    private const float GenieHomeSlack = 3;
    private const double GenieLeaderTargetMemory = 8;
    private const double GenieSupportRetryInterval = 2;
    private const double GeniePotionUiInterval = 1;
    private const float GenieMiscTabHeight = 560f;
    private static readonly Color[] GenieCellBorders = { new("a98632"), new("3f9a5b"), new("346675") };

    private GenieSetup _genie = new();
    private byte[] _genieShownBytes = Array.Empty<byte>();
    private readonly Button[] _genieSlots = new Button[GenieCellCount];
    private readonly CheckBox[] _genieParty = new CheckBox[GenieSetup.SupportCells];
    private readonly Dictionary<GenieMode, CheckButton> _genieModes = new();
    private readonly HashSet<string> _genieMonsters = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _genieMonstersOff = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Skill, int Target), double> _genieSupportAt = new();
    private readonly Dictionary<int, double> _genieRefusedUntil = new();
    private const double GenieRefusedSkillHold = 3.0;
    private ItemList _genieMonsterList = null!;
    private SpinBox _genieHp = null!, _genieMp = null!, _genieRange = null!, _genieRecovery = null!, _geniePetPercent = null!;
    private CheckButton _geniePetPotion = null!;
    private CheckButton _genieAutoParty = null!;
    private CheckButton _genieReturnHome = null!, _genieHideRange = null!, _genieCureCurse = null!, _genieHuntInPlace = null!;
    private HudWindow? _genieMore;
    private Decal? _genieRing;
    private LineEdit _geniePtCode = null!;
    private readonly GeniePartyCodeGate _geniePartyCodes = new();
    private int _geniePendingInviterId = -1;
    private string _geniePendingInviterName = "";
    private Label _genieRunLabel = null!;
    private Button _genieStart = null!;
    private PopupMenu _genieSkillPicker = null!;
    private int _genieEditingSlot, _genieLeaderTarget = -1, _genieLeaderId = -1;
    private double _geniePollAt, _genieDeadline, _genieLeaderSeenAt, _genieRequestAt;
    private bool _genieWasRunning, _genieMoving, _genieRequestPending, _genieWantsRunning, _genieSyncing;
    private Vector3 _genieOrigin;
    private string GenieSettingsPath => $"user://genie-{Net.I.MyCharId}.cfg";

    private VBoxContainer BuildAdvancedGenie(VBoxContainer root)
    {
        Net.I.ResetGenieSystem();
        var tabs = new TabContainer();
        root.AddChild(tabs);
        var main = new VBoxContainer { Name = "Main", CustomMinimumSize = new Vector2(510, 0) };
        var miscScroll = new ScrollContainer
        {
            Name = "Misc",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, GenieMiscTabHeight),
        };
        var misc = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        miscScroll.AddChild(misc);
        tabs.AddChild(main);
        if (_geniePanel.PluginReplaced) BuildGenieMoreWindow(miscScroll);
        else tabs.AddChild(miscScroll);
        main.AddThemeConstantOverride("separation", 8);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        main.AddChild(columns);
        var attack = GenieColumn(columns, "Attack Skills", GenieCellKind.Attack, 0, GenieSetup.AttackCells);
        AddGenieMode(attack, GenieMode.Attack, "Attack Mode");
        AddGenieMode(attack, GenieMode.BasicAttack, "R Attack");
        AddGenieMode(attack, GenieMode.Combo, ItemData.Text(GenieComboText, "Combo"));
        _genieModes[GenieMode.Combo].TooltipText = ItemData.Text(GenieComboTipText, "Failed to execute certain skills in the combo.");
        var recovery = GenieColumn(columns, "Recovery Skills", GenieCellKind.Recovery, GenieRecoveryFirstCell, GenieSetup.RecoveryCells);
        AddGenieMode(recovery, GenieMode.Recovery, ItemData.Text(GenieRecoveryModeText, "Recovery mode"));
        _genieRecovery = GeniePercent(recovery, ItemData.Text(GenieRecoveryText, "Use recovery skill at %"),
            GenieSetup.MinPercent, GenieSetup.MaxPercent, v => _genie.RecoveryPercent = v);
        var support = GenieColumn(columns, "Support Skills", GenieCellKind.Support, GenieSupportFirstCell, GenieSetup.SupportCells);
        AddGenieMode(support, GenieMode.Support, "Support Mode");
        AddGenieMode(support, GenieMode.LeaderTarget, "Party Leader Target");
        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 12);
        main.AddChild(lower);
        var potions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lower.AddChild(potions);
        potions.AddChild(UiTheme.SectionTitle("Potion Options"));
        AddGenieMode(potions, GenieMode.HpPotion, "Use HP Potion");
        BuildGeniePotionSelector(potions, HealTarget.Hp);
        _genieHp = GeniePercent(potions, "HP Threshold (%)", GenieSetup.MinPercent, GenieSetup.MaxPercent, v => _genie.HpPercent = v);
        AddGenieMode(potions, GenieMode.MpPotion, "Use MP Potion");
        BuildGeniePotionSelector(potions, HealTarget.Mp);
        _genieMp = GeniePercent(potions, "MP Threshold (%)", GenieSetup.MinPercent, GenieSetup.MaxPercent, v => _genie.MpPercent = v);
        _geniePetPotion = GenieOption(potions, $"Use {ItemData.Text(GeniePetText, "Pet")} Potion",
            on => _genie.Set(GenieMoreMode.PetPotion, on));
        BuildGeniePotionSelector(potions, GeniePetTarget);
        _geniePetPercent = GeniePercent(potions, "Familiar HP Threshold (%)", GenieSetup.MinPercent, GenieSetup.MaxPercent,
            v => _genie.PetPercent = v);
        var monsters = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lower.AddChild(monsters);
        monsters.AddChild(UiTheme.SectionTitle("Monster Attack List"));
        _genieMonsterList = new ItemList { CustomMinimumSize = new Vector2(245, 104) };
        monsters.AddChild(_genieMonsterList);
        var actions = new HBoxContainer();
        monsters.AddChild(actions);
        GenieButton(actions, "+ Add Target", () => AddGenieTarget());
        GenieButton(actions, "Remove", () =>
        {
            foreach (int i in _genieMonsterList.GetSelectedItems()) RemoveGenieTarget(_genieMonsterList.GetItemText(i));
        });
        main.AddChild(UiTheme.Text("Empty list: all monsters in range. Click a slot to select a skill.", 11, UiTheme.TextLo));
        _genieRunLabel = UiTheme.Text("Genie stopped.", 13, UiTheme.Gold);
        main.AddChild(_genieRunLabel);
        var footer = new HBoxContainer();
        main.AddChild(footer);
        _genieStart = GenieButton(footer, "Start", ToggleGenieRun);
        GenieButton(footer, "Save Settings", SaveAdvancedGenie);
        GenieButton(footer, "Use Spirit of Genie", () => Net.I.SendGenieSystem(Net.GenieUseSpiritPotion));
        misc.AddChild(UiTheme.SectionTitle("Genie Settings"));
        _genieRange = GeniePercent(misc, "Range from Start Position", GenieSetup.MinRange, GenieSetup.MaxRange, v => _genie.Range = v);
        _genieReturnHome = GenieOption(misc, ItemData.Text(GenieReturnHomeText, "When no monsters are in range, return to the start position"),
            on => _genie.Set(GenieExtra.StayInPlace, !on));
        _genieHuntInPlace = GenieOption(misc, ItemData.Text(GenieHuntInPlaceText, "Hunting in place"), on => _genie.Set(GenieExtra.HuntInPlace, on));
        _genieHideRange = GenieOption(misc, ItemData.Text(GenieHideRangeText, "Hide attack range"), on => _genie.Set(GenieExtra.HideRange, on));
        _genieCureCurse = GenieOption(misc, ItemData.Text(GenieCureCurseText, "Auto Cure Curse"), on => _genie.Set(GenieExtra.CureCurse, on));
        _genieAutoParty = new CheckButton { Text = "Auto Party" };
        misc.AddChild(_genieAutoParty);
        _geniePtCode = new LineEdit { PlaceholderText = "PT CODE — code to send in a private message", Secret = true, MaxLength = 64 };
        misc.AddChild(UiTheme.SectionTitle("PT CODE"));
        misc.AddChild(_geniePtCode);
        _geniePtCode.TextChanged += _ => ConfigureGeniePartyCode();
        _genieAutoParty.Toggled += _ => ConfigureGeniePartyCode();
        misc.AddChild(UiTheme.Text("Senders who PM the code can have their party invite accepted within 2 minutes.", 11, UiTheme.TextLo));
        misc.AddChild(UiTheme.Text("Leader Target uses the nearby party leader's most recent attack.", 11, UiTheme.TextLo));
        BuildGenieScrollOptions(misc);
        BuildGenieHammerOptions(misc);
        BuildGenieTownOptions(misc);
        misc.AddChild(new HSeparator());
        _genieSkillPicker = new PopupMenu();
        _genieLayer.AddChild(_genieSkillPicker);
        _genieSkillPicker.IdPressed += id =>
        {
            SetGenieCell(_genieEditingSlot, (int)id);
            GenieSetupEdited();
        };
        LoadAdvancedGenie();
        Net.I.GenieSystemState += OnGenieSystemState;
        Net.I.GenieAuraEvent += OnGenieAura;
        Net.I.GenieOptionsReceived += OnGenieOptions;
        Net.I.ChatEvent += OnGeniePrivateMessage;
        Net.I.AttackEvent += GenieObserveAttack;
        Net.I.MagicEvent += GenieObserveMagic;
        return misc;
    }

    private void BuildGenieMoreWindow(Control content)
    {
        _genieMore = new HudWindow(GenieMoreId, "Knight Genie: More Options", new Vector2(240, 120), bodyMinWidth: 420) { Visible = false };
        _genieMore.SetHeaderAccent(new Color("10383b"), UiTheme.Gold, UiTheme.GoldBright);
        _genieMore.Closed += () => _genieMore.Visible = false;
        _genieMore.Body.AddChild(content);
        _genieLayer.AddChild(_genieMore);
    }

    private void OpenGenieMore()
    {
        if (_genieMore == null) return;
        _genieMore.Visible = !_genieMore.Visible;
        if (_genieMore.Visible) _genieMore.MoveToFront();
    }

    private VBoxContainer GenieColumn(Node parent, string heading, GenieCellKind kind, int first, int count)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(column);
        column.AddChild(UiTheme.SectionTitle(heading));
        var grid = new GridContainer { Columns = GenieGridColumns };
        column.AddChild(grid);
        for (int i = 0; i < count; i++)
        {
            int cell = first + i;
            var button = new Button { Text = "+", CustomMinimumSize = new Vector2(36, 36),
                ExpandIcon = true, FocusMode = Control.FocusModeEnum.None };
            var slotStyle = new StyleBoxFlat { BgColor = new Color("091719"), BorderColor = GenieCellBorders[(int)kind] };
            slotStyle.SetBorderWidthAll(1);
            button.AddThemeStyleboxOverride("normal", slotStyle);
            var hoverStyle = (StyleBoxFlat)slotStyle.Duplicate();
            hoverStyle.BgColor = new Color("234044");
            button.AddThemeStyleboxOverride("hover", hoverStyle);
            button.AddThemeConstantOverride("icon_max_width", 30);
            button.Pressed += () => OpenGenieSkillPicker(cell);
            WireGenieSkillSlot(button, cell);
            _genieSlots[cell] = button;
            if (kind != GenieCellKind.Support)
            {
                grid.AddChild(button);
                continue;
            }
            int index = i;
            var stack = new VBoxContainer();
            stack.AddThemeConstantOverride("separation", 0);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            stack.AddChild(button);
            var party = new CheckBox
            {
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                TooltipText = $"{ItemData.Text(GeniePartyText, "Party")}: also cast this skill on party members",
            };
            party.Toggled += on =>
            {
                if (_genieSyncing) return;
                _genie.SupportParty[index] = on;
                GenieSetupEdited();
            };
            stack.AddChild(party);
            _genieParty[index] = party;
            grid.AddChild(stack);
        }
        if (kind == GenieCellKind.Support)
        {
            var hint = UiTheme.Text("A ticked box under a cell also casts it on party members.", 10, UiTheme.TextLo);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.CustomMinimumSize = new Vector2(GenieGridColumns * 40, 0);
            column.AddChild(hint);
        }
        return column;
    }

    private const int GenieTargetCap = 8;

    private void ToggleGenieRun()
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
        PluginNotifyGenie();
        Net.I.SendGenieSystem(Net.GenieStart);
    }

    private bool AddGenieTarget()
    {
        if (_selectedId < 0 || !_ents.TryGetValue(_selectedId, out var e) || !e.IsMonster)
        {
            CombatNotice("Select a monster first.");
            return false;
        }
        if (_genieMonsters.Contains(e.Name))
        {
            CombatNotice($"{e.Name} is already in the Genie attack list.");
            return true;
        }
        if (_genieMonsters.Count >= GenieTargetCap)
        {
            CombatNotice($"The Genie attack list is full ({GenieTargetCap}).");
            return true;
        }
        _genieMonsters.Add(e.Name);
        _genieMonstersOff.Remove(e.Name);
        CombatNotice($"{e.Name} added to the Genie attack list.");
        RefreshGenieMonsters();
        return true;
    }

    private void RemoveGenieTarget(string name)
    {
        _genieMonsters.Remove(name);
        _genieMonstersOff.Remove(name);
        RefreshGenieMonsters();
    }

    private void SetGenieTargetOn(string name, bool on)
    {
        if (on) _genieMonstersOff.Remove(name);
        else _genieMonstersOff.Add(name);
        RefreshGenieSub();
    }

    private List<string> GenieTargetsInOrder() => _genieMonsters.OrderBy(n => n).ToList();

    private static Button GenieButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private void AddGenieMode(Node parent, GenieMode mode, string text) =>
        _genieModes[mode] = GenieOption(parent, text, on => _genie.Set(mode, on));

    private CheckButton GenieOption(Node parent, string text, Action<bool> write)
    {
        var button = new CheckButton { Text = text };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Toggled += on =>
        {
            if (_genieSyncing) return;
            write(on);
            GenieSetupEdited();
        };
        parent.AddChild(button);
        return button;
    }

    private SpinBox GeniePercent(Node parent, string title, int min, int max, Action<int> write)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        row.AddChild(UiTheme.Text(title, 12, UiTheme.TextLo));
        var input = new SpinBox { MinValue = min, MaxValue = max, Step = 1 };
        input.ValueChanged += value =>
        {
            if (_genieSyncing) return;
            write((int)value);
            GenieSetupEdited();
        };
        row.AddChild(input);
        return input;
    }

    private static (GenieCellKind Kind, int Index) GenieCellAt(int cell) =>
        cell < GenieRecoveryFirstCell ? (GenieCellKind.Attack, cell)
        : cell < GenieSupportFirstCell ? (GenieCellKind.Recovery, cell - GenieRecoveryFirstCell)
        : (GenieCellKind.Support, cell - GenieSupportFirstCell);

    private int GenieCell(int cell)
    {
        var (kind, index) = GenieCellAt(cell);
        return _genie.Cells(kind)[index];
    }

    private void SetGenieCell(int cell, int skillId)
    {
        var (kind, index) = GenieCellAt(cell);
        _genie.Cells(kind)[index] = skillId;
    }

    private void OpenGenieSkillPicker(int cell)
    {
        _genieEditingSlot = cell;
        _genieSkillPicker.Clear();
        _genieSkillPicker.AddItem("Clear Slot", 0);
        foreach (var s in SkillData.ForClass(_selfClass).OrderBy(s => s.Level))
            if (GenieSkillAllowed(s, cell)) _genieSkillPicker.AddIconItem(SkillData.Icon(s.Id), s.Name, s.Id);
        _genieSkillPicker.Position = (Vector2I)(_genieSlots[cell].GetGlobalRect().End);
        _genieSkillPicker.Popup();
    }

    private void RefreshGenieSlots()
    {
        for (int i = 0; i < GenieCellCount; i++)
        {
            var skill = SkillData.Get(GenieCell(i));
            int number = GenieCellAt(i).Index + 1;
            _genieSlots[i].Icon = skill == null ? null : SkillData.Icon(skill.Id);
            _genieSlots[i].Text = skill == null ? "+" : _genieSlots[i].Icon == null ? number.ToString() : "";
            _genieSlots[i].TooltipText = skill == null ? "Select Skill" : SkillTooltip(skill);
        }
    }

    private void RefreshGenieMonsters()
    {
        _genieMonsterList.Clear();
        foreach (string name in _genieMonsters.OrderBy(n => n)) _genieMonsterList.AddItem(name);
        RefreshGenieSub();
    }

    private void SyncGenieControls()
    {
        _genieSyncing = true;
        foreach (var (mode, check) in _genieModes) check.SetPressedNoSignal(_genie.Has(mode));
        _geniePetPotion.SetPressedNoSignal(_genie.Has(GenieMoreMode.PetPotion));
        _genieHp.SetValueNoSignal(_genie.HpPercent);
        _genieMp.SetValueNoSignal(_genie.MpPercent);
        _genieRecovery.SetValueNoSignal(_genie.RecoveryPercent);
        _geniePetPercent.SetValueNoSignal(_genie.PetPercent);
        _genieRange.SetValueNoSignal(_genie.Range);
        _genieReturnHome.SetPressedNoSignal(!_genie.Has(GenieExtra.StayInPlace));
        _genieHideRange.SetPressedNoSignal(_genie.Has(GenieExtra.HideRange));
        _genieCureCurse.SetPressedNoSignal(_genie.Has(GenieExtra.CureCurse));
        _genieHuntInPlace.SetPressedNoSignal(_genie.Has(GenieExtra.HuntInPlace));
        for (int i = 0; i < GenieSetup.SupportCells; i++) _genieParty[i].SetPressedNoSignal(_genie.SupportParty[i]);
        SyncGenieTownControls();
        SyncGenieHammerControls();
        _genieSyncing = false;
        RefreshGenieSlots();
    }

    private void GenieSetupEdited() => PluginNotifyGenie();

    private void ApplyGenieSetup(GenieSetup setup)
    {
        ClearRefusedGenieSkills(setup);
        _genie = setup;
        SyncGenieControls();
        PluginNotifyGenie();
    }

    private void OnGenieOptions(byte[] saved)
    {
        if (_genieRequestPending || Net.I.GenieRunning) return;
        if (GenieSetup.FromBytes(saved, GenieKindOf) is not { } setup) return;
        ApplyGenieSetup(setup);
        _genieShownBytes = _genie.ToBytes();
    }

    private GenieCellKind GenieKindOf(int skillId) =>
        SkillData.Get(skillId) is { } skill ? GenieKindOf(skill) : GenieCellKind.Support;

    private static GenieCellKind GenieKindOf(SkillData.Skill s) =>
        s.IsEnemy ? GenieCellKind.Attack
        : GenieRestoresHp(s) || GenieRestoresMp(s) ? GenieCellKind.Recovery
        : GenieCellKind.Support;

    private static bool GenieRestoresHp(SkillData.Skill s) =>
        GenieRecovery.RestoresHp(s.IsFriendly, s.Type1, s.DirectType, s.FirstDamage, s.TimeDamage);

    private static bool GenieRestoresMp(SkillData.Skill s) =>
        GenieRecovery.RestoresMp(s.IsFriendly, s.Type1, s.DirectType, s.FirstDamage);

    private void SaveAdvancedGenie()
    {
        var config = new ConfigFile();
        var bytes = _genie.ToBytes();
        config.SetValue("genie", "options", bytes);
        config.SetValue("genie", "monsters", _genieMonsters.ToArray());
        config.SetValue("genie", "auto_party", _genieAutoParty.ButtonPressed);
        config.SetValue("genie", "pt_code", _geniePtCode.Text.Trim());
        config.SetValue("genie", "hp_potion_item", _genieHpItem);
        config.SetValue("genie", "mp_potion_item", _genieMpItem);
        config.SetValue("genie", "pet_potion_item", _geniePetItem);
        SaveGenieScrollSettings(config);
        if (config.Save(GenieSettingsPath) != Error.Ok) CombatNotice("Could not save Genie settings to disk.");
        _genieShownBytes = bytes;
        Net.I.SendGenieSystem(Net.GenieSaveOptions, bytes);
    }

    private void SaveGenieIfEdited()
    {
        if (!_genie.ToBytes().AsSpan().SequenceEqual(_genieShownBytes)) SaveAdvancedGenie();
    }

    private void LoadAdvancedGenie()
    {
        var config = new ConfigFile();
        if (config.Load(GenieSettingsPath) == Error.Ok)
        {
            _genieHpItem = config.GetValue("genie", "hp_potion_item", 0).AsInt32();
            _genieMpItem = config.GetValue("genie", "mp_potion_item", 0).AsInt32();
            _geniePetItem = config.GetValue("genie", "pet_potion_item", 0).AsInt32();
            LoadGenieScrollSettings(config);
            var saved = config.GetValue("genie", "options", Array.Empty<byte>()).AsByteArray();
            if (GenieSetup.FromBytes(saved, GenieKindOf) is { } setup)
            {
                if (GenieOptionsBlob.LayoutOf(saved) < GenieOptionsBlob.CurrentLayout) CarryGenieHammerSettings(config, setup);
                ClearRefusedGenieSkills(setup);
                _genie = setup;
            }
            foreach (string name in config.GetValue("genie", "monsters", Array.Empty<string>()).AsStringArray())
                _genieMonsters.Add(name);
            _genieAutoParty.ButtonPressed = config.GetValue("genie", "auto_party", false).AsBool();
            _geniePtCode.Text = config.GetValue("genie", "pt_code", "").AsString();
        }
        _genieShownBytes = _genie.ToBytes();
        SyncGenieControls();
        RefreshGenieMonsters();
        RefreshGeniePotionSelectors();
    }

    private void OnGenieSystemState(bool running, int minutes)
    {
        ShowGenieAura(_myId, running);
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
        }
        if (!running && _genieWasRunning) HaltGenieActions();
        if (running != _genieWasRunning) ResetGenieTownWatch();
        _genieWasRunning = running;
        if (!running) _genieWantsRunning = false;
        _genieDeadline = Now() + minutes * 60;
        _genieStart.Text = running ? "Stop" : "Start";
        _geniePoints = minutes;
        RefreshGenieSub();
        _genieRunLabel.Text = running ? $"Genie active • {minutes} minutes" :
            minutes > 0 ? $"Genie stopped • {minutes} minutes" : "No time remaining. Spirit of Genie required.";
        PluginNotifyGenie();
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
        ShowGenieAura(_myId, false);
        _genieWasRunning = false;
        _genieWantsRunning = false;
        _genieRequestPending = false;
        _genieStart.Disabled = false;
        _genieStart.Text = "Start";
        _genieRunLabel.Text = "Genie stopped.";
        if (Net.I.Connected) Net.I.SendGenieSystem(Net.GenieStop);
        PluginNotifyGenie();
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
        if (_invitePending && GenieAcceptInvite(_geniePendingInviterId, _geniePendingInviterName))
        {
            AnswerInvite(true);
            _inviteAskDialog.Hide();
        }
    }

    private void GenieObserveAttack(int type, int result, int attacker, int target, bool chained) => GenieObserveLeader(attacker, target);
    private void GenieObserveMagic(int stage, int skill, int caster, int target, short[] data)
    {
        if (stage is 1 or 3) GenieObserveLeader(caster, target);
        if (stage == 3 && caster == _myId && Net.I.GenieRunning
            && (_genie.Support.Contains(skill) || _genie.Recovery.Contains(skill)) && SkillData.Get(skill) is { } support)
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
        Net.I.GenieAuraEvent -= OnGenieAura;
        Net.I.GenieOptionsReceived -= OnGenieOptions;
        _geniePartyCodes.Clear();
        Net.I.ChatEvent -= OnGeniePrivateMessage;
        Net.I.AttackEvent -= GenieObserveAttack;
        Net.I.MagicEvent -= GenieObserveMagic;
    }

    private bool GenieValidMonster(int id) => _ents.TryGetValue(id, out var e)
        && e.IsMonster && e.Attackable && !e.Dead
        && e.Body.GlobalPosition.DistanceTo(_genieOrigin) <= _genie.Range
        && (_genieMonsters.Count == 0 || (_genieMonsters.Contains(e.Name) && !_genieMonstersOff.Contains(e.Name)));

    private void AdvancedGenieTick(double now)
    {
        GenieRingTick();
        if (_genieRequestPending && now - _genieRequestAt > GenieStartReplyTimeout) StopAdvancedGenie();
        if (!Net.I.GenieRunning)
        { if (_genieWasRunning) StopAdvancedGenie(); return; }
        if (_selfDead || !Net.I.Connected || Net.I.ReconnectBlocking || MerchantBlocksMove() || now >= _genieDeadline)
        { StopAdvancedGenie(); return; }
        if (now < _geniePollAt) return;
        _geniePollAt = now + GeniePollInterval;
        GenieHammerTick(now);
        GenieFamiliarTick(now);
        if (now >= _geniePotionUiAt)
        { _geniePotionUiAt = now + GeniePotionUiInterval; RefreshGeniePotionSelectors(); }
        if (GenieTownTick()) return;
        if (_genie.Has(GenieMode.HpPotion) && GenieRecovery.Below(Vitals.Hp, Vitals.MaxHp, _genie.HpPercent))
            UseGeniePotion(HealTarget.Hp);
        if (_genie.Has(GenieMode.MpPotion) && GenieRecovery.Below(Vitals.Mp, Vitals.MaxMp, _genie.MpPercent))
            UseGeniePotion(HealTarget.Mp);
        if (SelfCasting(now)) return;
        if (GenieCureCurse(now)) return;
        if (GenieRecover(now)) return;
        if (GenieScrollTick(now)) return;
        if (_genie.Has(GenieMode.Support) && GenieSupportTick(now)) return;
        if (!_genie.Has(GenieMode.Attack))
        { StopAutoAttack(); if (_genieMoving) _hasMoveTarget = false; _genieMoving = false; return; }
        int target = _selectedId;
        if (_genie.Has(GenieMode.LeaderTarget) && InParty && !AmLeader)
            target = _genieLeaderId == PartyMembers[0].CharId && now - _genieLeaderSeenAt < GenieLeaderTargetMemory && GenieValidMonster(_genieLeaderTarget) ? _genieLeaderTarget : -1;
        else if (!GenieValidMonster(target) || !GenieCanReach(target))
            target = _ents.Where(pair => GenieValidMonster(pair.Key) && GenieCanReach(pair.Key))
                .OrderBy(pair => pair.Value.Body.GlobalPosition.DistanceSquaredTo(_self.GlobalPosition))
                .Select(pair => pair.Key).DefaultIfEmpty(-1).First();
        if (target < 0)
        {
            StopAutoAttack();
            if (!_genie.Has(GenieExtra.StayInPlace) && _self.GlobalPosition.DistanceTo(_genieOrigin) > GenieHomeSlack)
            {
                _moveTarget = _genieOrigin;
                _hasMoveTarget = true; _genieMoving = true;
            }
            else if (_genieMoving) { _hasMoveTarget = false; _genieMoving = false; }
            return;
        }
        if (_selectedId != target) Select(target, _ents[target]);
        bool basic = _genie.Has(GenieMode.BasicAttack);
        bool basicInRange = basic && InBasicAttackRange(target);
        if (basicInRange && (!_autoAttack || _autoTargetId != target)) StartAutoAttack(target);
        if (!basicInRange && _autoAttack) StopAutoAttack();
        bool skillInRange = false;
        bool hasAttackSkill = false;
        foreach (int id in _genie.Attack)
        {
            var skill = SkillData.Get(id);
            if (skill == null || !skill.IsEnemy || !SkillRequirementMet(skill)) continue;
            hasAttackSkill = true;
            if (!InSkillRange(target, skill)) continue;
            skillInRange = true;
            if (!SkillReady(skill, now) || !CanCastWithGear(skill) || GenieSkillHeld(skill.Id, now)) continue;
            _hasMoveTarget = false; _genieMoving = false;
            CastSkill(skill.Id);
            return;
        }
        bool shouldMove = GenieApproach.ShouldMove(_genie.Has(GenieExtra.HuntInPlace), basic, basicInRange, hasAttackSkill, skillInRange);
        if (shouldMove)
        {
            _moveTarget = _ents[target].Body.GlobalPosition;
            _hasMoveTarget = true; _genieMoving = true;
        }
        else if (_genieMoving) { _hasMoveTarget = false; _genieMoving = false; }
    }

    private bool GenieComboOn => _genie.Has(GenieMode.Combo);

    private bool GenieCanReach(int target)
    {
        if (!_genie.Has(GenieExtra.HuntInPlace)) return true;
        if (target < 0 || !_ents.ContainsKey(target)) return false;
        if (_genie.Has(GenieMode.BasicAttack) && InBasicAttackRange(target)) return true;
        foreach (int id in _genie.Attack)
            if (SkillData.Get(id) is { IsEnemy: true } skill && InSkillRange(target, skill))
                return true;
        return false;
    }

    private void NoteGenieRefusal(int skillId)
    {
        if (Net.I.GenieRunning) _genieRefusedUntil[skillId] = Now() + GenieRefusedSkillHold;
    }

    private bool GenieSkillHeld(int skillId, double now) =>
        _genieRefusedUntil.TryGetValue(skillId, out double until) && now < until;

    private bool GenieCureCurse(double now)
    {
        if (!_genie.Has(GenieExtra.CureCurse) || !SelfCursed()) return false;
        var skill = SkillData.Get(_selfClass * ClassSkillBase + CureCurseSkillOffset);
        if (skill == null || !SkillRequirementMet(skill) || !SkillReady(skill, now)
            || GenieSkillHeld(skill.Id, now) || !CanCastWithGear(skill)) return false;
        if (_genieSupportAt.TryGetValue((skill.Id, _myId), out var next) && now < next) return false;
        GenieCastOn(_myId, skill.Id);
        _genieSupportAt[(skill.Id, _myId)] = now + GenieSupportRetryInterval;
        return true;
    }

    private void GenieCastOn(int target, int skillId)
    {
        int previous = _selectedId;
        _selectedId = target;
        try { CastSkill(skillId); }
        finally { _selectedId = previous; }
    }

    private bool SelfCursed() =>
        _buffs.Exists(buff => SkillData.Get(buff.SkillId) is { Type1: MagicType.Buff, IsEnemy: true });

    private void GenieRingTick()
    {
        if (_genieRange is null || _geniePanel is null) return;
        bool show = _self != null && !_genie.Has(GenieExtra.HideRange)
            && (Net.I.GenieRunning || _geniePanel.Visible);
        if (!show)
        {
            if (_genieRing != null) _genieRing.Visible = false;
            return;
        }
        if (_genieRing == null)
        {
            _genieRing = new Decal
            {
                TextureAlbedo = ResourceLoader.Load<Texture2D>(GenieRingTexture),
                CullMask = ItemShineLight.SceneryLayer,
            };
            AddChild(_genieRing);
        }
        float diameter = 2f * _genie.Range;
        _genieRing.Size = new Vector3(diameter, GenieRingDepth, diameter);
        _genieRing.GlobalPosition = Net.I.GenieRunning ? _genieOrigin : _self!.GlobalPosition;
        _genieRing.Visible = true;
    }

    private bool GenieSupportTick(double now)
    {
        if (GenieSupport(_myId, now)) return true;
        foreach (var member in PartyMembers)
            if (member.CharId != _myId && member.Hp > 0 && _ents.TryGetValue(member.CharId, out var ally)
                && !ally.Dead && !ally.Attackable && GenieSupport(member.CharId, now)) return true;
        return false;
    }

    private bool GenieSupport(int target, double now)
    {
        for (int i = 0; i < GenieSetup.SupportCells; i++)
        {
            if (target != _myId && !_genie.SupportParty[i]) continue;
            var skill = SkillData.Get(_genie.Support[i]);
            if (skill == null || skill.IsEnemy || skill.IsDeadFriend || skill.IsGroundArea || skill.IsBlink
                || !SkillRequirementMet(skill) || !SkillReady(skill, now) || GenieSkillHeld(skill.Id, now)) continue;
            if (target == _myId && skill.Moral is not (SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party or SkillTarget.PartyAll)) continue;
            if (target != _myId && (!skill.IsFriendly || skill.Moral == SkillTarget.Self || !InSkillRange(target, skill))) continue;
            if (target == _myId && GenieHasActiveBuff(skill, now)) continue;
            if (_genieSupportAt.TryGetValue((skill.Id, target), out var next) && now < next) continue;
            if (!CanCastWithGear(skill)) continue;
            GenieCastOn(target, skill.Id);
            _genieSupportAt[(skill.Id, target)] = now + GenieSupportRetryInterval;
            return true;
        }
        return false;
    }
}
