using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int AdminSkillTreeCount = MasteryPoints.LastTree - MasteryPoints.FirstTree + 1;

    private SpinBox _admLevelSpin = null!;

    private SpinBox _admSkillPool = null!;
    private readonly SpinBox[] _admSkillTrees = new SpinBox[AdminSkillTreeCount];
    private Label _admSkillLbl = null!;

    private OptionButton _admNationPick = null!;
    private OptionButton _admRacePick = null!;
    private OptionButton _admClassPick = null!;
    private int[] _admRacePickIds = System.Array.Empty<int>();
    private int[] _admClassPickIds = System.Array.Empty<int>();

    private static readonly int[] AdminClassSubtypes =
        { 1, 5, 6, 2, 7, 8, 3, 9, 10, 4, 11, 12, 13, 14, 15 };

    private Control BuildAdminLevelSection()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 7);
        box.AddChild(AdminHeading("Level", "system/level"));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        box.AddChild(row);
        row.AddChild(AdminFieldLabel("Level", 66));
        _admLevelSpin = AdminSpin(CharacterSheet.MinLevel, CharacterSheet.MaxLevel, 135);
        row.AddChild(_admLevelSpin);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        box.AddChild(actions);

        var set = AdminButton("Set", 56);
        set.Pressed += () =>
        {
            Net.I.SendAdminSetLevel((int)_admLevelSpin.Value, reset: false);
            SetAdminStatus("Setting level…", false);
        };
        actions.AddChild(set);

        var reset = AdminButton("Reset to level");
        reset.Pressed += () =>
        {
            Net.I.SendAdminSetLevel((int)_admLevelSpin.Value, reset: true);
            SetAdminStatus("Resetting to level…", false);
        };
        actions.AddChild(reset);

        if (_isGm)
        {
            box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
            box.AddChild(BuildAdminCollisionRow());
        }

        return box;
    }

    private void RefreshAdminLevelSpin()
    {
        if (_admLevelSpin == null) return;
        _admLevelSpin.Value = Mathf.Clamp(_admState.Level, CharacterSheet.MinLevel, CharacterSheet.MaxLevel);
    }

    private Control BuildAdminSkillsTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Skill points", UiIcons.Get("game/main-hand")));
        _admSkillLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(_admSkillLbl);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        box.AddChild(grid);

        grid.AddChild(UiTheme.Text("Pool (free)", 13, UiTheme.TextHi));
        _admSkillPool = MakeAdminSpin(0, byte.MaxValue, 1);
        grid.AddChild(_admSkillPool);

        string[] treeNames = { "Tree 1", "Tree 2", "Tree 3", "Master" };
        for (int i = 0; i < AdminSkillTreeCount; i++)
        {
            grid.AddChild(UiTheme.Text(treeNames[i], 13, UiTheme.TextHi));
            _admSkillTrees[i] = MakeAdminSpin(0, byte.MaxValue, 1);
            grid.AddChild(_admSkillTrees[i]);
        }

        var note = UiTheme.Text(
            "Pool = unspent mastery points. Trees = points already spent in each mastery branch. "
            + "Skills unlock as their branch reaches the required points.",
            11, UiTheme.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(360, 0);
        box.AddChild(note);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        box.AddChild(row);

        var apply = new Button { Text = "Apply", FocusMode = Control.FocusModeEnum.None };
        apply.Pressed += OnAdminApplySkills;
        row.AddChild(apply);

        var revert = new Button { Text = "Revert", FocusMode = Control.FocusModeEnum.None };
        revert.Pressed += LoadAdminSkillSpins;
        row.AddChild(revert);

        var reset = new Button { Text = "Reset skills", FocusMode = Control.FocusModeEnum.None };
        reset.Pressed += () =>
        {
            Net.I.SendAdminSetSkill(0, System.Array.Empty<int>(), reset: true);
            SetAdminStatus("Resetting skills…", false);
        };
        row.AddChild(reset);

        return box;
    }

    private void LoadAdminSkillSpins()
    {
        if (_admSkillPool == null) return;
        // Read the LIVE mastery object, not the last admin-state snapshot, so the pool
        // reflects points spent in the normal Skills window in real time.
        var sp = Mastery?.ToArray() ?? _admState.SkillPoints ?? new byte[MasteryPoints.SlotCount];
        int pool = sp.Length > MasteryPoints.PoolSlot ? sp[MasteryPoints.PoolSlot] : 0;
        _admSkillPool.Value = pool;
        int spent = 0;
        for (int i = 0; i < AdminSkillTreeCount; i++)
        {
            int slot = MasteryPoints.FirstTree + i;
            int points = sp.Length > slot ? sp[slot] : 0;
            _admSkillTrees[i].Value = points;
            spent += points;
        }
        if (_admSkillLbl != null)
            _admSkillLbl.Text = $"Pool {pool}   ·   spent {spent}";
    }

    private void OnAdminApplySkills()
    {
        var trees = new int[AdminSkillTreeCount];
        for (int i = 0; i < trees.Length; i++)
            trees[i] = (int)_admSkillTrees[i].Value;
        Net.I.SendAdminSetSkill((int)_admSkillPool.Value, trees, reset: false);
        SetAdminStatus("Applying skill points…", false);
    }

    private Control BuildAdminTransformSection()
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
        _admNationPick = UiTheme.Dropdown(new[] { "Karus", "El Morad" });
        _admNationPick.ItemSelected += _ => RebuildAdminLookPicks();
        grid.AddChild(_admNationPick);

        grid.AddChild(UiTheme.Text("Body (gender)", 13, UiTheme.TextHi));
        _admRacePick = UiTheme.Dropdown();
        grid.AddChild(_admRacePick);

        grid.AddChild(UiTheme.Text("Class", 13, UiTheme.TextHi));
        _admClassPick = UiTheme.Dropdown();
        grid.AddChild(_admClassPick);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        box.AddChild(row);

        var setClass = new Button { Text = "Set class", FocusMode = Control.FocusModeEnum.None };
        setClass.Pressed += OnAdminSetPickedClass;
        row.AddChild(setClass);

        var setLook = new Button { Text = "Set nation + body", FocusMode = Control.FocusModeEnum.None };
        setLook.Pressed += OnAdminSetPickedLook;
        row.AddChild(setLook);

        var note = UiTheme.Text(
            "Class + skills switch live (pick any class of any nation to test its skills). "
            + "Nation + body model apply after you relog to the character screen and back.",
            11, UiTheme.Warning);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(360, 0);
        box.AddChild(note);

        RebuildAdminLookPicks();
        return box;
    }

    private void RebuildAdminLookPicks()
    {
        if (_admNationPick == null) return;

        int nation = _admNationPick.Selected == 0 ? Nations.Karus : Nations.ElMorad;
        int classBase = Nations.ClassBase(nation);

        _admRacePick.Clear();
        _admRacePickIds = StarterStats.RacesFor(nation);
        foreach (int r in _admRacePickIds)
            _admRacePick.AddItem($"{StarterStats.RaceName(r)} ({r})");
        if (_admRacePickIds.Length > 0) _admRacePick.Selected = 0;

        _admClassPick.Clear();
        var ids = new int[AdminClassSubtypes.Length];
        for (int i = 0; i < AdminClassSubtypes.Length; i++)
        {
            ids[i] = classBase + AdminClassSubtypes[i];
            _admClassPick.AddItem($"{CharacterClassCatalog.SpecializationName(ids[i])} ({ids[i]})");
        }
        _admClassPickIds = ids;
        if (ids.Length > 0) _admClassPick.Selected = 0;
    }

    private void SyncAdminLookPicks()
    {
        if (_admNationPick == null) return;

        _admNationPick.Selected = _admState.Nation == Nations.Karus ? 0 : 1;
        RebuildAdminLookPicks();

        for (int i = 0; i < _admClassPickIds.Length; i++)
            if (_admClassPickIds[i] == _admState.Class) { _admClassPick.Selected = i; break; }
        for (int i = 0; i < _admRacePickIds.Length; i++)
            if (_admRacePickIds[i] == _admState.Race) { _admRacePick.Selected = i; break; }
    }

    private void OnAdminSetPickedClass()
    {
        if (_admClassPickIds.Length == 0 || _admClassPick.Selected < 0) return;
        int id = _admClassPickIds[_admClassPick.Selected];
        Net.I.SendAdminSetClass(id);
        SetAdminStatus($"Changing class to {CharacterClassCatalog.SpecializationName(id)} ({id})…", false);
    }

    private void OnAdminSetPickedLook()
    {
        if (_admRacePickIds.Length == 0 || _admRacePick.Selected < 0) return;
        int nation = _admNationPick.Selected == 0 ? Nations.Karus : Nations.ElMorad;
        int race = _admRacePickIds[_admRacePick.Selected];
        Net.I.SendAdminSetLook(nation, race);
        SetAdminStatus("Applying nation + body — relog to load the new model.", false);
    }
}
