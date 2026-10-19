using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminSkillsTab
{
    private const int SkillTreeCount = MasteryPoints.LastTree - MasteryPoints.FirstTree + 1;

    private readonly IAdminRequests requests;
    private readonly SelfSession self;
    private readonly Func<AdminState> state;
    private readonly Action<string, bool> setStatus;

    public AdminSkillsTab(IAdminRequests requests, SelfSession self, Func<AdminState> state, Action<string, bool> setStatus)
    {
        this.requests = requests;
        this.self = self;
        this.state = state;
        this.setStatus = setStatus;
    }

    private SpinBox skillPool = null!;
    private readonly SpinBox[] skillTrees = new SpinBox[SkillTreeCount];
    private Label skillLbl = null!;

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(AdminPanelUi.TabWidth, 0) };
        box.AddThemeConstantOverride("separation", 7);

        box.AddChild(UiTheme.SectionTitle("Skill points", UiIcons.Get("game/main-hand")));
        skillLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        box.AddChild(skillLbl);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        box.AddChild(grid);

        grid.AddChild(UiTheme.Text("Pool (free)", 13, UiTheme.TextHi));
        skillPool = PointsBox();
        grid.AddChild(skillPool);

        string[] treeNames = { "Tree 1", "Tree 2", "Tree 3", "Master" };
        for (int i = 0; i < SkillTreeCount; i++)
        {
            grid.AddChild(UiTheme.Text(treeNames[i], 13, UiTheme.TextHi));
            skillTrees[i] = PointsBox();
            grid.AddChild(skillTrees[i]);
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
        apply.Pressed += OnApplySkills;
        row.AddChild(apply);

        var revert = new Button { Text = "Revert", FocusMode = Control.FocusModeEnum.None };
        revert.Pressed += LoadSkillSpins;
        row.AddChild(revert);

        var reset = new Button { Text = "Reset skills", FocusMode = Control.FocusModeEnum.None };
        reset.Pressed += () =>
        {
            requests.SetSkill(0, System.Array.Empty<int>(), reset: true);
            setStatus("Resetting skills…", false);
        };
        row.AddChild(reset);

        return box;
    }

    private static SpinBox PointsBox() => UiTheme.NumberBox(0, byte.MaxValue, 1, 96);

    internal void LoadSkillSpins()
    {
        if (skillPool == null) return;
        var sp = self.Mastery?.ToArray() ?? state().SkillPoints ?? new byte[MasteryPoints.SlotCount];
        int pool = sp.Length > MasteryPoints.PoolSlot ? sp[MasteryPoints.PoolSlot] : 0;
        skillPool.Value = pool;
        int spent = 0;
        for (int i = 0; i < SkillTreeCount; i++)
        {
            int slot = MasteryPoints.FirstTree + i;
            int points = sp.Length > slot ? sp[slot] : 0;
            skillTrees[i].Value = points;
            spent += points;
        }
        if (skillLbl != null)
            skillLbl.Text = $"Pool {pool}   ·   spent {spent}";
    }

    private void OnApplySkills()
    {
        var trees = new int[SkillTreeCount];
        for (int i = 0; i < trees.Length; i++)
            trees[i] = (int)skillTrees[i].Value;
        requests.SetSkill((int)skillPool.Value, trees, reset: false);
        setStatus("Applying skill points…", false);
    }
}
