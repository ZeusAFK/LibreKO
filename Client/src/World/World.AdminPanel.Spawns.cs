using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private sealed record BossDef(int Id, string Name, int Level, string Category);

    private static readonly BossDef[] BossCatalog =
    {
        new(5702, "Felankor", 170, "World Boss"),
        new(5501, "Isiloon", 170, "Floor Boss"),
        new(6501, "Ultima", 90, "Underworld Boss"),
        new(2405, "Talos", 140, "Colony Boss"),
        new(2005, "Snake Queen", 100, "Colony Boss"),
        new(2205, "Harpy Queen", 110, "Colony Boss"),
        new(1725, "Troll King", 110, "Colony Boss"),
        new(2105, "Deruvish Founder", 100, "Colony Boss"),
        new(1400, "Attila", 85, "Eslant Boss"),
        new(1306, "Samma", 90, "Eslant Boss"),
        new(907, "Shaula", 75, "Eslant Boss"),
        new(908, "Lesath", 75, "Eslant Boss"),
        new(1205, "Duke", 55, "Ardream Boss"),
        new(1206, "Bach", 60, "Ardream Boss"),
        new(1207, "Bishop", 65, "Ardream Boss"),
        new(8260, "Javana", 65, "Colony Boss"),
        new(607, "Barrkk", 55, "Zone Boss"),
        new(608, "Barkirra", 60, "Zone Boss"),
        new(506, "Lobo", 45, "Lupus Boss"),
        new(507, "Lupus", 50, "Lupus Boss"),
        new(508, "Lycaon", 55, "Lupus Boss"),
        new(1106, "Bone Collector", 300, "Undead Boss"),
        new(1107, "Dragon Tooth", 80, "Undead Boss"),
        new(906, "Antares", 50, "Scorpion Boss"),
        new(1005, "Hyde", 45, "Zone Boss"),
        new(2817, "Orc Bandit Leader", 120, "Bandit Boss"),
        new(9589, "Hell Fire", 75, "Bifrost Boss"),
        new(9590, "Enigma", 75, "Bifrost Boss"),
        new(9591, "Havoc", 75, "Bifrost Boss"),
        new(9592, "Cruel", 75, "Bifrost Boss"),
    };

    private LineEdit _admBossFilter = null!;
    private VBoxContainer _admBossList = null!;
    private LineEdit _admCustomMobId = null!;
    private SpinBox _admCustomMobCount = null!;

    private Control BuildAdminSpawnsTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);

        // Section: Iconic Boss Catalog
        box.AddChild(UiTheme.SectionTitle("Iconic Boss Catalog", UiIcons.Get("system/combat-attack")));

        var bossDesc = UiTheme.Text("Spawn world bosses and signature encounters directly at your location.", 11, UiTheme.TextLo);
        box.AddChild(bossDesc);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(filterRow);

        _admBossFilter = new LineEdit
        {
            PlaceholderText = "Search bosses by name...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _admBossFilter.TextChanged += _ => RefreshAdminBossList();
        filterRow.AddChild(_admBossFilter);

        var clearFilter = UiTheme.IconButton(UiIcons.Get("system/close"), "Clear search filter");
        clearFilter.Pressed += () => { _admBossFilter.Text = ""; RefreshAdminBossList(); };
        filterRow.AddChild(clearFilter);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(640, 260),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);

        _admBossList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _admBossList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_admBossList);

        RefreshAdminBossList();

        box.AddChild(new HSeparator());

        // Section: Custom NPC / Monster Spawn
        box.AddChild(UiTheme.SectionTitle("Custom Monster & NPC Spawner", UiIcons.Get("system/combat-defence")));

        var customDesc = UiTheme.Text("Summon any NPC or monster instance using its exact database template ID.", 11, UiTheme.TextLo);
        box.AddChild(customDesc);

        var customRow = new HBoxContainer();
        customRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(customRow);

        _admCustomMobId = new LineEdit
        {
            PlaceholderText = "NPC ID (e.g. 100)",
            CustomMinimumSize = new Vector2(160, 0),
        };
        customRow.AddChild(_admCustomMobId);

        var countLbl = UiTheme.Text("Count:", 12, UiTheme.TextLo);
        customRow.AddChild(countLbl);

        _admCustomMobCount = UiTheme.NumberBox(1, 50, 1, 80);
        _admCustomMobCount.Value = 1;
        customRow.AddChild(_admCustomMobCount);

        var spawnCustomBtn = new Button { Text = "Spawn NPC", FocusMode = Control.FocusModeEnum.None };
        spawnCustomBtn.AddThemeFontSizeOverride("font_size", 12);
        spawnCustomBtn.Pressed += () =>
        {
            var text = _admCustomMobId.Text.Trim();
            if (!int.TryParse(text, out int mobId) || mobId <= 0)
            {
                SetAdminStatus("Please enter a valid numeric NPC ID.", true);
                return;
            }
            int count = (int)_admCustomMobCount.Value;
            Net.I.SendGmCommand($"mon {mobId} {count}");
            SetAdminStatus($"Spawned {count}x NPC ID {mobId} at your location.", false);
        };
        customRow.AddChild(spawnCustomBtn);

        return box;
    }

    private void RefreshAdminBossList()
    {
        if (_admBossList == null) return;
        foreach (Node child in _admBossList.GetChildren()) child.QueueFree();

        var query = _admBossFilter?.Text?.Trim().ToLowerInvariant() ?? "";

        foreach (var boss in BossCatalog)
        {
            if (!string.IsNullOrEmpty(query) && !boss.Name.ToLowerInvariant().Contains(query) && !boss.Category.ToLowerInvariant().Contains(query))
                continue;

            var row = UiTheme.RowPanel();
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var nameLbl = UiTheme.Text(boss.Name, 13, UiTheme.TextHi);
            nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(nameLbl);

            line.AddChild(UiTheme.Pill(boss.Category, UiTheme.GoldDark));
            line.AddChild(UiTheme.Pill($"Lv {boss.Level}", UiTheme.TextLo));
            line.AddChild(UiTheme.Pill($"#{boss.Id}", UiTheme.BronzeDark));

            var countSpin = UiTheme.NumberBox(1, 10, 1, 64);
            countSpin.Value = 1;
            line.AddChild(countSpin);

            var spawnBtn = new Button { Text = "Spawn", FocusMode = Control.FocusModeEnum.None };
            spawnBtn.AddThemeFontSizeOverride("font_size", 12);
            int bossId = boss.Id;
            string bossName = boss.Name;
            spawnBtn.Pressed += () =>
            {
                int count = (int)countSpin.Value;
                Net.I.SendGmCommand($"mon {bossId} {count}");
                SetAdminStatus($"Spawned {count}x {bossName} (#{bossId}) at your location.", false);
            };
            line.AddChild(spawnBtn);

            _admBossList.AddChild(row);
        }
    }
}
