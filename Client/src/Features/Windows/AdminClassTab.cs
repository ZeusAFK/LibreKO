using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminClassTab
{
    private static readonly int[] ClassSubtypes =
    { 1, 5, 6, 2, 7, 8, 3, 9, 10, 4, 11, 12, 13, 14, 15 };

    private readonly IAdminRequests requests;
    private readonly Func<AdminState> state;
    private readonly Action<string, bool> setStatus;

    public AdminClassTab(IAdminRequests requests, Func<AdminState> state, Action<string, bool> setStatus)
    {
        this.requests = requests;
        this.state = state;
        this.setStatus = setStatus;
    }

    private Label classLbl = null!;
    private VBoxContainer classList = null!;
    private OptionButton nationPick = null!;
    private OptionButton racePick = null!;
    private OptionButton classPick = null!;
    private int[] racePickIds = System.Array.Empty<int>();
    private int[] classPickIds = System.Array.Empty<int>();

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(AdminPanelUi.TabWidth, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Specialization", UiIcons.Get("game/main-hand")));
        classLbl = UiTheme.Text("", 14, UiTheme.GoldBright);
        box.AddChild(classLbl);

        box.AddChild(BuildTransformSection());

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.SectionTitle("Valid changes for this class"));
        classList = new VBoxContainer();
        classList.AddThemeConstantOverride("separation", 4);
        box.AddChild(classList);

        var note = UiTheme.Text(
            "A change refunds every mastery point, clears the branches and empties the skill bar.",
            11, UiTheme.Warning);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(340, 0);
        box.AddChild(note);

        return box;
    }

    private Control BuildTransformSection()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);

        box.AddChild(new HSeparator());
        box.AddChild(UiTheme.SectionTitle("Transform (test)"));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(grid);

        grid.AddChild(UiTheme.Text("Nation", 13, UiTheme.TextHi));
        nationPick = UiTheme.Dropdown(new[] { "Karus", "El Morad" });
        nationPick.ItemSelected += _ => RebuildLookPicks();
        grid.AddChild(nationPick);

        grid.AddChild(UiTheme.Text("Body (gender)", 13, UiTheme.TextHi));
        racePick = UiTheme.Dropdown();
        grid.AddChild(racePick);

        grid.AddChild(UiTheme.Text("Class", 13, UiTheme.TextHi));
        classPick = UiTheme.Dropdown();
        grid.AddChild(classPick);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        box.AddChild(row);

        var setClass = new Button { Text = "Set class", FocusMode = Control.FocusModeEnum.None };
        setClass.Pressed += OnSetPickedClass;
        row.AddChild(setClass);

        var setLook = new Button { Text = "Set nation + body", FocusMode = Control.FocusModeEnum.None };
        setLook.Pressed += OnSetPickedLook;
        row.AddChild(setLook);

        var note = UiTheme.Text(
            "Class + skills switch live (pick any class of any nation to test its skills). "
            + "Nation + body model apply after you relog to the character screen and back.",
            11, UiTheme.Warning);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(360, 0);
        box.AddChild(note);

        RebuildLookPicks();
        return box;
    }

    internal void Refresh()
    {
        if (classLbl == null) return;
        var current = state();
        classLbl.Text =
            $"{CharacterClassCatalog.SpecializationName(current.Class)}  ({current.Class})" +
            $"   ·   {CharacterClassCatalog.TierName(current.Class)}" +
            $"   ·   {Nations.Name(current.Nation)}";

        foreach (Node child in classList.GetChildren()) child.QueueFree();

        if (current.ClassOptions is not { Length: > 0 })
        {
            classList.AddChild(UiTheme.Text(
                "The server offers no alternate specialization for this class.", 12, UiTheme.TextLo));
            return;
        }

        foreach (int option in current.ClassOptions)
        {
            int target = option;
            var row = UiTheme.RowPanel();
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var name = UiTheme.Text(
                $"{CharacterClassCatalog.SpecializationName(target)}  ({target})", 13, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(name);
            line.AddChild(UiTheme.Pill(CharacterClassCatalog.TierName(target), UiTheme.Gold));

            var change = new Button { Text = "Change", FocusMode = Control.FocusModeEnum.None };
            change.AddThemeFontSizeOverride("font_size", 12);
            change.Pressed += () => requests.SetClass(target);
            line.AddChild(change);

            classList.AddChild(row);
        }
    }

    private void RebuildLookPicks()
    {
        if (nationPick == null) return;

        int nation = nationPick.Selected == 0 ? Nations.Karus : Nations.ElMorad;
        int classBase = Nations.ClassBase(nation);

        racePick.Clear();
        racePickIds = StarterStats.RacesFor(nation);
        foreach (int r in racePickIds)
            racePick.AddItem($"{StarterStats.RaceName(r)} ({r})");
        if (racePickIds.Length > 0) racePick.Selected = 0;

        classPick.Clear();
        var ids = new int[ClassSubtypes.Length];
        for (int i = 0; i < ClassSubtypes.Length; i++)
        {
            ids[i] = classBase + ClassSubtypes[i];
            classPick.AddItem($"{CharacterClassCatalog.SpecializationName(ids[i])} ({ids[i]})");
        }
        classPickIds = ids;
        if (ids.Length > 0) classPick.Selected = 0;
    }

    internal void SyncLookPicks()
    {
        if (nationPick == null) return;
        var current = state();

        nationPick.Selected = current.Nation == Nations.Karus ? 0 : 1;
        RebuildLookPicks();

        for (int i = 0; i < classPickIds.Length; i++)
            if (classPickIds[i] == current.Class) { classPick.Selected = i; break; }
        for (int i = 0; i < racePickIds.Length; i++)
            if (racePickIds[i] == current.Race) { racePick.Selected = i; break; }
    }

    private void OnSetPickedClass()
    {
        if (classPickIds.Length == 0 || classPick.Selected < 0) return;
        int id = classPickIds[classPick.Selected];
        requests.SetClass(id);
        setStatus($"Changing class to {CharacterClassCatalog.SpecializationName(id)} ({id})…", false);
    }

    private void OnSetPickedLook()
    {
        if (racePickIds.Length == 0 || racePick.Selected < 0) return;
        int nation = nationPick.Selected == 0 ? Nations.Karus : Nations.ElMorad;
        int race = racePickIds[racePick.Selected];
        requests.SetLook(nation, race);
        setStatus("Applying nation + body — relog to load the new model.", false);
    }
}
