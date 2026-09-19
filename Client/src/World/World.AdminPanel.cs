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
    private SpinBox _admPointsSpin = null!;
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
            Class = info.Class, Level = Sheet.Level,
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

        var tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 4);
        root.AddChild(tabBar);

        _admTabHost = new MarginContainer();
        root.AddChild(_admTabHost);
        _admTabPark = new Control { Visible = false };
        root.AddChild(_admTabPark);

        AddAdminTab(tabBar, "Character", BuildAdminCharacterTab());
        AddAdminTab(tabBar, "Items", BuildAdminItemsTab());
        AddAdminTab(tabBar, "Class", BuildAdminClassTab());
        AddAdminTab(tabBar, "Zones", BuildAdminZonesTab());

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

    private void AddAdminTab(HBoxContainer tabBar, string label, Control body)
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
        if (label != "Items") { HideItemTooltip(); return; }
        if (_admItemsLoaded) return;
        _admItemsLoaded = true;
        _admItemSearch.Refresh();
    }

    private Control BuildAdminCharacterTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Coins", UiIcons.Get("system/coins")));
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

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.SectionTitle("Stats", UiIcons.Get("game/chest")));

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

        _admDerivedLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(_admDerivedLbl);

        var statRow = new HBoxContainer();
        statRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(statRow);
        var applyStats = new Button { Text = "Apply stats", FocusMode = Control.FocusModeEnum.None };
        applyStats.Pressed += OnAdminApplyStats;
        statRow.AddChild(applyStats);
        var revertStats = new Button { Text = "Revert", FocusMode = Control.FocusModeEnum.None };
        revertStats.Pressed += LoadAdminStatSpins;
        statRow.AddChild(revertStats);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Re-read state from the server");
        refresh.Pressed += () => Net.I.SendAdminStateRequest();
        statRow.AddChild(refresh);

        return box;
    }

    private Control BuildAdminClassTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Specialization", UiIcons.Get("game/main-hand")));
        _admClassLbl = UiTheme.Text("", 14, UiTheme.GoldBright);
        box.AddChild(_admClassLbl);

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.SectionTitle("Valid changes for this class"));
        _admClassList = new VBoxContainer();
        _admClassList.AddThemeConstantOverride("separation", 4);
        box.AddChild(_admClassList);

        var note = UiTheme.Text(
            "A change refunds every mastery point, clears the branches and empties the skill bar.",
            11, UiTheme.Warning);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(340, 0);
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
        if (state.Class != previousClass) ApplyClassChange(state.Class);
        else { _selfClass = state.Class; RefreshStatsUI(); }

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
        _admDerivedLbl.Text =
            $"HP {_admState.MaxHp:n0}   MP {_admState.MaxMp:n0}   AP {_admState.Ap:n0}   AC {_admState.Ac:n0}" +
            $"   ·   Lv {_admState.Level}";
    }

    private void RefreshAdminClassTab()
    {
        if (_admClassLbl == null) return;
        _admClassLbl.Text =
            $"{CharacterClassCatalog.SpecializationName(_admState.Class)}  ({_admState.Class})" +
            $"   ·   {CharacterClassCatalog.TierName(_admState.Class)}" +
            $"   ·   {Nations.Name(Net.I.LastEnter.Nation)}";

        foreach (Node child in _admClassList.GetChildren()) child.QueueFree();

        if (_admState.ClassOptions is not { Length: > 0 })
        {
            _admClassList.AddChild(UiTheme.Text(
                "The server offers no alternate specialization for this class.", 12, UiTheme.TextLo));
            return;
        }

        foreach (int option in _admState.ClassOptions)
        {
            int target = option;
            var row = UiTheme.RowPanel();
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var name = UiTheme.Text(
                $"{CharacterClassCatalog.SpecializationName(target)}  ({target})", 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(name);
            line.AddChild(UiTheme.Pill(CharacterClassCatalog.TierName(target), UiTheme.Gold));

            var change = new Button { Text = "Change", FocusMode = Control.FocusModeEnum.None };
            change.AddThemeFontSizeOverride("font_size", 12);
            change.Pressed += () => Net.I.SendAdminSetClass(target);
            line.AddChild(change);

            _admClassList.AddChild(row);
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
