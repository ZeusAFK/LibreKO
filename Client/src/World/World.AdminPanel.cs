using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private static readonly int[] AdminCoinPresets = { 1_000_000, 10_000_000, 100_000_000 };

    private CanvasLayer _admLayer = null!;
    private HudWindow _admPanel = null!;
    private bool _admEnabled, _admShown;
    private AdminPanelGrant _admGrant;
    private AdminState _admState;

    private readonly Dictionary<string, Control> _admTabs = new();
    private readonly Dictionary<string, Button> _admTabBtns = new();
    private MarginContainer _admTabHost = null!;
    private Control _admTabPark = null!;

    private Label _admStatusLbl = null!;
    private Label _admCoinsLbl = null!, _admDerivedLbl = null!;
    private LineEdit _admCoinsInput = null!;
    private readonly SpinBox[] _admStatSpins = new SpinBox[CharacterSheet.StatCount];
    private SpinBox _admPointsSpin = null!, _admLevelSpin = null!;
    private Label _admClassLbl = null!;
    private VBoxContainer _admClassList = null!;

    private void AdminPanelInit()
    {
        Net.I.AdminGrantEvent += OnAdminGrant;
        Net.I.AdminStateEvent += OnAdminState;
        Net.I.AdminResultEvent += OnAdminResult;
        EnableAdminPanel(_isGm ? AdminPanelGrant.GameMaster : Net.I.PanelGrant);
    }

    private void OnAdminGrant(AdminPanelGrant grant) => EnableAdminPanel(grant);

    private void EnableAdminPanel(AdminPanelGrant grant)
    {
        if (_admEnabled || grant == AdminPanelGrant.None) return;
        _admGrant = grant;
        _admEnabled = true;
        SeedAdminState();
        BuildAdminPanel();
    }

    private void SeedAdminState()
    {
        var info = Net.I.LastEnter;
        _admState = new AdminState
        {
            Granted = true,
            Class = info.Class, Race = (byte)info.Race, Level = Sheet.Level,
            Str = Sheet.Str, Sta = Sheet.Sta, Dex = Sheet.Dex,
            Intel = Sheet.Intel, MagicStat = Sheet.Mag,
            StatPoints = Sheet.Points,
            MaxHp = Vitals.MaxHp, MaxMp = Vitals.MaxMp,
            Ap = Sheet.Ap, Ac = Sheet.Ac, Gold = Sheet.Gold,
            SkillPoints = Mastery.ToArray(),
            ClassOptions = System.Array.Empty<int>(),
        };
    }

    private void AdminPanelDispose()
    {
        Net.I.AdminGrantEvent -= OnAdminGrant;
        Net.I.AdminStateEvent -= OnAdminState;
        Net.I.AdminResultEvent -= OnAdminResult;
    }

    private void BuildAdminPanel()
    {
        _admLayer = new CanvasLayer { Layer = 74 };
        AddChild(_admLayer);

        _admPanel = new HudWindow(
            "admin_panel", "Game Master", new Vector2(150, 70),
            titleIcon: UiIcons.Get("system/lock")) { Visible = false };
        _admPanel.Closed += CloseAdminPanel;
        _admLayer.AddChild(_admPanel);

        var root = _admPanel.Body;
        root.AddThemeConstantOverride("separation", 7);

        string who = Net.I.LastEnter.Name is { Length: > 0 } name ? name : "GM";
        string how = _admGrant == AdminPanelGrant.PublicDemo
            ? "public demo grant"
            : "authority 0";
        root.AddChild(UiTheme.Text(
            $"{who} · {how} · every action is re-checked by the server", 11, UiTheme.TextDim));

        if (_isGm) root.AddChild(BuildAdminCollisionRow());

        var tabBar = new HFlowContainer();
        tabBar.AddThemeConstantOverride("h_separation", 4);
        tabBar.AddThemeConstantOverride("v_separation", 4);
        tabBar.CustomMinimumSize = new Vector2(640, 0);
        root.AddChild(tabBar);

        _admTabHost = new MarginContainer();
        root.AddChild(_admTabHost);
        _admTabPark = new Control { Visible = false };
        root.AddChild(_admTabPark);

        AddAdminTab(tabBar, "Character", BuildAdminCharacterTab());
        AddAdminTab(tabBar, "Items", BuildAdminItemsTab());
        AddAdminTab(tabBar, "Class", BuildAdminClassTab());
        AddAdminTab(tabBar, "Zones", BuildAdminZonesTab());
        AddAdminTab(tabBar, "World", BuildAdminWorldTab());
        AddAdminTab(tabBar, "Events", BuildAdminEventsTab());
        AddAdminTab(tabBar, "Players", BuildAdminPlayersTab());
        AddAdminTab(tabBar, "Spawns", BuildAdminSpawnsTab());
        AddAdminTab(tabBar, "Tools", BuildAdminToolsTab());

        root.AddChild(new HSeparator());
        _admStatusLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        _admStatusLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _admStatusLbl.CustomMinimumSize = new Vector2(640, 0);
        root.AddChild(_admStatusLbl);

        LoadAdminStatSpins();
        RefreshAdminCharacterTab();
        RefreshAdminClassTab();
        RefreshAdminZonesTab();
        SelectAdminTab("Character");
        Callable.From(_admPanel.ResetSize).CallDeferred();
    }

    private CheckButton? _admCollisionSwitch;

    private Control BuildAdminCollisionRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var label = UiTheme.Text("Collision", 12, UiTheme.TextLo);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);

        _admCollisionSwitch = new CheckButton
        {
            ButtonPressed = !_collisionsOff,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Walk through terrain objects, monsters and players (GM only) — /collision on|off",
        };
        _admCollisionSwitch.Toggled += on => SetCollisions(on);
        row.AddChild(_admCollisionSwitch);
        return row;
    }

    private void RefreshAdminCollisionSwitch()
    {
        if (_admCollisionSwitch == null || !GodotObject.IsInstanceValid(_admCollisionSwitch)) return;
        if (_admCollisionSwitch.ButtonPressed != !_collisionsOff)
            _admCollisionSwitch.SetPressedNoSignal(!_collisionsOff);
    }

    private void AddAdminTab(Container tabBar, string label, Control body)
    {
        _admTabPark.AddChild(body);
        _admTabs[label] = body;
        tabBar.AddChild(MakeSubTabButton(label, label, () => SelectAdminTab(label), _admTabBtns));
    }

    private void SelectAdminTab(string label)
    {
        if (!_admTabs.TryGetValue(label, out Control? next)) return;

        foreach (var (key, body) in _admTabs)
        {
            var wanted = key == label ? (Node)_admTabHost : _admTabPark;
            if (body.GetParent() == wanted) continue;
            body.GetParent()?.RemoveChild(body);
            wanted.AddChild(body);
        }
        next.Visible = true;
        foreach (var (key, button) in _admTabBtns) button.ButtonPressed = key == label;
        Callable.From(_admPanel.ResetSize).CallDeferred();

        if (label == "Zones") RefreshAdminZonesTab();
        if (label == "Spawns") RefreshAdminBossList();
        if (label != "Items") { HideItemTooltip(); return; }
        if (_admItemsLoaded) return;
        _admItemsLoaded = true;
        _admItemSearch.Refresh();
    }

    private Control BuildAdminCharacterTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);

        // 1. Character Identity & Vitals Overview Card
        var card = UiTheme.RowPanel();
        var cardBox = new VBoxContainer();
        cardBox.AddThemeConstantOverride("separation", 4);
        card.AddChild(cardBox);

        var vitalsHeader = new HBoxContainer();
        vitalsHeader.AddThemeConstantOverride("separation", 8);
        cardBox.AddChild(vitalsHeader);

        _admDerivedLbl = UiTheme.Text("", 13, UiTheme.GoldBright);
        _admDerivedLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vitalsHeader.AddChild(_admDerivedLbl);

        var fullHpBtn = new Button { Text = "Full HP/MP", FocusMode = Control.FocusModeEnum.None };
        fullHpBtn.AddThemeFontSizeOverride("font_size", 12);
        fullHpBtn.TooltipText = "Restore HP and MP to maximum (+hp)";
        fullHpBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("hp");
            SetAdminStatus("Restoring HP/MP…", false);
            Callable.From(Net.I.SendAdminStateRequest).CallDeferred();
        };
        vitalsHeader.AddChild(fullHpBtn);

        var refreshBtn = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Refresh state from server");
        refreshBtn.Pressed += () => Net.I.SendAdminStateRequest();
        vitalsHeader.AddChild(refreshBtn);

        box.AddChild(card);

        // 2. Level & Experience Section
        box.AddChild(UiTheme.SectionTitle("Level & Progression", UiIcons.Get("system/trophy")));

        var lvlRow = new HBoxContainer();
        lvlRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(lvlRow);

        lvlRow.AddChild(UiTheme.Text("Level:", 13, UiTheme.TextHi));
        _admLevelSpin = MakeAdminSpin(1, 83, 1);
        _admLevelSpin.Value = Sheet.Level > 0 ? Sheet.Level : 83;
        lvlRow.AddChild(_admLevelSpin);

        var setLvlBtn = new Button { Text = "Set Level", FocusMode = Control.FocusModeEnum.None };
        setLvlBtn.AddThemeFontSizeOverride("font_size", 12);
        setLvlBtn.Pressed += () =>
        {
            int lvl = (int)_admLevelSpin.Value;
            Net.I.SendGmCommand($"setlevel {lvl}");
            SetAdminStatus($"Setting level to {lvl}…", false);
        };
        lvlRow.AddChild(setLvlBtn);

        // Quick Level Presets
        var qkLvlRow = new HBoxContainer();
        qkLvlRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(qkLvlRow);
        qkLvlRow.AddChild(UiTheme.Text("Quick Level:", 12, UiTheme.TextLo));

        int[] levelPresets = { 1, 60, 70, 80, 83 };
        foreach (int targetLvl in levelPresets)
        {
            int lvl = targetLvl;
            string lbl = lvl == 83 ? "Lv 83 (Max)" : $"Lv {lvl}";
            var btn = new Button { Text = lbl, FocusMode = Control.FocusModeEnum.None };
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.Pressed += () =>
            {
                _admLevelSpin.Value = lvl;
                Net.I.SendGmCommand($"setlevel {lvl}");
                SetAdminStatus($"Setting level to {lvl}…", false);
            };
            qkLvlRow.AddChild(btn);
        }

        // Experience Boost Presets
        var expRow = new HBoxContainer();
        expRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(expRow);
        expRow.AddChild(UiTheme.Text("Add EXP:", 12, UiTheme.TextLo));

        (string expLabel, long expAmount)[] expPresets =
        {
            ("+100K", 100_000L),
            ("+1M", 1_000_000L),
            ("+10M", 10_000_000L),
            ("+100M", 100_000_000L),
        };
        foreach (var (expText, amt) in expPresets)
        {
            long award = amt;
            var btn = new Button { Text = expText, FocusMode = Control.FocusModeEnum.None };
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.Pressed += () =>
            {
                Net.I.SendGmCommand($"exp {award}");
                SetAdminStatus($"Adding {award:n0} EXP…", false);
                Callable.From(Net.I.SendAdminStateRequest).CallDeferred();
            };
            expRow.AddChild(btn);
        }

        box.AddChild(new HSeparator());

        // 3. Stats & Free Points Section
        box.AddChild(UiTheme.SectionTitle("Attributes & Stat Points", UiIcons.Get("game/chest")));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        box.AddChild(grid);
        for (int i = 0; i < CharacterSheet.StatCount; i++)
        {
            var name = UiTheme.Text(StatLabels[i], 13, UiTheme.TextHi);
            name.CustomMinimumSize = new Vector2(52, 0);
            grid.AddChild(name);
            _admStatSpins[i] = MakeAdminSpin(1, 255, 1);
            grid.AddChild(_admStatSpins[i]);
        }
        grid.AddChild(UiTheme.Text("Free", 13, UiTheme.TextHi));
        _admPointsSpin = MakeAdminSpin(0, 10_000, 1);
        grid.AddChild(_admPointsSpin);

        // Stat Quick Modifiers
        var batchRow1 = new HBoxContainer();
        batchRow1.AddThemeConstantOverride("separation", 5);
        box.AddChild(batchRow1);
        batchRow1.AddChild(UiTheme.Text("Stat Presets:", 12, UiTheme.TextLo));

        var maxStatsBtn = new Button { Text = "Max All (255)", FocusMode = Control.FocusModeEnum.None };
        maxStatsBtn.AddThemeFontSizeOverride("font_size", 12);
        maxStatsBtn.Pressed += () =>
        {
            for (int i = 0; i < CharacterSheet.StatCount; i++) _admStatSpins[i].Value = 255;
        };
        batchRow1.AddChild(maxStatsBtn);

        var baseStatsBtn = new Button { Text = "Base Stats (50)", FocusMode = Control.FocusModeEnum.None };
        baseStatsBtn.AddThemeFontSizeOverride("font_size", 12);
        baseStatsBtn.Pressed += () =>
        {
            for (int i = 0; i < CharacterSheet.StatCount; i++) _admStatSpins[i].Value = 50;
        };
        batchRow1.AddChild(baseStatsBtn);

        var add10Btn = new Button { Text = "+10 All", FocusMode = Control.FocusModeEnum.None };
        add10Btn.AddThemeFontSizeOverride("font_size", 12);
        add10Btn.Pressed += () =>
        {
            for (int i = 0; i < CharacterSheet.StatCount; i++)
                _admStatSpins[i].Value = Mathf.Clamp((int)_admStatSpins[i].Value + 10, 1, 255);
        };
        batchRow1.AddChild(add10Btn);

        var batchRow2 = new HBoxContainer();
        batchRow2.AddThemeConstantOverride("separation", 5);
        box.AddChild(batchRow2);
        batchRow2.AddChild(UiTheme.Text("Free Points:", 12, UiTheme.TextLo));

        var p100Btn = new Button { Text = "+100 Free", FocusMode = Control.FocusModeEnum.None };
        p100Btn.AddThemeFontSizeOverride("font_size", 12);
        p100Btn.Pressed += () => _admPointsSpin.Value = Mathf.Clamp((int)_admPointsSpin.Value + 100, 0, 10_000);
        batchRow2.AddChild(p100Btn);

        var p500Btn = new Button { Text = "+500 Free", FocusMode = Control.FocusModeEnum.None };
        p500Btn.AddThemeFontSizeOverride("font_size", 12);
        p500Btn.Pressed += () => _admPointsSpin.Value = Mathf.Clamp((int)_admPointsSpin.Value + 500, 0, 10_000);
        batchRow2.AddChild(p500Btn);

        var clearFreeBtn = new Button { Text = "Clear Free (0)", FocusMode = Control.FocusModeEnum.None };
        clearFreeBtn.AddThemeFontSizeOverride("font_size", 12);
        clearFreeBtn.Pressed += () => _admPointsSpin.Value = 0;
        batchRow2.AddChild(clearFreeBtn);

        var statActionRow = new HBoxContainer();
        statActionRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(statActionRow);

        var applyStats = new Button { Text = "Apply Stats", FocusMode = Control.FocusModeEnum.None };
        applyStats.Pressed += OnAdminApplyStats;
        statActionRow.AddChild(applyStats);

        var revertStats = new Button { Text = "Revert", FocusMode = Control.FocusModeEnum.None };
        revertStats.Pressed += LoadAdminStatSpins;
        statActionRow.AddChild(revertStats);

        box.AddChild(new HSeparator());

        // 4. Noah / Gold Section
        box.AddChild(UiTheme.SectionTitle("Noah / Gold", UiIcons.Get("system/coins")));
        _admCoinsLbl = UiTheme.Text("", 13, UiTheme.GoldBright);
        box.AddChild(_admCoinsLbl);

        var coinRow = new HBoxContainer();
        coinRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(coinRow);
        _admCoinsInput = new LineEdit
        {
            PlaceholderText = "amount",
            Text = "1000000",
            CustomMinimumSize = new Vector2(112, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        coinRow.AddChild(_admCoinsInput);
        var giveCoins = new Button { Text = "Give", FocusMode = Control.FocusModeEnum.None };
        giveCoins.Pressed += () => SendAdminCoinDelta(1);
        coinRow.AddChild(giveCoins);
        var takeCoins = new Button { Text = "Take", FocusMode = Control.FocusModeEnum.None };
        takeCoins.Pressed += () => SendAdminCoinDelta(-1);
        coinRow.AddChild(takeCoins);

        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(presetRow);
        foreach (int preset in AdminCoinPresets)
        {
            int amount = preset;
            var button = new Button { Text = $"+{FormatCoinShort(amount)}", FocusMode = Control.FocusModeEnum.None };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => Net.I.SendAdminCoins(amount);
            presetRow.AddChild(button);
        }

        var maxGoldBtn = new Button { Text = "Max Gold (2.1B)", FocusMode = Control.FocusModeEnum.None };
        maxGoldBtn.AddThemeFontSizeOverride("font_size", 12);
        maxGoldBtn.Pressed += () => Net.I.SendAdminCoins(int.MaxValue);
        presetRow.AddChild(maxGoldBtn);

        var zeroGoldBtn = new Button { Text = "Zero Gold (0)", FocusMode = Control.FocusModeEnum.None };
        zeroGoldBtn.AddThemeFontSizeOverride("font_size", 12);
        zeroGoldBtn.Pressed += () => Net.I.SendAdminCoins(-int.MaxValue);
        presetRow.AddChild(zeroGoldBtn);

        return box;
    }

    private Control BuildAdminClassTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Current Specialization", UiIcons.Get("game/main-hand")));

        var currentCard = UiTheme.RowPanel();
        var cardLine = new HBoxContainer();
        cardLine.AddThemeConstantOverride("separation", 8);
        currentCard.AddChild(cardLine);

        _admClassLbl = UiTheme.Text("", 13, UiTheme.GoldBright);
        _admClassLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cardLine.AddChild(_admClassLbl);

        cardLine.AddChild(UiTheme.Pill("Active", UiTheme.Gold));
        box.AddChild(currentCard);

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.SectionTitle("Available Class Specializations"));

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(640, 340),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);

        _admClassList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _admClassList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_admClassList);

        var note = UiTheme.Text(
            "Changing class refunds every mastery point, resets stats according to class base, clears skill branches and empties the skill bar.",
            11, UiTheme.Warning);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(640, 0);
        box.AddChild(note);

        return box;
    }

    private static SpinBox MakeAdminSpin(int min, int max, int step) =>
        UiTheme.NumberBox(min, max, step, 96);

    private void ToggleAdminPanel()
    {
        if (!_admEnabled)
        {
            if (_isGm || Net.I.PanelGrant != AdminPanelGrant.None)
                EnableAdminPanel(_isGm ? AdminPanelGrant.GameMaster : Net.I.PanelGrant);

            if (!_admEnabled)
            {
                Chat.Info("GM Panel is not available for this account (Authority required).");
                return;
            }
        }
        if (_admShown) { CloseAdminPanel(); return; }
        _admShown = true;
        _admPanel.Visible = true;
        _admPanel.GetParent()?.MoveChild(_admPanel, _admPanel.GetParent().GetChildCount() - 1);
        SetAdminStatus("Requesting state…", false);
        Net.I.SendAdminStateRequest();
    }

    private void CloseAdminPanel()
    {
        if (!_admShown) return;
        _admShown = false;
        _admPanel.Visible = false;
        HideItemTooltip();
    }

    private void OnAdminState(AdminState state)
    {
        if (!_admEnabled) return;
        if (!state.Granted)
        {
            CloseAdminPanel();
            Chat.Info("The server refused the GM panel for this account.");
            return;
        }

        int previousClass = _admState.Class;
        int previousRace = _admState.Race;
        _admState = state;

        Sheet.ApplyLevel(state.Level, state.StatPoints, Sheet.Exp, Sheet.MaxExp);
        Sheet.ApplyReset(
            new[] { state.Str, state.Sta, state.Dex, state.Intel, state.MagicStat },
            state.StatPoints, state.Ap);
        Sheet.SetGold(state.Gold);
        Vitals.ApplyMaxima(state.MaxHp, state.MaxMp);
        _hpBar?.Set(Vitals.Hp, Vitals.MaxHp);
        _mpBar?.Set(Vitals.Mp, Vitals.MaxMp);
        SyncMasteryPoints(state.SkillPoints);
        UpdateLevelOrb();

        if (state.Class != previousClass || (state.Race > 0 && state.Race != _selfRace))
        {
            ApplyClassChange(state.Class, state.Race);
        }
        else
        {
            _selfClass = state.Class;
            RefreshStatsUI();
        }

        LoadAdminStatSpins();
        RefreshAdminCharacterTab();
        RefreshAdminClassTab();
        if (state.Class != previousClass && _admItemsLoaded) _admItemSearch.Refresh();
    }

    private void OnAdminResult(bool ok, string message)
    {
        if (!_admEnabled) return;
        SetAdminStatus(message, !ok);
        if (!ok) Chat.Info(message);
    }

    private void RefreshAdminCharacterTab()
    {
        if (_admCoinsLbl == null) return;
        _admCoinsLbl.Text = $"{_admState.Gold:n0} coins";
        string who = Net.I.LastEnter.Name is { Length: > 0 } name ? name : "GM";
        string spec = CharacterClassCatalog.SpecializationName(_admState.Class);
        string nat = Nations.Name(Net.I.LastEnter.Nation);
        _admDerivedLbl.Text =
            $"{who} · Lv {_admState.Level} {spec} ({nat})   |   HP {_admState.MaxHp:n0}   MP {_admState.MaxMp:n0}   AP {_admState.Ap:n0}   AC {_admState.Ac:n0}";
        if (_admLevelSpin != null) _admLevelSpin.Value = _admState.Level;
    }

    private void RefreshAdminClassTab()
    {
        if (_admClassLbl == null) return;
        _admClassLbl.Text =
            $"{CharacterClassCatalog.SpecializationName(_admState.Class)}  (ID: {_admState.Class})" +
            $"   ·   {CharacterClassCatalog.TierName(_admState.Class)}" +
            $"   ·   {Nations.Name(Net.I.LastEnter.Nation)}";

        foreach (Node child in _admClassList.GetChildren()) child.QueueFree();

        if (_admState.ClassOptions is not { Length: > 0 })
        {
            _admClassList.AddChild(UiTheme.Text(
                "The server offers no alternate specialization for this class.", 12, UiTheme.TextLo));
            return;
        }

        var families = new Dictionary<int, List<int>>();
        foreach (int opt in _admState.ClassOptions)
        {
            int fam = CharacterClassCatalog.Family(opt);
            if (!families.TryGetValue(fam, out var list))
            {
                list = new List<int>();
                families[fam] = list;
            }
            list.Add(opt);
        }

        foreach (var (famId, members) in families)
        {
            string famName = famId switch
            {
                1 => "Warrior Specializations",
                2 => "Rogue Specializations",
                3 => "Magician Specializations",
                4 => "Priest Specializations",
                5 => "Kurian / Porutu Specializations",
                _ => $"Class Family {famId}",
            };

            var header = UiTheme.SectionTitle(famName, UiIcons.Get("game/main-hand"));
            _admClassList.AddChild(header);

            members.Sort((a, b) => CharacterClassCatalog.Tier(a).CompareTo(CharacterClassCatalog.Tier(b)));

            foreach (int option in members)
            {
                int target = option;
                var row = UiTheme.RowPanel();
                var line = new HBoxContainer();
                line.AddThemeConstantOverride("separation", 8);
                row.AddChild(line);

                var name = UiTheme.Text(
                    $"{CharacterClassCatalog.SpecializationName(target)}  (ID: {target})", 13, UiTheme.TextHi);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                line.AddChild(name);

                int tier = CharacterClassCatalog.Tier(target);
                Color tierColor = tier switch
                {
                    CharacterClassCatalog.TierMaster => UiTheme.Gold,
                    CharacterClassCatalog.TierNovice => UiTheme.TextHi,
                    _ => UiTheme.TextLo,
                };
                line.AddChild(UiTheme.Pill(CharacterClassCatalog.TierName(target), tierColor));

                if (target == _admState.Class)
                {
                    line.AddChild(UiTheme.Pill("Current", UiTheme.GoldBright));
                }
                else
                {
                    var change = new Button { Text = "Switch Class", FocusMode = Control.FocusModeEnum.None };
                    change.AddThemeFontSizeOverride("font_size", 12);
                    change.Pressed += () =>
                    {
                        Net.I.SendAdminSetClass(target);
                        SetAdminStatus($"Switching class to {CharacterClassCatalog.SpecializationName(target)}…", false);
                    };
                    line.AddChild(change);
                }

                _admClassList.AddChild(row);
            }
        }
    }

    private void LoadAdminStatSpins()
    {
        if (_admStatSpins[0] == null) return;
        int[] values = { _admState.Str, _admState.Sta, _admState.Dex, _admState.Intel, _admState.MagicStat };
        for (int i = 0; i < CharacterSheet.StatCount; i++)
            _admStatSpins[i].Value = Mathf.Clamp(values[i], 1, 255);
        _admPointsSpin.Value = Mathf.Clamp(_admState.StatPoints, 0, 10_000);
    }

    private void OnAdminApplyStats()
    {
        Net.I.SendAdminStats(
            (int)_admStatSpins[0].Value, (int)_admStatSpins[1].Value, (int)_admStatSpins[2].Value,
            (int)_admStatSpins[3].Value, (int)_admStatSpins[4].Value, (int)_admPointsSpin.Value);
        SetAdminStatus("Applying stats…", false);
    }

    private void SendAdminCoinDelta(int sign)
    {
        if (!long.TryParse(_admCoinsInput.Text.Trim().Replace(",", ""), out long amount) || amount == 0)
        {
            SetAdminStatus("Enter a coin amount.", true);
            return;
        }
        long signed = System.Math.Clamp(amount * sign, int.MinValue, int.MaxValue);
        Net.I.SendAdminCoins((int)signed);
    }

    private void SetAdminStatus(string text, bool warn)
    {
        if (_admStatusLbl == null) return;
        _admStatusLbl.Text = text;
        _admStatusLbl.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private static string FormatCoinShort(int amount) => amount switch
    {
        >= 1_000_000 => $"{amount / 1_000_000}M",
        >= 1_000 => $"{amount / 1_000}K",
        _ => amount.ToString(),
    };
}
