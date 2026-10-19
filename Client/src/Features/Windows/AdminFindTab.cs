using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminFindTab
{
    private const int FindListWidth = 720;
    private const int FindListHeight = 360;
    private const float FindLevelWidth = 48;
    private const float FindMapWidth = 140;
    private const float FindSpotWidth = 96;
    private const float FindGoWidth = 58;
    private const float FindEditWidth = 58;
    private static readonly string[] FindKinds = ["NPC", "Monster", "Player"];

    private readonly IAdminRequests requests;
    private readonly Func<int> zone;
    private readonly Action<string, bool> setStatus;

    public AdminFindTab(IAdminRequests requests, Func<int> zone, Action<string, bool> setStatus)
    {
        this.requests = requests;
        this.zone = zone;
        this.setStatus = setStatus;
    }

    private OptionButton findKind = null!;
    internal LineEdit FindQuery { get; private set; } = null!;
    private Label findSummary = null!;
    private VBoxContainer findList = null!;
    private List<AdminFindHit> findHits = [];
    private int findTotal;
    internal string LastFindQuery { get; set; } = "";

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(FindListWidth, 0) };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.SectionTitle("Find", UiIcons.Get("system/search")));

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(findRow);
        findKind = UiTheme.Dropdown(FindKinds);
        findKind.CustomMinimumSize = new Vector2(110, 0);
        findRow.AddChild(findKind);
        FindQuery = new LineEdit
        {
            PlaceholderText = "name or id, part of it is enough",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        FindQuery.TextSubmitted += _ => SendFind();
        findRow.AddChild(FindQuery);
        var search = UiTheme.IconButton(UiIcons.Get("system/search"), "Search");
        search.Pressed += SendFind;
        findRow.AddChild(search);

        findSummary = UiTheme.Text("Search the live world: every placed NPC or monster, or every player online.", 12, UiTheme.TextLo);
        box.AddChild(findSummary);

        box.AddChild(BuildFindHeader());

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(FindListWidth, FindListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        findList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        findList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(findList);

        box.AddChild(UiTheme.Text(
            "Go lands you on the spot the list shows; a monster may have wandered since.", 11, UiTheme.TextLo));
        return box;
    }

    private static Control BuildFindHeader()
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(FindHeaderCell("Name", 0, expand: true));
        line.AddChild(FindHeaderCell("Level", FindLevelWidth));
        line.AddChild(FindHeaderCell("Map", FindMapWidth));
        line.AddChild(FindHeaderCell("Location", FindSpotWidth));
        line.AddChild(FindHeaderCell("", FindEditWidth));
        line.AddChild(FindHeaderCell("", FindGoWidth));
        return line;
    }

    private static Label FindHeaderCell(string text, float width, bool expand = false)
    {
        var label = UiTheme.Text(text, 11, UiTheme.TextDim);
        label.CustomMinimumSize = new Vector2(width, 0);
        if (expand) label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    private void SendFind()
    {
        string query = FindQuery.Text.Trim();
        if (query.Length == 0)
        {
            setStatus("Type a name or an id to search for.", true);
            return;
        }
        LastFindQuery = query;
        setStatus($"Searching for “{query}”…", false);
        requests.Find(findKind.Selected, query);
    }

    internal void OnFind(int kind, int total, List<AdminFindHit> hits)
    {
        findHits = hits;
        findTotal = total;
        RefreshFindList();
        setStatus("", false);
    }

    private void RefreshFindList()
    {
        if (findList == null || !GodotObject.IsInstanceValid(findList)) return;
        foreach (Node child in findList.GetChildren()) child.QueueFree();

        string what = (AdminFindKind)findKind.Selected switch
        {
            AdminFindKind.Players => "player",
            AdminFindKind.Monsters => "monster",
            _ => "NPC",
        };
        findSummary.Text = findHits.Count switch
        {
            0 => $"No {what} matches “{LastFindQuery}”.",
            _ when findTotal > findHits.Count =>
                $"{findTotal} matches for “{LastFindQuery}”, showing the first {findHits.Count}. Narrow the search.",
            1 => $"1 match for “{LastFindQuery}”.",
            _ => $"{findHits.Count} matches for “{LastFindQuery}”.",
        };

        foreach (var hit in findHits)
            findList.AddChild(BuildFindRow(hit));
    }

    private Control BuildFindRow(AdminFindHit hit)
    {
        var row = UiTheme.RowPanel(false);
        row.CustomMinimumSize = new Vector2(0, 32);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        var nameCell = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameCell.AddThemeConstantOverride("separation", 6);
        line.AddChild(nameCell);
        var name = UiTheme.Text(hit.Name, 13, UiTheme.TextHi);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        nameCell.AddChild(name);
        var id = UiTheme.Pill(hit.Id.ToString(), UiTheme.TextLo);
        id.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        nameCell.AddChild(id);
        if (hit.Bot)
        {
            var tag = UiTheme.Pill("bot", UiTheme.TextDim);
            tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            nameCell.AddChild(tag);
        }

        var level = UiTheme.Pill(hit.Level.ToString(), UiTheme.GoldBright);
        level.CustomMinimumSize = new Vector2(FindLevelWidth, 0);
        level.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(level);

        var map = UiTheme.Text(AdminPanelUi.ZoneName(hit.Zone), 12, hit.Zone == zone() ? UiTheme.GoldBright : UiTheme.TextHi);
        map.CustomMinimumSize = new Vector2(FindMapWidth, 0);
        map.ClipText = true;
        line.AddChild(map);

        var spot = UiTheme.Text($"{hit.X}, {hit.Z}", 12, UiTheme.TextHi);
        spot.CustomMinimumSize = new Vector2(FindSpotWidth, 0);
        line.AddChild(spot);

        var edit = new Button
        {
            Text = "Edit",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(FindEditWidth, 0),
            TooltipText = hit.SpawnRow > 0 ? $"Edit spawn row {hit.SpawnRow}" : "Not placed by a spawn row",
            Disabled = hit.SpawnRow <= 0,
            Visible = (AdminFindKind)findKind.Selected != AdminFindKind.Players,
        };
        edit.AddThemeFontSizeOverride("font_size", 12);
        edit.Pressed += () =>
        {
            setStatus($"Loading spawn row {hit.SpawnRow}…", false);
            requests.SpawnRowRequest(hit.SpawnRow);
        };
        line.AddChild(edit);

        var go = new Button
        {
            Text = "Go",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(FindGoWidth, 0),
            TooltipText = $"Travel to {hit.Name} in {AdminPanelUi.ZoneName(hit.Zone)}",
        };
        go.AddThemeFontSizeOverride("font_size", 12);
        go.Pressed += () =>
        {
            setStatus($"Moving to {hit.Name}…", false);
            requests.Go(hit.Zone, hit.X, hit.Z);
        };
        line.AddChild(go);

        return row;
    }
}
