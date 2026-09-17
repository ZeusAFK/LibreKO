using System;
using Godot;

namespace LibreKO;

public partial class World
{
    private LineEdit _admZoneFilter = null!;
    private ScrollContainer _admZoneScroll = null!;
    private VBoxContainer _admZoneList = null!;
    private Label _admZoneHere = null!, _admZoneSummary = null!;
    private Control? _admZoneHereRow;

    private Control BuildAdminZonesTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(470, 0) };
        box.AddThemeConstantOverride("separation", 6);

        box.AddChild(UiTheme.SectionTitle("Travel", UiIcons.Get("system/home")));

        _admZoneHere = UiTheme.Text("", 13, UiTheme.GoldBright);
        box.AddChild(_admZoneHere);

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(findRow);
        _admZoneFilter = new LineEdit
        {
            PlaceholderText = "filter by name or number",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _admZoneFilter.TextChanged += _ => RefreshAdminZoneList();
        findRow.AddChild(_admZoneFilter);
        var clear = UiTheme.IconButton(UiIcons.Get("system/close"), "Clear the filter");
        clear.Pressed += () => { _admZoneFilter.Text = ""; RefreshAdminZoneList(); };
        findRow.AddChild(clear);

        _admZoneSummary = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(_admZoneSummary);

        _admZoneScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(470, 360),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(_admZoneScroll);
        _admZoneList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _admZoneList.AddThemeConstantOverride("separation", 3);
        _admZoneScroll.AddChild(_admZoneList);

        box.AddChild(UiTheme.Text(
            "You arrive at the zone's own start position for your nation.", 11, UiTheme.TextLo));

        return box;
    }

    private void RefreshAdminZonesTab()
    {
        if (_admZoneHere == null) return;
        _admZoneHere.Text = $"Currently in {AdminZoneName(_zone)}  ({_zone})";
        RefreshAdminZoneList();
        if (_admZoneHereRow is { } row)
            Callable.From(() => _admZoneScroll.EnsureControlVisible(row)).CallDeferred();
    }

    private void RefreshAdminZoneList()
    {
        if (_admZoneList == null) return;
        foreach (Node child in _admZoneList.GetChildren()) child.QueueFree();
        _admZoneHereRow = null;

        string filter = _admZoneFilter.Text.Trim();
        int shown = 0;
        foreach (var zone in ZoneCatalog.All)
        {
            if (!AdminZoneMatches(zone, filter)) continue;
            var row = BuildAdminZoneRow(zone);
            if (zone.Id == _zone) _admZoneHereRow = row;
            _admZoneList.AddChild(row);
            shown++;
        }

        _admZoneSummary.Text = shown switch
        {
            0 => $"No zone matches \"{filter}\".",
            1 => "1 zone.",
            _ when filter.Length == 0 => $"{shown} zones.",
            _ => $"{shown} zones match.",
        };
    }

    private static bool AdminZoneMatches(ZoneCatalog.Zone zone, string filter)
    {
        if (filter.Length == 0) return true;
        return zone.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || zone.Id.ToString().StartsWith(filter, StringComparison.Ordinal);
    }

    private Control BuildAdminZoneRow(ZoneCatalog.Zone zone)
    {
        bool here = zone.Id == _zone;
        var row = UiTheme.RowPanel(here);
        row.CustomMinimumSize = new Vector2(0, 32);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        var name = UiTheme.Text(zone.Name, 13, here ? UiTheme.GoldBright : UiTheme.TextHi);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        line.AddChild(name);

        var id = UiTheme.Pill(zone.Id.ToString(), here ? UiTheme.GoldBright : UiTheme.TextLo);
        id.CustomMinimumSize = new Vector2(44, 0);
        id.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(id);

        var go = new Button
        {
            Text = here ? "Here" : "Go",
            Disabled = here,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(58, 0),
            TooltipText = here ? "" : $"Travel to {zone.Name}",
        };
        go.AddThemeFontSizeOverride("font_size", 12);
        int target = zone.Id;
        string label = zone.Name;
        go.Pressed += () =>
        {
            SetAdminStatus($"Moving to {label}…", false);
            Net.I.SendAdminZone(target);
        };
        line.AddChild(go);

        return row;
    }

    private static string AdminZoneName(int zoneId)
    {
        foreach (var zone in ZoneCatalog.All)
            if (zone.Id == zoneId) return zone.Name;
        return $"zone {zoneId}";
    }
}
