using Godot;

namespace LibreKO;

public partial class World
{
    private Control BuildAdminEventsTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);

        // Section: Temple Events
        box.AddChild(UiTheme.SectionTitle("Temple Events", UiIcons.Get("system/trophy")));

        var templeDesc = UiTheme.Text("Schedule automated temple events or teleport directly to the arena.", 11, UiTheme.TextLo);
        box.AddChild(templeDesc);

        (string Title, string Code)[] templeList =
        {
            ("Border Defense War (BDW)", "bdw"),
            ("Juraid Mountain (JR)", "jr"),
            ("Chaos Dungeon", "chaos"),
        };

        foreach (var (title, code) in templeList)
        {
            var row = UiTheme.RowPanel();
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var nameLbl = UiTheme.Text(title, 13, UiTheme.TextHi);
            nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(nameLbl);

            var startBtn = new Button { Text = "Start (30s)", FocusMode = Control.FocusModeEnum.None };
            startBtn.AddThemeFontSizeOverride("font_size", 12);
            string eventCode = code;
            string eventTitle = title;
            startBtn.Pressed += () =>
            {
                Net.I.SendGmCommand($"{eventCode} 30");
                SetAdminStatus($"Started 30s registration for {eventTitle}.", false);
            };
            line.AddChild(startBtn);

            var joinNowBtn = new Button { Text = "Teleport Directly", FocusMode = Control.FocusModeEnum.None };
            joinNowBtn.AddThemeFontSizeOverride("font_size", 12);
            joinNowBtn.Pressed += () =>
            {
                Net.I.SendGmCommand($"{eventCode} 0");
                SetAdminStatus($"Teleporting to {eventTitle} arena...", false);
            };
            line.AddChild(joinNowBtn);

            box.AddChild(row);
        }

        var cancelRow = new HBoxContainer();
        cancelRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(cancelRow);

        var cancelEventBtn = new Button { Text = "Cancel Active Temple Event", FocusMode = Control.FocusModeEnum.None };
        cancelEventBtn.AddThemeFontSizeOverride("font_size", 12);
        cancelEventBtn.AddThemeColorOverride("font_color", UiTheme.Warning);
        cancelEventBtn.Pressed += () =>
        {
            Net.I.SendGmCommand("templecancel");
            SetAdminStatus("Cancelled active temple event.", false);
        };
        cancelRow.AddChild(cancelEventBtn);

        box.AddChild(new HSeparator());

        // Section: Warfare & Incursions
        box.AddChild(UiTheme.SectionTitle("Warfare & Incursions", UiIcons.Get("system/combat-attack")));

        (string WarName, string OpenCmd, string CloseCmd)[] warList =
        {
            ("Lunar War (Zone 101)", "waropen 101", "warclose"),
            ("Snow War (Zone 102)", "snowwar", "warclose"),
            ("Bifrost (30 Minutes)", "bifroststart 30", "bifrostclose"),
        };

        foreach (var (warName, openCmd, closeCmd) in warList)
        {
            var row = UiTheme.RowPanel();
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var nameLbl = UiTheme.Text(warName, 13, UiTheme.TextHi);
            nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(nameLbl);

            var openBtn = new Button { Text = "Open", FocusMode = Control.FocusModeEnum.None };
            openBtn.AddThemeFontSizeOverride("font_size", 12);
            string oCmd = openCmd;
            string wName = warName;
            openBtn.Pressed += () =>
            {
                Net.I.SendGmCommand(oCmd);
                SetAdminStatus($"Triggered open for {wName}.", false);
            };
            line.AddChild(openBtn);

            var closeBtn = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
            closeBtn.AddThemeFontSizeOverride("font_size", 12);
            string cCmd = closeCmd;
            closeBtn.Pressed += () =>
            {
                Net.I.SendGmCommand(cCmd);
                SetAdminStatus($"Closed {wName}.", false);
            };
            line.AddChild(closeBtn);

            box.AddChild(row);
        }

        box.AddChild(new HSeparator());

        // Section: Global Event Rates
        box.AddChild(UiTheme.SectionTitle("Global Event Rates", UiIcons.Get("system/coins")));

        var rateDesc = UiTheme.Text("Apply server-wide bonus multipliers for experience, coin drops, national points, and loot.", 11, UiTheme.TextLo);
        box.AddChild(rateDesc);

        (string RateLabel, string CmdPrefix)[] rateTypes =
        {
            ("Experience Bonus (EXP)", "expadd"),
            ("Noah / Coin Bonus", "noahadd"),
            ("National Points (NP)", "np_add"),
            ("Drop Rate Bonus", "drop_add"),
        };

        foreach (var (rLabel, prefix) in rateTypes)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var titleLbl = UiTheme.Text(rLabel, 12, UiTheme.TextHi);
            titleLbl.CustomMinimumSize = new Vector2(180, 0);
            row.AddChild(titleLbl);

            (string PctLabel, int Pct)[] presets =
            {
                ("Off (+0%)", 0),
                ("+25%", 25),
                ("+50%", 50),
                ("+100%", 100),
            };

            foreach (var (pLabel, val) in presets)
            {
                var btn = new Button { Text = pLabel, FocusMode = Control.FocusModeEnum.None };
                btn.AddThemeFontSizeOverride("font_size", 11);
                string pfx = prefix;
                string typeName = rLabel;
                int amount = val;
                btn.Pressed += () =>
                {
                    Net.I.SendGmCommand($"{pfx} {amount}");
                    SetAdminStatus($"Set {typeName} bonus to {amount}%.", false);
                };
                row.AddChild(btn);
            }

            box.AddChild(row);
        }

        return box;
    }
}
