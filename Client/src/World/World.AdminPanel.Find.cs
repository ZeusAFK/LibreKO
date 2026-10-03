using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int AdminFindListWidth = 720;
    private const int AdminFindListHeight = 360;
    private const float AdminFindLevelWidth = 48;
    private const float AdminFindMapWidth = 140;
    private const float AdminFindSpotWidth = 96;
    private const float AdminFindGoWidth = 58;
    private const float AdminFindEditWidth = 58;
    private static readonly string[] AdminFindKinds = [Localization.Loc.Tr("NPC"), Localization.Loc.Tr("Monster"), Localization.Loc.Tr("Player")];

    private OptionButton _admFindKind = null!;
    private LineEdit _admFindQuery = null!;
    private Label _admFindSummary = null!;
    private VBoxContainer _admFindList = null!;
    private List<AdminFindHit> _admFindHits = [];
    private int _admFindTotal;
    private string _admFindLastQuery = "";

    private Control BuildAdminFindTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(AdminFindListWidth, 0) };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Find"), UiIcons.Get("system/search")));

        var findRow = new HBoxContainer();
        findRow.AddThemeConstantOverride("separation", 5);
        box.AddChild(findRow);
        _admFindKind = UiTheme.Dropdown(AdminFindKinds);
        _admFindKind.CustomMinimumSize = new Vector2(110, 0);
        findRow.AddChild(_admFindKind);
        _admFindQuery = new LineEdit
        {
            PlaceholderText = Localization.Loc.Tr("name or id, part of it is enough"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _admFindQuery.TextSubmitted += _ => SendAdminFind();
        findRow.AddChild(_admFindQuery);
        var search = UiTheme.IconButton(UiIcons.Get("system/search"), Localization.Loc.Tr("Search"));
        search.Pressed += SendAdminFind;
        findRow.AddChild(search);

        _admFindSummary = UiTheme.Text(Localization.Loc.Tr("Search the live world: every placed NPC or monster, or every player online."), 12, UiTheme.TextLo);
        box.AddChild(_admFindSummary);

        box.AddChild(BuildAdminFindHeader());

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(AdminFindListWidth, AdminFindListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _admFindList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _admFindList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_admFindList);

        box.AddChild(UiTheme.Text(
            Localization.Loc.Tr("Go lands you on the spot the list shows; a monster may have wandered since."), 11, UiTheme.TextLo));

        if (Net.I != null) Net.I.AdminFindEvent += OnAdminFind;
        return box;
    }

    private static Control BuildAdminFindHeader()
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(AdminFindHeaderCell(Localization.Loc.Tr("Name"), 0, expand: true));
        line.AddChild(AdminFindHeaderCell(Localization.Loc.Tr("Level"), AdminFindLevelWidth));
        line.AddChild(AdminFindHeaderCell(Localization.Loc.Tr("Map"), AdminFindMapWidth));
        line.AddChild(AdminFindHeaderCell(Localization.Loc.Tr("Location"), AdminFindSpotWidth));
        line.AddChild(AdminFindHeaderCell("", AdminFindEditWidth));
        line.AddChild(AdminFindHeaderCell("", AdminFindGoWidth));
        return line;
    }

    private static Label AdminFindHeaderCell(string text, float width, bool expand = false)
    {
        var label = UiTheme.Text(text, 11, UiTheme.TextDim);
        label.CustomMinimumSize = new Vector2(width, 0);
        if (expand) label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    private void SendAdminFind()
    {
        string query = _admFindQuery.Text.Trim();
        if (query.Length == 0)
        {
            SetAdminStatus(Localization.Loc.Tr("Type a name or an id to search for."), true);
            return;
        }
        _admFindLastQuery = query;
        SetAdminStatus($"{Localization.Loc.Tr("Searching for")} “{query}”…", false);
        Net.I.SendAdminFind(_admFindKind.Selected, query);
    }

    private void OnAdminFind(int kind, int total, List<AdminFindHit> hits)
    {
        _admFindHits = hits;
        _admFindTotal = total;
        RefreshAdminFindList();
        SetAdminStatus("", false);
    }

    private void RefreshAdminFindList()
    {
        if (_admFindList == null || !IsInstanceValid(_admFindList)) return;
        foreach (Node child in _admFindList.GetChildren()) child.QueueFree();

        string what = _admFindKind.Selected switch
        {
            Net.AdminFindPlayers => Localization.Loc.Tr("player"),
            Net.AdminFindMonsters => Localization.Loc.Tr("monster"),
            _ => Localization.Loc.Tr("NPC"),
        };
        _admFindSummary.Text = _admFindHits.Count switch
        {
            0 => $"{Localization.Loc.Tr("No")} {what} {Localization.Loc.Tr("matches")} “{_admFindLastQuery}”.",
            _ when _admFindTotal > _admFindHits.Count =>
                $"{_admFindTotal} {Localization.Loc.Tr("matches for")} “{_admFindLastQuery}”, {Localization.Loc.Tr("showing the first")} {_admFindHits.Count}. {Localization.Loc.Tr("Narrow the search.")}",
            1 => $"{Localization.Loc.Tr("1 match for")} “{_admFindLastQuery}”.",
            _ => $"{_admFindHits.Count} {Localization.Loc.Tr("matches for")} “{_admFindLastQuery}”.",
        };

        foreach (var hit in _admFindHits)
            _admFindList.AddChild(BuildAdminFindRow(hit));
    }

    private Control BuildAdminFindRow(AdminFindHit hit)
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
            var tag = UiTheme.Pill(Localization.Loc.Tr("bot"), UiTheme.TextDim);
            tag.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            nameCell.AddChild(tag);
        }

        var level = UiTheme.Pill(hit.Level.ToString(), UiTheme.GoldBright);
        level.CustomMinimumSize = new Vector2(AdminFindLevelWidth, 0);
        level.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(level);

        var map = UiTheme.Text(AdminZoneName(hit.Zone), 12, hit.Zone == _zone ? UiTheme.GoldBright : UiTheme.TextHi);
        map.CustomMinimumSize = new Vector2(AdminFindMapWidth, 0);
        map.ClipText = true;
        line.AddChild(map);

        var spot = UiTheme.Text($"{hit.X}, {hit.Z}", 12, UiTheme.TextHi);
        spot.CustomMinimumSize = new Vector2(AdminFindSpotWidth, 0);
        line.AddChild(spot);

        var edit = new Button
        {
            Text = Localization.Loc.Tr("Edit"),
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(AdminFindEditWidth, 0),
            TooltipText = hit.SpawnRow > 0 ? $"{Localization.Loc.Tr("Edit spawn row")} {hit.SpawnRow}" : Localization.Loc.Tr("Not placed by a spawn row"),
            Disabled = hit.SpawnRow <= 0,
            Visible = _admFindKind.Selected != Net.AdminFindPlayers,
        };
        edit.AddThemeFontSizeOverride("font_size", 12);
        edit.Pressed += () =>
        {
            SetAdminStatus($"{Localization.Loc.Tr("Loading spawn row")} {hit.SpawnRow}…", false);
            Net.I.SendAdminSpawnRowRequest(hit.SpawnRow);
        };
        line.AddChild(edit);

        var go = new Button
        {
            Text = Localization.Loc.Tr("Go"),
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(AdminFindGoWidth, 0),
            TooltipText = $"{Localization.Loc.Tr("Travel to")} {hit.Name} {Localization.Loc.Tr("in")} {AdminZoneName(hit.Zone)}",
        };
        go.AddThemeFontSizeOverride("font_size", 12);
        go.Pressed += () =>
        {
            SetAdminStatus($"{Localization.Loc.Tr("Moving to")} {hit.Name}…", false);
            Net.I.SendAdminGo(hit.Zone, hit.X, hit.Z);
        };
        line.AddChild(go);

        return row;
    }
}
