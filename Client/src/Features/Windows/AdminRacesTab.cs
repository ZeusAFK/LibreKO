using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminRacesTab
{
    private const int RaceListHeight = 380;
    private const int RaceListWidth = 560;

    private readonly IAdminRequests requests;

    public AdminRacesTab(IAdminRequests requests) => this.requests = requests;

    private LineEdit raceFilter = null!;
    private VBoxContainer raceList = null!;
    private Label raceSummary = null!;
    private CheckBox raceActiveOnly = null!;
    private List<AdminCollectionRace> races = [];

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(RaceListWidth, 0) };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.SectionTitle("Collection Races", UiIcons.Get("system/home")));

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(findRow);
        raceFilter = new LineEdit
        {
            PlaceholderText = "filter by name, zone or level",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        raceFilter.TextChanged += _ => RefreshRaceList();
        findRow.AddChild(raceFilter);
        raceActiveOnly = UiTheme.FlatCheck("Active only", false);
        raceActiveOnly.Toggled += _ => RefreshRaceList();
        findRow.AddChild(raceActiveOnly);
        var refresh = UiTheme.IconButton(UiIcons.Get("system/refresh"), "Refresh");
        refresh.Pressed += () => requests.CollectionRacesRequest();
        findRow.AddChild(refresh);

        raceSummary = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(raceSummary);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(RaceListWidth, RaceListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        raceList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        raceList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(raceList);
        return box;
    }

    internal void OnCollectionRaces(List<AdminCollectionRace> races)
    {
        this.races = races;
        RefreshRaceList();
    }

    private void RefreshRaceList()
    {
        if (raceList == null || !GodotObject.IsInstanceValid(raceList)) return;
        foreach (var child in raceList.GetChildren())
        {
            raceList.RemoveChild(child);
            child.QueueFree();
        }

        var filter = raceFilter.Text.Trim();
        var activeOnly = raceActiveOnly.ButtonPressed;
        int shownCount = 0, active = 0;
        foreach (var race in races)
        {
            if (race.Active) active++;
            if (activeOnly && !race.Active) continue;
            if (filter.Length > 0 && !RaceMatches(race, filter)) continue;
            raceList.AddChild(BuildRaceRow(race));
            shownCount++;
        }

        raceSummary.Text = races.Count == 0
            ? "No races loaded."
            : $"{shownCount} of {races.Count} races shown, {active} running.";
    }

    private static bool RaceMatches(AdminCollectionRace race, string filter) =>
        race.Name.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
    || ZoneCatalog.Name(race.ZoneId).Contains(filter, System.StringComparison.OrdinalIgnoreCase)
    || race.ZoneId.ToString() == filter
    || $"{race.MinLevel}-{race.MaxLevel}".Contains(filter, System.StringComparison.Ordinal);

    private Control BuildRaceRow(AdminCollectionRace race)
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
            if (wasActive) requests.CollectionRaceClose(raceId);
            else requests.CollectionRaceStart(raceId);
        };
        head.AddChild(action);

        var meta = UiTheme.Text(
            $"{ZoneCatalog.Name(race.ZoneId)} ({race.ZoneId})  ·  level {race.MinLevel}-{race.MaxLevel}  ·  {race.DurationMinutes} min" +
            (race.MaxWinners > 0 ? $"  ·  {race.MaxWinners} winners" : "  ·  no winner limit") +
            (race.Active ? $"  ·  {race.Completions} finished" : ""),
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
