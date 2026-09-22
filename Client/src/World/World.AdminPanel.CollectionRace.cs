using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int AdminRaceListHeight = 380;
    private const int AdminRaceListWidth = 560;

    private LineEdit _admRaceFilter = null!;
    private VBoxContainer _admRaceList = null!;
    private Label _admRaceSummary = null!;
    private CheckBox _admRaceActiveOnly = null!;
    private List<AdminCollectionRace> _admRaces = [];

    private Control BuildAdminCollectionRaceTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(AdminRaceListWidth, 0) };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.SectionTitle("Collection Races", UiIcons.Get("system/home")));

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(findRow);
        _admRaceFilter = new LineEdit
        {
            PlaceholderText = "filter by name, zone or level",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _admRaceFilter.TextChanged += _ => RefreshAdminRaceList();
        findRow.AddChild(_admRaceFilter);
        _admRaceActiveOnly = UiTheme.FlatCheck("Active only", false);
        _admRaceActiveOnly.Toggled += _ => RefreshAdminRaceList();
        findRow.AddChild(_admRaceActiveOnly);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Refresh");
        refresh.Pressed += () => Net.I.SendAdminCollectionRacesRequest();
        findRow.AddChild(refresh);

        _admRaceSummary = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(_admRaceSummary);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(AdminRaceListWidth, AdminRaceListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _admRaceList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _admRaceList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_admRaceList);

        if (Net.I != null) Net.I.AdminCollectionRacesEvent += OnAdminCollectionRaces;
        return box;
    }

    private void OnAdminCollectionRaces(List<AdminCollectionRace> races)
    {
        _admRaces = races;
        RefreshAdminRaceList();
    }

    private void RefreshAdminRaceList()
    {
        if (_admRaceList == null || !GodotObject.IsInstanceValid(_admRaceList)) return;
        foreach (var child in _admRaceList.GetChildren())
        {
            _admRaceList.RemoveChild(child);
            child.QueueFree();
        }

        var filter = _admRaceFilter.Text.Trim();
        var activeOnly = _admRaceActiveOnly.ButtonPressed;
        int shown = 0, active = 0;
        foreach (var race in _admRaces)
        {
            if (race.Active) active++;
            if (activeOnly && !race.Active) continue;
            if (filter.Length > 0 && !AdminRaceMatches(race, filter)) continue;
            _admRaceList.AddChild(BuildAdminRaceRow(race));
            shown++;
        }

        _admRaceSummary.Text = _admRaces.Count == 0
            ? "No races loaded."
            : $"{shown} of {_admRaces.Count} races shown, {active} running.";
    }

    private static bool AdminRaceMatches(AdminCollectionRace race, string filter) =>
        race.Name.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
        || ZoneCatalog.Name(race.ZoneId).Contains(filter, System.StringComparison.OrdinalIgnoreCase)
        || race.ZoneId.ToString() == filter
        || $"{race.MinLevel}-{race.MaxLevel}".Contains(filter, System.StringComparison.Ordinal);

    private Control BuildAdminRaceRow(AdminCollectionRace race)
    {
        var panel = UiTheme.RowPanel(race.Active);
        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 5, 8, 5);
        panel.AddChild(margin);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        margin.AddChild(rows);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        rows.AddChild(head);
        var title = UiTheme.Text($"{race.Id}  {race.Name}", 13, race.Active ? UiTheme.Good : UiTheme.GoldBright);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(UiTheme.Pill(race.Active ? $"running  {race.RemainingSeconds / 60:D2}:{race.RemainingSeconds % 60:D2}" : race.AutoStart ? "scheduled" : "manual",
            race.Active ? UiTheme.Good : UiTheme.TextLo));
        var action = UiTheme.SmallButton(race.Active ? "Close" : "Start", race.Active ? "End this race now; completers are mailed their rewards" : "Start this race now in its zone");
        var raceId = race.Id;
        var wasActive = race.Active;
        action.Pressed += () =>
        {
            if (wasActive) Net.I.SendAdminCollectionRaceClose(raceId);
            else Net.I.SendAdminCollectionRaceStart(raceId);
        };
        head.AddChild(action);

        var meta = UiTheme.Text(
            $"{ZoneCatalog.Name(race.ZoneId)} ({race.ZoneId})  ·  level {race.MinLevel}-{race.MaxLevel}  ·  {race.DurationMinutes} min" +
            (race.Active ? $"  ·  {race.Completions} completed" : ""),
            12, UiTheme.TextLo);
        rows.AddChild(meta);
        var objectives = UiTheme.Text(race.Objectives, 12, UiTheme.TextHi);
        objectives.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rows.AddChild(objectives);
        var schedule = UiTheme.Text($"Schedule (UTC): {race.Schedule}", 11, UiTheme.TextDim);
        schedule.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rows.AddChild(schedule);
        return panel;
    }
}
