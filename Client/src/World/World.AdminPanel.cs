using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private static readonly int[] AdminCoinPresets = { 1_000_000, 10_000_000, 100_000_000 };

    private const float AdminLabelWidth = 58;
    private const float AdminSpinWidth = 110;
    private const float AdminColumnGap = 32;
    private const float AdminControlHeight = 30;

    private CanvasLayer _admLayer = null!;
    private HudWindow _admPanel = null!;
    private bool _admEnabled, _admShown;
    private AdminState _admState;

    private readonly Dictionary<string, Control> _admTabs = new();
    private readonly Dictionary<string, Button> _admTabBtns = new();
    private MarginContainer _admTabHost = null!;
    private Control _admTabPark = null!;

    private Label _admStatusLbl = null!;
    private Label _admCoinsLbl = null!;
    private LineEdit _admCoinsInput = null!;
    private readonly SpinBox[] _admStatSpins = new SpinBox[CharacterSheet.StatCount];
    private SpinBox _admPointsSpin = null!;
    private SpinBox _admNpSpin = null!;
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
            Loyalty = Sheet.Np,
            SkillPoints = Mastery.ToArray(),
            ClassOptions = System.Array.Empty<int>(),
            Nation = info.Nation, Race = info.Race,
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
        root.AddThemeConstantOverride("separation", 9);

        var tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 5);
        root.AddChild(tabBar);
        root.AddChild(UiTheme.Rule());

        _admTabHost = new MarginContainer();
        root.AddChild(_admTabHost);
        _admTabPark = new Control { Visible = false };
        root.AddChild(_admTabPark);

        AddAdminTab(tabBar, "Character", BuildAdminCharacterTab());
        AddAdminTab(tabBar, "Items", BuildAdminItemsTab());
        AddAdminTab(tabBar, "Class", BuildAdminClassTab());
        AddAdminTab(tabBar, "Skills", BuildAdminSkillsTab());
        AddAdminTab(tabBar, "Zones", BuildAdminZonesTab());
        AddAdminTab(tabBar, "Races", BuildAdminCollectionRaceTab());

        _admStatusLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        _admStatusLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _admStatusLbl.CustomMinimumSize = new Vector2(640, 0);
        root.AddChild(_admStatusLbl);

        LoadAdminStatSpins();
        LoadAdminSkillSpins();
        RefreshAdminLevelSpin();
        RefreshAdminCharacterTab();
        RefreshAdminClassTab();
        SyncAdminLookPicks();
        RefreshAdminZonesTab();
        SelectAdminTab("Character");
        Callable.From(_admPanel.ResetSize).CallDeferred();
    }

    private CheckButton? _admCollisionSwitch;

    private Control BuildAdminCollisionRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(AdminFieldLabel("Collision", 0));

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
        var button = UiTheme.UnderlineTabButton(label);
        button.Pressed += () => SelectAdminTab(label);
        _admTabBtns[label] = button;
        tabBar.AddChild(button);
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
        if (label == "Skills") LoadAdminSkillSpins();
        if (label == "Class") SyncAdminLookPicks();
        if (label == "Races") Net.I?.SendAdminCollectionRacesRequest();
        if (label != "Items") { HideItemTooltip(); return; }
        if (_admItemsLoaded) return;
        _admItemsLoaded = true;
        _admItemSearch.Refresh();
    }

    private Control BuildAdminCharacterTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 10);

        box.AddChild(BuildAdminCoinRow());
        box.AddChild(UiTheme.Rule());

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 14);
        box.AddChild(columns);
        columns.AddChild(BuildAdminStatsSection());
        columns.AddChild(UiTheme.Rule(vertical: true));
        var level = BuildAdminLevelSection();
        level.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(level);

        return box;
    }

    private Control BuildAdminCoinRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 7);

        row.AddChild(AdminHeading("Coins", "system/coins"));
        _admCoinsLbl = UiTheme.Text("", 15, UiTheme.TextHi);
        _admCoinsLbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_admCoinsLbl);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(4, 0) });

        foreach (int preset in AdminCoinPresets)
        {
            int amount = preset;
            var button = AdminButton($"+{FormatCoinShort(amount)}");
            button.Pressed += () => Net.I.SendAdminCoins(amount);
            row.AddChild(button);
        }

        _admCoinsInput = new LineEdit
        {
            PlaceholderText = "amount",
            Text = "1000000",
            CustomMinimumSize = new Vector2(96, AdminControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _admCoinsInput.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(_admCoinsInput);

        var give = AdminButton("Give", 60);
        give.Pressed += () => SendAdminCoinDelta(1);
        row.AddChild(give);
        var take = AdminButton("Take", 60);
        take.Pressed += () => SendAdminCoinDelta(-1);
        row.AddChild(take);
        return row;
    }

    private Control BuildAdminStatsSection()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 7);
        box.AddChild(AdminHeading("Stats", "game/chest"));

        for (int i = 0; i < CharacterSheet.StatCount; i++)
            _admStatSpins[i] = AdminSpin(1, 255, AdminSpinWidth);
        _admPointsSpin = AdminSpin(0, 10_000, AdminSpinWidth);

        int rows = (CharacterSheet.StatCount + 1) / 2;
        for (int i = 0; i < rows; i++)
        {
            int right = i + rows;
            box.AddChild(right < CharacterSheet.StatCount
                ? AdminStatRow(StatLabels[i], _admStatSpins[i], StatLabels[right], _admStatSpins[right])
                : AdminStatRow(StatLabels[i], _admStatSpins[i], "Free", _admPointsSpin));
        }

        var npRow = new HBoxContainer();
        npRow.AddThemeConstantOverride("separation", 0);
        npRow.AddChild(AdminFieldLabel("NP"));
        _admNpSpin = AdminSpin(0, int.MaxValue, AdminSpinWidth * 2 + AdminColumnGap + AdminLabelWidth);
        _admNpSpin.TooltipText = "National points";
        npRow.AddChild(_admNpSpin);
        box.AddChild(npRow);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 7);
        box.AddChild(actions);
        var apply = AdminButton("Apply stats", 108);
        apply.Pressed += OnAdminApplyStats;
        actions.AddChild(apply);
        var revert = AdminButton("Revert", 76);
        revert.Pressed += LoadAdminStatSpins;
        actions.AddChild(revert);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Re-read state from the server");
        refresh.CustomMinimumSize = new Vector2(36, AdminControlHeight);
        refresh.Pressed += () => Net.I.SendAdminStateRequest();
        actions.AddChild(refresh);

        return box;
    }

    private static HBoxContainer AdminStatRow(string leftName, SpinBox left, string rightName, SpinBox right)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        row.AddChild(AdminFieldLabel(leftName));
        row.AddChild(left);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(AdminColumnGap, 0) });
        row.AddChild(AdminFieldLabel(rightName));
        row.AddChild(right);
        return row;
    }

    private static HBoxContainer AdminHeading(string text, string icon, int size = 15)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 7);
        row.AddChild(UiIcons.Image(icon, new Vector2(size + 3, size + 3), UiTheme.GoldVivid));
        var label = UiTheme.Text(text, size, UiTheme.GoldVivid);
        label.AddThemeFontOverride("font", UiTheme.Strong);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        return row;
    }

    private static Label AdminFieldLabel(string text, float width = AdminLabelWidth)
    {
        var label = UiTheme.Text(text, 15, UiTheme.TextHi);
        label.CustomMinimumSize = new Vector2(width, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static SpinBox AdminSpin(int min, int max, float width) =>
        UiTheme.NumberBox(min, max, 1, width, 14);

    private static Button AdminButton(string text, float minWidth = 0)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(minWidth, AdminControlHeight),
        };
        button.AddThemeFontSizeOverride("font_size", 15);
        return button;
    }

    private Control BuildAdminClassTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Specialization", UiIcons.Get("game/main-hand")));
        _admClassLbl = UiTheme.Text("", 14, UiTheme.GoldBright);
        box.AddChild(_admClassLbl);

        box.AddChild(BuildAdminTransformSection());

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
        if (!_admEnabled) return;
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
        LoadAdminSkillSpins();
        RefreshAdminLevelSpin();
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
        _admCoinsLbl.Text = $"{_admState.Gold:n0}";
    }

    private void RefreshAdminClassTab()
    {
        if (_admClassLbl == null) return;
        _admClassLbl.Text =
            $"{CharacterClassCatalog.SpecializationName(_admState.Class)}  ({_admState.Class})" +
            $"   ·   {CharacterClassCatalog.TierName(_admState.Class)}" +
            $"   ·   {Nations.Name(_admState.Nation)}";

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
        _admNpSpin.Value = System.Math.Max(0, _admState.Loyalty);
    }

    private void OnAdminApplyStats()
    {
        Net.I.SendAdminStats(
            (int)_admStatSpins[0].Value, (int)_admStatSpins[1].Value, (int)_admStatSpins[2].Value,
            (int)_admStatSpins[3].Value, (int)_admStatSpins[4].Value, (int)_admPointsSpin.Value,
            (int)_admNpSpin.Value);
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
