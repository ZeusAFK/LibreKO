using System;
using Godot;

namespace LibreKO;

public partial class World
{
    private LineEdit _admTimeInput = null!;
    private LineEdit _admNoticeInput = null!;

    private Control BuildAdminWorldTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);

        // Section: Game Time
        box.AddChild(UiTheme.SectionTitle("Game Time", UiIcons.Get("system/home")));

        var timeDesc = UiTheme.Text("Synchronizes the sky, lighting, and ambient atmosphere across all players.", 11, UiTheme.TextLo);
        box.AddChild(timeDesc);

        var timePresets = new HBoxContainer();
        timePresets.AddThemeConstantOverride("separation", 6);
        box.AddChild(timePresets);

        (string Label, string Time)[] timeButtons =
        {
            ("Dawn (06:00)", "06:00"),
            ("Noon (12:00)", "12:00"),
            ("Dusk (18:00)", "18:00"),
            ("Midnight (00:00)", "00:00"),
        };

        foreach (var (lbl, t) in timeButtons)
        {
            var btn = new Button { Text = lbl, FocusMode = Control.FocusModeEnum.None };
            btn.AddThemeFontSizeOverride("font_size", 12);
            string timeStr = t;
            btn.Pressed += () =>
            {
                Net.I.SendGmCommand($"time {timeStr}");
                SetAdminStatus($"Set game time to {timeStr}.", false);
            };
            timePresets.AddChild(btn);
        }

        var customTimeRow = new HBoxContainer();
        customTimeRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(customTimeRow);

        _admTimeInput = new LineEdit
        {
            PlaceholderText = "HH:MM (e.g. 15:30)",
            CustomMinimumSize = new Vector2(160, 0),
        };
        customTimeRow.AddChild(_admTimeInput);

        var setTimeBtn = new Button { Text = "Set Time", FocusMode = Control.FocusModeEnum.None };
        setTimeBtn.AddThemeFontSizeOverride("font_size", 12);
        setTimeBtn.Pressed += () =>
        {
            var val = _admTimeInput.Text.Trim();
            if (string.IsNullOrEmpty(val))
            {
                SetAdminStatus("Please specify time in HH or HH:MM format.", true);
                return;
            }
            Net.I.SendGmCommand($"time {val}");
            SetAdminStatus($"Requested time change to {val}.", false);
        };
        customTimeRow.AddChild(setTimeBtn);

        box.AddChild(new HSeparator());

        // Section: Weather
        box.AddChild(UiTheme.SectionTitle("Weather", UiIcons.Get("system/res-ice")));

        var weatherDesc = UiTheme.Text("Controls precipitation and particle environmental effects in the current zone.", 11, UiTheme.TextLo);
        box.AddChild(weatherDesc);

        var weatherRow = new HBoxContainer();
        weatherRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(weatherRow);

        (string Label, string Cmd, string Desc)[] weatherPresets =
        {
            ("Clear Sky", "weather clear 0", "Clear weather"),
            ("Light Rain", "weather rain 30", "Light rain (30%)"),
            ("Heavy Rain", "weather rain 90", "Heavy storm (90%)"),
            ("Snowfall", "weather snow 60", "Snowfall (60%)"),
            ("Autumn Leaves", "weather leaves 60", "Falling leaves (60%)"),
        };

        foreach (var (lbl, cmd, desc) in weatherPresets)
        {
            var btn = new Button { Text = lbl, FocusMode = Control.FocusModeEnum.None };
            btn.AddThemeFontSizeOverride("font_size", 12);
            string command = cmd;
            string description = desc;
            btn.Pressed += () =>
            {
                Net.I.SendGmCommand(command);
                SetAdminStatus($"Set weather: {description}.", false);
            };
            weatherRow.AddChild(btn);
        }

        box.AddChild(new HSeparator());

        // Section: Server Notice
        box.AddChild(UiTheme.SectionTitle("Server Notice", UiIcons.Get("system/scroll")));

        var noticeDesc = UiTheme.Text("Broadcasts an official high-priority system notice to all online players.", 11, UiTheme.TextLo);
        box.AddChild(noticeDesc);

        var noticeRow = new HBoxContainer();
        noticeRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(noticeRow);

        _admNoticeInput = new LineEdit
        {
            PlaceholderText = "Enter announcement message...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        noticeRow.AddChild(_admNoticeInput);

        var sendNoticeBtn = new Button { Text = "Broadcast Notice", FocusMode = Control.FocusModeEnum.None };
        sendNoticeBtn.AddThemeFontSizeOverride("font_size", 12);
        sendNoticeBtn.Pressed += () =>
        {
            var msg = _admNoticeInput.Text.Trim();
            if (string.IsNullOrEmpty(msg))
            {
                SetAdminStatus("Please enter a notice message to broadcast.", true);
                return;
            }
            Net.I.SendGmCommand($"notice {msg}");
            SetAdminStatus($"Notice broadcasted: \"{msg}\"", false);
            _admNoticeInput.Text = "";
        };
        noticeRow.AddChild(sendNoticeBtn);

        // Quick Notice Presets
        var quickNoticeRow = new HBoxContainer();
        quickNoticeRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(quickNoticeRow);

        string[] noticeTemplates =
        {
            "Server restart in 10 minutes. Please find a safe spot.",
            "Special Boss Event is about to start in Moradon!",
            "Welcome to LibreKO Server! Enjoy your adventure!",
        };

        foreach (var tpl in noticeTemplates)
        {
            var tplBtn = new Button { Text = tpl.Length > 28 ? tpl.Substring(0, 26) + "..." : tpl, FocusMode = Control.FocusModeEnum.None };
            tplBtn.AddThemeFontSizeOverride("font_size", 11);
            tplBtn.TooltipText = tpl;
            string textToUse = tpl;
            tplBtn.Pressed += () =>
            {
                _admNoticeInput.Text = textToUse;
            };
            quickNoticeRow.AddChild(tplBtn);
        }

        return box;
    }
}
