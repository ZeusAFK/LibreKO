using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GenieFeature : IFeature, ILateTickFeature, IClosableWindow, IGenieCombo, IInviteAutoAccept, IGenieRunner
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly CombatSession combat;
    private readonly GenieSession genieSession;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly IGenieRequests requests;
    private readonly PartyInvitesFeature partyInvites;
    private readonly EntityActionSystem entityAction;
    private readonly MerchantStallsFeature merchantStalls;
    private readonly SkillCastSystem skillCast;
    private readonly BasicAttackSystem basicAttack;
    private readonly GenieHammerFeature genieHammer;
    private readonly GeniePotionsFeature geniePotions;
    private readonly GenieScrollsFeature genieScrolls;
    private readonly GenieTownFeature genieTown;

    public GenieFeature(IWorldContext context, ChatSession chat, CombatSession combat, GenieSession genie, PartySession party, SelfSession self, IGenieRequests requests, PartyInvitesFeature partyInvites, EntityActionSystem entityAction, MerchantStallsFeature merchantStalls, SkillCastSystem skillCast, BasicAttackSystem basicAttack, GenieHammerFeature genieHammer, GeniePotionsFeature geniePotions, GenieScrollsFeature genieScrolls, GenieTownFeature genieTown)
    {
        this.context = context;
        this.chat = chat;
        this.combat = combat;
        genieSession = genie;
        this.party = party;
        this.self = self;
        this.requests = requests;
        this.partyInvites = partyInvites;
        this.entityAction = entityAction;
        this.merchantStalls = merchantStalls;
        this.skillCast = skillCast;
        this.basicAttack = basicAttack;
        this.genieHammer = genieHammer;
        this.geniePotions = geniePotions;
        this.genieScrolls = genieScrolls;
        this.genieTown = genieTown;
        controls = new GenieControls(SetupEdited);
    }

    private readonly GenieControls controls;

    GenieSetup IGenieRunner.Setup => Genie;

    int IGenieRunner.Points => Points;

    void IGenieRunner.Stop() => StopAdvanced();

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private Label tipLabel = null!;
    private Label status = null!;
    private Button claimBtn = null!;
    private bool shown;

    public bool IsOpen => shown;

    public void Build()
    {
        genieHammer.AttachRunner(this);
        geniePotions.AttachRunner(this);
        genieTown.AttachRunner(this);
        skillCast.SelfSkillRefused += NoteRefusal;
        entityAction.AttachGenieCombo(this);
        partyInvites.AttachInviteAutoAccept(this);
        Layer = new CanvasLayer { Layer = UiLayers.EventScoreboards };
        context.Root.AddChild(Layer);
        Panel = new HudWindow("genie", "Advanced Genie", new Vector2(220, 100), bodyMinWidth: 510) { Visible = false };
        Panel.SetHeaderAccent(new Color("10383b"), UiTheme.Gold, UiTheme.GoldBright);
        Panel.Closed += Close;
        Layer.AddChild(Panel);

        var root = BuildAdvanced(Panel.Body);
        root.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiTheme.SectionTitle("Your Genie"));

        tipLabel = UiTheme.Text("...", 13, UiTheme.TextHi);
        tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        tipLabel.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(tipLabel);

        status = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(status);

        claimBtn = new Button { Text = "Claim Daily Reward", FocusMode = Control.FocusModeEnum.None };
        claimBtn.Pressed += () => requests.Claim();
        root.AddChild(claimBtn);

        genieSession.GenieStatusEvent += OnStatus;
        genieSession.GenieClaimEvent += OnClaim;
    }

    public void Dispose()
    {
        genieHammer.DetachRunner();
        geniePotions.DetachRunner();
        genieTown.DetachRunner();
        skillCast.SelfSkillRefused -= NoteRefusal;
        entityAction.DetachGenieCombo();
        partyInvites.DetachInviteAutoAccept();
        if (Game.Connection.Connected) SaveIfEdited();
        AdvancedDispose();
        genieSession.GenieStatusEvent -= OnStatus;
        genieSession.GenieClaimEvent -= OnClaim;
    }

    public void LateTick(double delta, double nowSec) => AdvancedTick(nowSec);

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        genieScrolls.RefreshScrolls();
        geniePotions.RefreshPotionSelectors();
        Panel.Visible = true;
        shown = true;
        shownBytes = Genie.ToBytes();
        context.Plugins.Genie();
        requests.Status();
        requests.System(GenieSystemCommand.LoadOptions);
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        SaveIfEdited();
        context.Plugins.Genie();
    }

    private void OnStatus(string tip, bool rewardAvail)
    {
        tipLabel.Text = tip;
        if (rewardAvail)
        {
            status.Text = "A daily reward is waiting for you!";
            status.AddThemeColorOverride("font_color", UiTheme.Gold);
            claimBtn.Disabled = false;
        }
        else
        {
            status.Text = "Daily reward already claimed. Come back tomorrow.";
            status.AddThemeColorOverride("font_color", UiTheme.TextLo);
            claimBtn.Disabled = true;
        }
    }

    private void OnClaim(bool ok, int rewardGold)
    {
        if (ok)
        {
            context.Log.Notice($"Your genie granted you {rewardGold} gold.");
            requests.Status();
        }
    }

    private const int CellCount = GenieSetup.AttackCells + GenieSetup.RecoveryCells + GenieSetup.SupportCells;
    internal const int RecoveryFirstCell = GenieSetup.AttackCells;
    internal const int SupportFirstCell = RecoveryFirstCell + GenieSetup.RecoveryCells;
    private const int GridColumns = 4;
    private const int HuntInPlaceText = 31071;
    private const int ComboText = 31074;
    private const int ComboTipText = 31075;
    private const int RecoveryText = 31022;
    private const int RecoveryModeText = 31020;
    private const int PartyText = 31032;
    private const int PetText = 31027;
    private const int HideRangeText = 31072;
    private const int ReturnHomeText = 31073;
    private const int CureCurseText = 31078;
    private const int ClassSkillBase = 1000;
    private const int CureCurseSkillOffset = 525;
    private const string RingTexture = "res://assets/ui/genie/genie_circle_03.png";
    private const string MoreId = "genie_more";
    private const float RingDepth = 12f;
    private const double StartReplyTimeout = 8;
    private const double PollInterval = 0.3;
    private const float HomeSlack = 3;
    private const double LeaderTargetMemory = 8;
    private const double SupportRetryInterval = 2;
    private const double PotionUiInterval = 1;
    private const float MiscTabHeight = 560f;

    private static readonly Color[] CellBorders = { new("a98632"), new("3f9a5b"), new("346675") };

    internal GenieSetup Genie { get; private set; } = new();
    private byte[] shownBytes = Array.Empty<byte>();
    private readonly Button[] slots = new Button[CellCount];
    private readonly CheckBox[] genieParty = new CheckBox[GenieSetup.SupportCells];
    private readonly Dictionary<GenieMode, CheckButton> modes = new();
    internal HashSet<string> Monsters { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> MonstersOff { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Skill, int Target), double> genieSupportAt = new();

    private readonly Dictionary<int, double> refusedUntil = new();

    private const double RefusedSkillHold = 3.0;

    private ItemList monsterList = null!;
    private SpinBox hpThreshold = null!;
    private SpinBox mpThreshold = null!;
    private SpinBox range = null!;
    private SpinBox recoverySpin = null!;
    private SpinBox petPercent = null!;
    private CheckButton petPotion = null!;
    private CheckButton autoParty = null!;
    private CheckButton returnHome = null!;
    private CheckButton hideRange = null!;
    private CheckButton cureCurse = null!;
    private CheckButton huntInPlace = null!;
    internal HudWindow? More { get; private set; }
    private Decal? ring;
    private LineEdit ptCode = null!;
    private readonly GeniePartyCodeGate partyCodes = new();
    internal Label RunLabel { get; private set; } = null!;
    private Button start = null!;
    internal PopupMenu SkillPicker { get; private set; } = null!;
    private int editingSlot;
    private int leaderTarget = -1;
    private int leaderId = -1;
    private double pollAt;
    private double deadline;
    private double leaderSeenAt;
    private double requestAt;
    private bool wasRunning;
    private bool moving;
    internal bool RequestPending { get; private set; }
    private bool wantsRunning;
    private Vector3 origin;

    private string SettingsPath => $"user://genie-{self.MyCharId}.cfg";

    private VBoxContainer BuildAdvanced(VBoxContainer root)
    {
        genieSession.Reset();
        var tabs = new TabContainer();
        root.AddChild(tabs);
        var main = new VBoxContainer { Name = "Main", CustomMinimumSize = new Vector2(510, 0) };
        var miscScroll = new ScrollContainer
        {
            Name = "Misc",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, MiscTabHeight),
        };
        var misc = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        miscScroll.AddChild(misc);
        tabs.AddChild(main);
        if (Panel.PluginReplaced) BuildMoreWindow(miscScroll);
        else tabs.AddChild(miscScroll);
        main.AddThemeConstantOverride("separation", 8);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 12);
        main.AddChild(columns);
        var attack = Column(columns, "Attack Skills", GenieCellKind.Attack, 0, GenieSetup.AttackCells);
        AddMode(attack, GenieMode.Attack, "Attack Mode");
        AddMode(attack, GenieMode.BasicAttack, "R Attack");
        AddMode(attack, GenieMode.Combo, ItemData.Text(ComboText, "Combo"));
        modes[GenieMode.Combo].TooltipText = ItemData.Text(ComboTipText, "Failed to execute certain skills in the combo.");
        var recovery = Column(columns, "Recovery Skills", GenieCellKind.Recovery, RecoveryFirstCell, GenieSetup.RecoveryCells);
        AddMode(recovery, GenieMode.Recovery, ItemData.Text(RecoveryModeText, "Recovery mode"));
        recoverySpin = controls.Percent(recovery, ItemData.Text(RecoveryText, "Use recovery skill at %"),
            GenieSetup.MinPercent, GenieSetup.MaxPercent, v => Genie.RecoveryPercent = v);
        var support = Column(columns, "Support Skills", GenieCellKind.Support, SupportFirstCell, GenieSetup.SupportCells);
        AddMode(support, GenieMode.Support, "Support Mode");
        AddMode(support, GenieMode.LeaderTarget, "Party Leader Target");
        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 12);
        main.AddChild(lower);
        var potions = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lower.AddChild(potions);
        potions.AddChild(UiTheme.SectionTitle("Potion Options"));
        AddMode(potions, GenieMode.HpPotion, "Use HP Potion");
        geniePotions.BuildPotionSelector(potions, HealTarget.Hp, Layer);
        hpThreshold = controls.Percent(potions, "HP Threshold (%)", GenieSetup.MinPercent, GenieSetup.MaxPercent, v => Genie.HpPercent = v);
        AddMode(potions, GenieMode.MpPotion, "Use MP Potion");
        geniePotions.BuildPotionSelector(potions, HealTarget.Mp, Layer);
        mpThreshold = controls.Percent(potions, "MP Threshold (%)", GenieSetup.MinPercent, GenieSetup.MaxPercent, v => Genie.MpPercent = v);
        petPotion = controls.Option(potions, $"Use {ItemData.Text(PetText, "Pet")} Potion",
            on => Genie.Set(GenieMoreMode.PetPotion, on));
        geniePotions.BuildPotionSelector(potions, GeniePotionsFeature.PetTarget, Layer);
        petPercent = controls.Percent(potions, "Familiar HP Threshold (%)", GenieSetup.MinPercent, GenieSetup.MaxPercent,
            v => Genie.PetPercent = v);
        var monstersBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        lower.AddChild(monstersBox);
        monstersBox.AddChild(UiTheme.SectionTitle("Monster Attack List"));
        monsterList = new ItemList { CustomMinimumSize = new Vector2(245, 104) };
        monstersBox.AddChild(monsterList);
        var actions = new HBoxContainer();
        monstersBox.AddChild(actions);
        GenieControls.Button(actions, "+ Add Target", () => AddTarget());
        GenieControls.Button(actions, "Remove", () =>
        {
            foreach (int i in monsterList.GetSelectedItems()) RemoveTarget(monsterList.GetItemText(i));
        });
        main.AddChild(UiTheme.Text("Empty list: all monsters in range. Click a slot to select a skill.", 11, UiTheme.TextLo));
        RunLabel = UiTheme.Text("Genie stopped.", 13, UiTheme.Gold);
        main.AddChild(RunLabel);
        var footer = new HBoxContainer();
        main.AddChild(footer);
        start = GenieControls.Button(footer, "Start", ToggleRun);
        GenieControls.Button(footer, "Save Settings", SaveAdvanced);
        GenieControls.Button(footer, "Use Spirit of Genie", () => requests.System(GenieSystemCommand.UseSpiritPotion));
        misc.AddChild(UiTheme.SectionTitle("Genie Settings"));
        range = controls.Percent(misc, "Range from Start Position", GenieSetup.MinRange, GenieSetup.MaxRange, v => Genie.Range = v);
        returnHome = controls.Option(misc, ItemData.Text(ReturnHomeText, "When no monsters are in range, return to the start position"),
            on => Genie.Set(GenieExtra.StayInPlace, !on));
        huntInPlace = controls.Option(misc, ItemData.Text(HuntInPlaceText, "Hunting in place"), on => Genie.Set(GenieExtra.HuntInPlace, on));
        hideRange = controls.Option(misc, ItemData.Text(HideRangeText, "Hide attack range"), on => Genie.Set(GenieExtra.HideRange, on));
        cureCurse = controls.Option(misc, ItemData.Text(CureCurseText, "Auto Cure Curse"), on => Genie.Set(GenieExtra.CureCurse, on));
        autoParty = new CheckButton { Text = "Auto Party" };
        misc.AddChild(autoParty);
        ptCode = new LineEdit { PlaceholderText = "PT CODE — code to send in a private message", Secret = true, MaxLength = 64 };
        misc.AddChild(UiTheme.SectionTitle("PT CODE"));
        misc.AddChild(ptCode);
        ptCode.TextChanged += _ => ConfigurePartyCode();
        autoParty.Toggled += _ => ConfigurePartyCode();
        misc.AddChild(UiTheme.Text("Senders who PM the code can have their party invite accepted within 2 minutes.", 11, UiTheme.TextLo));
        misc.AddChild(UiTheme.Text("Leader Target uses the nearby party leader's most recent attack.", 11, UiTheme.TextLo));
        genieScrolls.BuildScrollOptions(misc, Layer);
        genieHammer.BuildHammerOptions(misc, controls);
        genieTown.BuildTownOptions(misc, controls);
        misc.AddChild(new HSeparator());
        SkillPicker = new PopupMenu();
        Layer.AddChild(SkillPicker);
        SkillPicker.IdPressed += id =>
        {
            SetCell(editingSlot, (int)id);
            SetupEdited();
        };
        LoadAdvanced();
        genieSession.GenieSystemState += OnSystemState;
        genieSession.GenieOptionsReceived += OnOptions;
        chat.ChatEvent += OnPrivateMessage;
        combat.AttackEvent += ObserveAttack;
        combat.MagicEvent += ObserveMagic;
        return misc;
    }

    private void BuildMoreWindow(Control content)
    {
        More = new HudWindow(MoreId, "Knight Genie: More Options", new Vector2(240, 120), bodyMinWidth: 420) { Visible = false };
        More.SetHeaderAccent(new Color("10383b"), UiTheme.Gold, UiTheme.GoldBright);
        More.Closed += () => More.Visible = false;
        More.Body.AddChild(content);
        Layer.AddChild(More);
    }

    internal void OpenMore()
    {
        if (More == null) return;
        More.Visible = !More.Visible;
        if (More.Visible) More.MoveToFront();
    }

    private VBoxContainer Column(Node parent, string heading, GenieCellKind kind, int first, int count)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(column);
        column.AddChild(UiTheme.SectionTitle(heading));
        var grid = new GridContainer { Columns = GridColumns };
        column.AddChild(grid);
        for (int i = 0; i < count; i++)
        {
            int cell = first + i;
            var button = new Button { Text = "+", CustomMinimumSize = new Vector2(36, 36),
                ExpandIcon = true, FocusMode = Control.FocusModeEnum.None };
            var slotStyle = new StyleBoxFlat { BgColor = new Color("091719"), BorderColor = CellBorders[(int)kind] };
            slotStyle.SetBorderWidthAll(1);
            button.AddThemeStyleboxOverride("normal", slotStyle);
            var hoverStyle = (StyleBoxFlat)slotStyle.Duplicate();
            hoverStyle.BgColor = new Color("234044");
            button.AddThemeStyleboxOverride("hover", hoverStyle);
            button.AddThemeConstantOverride("icon_max_width", 30);
            button.Pressed += () => OpenSkillPicker(cell);
            WireSkillSlot(button, cell);
            slots[cell] = button;
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
            var partyToggle = new CheckBox
            {
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                TooltipText = $"{ItemData.Text(PartyText, "Party")}: also cast this skill on party members",
            };
            partyToggle.Toggled += on =>
            {
                if (controls.Syncing) return;
                Genie.SupportParty[index] = on;
                SetupEdited();
            };
            stack.AddChild(partyToggle);
            genieParty[index] = partyToggle;
            grid.AddChild(stack);
        }
        if (kind == GenieCellKind.Support)
        {
            var hint = UiTheme.Text("A ticked box under a cell also casts it on party members.", 10, UiTheme.TextLo);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.CustomMinimumSize = new Vector2(GridColumns * 40, 0);
            column.AddChild(hint);
        }
        return column;
    }

    private const int TargetCap = 8;

    internal void ToggleRun()
    {
        if (RequestPending) return;
        if (genieSession.GenieRunning) { StopAdvanced(); return; }
        if (context.Self.Dead || merchantStalls.BlocksMove() || !Game.Connection.Connected || Game.Connection.Reconnect.Blocking) return;
        SaveAdvanced();
        wantsRunning = true;
        RequestPending = true;
        requestAt = context.Now;
        start.Disabled = true;
        RunLabel.Text = "Waiting for server response…";
        context.Plugins.Genie();
        requests.System(GenieSystemCommand.Start);
    }

    internal bool AddTarget()
    {
        if (context.Selection.SelectedId < 0 || !context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e) || !e.IsMonster)
        {
            context.Log.Notice("Select a monster first.");
            return false;
        }
        if (Monsters.Contains(e.Name))
        {
            context.Log.Notice($"{e.Name} is already in the Genie attack list.");
            return true;
        }
        if (Monsters.Count >= TargetCap)
        {
            context.Log.Notice($"The Genie attack list is full ({TargetCap}).");
            return true;
        }
        Monsters.Add(e.Name);
        MonstersOff.Remove(e.Name);
        context.Log.Notice($"{e.Name} added to the Genie attack list.");
        RefreshMonsters();
        return true;
    }

    internal void RemoveTarget(string name)
    {
        Monsters.Remove(name);
        MonstersOff.Remove(name);
        RefreshMonsters();
    }

    internal void SetTargetOn(string name, bool on)
    {
        if (on) MonstersOff.Remove(name);
        else MonstersOff.Add(name);
        Changed?.Invoke();
    }

    internal List<string> TargetsInOrder() => Monsters.OrderBy(n => n).ToList();

    private void AddMode(Node parent, GenieMode mode, string text) =>
        modes[mode] = controls.Option(parent, text, on => Genie.Set(mode, on));

    private static (GenieCellKind Kind, int Index) GenieCellAt(int cell) =>
        cell < RecoveryFirstCell ? (GenieCellKind.Attack, cell)
    : cell < SupportFirstCell ? (GenieCellKind.Recovery, cell - RecoveryFirstCell)
    : (GenieCellKind.Support, cell - SupportFirstCell);

    internal int GenieCell(int cell)
    {
        var (kind, index) = GenieCellAt(cell);
        return Genie.Cells(kind)[index];
    }

    internal void SetCell(int cell, int skillId)
    {
        var (kind, index) = GenieCellAt(cell);
        Genie.Cells(kind)[index] = skillId;
    }

    internal void OpenSkillPicker(int cell)
    {
        editingSlot = cell;
        SkillPicker.Clear();
        SkillPicker.AddItem("Clear Slot", 0);
        foreach (var s in SkillData.ForClass(self.Class).OrderBy(s => s.Level))
            if (SkillAllowed(s, cell)) SkillPicker.AddIconItem(SkillIcons.Get(s.Id), s.Name, s.Id);
        SkillPicker.Position = (Vector2I)(slots[cell].GetGlobalRect().End);
        SkillPicker.Popup();
    }

    internal void RefreshSlots()
    {
        for (int i = 0; i < CellCount; i++)
        {
            var skill = SkillData.Get(GenieCell(i));
            int number = GenieCellAt(i).Index + 1;
            slots[i].Icon = skill == null ? null : SkillIcons.Get(skill.Id);
            slots[i].Text = skill == null ? "+" : slots[i].Icon == null ? number.ToString() : "";
            slots[i].TooltipText = skill == null ? "Select Skill" : SkillText.Tooltip(skill);
        }
    }

    private void RefreshMonsters()
    {
        monsterList.Clear();
        foreach (string name in Monsters.OrderBy(n => n)) monsterList.AddItem(name);
        Changed?.Invoke();
    }

    private void SyncControls()
    {
        controls.Syncing = true;
        foreach (var (mode, check) in modes) check.SetPressedNoSignal(Genie.Has(mode));
        petPotion.SetPressedNoSignal(Genie.Has(GenieMoreMode.PetPotion));
        hpThreshold.SetValueNoSignal(Genie.HpPercent);
        mpThreshold.SetValueNoSignal(Genie.MpPercent);
        recoverySpin.SetValueNoSignal(Genie.RecoveryPercent);
        petPercent.SetValueNoSignal(Genie.PetPercent);
        range.SetValueNoSignal(Genie.Range);
        returnHome.SetPressedNoSignal(!Genie.Has(GenieExtra.StayInPlace));
        hideRange.SetPressedNoSignal(Genie.Has(GenieExtra.HideRange));
        cureCurse.SetPressedNoSignal(Genie.Has(GenieExtra.CureCurse));
        huntInPlace.SetPressedNoSignal(Genie.Has(GenieExtra.HuntInPlace));
        for (int i = 0; i < GenieSetup.SupportCells; i++) genieParty[i].SetPressedNoSignal(Genie.SupportParty[i]);
        genieTown.SyncTownControls();
        genieHammer.SyncHammerControls();
        controls.Syncing = false;
        RefreshSlots();
    }

    internal void SetupEdited() => context.Plugins.Genie();

    internal void ApplySetup(GenieSetup setup)
    {
        ClearRefusedSkills(setup);
        Genie = setup;
        SyncControls();
        context.Plugins.Genie();
    }

    private void OnOptions(byte[] saved)
    {
        if (RequestPending || genieSession.GenieRunning) return;
        if (GenieSetup.FromBytes(saved, KindOf) is not { } setup) return;
        ApplySetup(setup);
        shownBytes = Genie.ToBytes();
    }

    private GenieCellKind KindOf(int skillId) =>
        SkillData.Get(skillId) is { } skill ? KindOf(skill) : GenieCellKind.Support;

    private static GenieCellKind KindOf(SkillData.Skill s) =>
        s.IsEnemy ? GenieCellKind.Attack
        : RestoresHp(s) || RestoresMp(s) ? GenieCellKind.Recovery
        : GenieCellKind.Support;

    private static bool RestoresHp(SkillData.Skill s) =>
        GenieRecovery.RestoresHp(s.IsFriendly, s.Type1, s.DirectType, s.FirstDamage, s.TimeDamage);

    private static bool RestoresMp(SkillData.Skill s) =>
        GenieRecovery.RestoresMp(s.IsFriendly, s.Type1, s.DirectType, s.FirstDamage);

    private void SaveAdvanced()
    {
        var config = new ConfigFile();
        var bytes = Genie.ToBytes();
        config.SetValue("genie", "options", bytes);
        config.SetValue("genie", "monsters", Monsters.ToArray());
        config.SetValue("genie", "auto_party", autoParty.ButtonPressed);
        config.SetValue("genie", "pt_code", ptCode.Text.Trim());
        geniePotions.SavePotionSettings(config);
        genieScrolls.SaveScrollSettings(config);
        if (config.Save(SettingsPath) != Error.Ok) context.Log.Notice("Could not save Genie settings to disk.");
        shownBytes = bytes;
        requests.System(GenieSystemCommand.SaveOptions, bytes);
    }

    private void SaveIfEdited()
    {
        if (!Genie.ToBytes().AsSpan().SequenceEqual(shownBytes)) SaveAdvanced();
    }

    private void LoadAdvanced()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) == Error.Ok)
        {
            geniePotions.LoadPotionSettings(config);
            genieScrolls.LoadScrollSettings(config);
            var saved = config.GetValue("genie", "options", Array.Empty<byte>()).AsByteArray();
            if (GenieSetup.FromBytes(saved, KindOf) is { } setup)
            {
                if (GenieOptionsBlob.LayoutOf(saved) < GenieOptionsBlob.CurrentLayout) GenieHammerFeature.CarryHammerSettings(config, setup);
                ClearRefusedSkills(setup);
                Genie = setup;
            }
            foreach (string name in config.GetValue("genie", "monsters", Array.Empty<string>()).AsStringArray())
                Monsters.Add(name);
            autoParty.ButtonPressed = config.GetValue("genie", "auto_party", false).AsBool();
            ptCode.Text = config.GetValue("genie", "pt_code", "").AsString();
        }
        shownBytes = Genie.ToBytes();
        SyncControls();
        RefreshMonsters();
        geniePotions.RefreshPotionSelectors();
    }

    private void OnSystemState(bool running, int minutes)
    {
        context.Entities.GenieAura(context.Self.CharId, running);
        if (running && !wantsRunning) { StopAdvanced(); return; }
        RequestPending = false;
        start.Disabled = false;
        if (running && !wasRunning)
        {
            origin = context.Self.Body.GlobalPosition;
            genieSupportAt.Clear();
            genieScrolls.ResetScrollWatch();
            pollAt = 0;
        }
        if (!running && wasRunning) HaltActions();
        if (running != wasRunning) genieTown.ResetTownWatch();
        wasRunning = running;
        if (!running) wantsRunning = false;
        deadline = context.Now + minutes * 60;
        start.Text = running ? "Stop" : "Start";
        Points = minutes;
        Changed?.Invoke();
        RunLabel.Text = running ? $"Genie active • {minutes} minutes" :
            minutes > 0 ? $"Genie stopped • {minutes} minutes" : "No time remaining. Spirit of Genie required.";
        context.Plugins.Genie();
    }

    private void HaltActions()
    {
        partyCodes.Clear();
        basicAttack.StopAutoAttack();
        if (moving) context.Movement.CancelMoveTo();
        moving = false;
        skillCast.CancelAllSelfCasts();
    }

    private void StopAdvanced()
    {
        HaltActions();
        genieSession.Reset();
        context.Entities.GenieAura(context.Self.CharId, false);
        wasRunning = false;
        wantsRunning = false;
        RequestPending = false;
        start.Disabled = false;
        start.Text = "Start";
        RunLabel.Text = "Genie stopped.";
        if (Game.Connection.Connected) requests.System(GenieSystemCommand.Stop);
        context.Plugins.Genie();
    }

    private void ConfigurePartyCode() => partyCodes.Configure(
        genieSession.GenieRunning && Game.Connection.Connected && !Game.Connection.Reconnect.Blocking && !context.Self.Dead
        && autoParty != null && autoParty.ButtonPressed,
        ptCode?.Text ?? "");

    bool IInviteAutoAccept.AcceptInvite(int inviterId, string name) => AcceptInvite(inviterId, name);

    internal bool AcceptInvite(int inviterId, string name)
    {
        ConfigurePartyCode();
        return partyCodes.TryAccept(inviterId, name, context.Now);
    }

    private void OnPrivateMessage(ChatLine line)
    {
        ConfigurePartyCode();
        if (!partyCodes.Receive(line.Type, line.CharId, line.Name, line.Message, context.Self.CharId, context.Now)) return;
        partyInvites.AcceptPendingInviteIf(AcceptInvite);
    }

    private void ObserveAttack(AttackReply reply) => ObserveLeader(reply.AttackerId, reply.TargetId);

    private void ObserveMagic(MagicReply reply)
    {
        if (reply.Sub is 1 or 3) ObserveLeader(reply.CasterId, reply.TargetId);
        if (reply.Sub == 3 && reply.CasterId == context.Self.CharId && genieSession.GenieRunning
            && (Genie.Support.Contains(reply.SkillId) || Genie.Recovery.Contains(reply.SkillId)) && SkillData.Get(reply.SkillId) is { } support)
            genieSupportAt[(reply.SkillId, reply.TargetId)] = context.Now + Math.Max(SupportRetryInterval, support.Duration > 0 ? support.Duration - SupportRetryInterval : support.RecastSeconds);
    }

    private void ObserveLeader(int source, int target)
    {
        if (party.InParty && !party.IsLeader(context.Self.CharId) && party.Members[0].CharId == source)
        { leaderTarget = target; leaderId = source; leaderSeenAt = context.Now; }
    }

    private void AdvancedDispose()
    {
        if (genieSession.GenieRunning || RequestPending) StopAdvanced();
        genieSession.Reset();
        genieSession.GenieSystemState -= OnSystemState;
        genieSession.GenieOptionsReceived -= OnOptions;
        partyCodes.Clear();
        chat.ChatEvent -= OnPrivateMessage;
        combat.AttackEvent -= ObserveAttack;
        combat.MagicEvent -= ObserveMagic;
    }

    private bool ValidMonster(int id) => context.Entities.Views.TryGetValue(id, out var e)
    && e.IsMonster && e.Attackable && !e.Dead
    && e.Body.GlobalPosition.DistanceTo(origin) <= Genie.Range
    && (Monsters.Count == 0 || (Monsters.Contains(e.Name) && !MonstersOff.Contains(e.Name)));

    private void AdvancedTick(double now)
    {
        RingTick();
        if (RequestPending && now - requestAt > StartReplyTimeout) StopAdvanced();
        if (!genieSession.GenieRunning)
        { if (wasRunning) StopAdvanced(); return; }
        if (context.Self.Dead || !Game.Connection.Connected || Game.Connection.Reconnect.Blocking || merchantStalls.BlocksMove() || now >= deadline)
        { StopAdvanced(); return; }
        if (now < pollAt) return;
        pollAt = now + PollInterval;
        genieHammer.HammerTick(now);
        geniePotions.FamiliarTick(now);
        if (now >= potionUiAt)
        { potionUiAt = now + PotionUiInterval; geniePotions.RefreshPotionSelectors(); }
        if (genieTown.TownTick()) return;
        if (Genie.Has(GenieMode.HpPotion) && GenieRecovery.Below(self.Vitals.Hp, self.Vitals.MaxHp, Genie.HpPercent))
            geniePotions.UsePotion(HealTarget.Hp);
        if (Genie.Has(GenieMode.MpPotion) && GenieRecovery.Below(self.Vitals.Mp, self.Vitals.MaxMp, Genie.MpPercent))
            geniePotions.UsePotion(HealTarget.Mp);
        if (skillCast.SelfCasting(now)) return;
        if (CureCurse(now)) return;
        if (Recover(now)) return;
        if (genieScrolls.ScrollTick(now)) return;
        if (Genie.Has(GenieMode.Support) && SupportTick(now)) return;
        if (!Genie.Has(GenieMode.Attack))
        { basicAttack.StopAutoAttack(); if (moving) context.Movement.CancelMoveTo(); moving = false; return; }
        int target = context.Selection.SelectedId;
        if (Genie.Has(GenieMode.LeaderTarget) && party.InParty && !party.IsLeader(context.Self.CharId))
            target = leaderId == party.Members[0].CharId && now - leaderSeenAt < LeaderTargetMemory && ValidMonster(leaderTarget) ? leaderTarget : -1;
        else if (!ValidMonster(target) || !CanReach(target))
            target = context.Entities.Views.Where(pair => ValidMonster(pair.Key) && CanReach(pair.Key))
                .OrderBy(pair => pair.Value.Body.GlobalPosition.DistanceSquaredTo(context.Self.Body.GlobalPosition))
                .Select(pair => pair.Key).DefaultIfEmpty(-1).First();
        if (target < 0)
        {
            basicAttack.StopAutoAttack();
            if (!Genie.Has(GenieExtra.StayInPlace) && context.Self.Body.GlobalPosition.DistanceTo(origin) > HomeSlack)
            {
                context.Movement.MoveTo(origin);
                moving = true;
            }
            else if (moving) { context.Movement.CancelMoveTo(); moving = false; }
            return;
        }
        if (context.Selection.SelectedId != target) context.Selection.Select(target);
        bool basic = Genie.Has(GenieMode.BasicAttack);
        bool basicInRange = basic && basicAttack.InBasicAttackRange(target);
        if (basicInRange && (!basicAttack.AutoAttack || basicAttack.AutoTargetId != target)) basicAttack.StartAutoAttack(target);
        if (!basicInRange && basicAttack.AutoAttack) basicAttack.StopAutoAttack();
        bool skillInRange = false;
        bool hasAttackSkill = false;
        foreach (int id in Genie.Attack)
        {
            var skill = SkillData.Get(id);
            if (skill == null || !skill.IsEnemy || !self.SkillLearned(skill)) continue;
            hasAttackSkill = true;
            if (!skillCast.InSkillRange(target, skill)) continue;
            skillInRange = true;
            if (!skillCast.SkillReady(skill, now) || !skillCast.CanCastWithGear(skill) || SkillHeld(skill.Id, now)) continue;
            context.Movement.CancelMoveTo(); moving = false;
            skillCast.CastSkill(skill.Id);
            return;
        }
        bool shouldMove = GenieApproach.ShouldMove(Genie.Has(GenieExtra.HuntInPlace), basic, basicInRange, hasAttackSkill, skillInRange);
        if (shouldMove)
        {
            context.Movement.MoveTo(context.Entities.Views[target].Body.GlobalPosition);
            moving = true;
        }
        else if (moving) { context.Movement.CancelMoveTo(); moving = false; }
    }

    bool IGenieCombo.ComboOn => Genie.Has(GenieMode.Combo);

    private bool CanReach(int target)
    {
        if (!Genie.Has(GenieExtra.HuntInPlace)) return true;
        if (target < 0 || !context.Entities.Views.ContainsKey(target)) return false;
        if (Genie.Has(GenieMode.BasicAttack) && basicAttack.InBasicAttackRange(target)) return true;
        foreach (int id in Genie.Attack)
            if (SkillData.Get(id) is { IsEnemy: true } skill && skillCast.InSkillRange(target, skill))
                return true;
        return false;
    }

    private void NoteRefusal(int skillId)
    {
        if (genieSession.GenieRunning) refusedUntil[skillId] = context.Now + RefusedSkillHold;
    }

    private bool SkillHeld(int skillId, double now) =>
        refusedUntil.TryGetValue(skillId, out double until) && now < until;

    private bool CureCurse(double now)
    {
        if (!Genie.Has(GenieExtra.CureCurse) || !context.Self.Cursed) return false;
        var skill = SkillData.Get(self.Class * ClassSkillBase + CureCurseSkillOffset);
        if (skill == null || !self.SkillLearned(skill) || !skillCast.SkillReady(skill, now)
            || SkillHeld(skill.Id, now) || !skillCast.CanCastWithGear(skill)) return false;
        if (genieSupportAt.TryGetValue((skill.Id, context.Self.CharId), out var next) && now < next) return false;
        CastOn(context.Self.CharId, skill.Id);
        genieSupportAt[(skill.Id, context.Self.CharId)] = now + SupportRetryInterval;
        return true;
    }

    private void CastOn(int target, int skillId)
    {
        context.Selection.WithSelected(target, () => skillCast.CastSkill(skillId));
    }

    private void RingTick()
    {
        if (range is null || Panel is null) return;
        bool show = context.Self.Body != null && !Genie.Has(GenieExtra.HideRange)
            && (genieSession.GenieRunning || Panel.Visible);
        if (!show)
        {
            if (ring != null) ring.Visible = false;
            return;
        }
        if (ring == null)
        {
            ring = new Decal
            {
                TextureAlbedo = ResourceLoader.Load<Texture2D>(RingTexture),
                CullMask = ItemShineLight.SceneryLayer,
            };
            context.Root.AddChild(ring);
        }
        float diameter = 2f * Genie.Range;
        ring.Size = new Vector3(diameter, RingDepth, diameter);
        ring.GlobalPosition = genieSession.GenieRunning ? origin : context.Self.Body!.GlobalPosition;
        ring.Visible = true;
    }

    private bool SupportTick(double now)
    {
        if (Support(context.Self.CharId, now)) return true;
        foreach (var member in party.Members)
            if (member.CharId != context.Self.CharId && member.Hp > 0 && context.Entities.Views.TryGetValue(member.CharId, out var ally)
                && !ally.Dead && !ally.Attackable && Support(member.CharId, now)) return true;
        return false;
    }

    private bool Support(int target, double now)
    {
        for (int i = 0; i < GenieSetup.SupportCells; i++)
        {
            if (target != context.Self.CharId && !Genie.SupportParty[i]) continue;
            var skill = SkillData.Get(Genie.Support[i]);
            if (skill == null || skill.IsEnemy || skill.IsDeadFriend || skill.IsGroundArea || skill.IsBlink
                || !self.SkillLearned(skill) || !skillCast.SkillReady(skill, now) || SkillHeld(skill.Id, now)) continue;
            if (target == context.Self.CharId && skill.Moral is not (SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party or SkillTarget.PartyAll)) continue;
            if (target != context.Self.CharId && (!skill.IsFriendly || skill.Moral == SkillTarget.Self || !skillCast.InSkillRange(target, skill))) continue;
            if (target == context.Self.CharId && skillCast.SelfBuffActive(skill, now)) continue;
            if (genieSupportAt.TryGetValue((skill.Id, target), out var next) && now < next) continue;
            if (!skillCast.CanCastWithGear(skill)) continue;
            CastOn(target, skill.Id);
            genieSupportAt[(skill.Id, target)] = now + SupportRetryInterval;
            return true;
        }
        return false;
    }

    private static readonly int[] RefusalTexts = { 31045, 31046, 31048 };

    private static readonly string[] RefusalFallbacks =
    {
        "This skill can be used on Attack tab > Attack Skill slot.",
        "This skill can be used on Attack tab > Recovery Skill slot.",
        "This skill can be used on Support tab.",
    };

    internal bool SkillAllowed(SkillData.Skill s, int cell)
    {
        if (s.IsPotion || s.IsDeadFriend || s.IsBlink || s.IsGroundArea || !HotbarAssign.CanDrag(s.Type1, s.Type2) || !self.SkillLearned(s)) return false;
        var kind = GenieCellAt(cell).Kind;
        if (KindOf(s) != kind) return false;
        return kind != GenieCellKind.Support
            || s.Moral is SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party or SkillTarget.PartyAll;
    }

    private static void ClearRefusedSkills(GenieSetup setup)
    {
        foreach (var cells in new[] { setup.Attack, setup.Recovery, setup.Support })
            HotbarAssign.ClearRefused(cells, id => HotbarFeature.Takes(id, dragged: true));
    }

    internal void RefuseSkill(SkillData.Skill s)
    {
        if (s.IsPotion || !HotbarAssign.CanDrag(s.Type1, s.Type2) || !self.SkillLearned(s)) return;
        int kind = (int)KindOf(s);
        context.Log.Notice(ItemData.Text(RefusalTexts[kind], RefusalFallbacks[kind]));
    }

    private void WireSkillSlot(Button button, int slot)
    {
        button.SetDragForwarding(
            Callable.From<Vector2, Variant>(_ => GenieDrag.Begin(button, GenieDrag.SkillSlotKey, slot, GenieCell(slot), SkillIcons.Get(GenieCell(slot)), genieSession.GenieRunning)),
            Callable.From<Vector2, Variant, bool>((_, data) => SkillDrop(data, slot) != 0),
            Callable.From<Vector2, Variant>((_, data) =>
            {
                int id = SkillDrop(data, slot);
                if (id == 0) return;
                MoveSkill(id, GenieDrag.TryDragSlot(data, GenieDrag.SkillSlotKey, out int from) ? from : -1, slot);
            }));
        button.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
            SetCell(slot, 0);
            RefreshSlots();
            SetupEdited();
            button.AcceptEvent();
        };
    }

    internal void MoveSkill(int id, int from, int to)
    {
        if (from >= 0 && from != to)
        {
            int displaced = GenieCell(to);
            SetCell(from, displaced != 0 && SkillData.Get(displaced) is { } back && SkillAllowed(back, from) ? displaced : 0);
        }
        SetCell(to, id);
        RefreshSlots();
        SetupEdited();
    }

    private int SkillDrop(Variant data, int slot)
    {
        if (genieSession.GenieRunning || !GenieDrag.DragId(data, out int id) || SkillData.Get(id) is not { } skill) return 0;
        return SkillAllowed(skill, slot) ? id : 0;
    }

    private double potionUiAt;

    private bool Recover(double now)
    {
        if (!Genie.Has(GenieMode.Recovery)) return false;
        bool lowHp = GenieRecovery.Below(self.Vitals.Hp, self.Vitals.MaxHp, Genie.RecoveryPercent);
        bool lowMp = GenieRecovery.Below(self.Vitals.Mp, self.Vitals.MaxMp, Genie.RecoveryPercent);
        if ((lowHp || lowMp) && RecoveryCast(context.Self.CharId, lowHp, lowMp, now)) return true;
        foreach (var member in party.Members)
            if (member.CharId != context.Self.CharId && member.Hp > 0 && context.Entities.Views.TryGetValue(member.CharId, out var ally)
                && !ally.Dead && !ally.Attackable && GenieRecovery.Below(member.Hp, member.MaxHp, Genie.RecoveryPercent)
                && RecoveryCast(member.CharId, true, false, now)) return true;
        return false;
    }

    private bool RecoveryCast(int target, bool hp, bool mp, double now)
    {
        foreach (int id in Genie.Recovery)
        {
            var skill = SkillData.Get(id);
            if (skill == null || !((hp && RestoresHp(skill)) || (mp && RestoresMp(skill)))) continue;
            if (!self.SkillLearned(skill) || !skillCast.SkillReady(skill, now) || SkillHeld(skill.Id, now) || !skillCast.CanCastWithGear(skill)) continue;
            if (target == context.Self.CharId && skill.Moral is not (SkillTarget.Self or SkillTarget.FriendWithMe or SkillTarget.Party or SkillTarget.PartyAll)) continue;
            if (target != context.Self.CharId && (skill.Moral == SkillTarget.Self || !skillCast.InSkillRange(target, skill))) continue;
            if (skill.FirstDamage <= 0 && skillCast.SelfBuffActive(skill, now)) continue;
            if (genieSupportAt.TryGetValue((skill.Id, target), out var next) && now < next) continue;
            if (target == context.Self.CharId && HealStrikes(skill)) skillCast.CastSkill(skill.Id);
            else CastOn(target, skill.Id);
            genieSupportAt[(skill.Id, target)] = now + SupportRetryInterval;
            return true;
        }
        return false;
    }

    private bool HealStrikes(SkillData.Skill skill) =>
        self.HealStrikesEnemies && !Genie.Has(GenieOther.DarkKnightMace)
    && skill.Moral == SkillTarget.FriendWithMe
    && HealStrike.StrikesWith(skill.Type1, skill.DirectType, skill.FirstDamage, skill.TimeDamage)
    && ValidMonster(context.Selection.SelectedId);

    internal int Points { get; set; }

    internal event Action? Changed;

}
