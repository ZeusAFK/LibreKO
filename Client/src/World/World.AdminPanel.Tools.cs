using Godot;

namespace LibreKO;

public partial class World
{
    private CheckButton? _admGmSpeedSwitch;
    private SpinBox? _admGmSpeedMultiplierSpin;

    private Control BuildAdminToolsTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);

        // Section: GM Locomotion & Visuals
        box.AddChild(UiTheme.SectionTitle("GM Locomotion & Visuals", UiIcons.Get("system/combat-defence")));

        var speedDesc = UiTheme.Text("Enhance GM locomotion speed and toggle visual aura badges.", 11, UiTheme.TextLo);
        box.AddChild(speedDesc);

        var speedRow = UiTheme.RowPanel();
        var speedLine = new HBoxContainer();
        speedLine.AddThemeConstantOverride("separation", 10);
        speedRow.AddChild(speedLine);

        var speedToggleLbl = UiTheme.Text("Always-On GM Speed", 13, UiTheme.TextHi);
        speedToggleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        speedLine.AddChild(speedToggleLbl);

        _admGmSpeedSwitch = new CheckButton
        {
            ButtonPressed = _gmSpeedAlwaysOn,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Enables GM speed without needing to hold Key G.",
        };
        _admGmSpeedSwitch.Toggled += on =>
        {
            _gmSpeedAlwaysOn = on;
            SetAdminStatus(_gmSpeedAlwaysOn ? "GM Speed set to always active." : "GM Speed hold-only mode.", false);
        };
        speedLine.AddChild(_admGmSpeedSwitch);

        var multRow = new HBoxContainer();
        multRow.AddThemeConstantOverride("separation", 8);

        var multLbl = UiTheme.Text("Speed Multiplier:", 12, UiTheme.TextLo);
        multRow.AddChild(multLbl);

        _admGmSpeedMultiplierSpin = UiTheme.NumberBox(1, 10, 1, 80);
        _admGmSpeedMultiplierSpin.Step = 0.5;
        _admGmSpeedMultiplierSpin.Value = _gmSpeedMultiplier;
        _admGmSpeedMultiplierSpin.ValueChanged += val =>
        {
            _gmSpeedMultiplier = (float)val;
            SetAdminStatus($"GM Speed multiplier adjusted to {val:0.#}x.", false);
        };
        multRow.AddChild(_admGmSpeedMultiplierSpin);

        var multHint = UiTheme.Text("(Default: 5.0x)", 11, UiTheme.TextDim);
        multRow.AddChild(multHint);

        box.AddChild(speedRow);
        box.AddChild(multRow);

        var visualRow = new HBoxContainer();
        visualRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(visualRow);

        var gmFxBtn = new Button { Text = "Toggle GM Aura & Wings (gmfx)", FocusMode = Control.FocusModeEnum.None };
        gmFxBtn.AddThemeFontSizeOverride("font_size", 12);
        gmFxBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("gmfx");
            SetAdminStatus("Toggled GM visual effect aura.", false);
        };
        visualRow.AddChild(gmFxBtn);

        box.AddChild(new HSeparator());

        // Section: Character Vitals
        box.AddChild(UiTheme.SectionTitle("Character Vitals & Recovery", UiIcons.Get("system/stat-hp")));

        var vitalsRow = new HBoxContainer();
        vitalsRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(vitalsRow);

        var restoreBtn = new Button { Text = "Full Restore HP & MP", FocusMode = Control.FocusModeEnum.None };
        restoreBtn.AddThemeFontSizeOverride("font_size", 12);
        restoreBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("hp");
            SetAdminStatus("Requested full HP and MP restoration.", false);
        };
        vitalsRow.AddChild(restoreBtn);

        box.AddChild(new HSeparator());

        // Section: Server Hot-Reload & Diagnostics
        box.AddChild(UiTheme.SectionTitle("Server Maintenance & Hot-Reload", UiIcons.Get("system/refresh")));

        var maintDesc = UiTheme.Text("Reload server quest scripts and reseed database drops in runtime without kicking players.", 11, UiTheme.TextLo);
        box.AddChild(maintDesc);

        var maintRow = new HBoxContainer();
        maintRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(maintRow);

        var reloadScriptsBtn = new Button { Text = "Hot-Reload Quest Scripts", FocusMode = Control.FocusModeEnum.None };
        reloadScriptsBtn.AddThemeFontSizeOverride("font_size", 12);
        reloadScriptsBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("reloadscripts");
            SetAdminStatus("Cleared server quest script cache.", false);
        };
        maintRow.AddChild(reloadScriptsBtn);

        var reseedBtn = new Button { Text = "Reseed DB & Drops", FocusMode = Control.FocusModeEnum.None };
        reseedBtn.AddThemeFontSizeOverride("font_size", 12);
        reseedBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("reseed");
            SetAdminStatus("Triggered server JSON reseed to database.", false);
        };
        maintRow.AddChild(reseedBtn);

        var onlineBtn = new Button { Text = "Online Count", FocusMode = Control.FocusModeEnum.None };
        onlineBtn.AddThemeFontSizeOverride("font_size", 12);
        onlineBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("online");
        };
        maintRow.AddChild(onlineBtn);

        return box;
    }
}
