using Godot;
using static LibreKO.Features.Windows.AdminPanelUi;

namespace LibreKO.Features.Windows;

internal sealed class AdminCharacterTab
{
    private static readonly int[] CoinPresets = { 1_000_000, 10_000_000, 100_000_000 };

    private static readonly int[] CashPresets = { 1_000, 10_000, 100_000 };

    private const int StatMin = 1;
    private const int FreePointsMax = 10_000;

    private readonly IAdminRequests requests;
    private readonly SelfSession self;
    private readonly IMovement movement;
    private readonly Func<bool> isGm;
    private readonly Func<AdminState> state;
    private readonly Action<string, bool> setStatus;

    public AdminCharacterTab(IAdminRequests requests, SelfSession self, IMovement movement, Func<bool> isGm, Func<AdminState> state, Action<string, bool> setStatus)
    {
        this.requests = requests;
        this.self = self;
        this.movement = movement;
        this.isGm = isGm;
        this.state = state;
        this.setStatus = setStatus;
    }

    private Label coinsLbl = null!;
    private LineEdit coinsInput = null!;
    private Label cashLbl = null!;
    private LineEdit cashInput = null!;
    private readonly SpinBox[] statSpins = new SpinBox[CharacterSheet.StatCount];
    private SpinBox pointsSpin = null!;
    private SpinBox npSpin = null!;
    private SpinBox levelSpin = null!;
    private CheckButton? collisionSwitch;

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(TabWidth, 0) };
        box.AddThemeConstantOverride("separation", 10);

        box.AddChild(BuildCoinRow());
        box.AddChild(BuildCashRow());
        box.AddChild(UiTheme.Rule());

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 14);
        box.AddChild(columns);
        columns.AddChild(BuildStatsSection());
        columns.AddChild(UiTheme.Rule(vertical: true));
        var level = BuildLevelSection();
        level.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(level);

        return box;
    }

    private Control BuildCoinRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 7);

        row.AddChild(Heading("Coins", "system/coins"));
        coinsLbl = UiTheme.Text("", 15, UiTheme.TextHi);
        coinsLbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(coinsLbl);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(4, 0) });

        foreach (int preset in CoinPresets)
        {
            int amount = preset;
            var button = Button($"+{FormatCoinShort(amount)}");
            button.Pressed += () => requests.Coins(amount);
            row.AddChild(button);
        }

        coinsInput = new LineEdit
        {
            PlaceholderText = "amount",
            Text = "1000000",
            CustomMinimumSize = new Vector2(96, ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        coinsInput.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(coinsInput);

        var give = Button("Give", 60);
        give.Pressed += () => SendCoinDelta(1);
        row.AddChild(give);
        var take = Button("Take", 60);
        take.Pressed += () => SendCoinDelta(-1);
        row.AddChild(take);
        return row;
    }

    private Control BuildCashRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 7);

        row.AddChild(Heading("Cash", "system/gem"));
        cashLbl = UiTheme.Text("", 15, UiTheme.Premium);
        cashLbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(cashLbl);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(4, 0) });

        foreach (int preset in CashPresets)
        {
            int amount = preset;
            var button = Button($"+{FormatCoinShort(amount)}");
            button.Pressed += () => requests.Cash(amount);
            row.AddChild(button);
        }

        cashInput = new LineEdit
        {
            PlaceholderText = "amount",
            Text = "1000",
            CustomMinimumSize = new Vector2(96, ControlHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        cashInput.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(cashInput);

        var give = Button("Give", 60);
        give.Pressed += () => SendCashDelta(1);
        row.AddChild(give);
        var take = Button("Take", 60);
        take.Pressed += () => SendCashDelta(-1);
        row.AddChild(take);
        return row;
    }

    private Control BuildStatsSection()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 7);
        box.AddChild(Heading("Stats", "game/chest"));

        for (int i = 0; i < CharacterSheet.StatCount; i++)
            statSpins[i] = Spin(StatMin, CharacterSheet.StatMax, SpinWidth);
        pointsSpin = Spin(0, FreePointsMax, SpinWidth);

        int rows = (CharacterSheet.StatCount + 1) / 2;
        for (int i = 0; i < rows; i++)
        {
            int right = i + rows;
            box.AddChild(right < CharacterSheet.StatCount
                ? StatRow(CharacterSheet.StatLabels[i], statSpins[i], CharacterSheet.StatLabels[right], statSpins[right])
                : StatRow(CharacterSheet.StatLabels[i], statSpins[i], "Free", pointsSpin));
        }

        var npRow = new HBoxContainer();
        npRow.AddThemeConstantOverride("separation", 0);
        npRow.AddChild(FieldLabel("NP"));
        npSpin = Spin(0, int.MaxValue, SpinWidth * 2 + ColumnGap + LabelWidth);
        npSpin.TooltipText = "National points";
        npRow.AddChild(npSpin);
        box.AddChild(npRow);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 7);
        box.AddChild(actions);
        var apply = Button("Apply stats", 108);
        apply.Pressed += OnApplyStats;
        actions.AddChild(apply);
        var revert = Button("Revert", 76);
        revert.Pressed += LoadStatSpins;
        actions.AddChild(revert);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Re-read state from the server");
        refresh.CustomMinimumSize = new Vector2(36, ControlHeight);
        refresh.Pressed += () => requests.StateRequest();
        actions.AddChild(refresh);

        return box;
    }

    private static HBoxContainer StatRow(string leftName, SpinBox left, string rightName, SpinBox right)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        row.AddChild(FieldLabel(leftName));
        row.AddChild(left);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(ColumnGap, 0) });
        row.AddChild(FieldLabel(rightName));
        row.AddChild(right);
        return row;
    }

    private Control BuildLevelSection()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 7);
        box.AddChild(Heading("Level", "system/level"));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        box.AddChild(row);
        row.AddChild(FieldLabel("Level", 66));
        levelSpin = Spin(CharacterSheet.MinLevel, CharacterSheet.MaxLevel, 135);
        row.AddChild(levelSpin);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        box.AddChild(actions);

        var set = Button("Set", 56);
        set.Pressed += () =>
        {
            requests.SetLevel((int)levelSpin.Value, reset: false);
            setStatus("Setting level…", false);
        };
        actions.AddChild(set);

        var reset = Button("Reset to level");
        reset.Pressed += () =>
        {
            requests.SetLevel((int)levelSpin.Value, reset: true);
            setStatus("Resetting to level…", false);
        };
        actions.AddChild(reset);

        if (isGm())
        {
            box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
            box.AddChild(BuildCollisionRow());
        }

        return box;
    }

    private Control BuildCollisionRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(FieldLabel("Collision", 0));

        collisionSwitch = new CheckButton
        {
            ButtonPressed = !self.GmCollisionsOff,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Walk through terrain objects, monsters and players (GM only) — /collision on|off",
        };
        collisionSwitch.Toggled += on => movement.SetCollisions(on);
        row.AddChild(collisionSwitch);
        return row;
    }

    internal void RefreshCollisionSwitch()
    {
        if (collisionSwitch == null || !GodotObject.IsInstanceValid(collisionSwitch)) return;
        if (collisionSwitch.ButtonPressed != !self.GmCollisionsOff)
            collisionSwitch.SetPressedNoSignal(!self.GmCollisionsOff);
    }

    internal void Refresh()
    {
        LoadStatSpins();
        RefreshLevelSpin();
        if (coinsLbl == null) return;
        coinsLbl.Text = $"{state().Gold:n0}";
        RefreshCash();
    }

    internal void ShowCash(int knightCash)
    {
        if (cashLbl == null || !GodotObject.IsInstanceValid(cashLbl)) return;
        cashLbl.Text = $"{knightCash:n0}";
    }

    internal void RefreshCash() => ShowCash(self.Sheet.KnightCash);

    private void RefreshLevelSpin()
    {
        if (levelSpin == null) return;
        levelSpin.Value = Mathf.Clamp(state().Level, CharacterSheet.MinLevel, CharacterSheet.MaxLevel);
    }

    private void LoadStatSpins()
    {
        if (statSpins[0] == null) return;
        var current = state();
        int[] values = { current.Str, current.Sta, current.Dex, current.Intel, current.MagicStat };
        for (int i = 0; i < CharacterSheet.StatCount; i++)
            statSpins[i].Value = Mathf.Clamp(values[i], StatMin, CharacterSheet.StatMax);
        pointsSpin.Value = Mathf.Clamp(current.StatPoints, 0, FreePointsMax);
        npSpin.Value = System.Math.Max(0, current.Loyalty);
    }

    private void OnApplyStats()
    {
        requests.Stats(
            (int)statSpins[0].Value, (int)statSpins[1].Value, (int)statSpins[2].Value,
            (int)statSpins[3].Value, (int)statSpins[4].Value, (int)pointsSpin.Value,
            (int)npSpin.Value);
        setStatus("Applying stats…", false);
    }

    private void SendCoinDelta(int sign)
    {
        if (!long.TryParse(coinsInput.Text.Trim().Replace(",", ""), out long amount) || amount == 0)
        {
            setStatus("Enter a gold amount.", true);
            return;
        }
        long signed = System.Math.Clamp(amount * sign, int.MinValue, int.MaxValue);
        requests.Coins((int)signed);
    }

    private void SendCashDelta(int sign)
    {
        if (!long.TryParse(cashInput.Text.Trim().Replace(",", ""), out long amount) || amount == 0)
        {
            setStatus("Enter a cash amount.", true);
            return;
        }
        long signed = System.Math.Clamp(amount * sign, int.MinValue, int.MaxValue);
        requests.Cash((int)signed);
    }

    private static string FormatCoinShort(int amount) => amount switch
    {
        >= 1_000_000 => $"{amount / 1_000_000}M",
        >= 1_000 => $"{amount / 1_000}K",
        _ => amount.ToString(),
    };
}
