using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminZonesTab
{
    private const float ListWidth = 470;
    private const float ListHeight = 360;

    private readonly IAdminRequests requests;
    private readonly Func<int> zone;
    private readonly Action<string, bool> setStatus;

    public AdminZonesTab(IAdminRequests requests, Func<int> zone, Action<string, bool> setStatus)
    {
        this.requests = requests;
        this.zone = zone;
        this.setStatus = setStatus;
    }

    internal LineEdit ZoneFilter { get; private set; } = null!;
    private ScrollContainer zoneScroll = null!;
    private VBoxContainer zoneList = null!;
    private Label zoneHere = null!;
    private Label zoneSummary = null!;
    private Control? zoneHereRow;

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(ListWidth, 0) };
        box.AddThemeConstantOverride("separation", 6);

        box.AddChild(UiTheme.SectionTitle("Travel", UiIcons.Get("system/home")));

        zoneHere = UiTheme.Text("", 13, UiTheme.GoldBright);
        box.AddChild(zoneHere);

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(findRow);
        ZoneFilter = new LineEdit
        {
            PlaceholderText = "filter by name or number",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ZoneFilter.TextChanged += _ => RefreshZoneList();
        findRow.AddChild(ZoneFilter);
        var clear = UiTheme.IconButton(UiIcons.Get("system/close"), "Clear the filter");
        clear.Pressed += () => { ZoneFilter.Text = ""; RefreshZoneList(); };
        findRow.AddChild(clear);

        zoneSummary = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(zoneSummary);

        zoneScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(ListWidth, ListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(zoneScroll);
        zoneList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        zoneList.AddThemeConstantOverride("separation", 3);
        zoneScroll.AddChild(zoneList);

        box.AddChild(UiTheme.Text(
            "You arrive at the zone's own start position for your nation.", 11, UiTheme.TextLo));

        return box;
    }

    internal void Refresh()
    {
        if (zoneHere == null) return;
        zoneHere.Text = $"Currently in {AdminPanelUi.ZoneName(zone())}  ({zone()})";
        RefreshZoneList();
        if (zoneHereRow is { } row)
            Callable.From(() => zoneScroll.EnsureControlVisible(row)).CallDeferred();
    }

    internal void RefreshZoneList()
    {
        if (zoneList == null) return;
        foreach (Node child in zoneList.GetChildren()) child.QueueFree();
        zoneHereRow = null;

        string filter = ZoneFilter.Text.Trim();
        int shownCount = 0;
        foreach (var entry in ZoneCatalog.All)
        {
            if (!ZoneMatches(entry, filter)) continue;
            var row = BuildZoneRow(entry);
            if (entry.Id == zone()) zoneHereRow = row;
            zoneList.AddChild(row);
            shownCount++;
        }

        zoneSummary.Text = shownCount switch
        {
            0 => $"No zone matches \"{filter}\".",
            1 => "1 zone.",
            _ when filter.Length == 0 => $"{shownCount} zones.",
            _ => $"{shownCount} zones match.",
        };
    }

    private static bool ZoneMatches(ZoneCatalog.Zone entry, string filter)
    {
        if (filter.Length == 0) return true;
        return entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Id.ToString().StartsWith(filter, StringComparison.Ordinal);
    }

    private Control BuildZoneRow(ZoneCatalog.Zone entry)
    {
        bool here = entry.Id == zone();
        var row = UiTheme.RowPanel(here);
        row.CustomMinimumSize = new Vector2(0, 32);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        var name = UiTheme.Text(entry.Name, 13, here ? UiTheme.GoldBright : UiTheme.TextHi);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        line.AddChild(name);

        var id = UiTheme.Pill(entry.Id.ToString(), here ? UiTheme.GoldBright : UiTheme.TextLo);
        id.CustomMinimumSize = new Vector2(44, 0);
        id.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(id);

        var go = new Button
        {
            Text = here ? "Here" : "Go",
            Disabled = here,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(58, 0),
            TooltipText = here ? "" : $"Travel to {entry.Name}",
        };
        go.AddThemeFontSizeOverride("font_size", 12);
        int target = entry.Id;
        string label = entry.Name;
        go.Pressed += () =>
        {
            setStatus($"Moving to {label}…", false);
            requests.Zone(target);
        };
        line.AddChild(go);

        return row;
    }
}
