using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string PresetStatKind = "stat";
    private const string PresetSkillKind = "skill";
    private const int PresetMaxRaceBaseStat = 70;

    private CanvasLayer _presetLayer = null!;
    private HudWindow _presetPanel = null!;
    private bool _presetShown;
    private int _presetSlot;

    private readonly PresetPlan[] _presetPlans = new PresetPlan[PresetPlan.SlotCount];
    private readonly Button[] _presetSlotBtns = new Button[PresetPlan.SlotCount];
    private readonly Label[] _presetStatLbls = new Label[CharacterSheet.StatCount];
    private readonly Label[] _presetTreeLbls = new Label[PresetPlan.TreeCount];
    private readonly Label[] _presetTreeNameLbls = new Label[PresetPlan.TreeCount];
    private Label _presetStatPointsLbl = null!;
    private Label _presetTreePointsLbl = null!;

    private PresetPlan ActivePreset => _presetPlans[_presetSlot];

    private void PresetInit()
    {
        for (int i = 0; i < _presetPlans.Length; i++) _presetPlans[i] = new PresetPlan();
        LoadPresetPlans();

        _presetLayer = new CanvasLayer { Layer = 74 };
        AddChild(_presetLayer);
        _presetPanel = new HudWindow("presets", "Presets", new Vector2(200, 140), bodyMinWidth: 260)
        { Visible = false };
        _presetPanel.Closed += ClosePreset;
        _presetLayer.AddChild(_presetPanel);

        var root = _presetPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        BuildPresetSlotBar(root);
        BuildPresetStatBlock(root);
        BuildPresetSkillBlock(root);
        RefreshPresetUI();

        Net.I.PresetStatResultEvent += OnPresetStatResult;
        Net.I.PresetSkillResultEvent += OnPresetSkillResult;
    }

    private void PresetDispose()
    {
        Net.I.PresetStatResultEvent -= OnPresetStatResult;
        Net.I.PresetSkillResultEvent -= OnPresetSkillResult;
    }

    private void BuildPresetSlotBar(VBoxContainer root)
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 4);
        root.AddChild(bar);
        for (int i = 0; i < PresetPlan.SlotCount; i++)
        {
            int slot = i;
            var btn = UiTheme.TopTabButton($"Plan {i + 1}");
            btn.Pressed += () => SelectPresetSlot(slot);
            _presetSlotBtns[i] = btn;
            bar.AddChild(btn);
        }
    }

    private void BuildPresetStatBlock(VBoxContainer root)
    {
        root.AddChild(UiTheme.SectionTitle("Stats"));
        _presetStatPointsLbl = HudStyle.Label(13);
        root.AddChild(_presetStatPointsLbl);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 3);
        root.AddChild(grid);

        for (int i = 0; i < CharacterSheet.StatCount; i++)
        {
            int index = i;
            var name = HudStyle.Label(14);
            name.Text = StatLabels[i];
            name.CustomMinimumSize = new Vector2(40, 0);
            grid.AddChild(name);

            _presetStatLbls[i] = HudStyle.Label(14, HorizontalAlignment.Right);
            _presetStatLbls[i].CustomMinimumSize = new Vector2(74, 0);
            grid.AddChild(_presetStatLbls[i]);

            grid.AddChild(PresetStepButton("-", () => PlanStat(index, -1)));
            grid.AddChild(PresetStepButton("+", () => PlanStat(index, 1)));
        }

        var apply = new Button
        {
            Text = "Apply stat plan",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Retail only accepts this straight after a stat redistribution",
        };
        apply.Pressed += ApplyStatPreset;
        root.AddChild(apply);
        root.AddChild(new HSeparator());
    }

    private void BuildPresetSkillBlock(VBoxContainer root)
    {
        root.AddChild(UiTheme.SectionTitle("Mastery"));
        _presetTreePointsLbl = HudStyle.Label(13);
        root.AddChild(_presetTreePointsLbl);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 3);
        root.AddChild(grid);

        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
        {
            int index = tree - MasteryPoints.FirstTree;
            _presetTreeNameLbls[index] = HudStyle.Label(13);
            _presetTreeNameLbls[index].CustomMinimumSize = new Vector2(96, 0);
            grid.AddChild(_presetTreeNameLbls[index]);

            _presetTreeLbls[index] = HudStyle.Label(13, HorizontalAlignment.Right);
            _presetTreeLbls[index].CustomMinimumSize = new Vector2(36, 0);
            grid.AddChild(_presetTreeLbls[index]);

            grid.AddChild(PresetStepButton("-", () => PlanTree(index, -1)));
            grid.AddChild(PresetStepButton("+", () => PlanTree(index, 1)));
        }

        var apply = new Button
        {
            Text = "Apply mastery plan",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Retail only accepts this straight after a skill redistribution",
        };
        apply.Pressed += ApplySkillPreset;
        root.AddChild(apply);
    }

    private static Button PresetStepButton(string text, System.Action pressed)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(26, 22),
            FocusMode = Control.FocusModeEnum.None,
        };
        btn.Pressed += pressed;
        return btn;
    }

    private void TogglePreset()
    {
        if (_presetShown) { ClosePreset(); return; }
        _presetPanel.Visible = true;
        _presetShown = true;
        RefreshPresetUI();
    }

    private void ClosePreset()
    {
        if (!_presetShown) return;
        _presetShown = false;
        _presetPanel.Visible = false;
    }

    private void SelectPresetSlot(int slot)
    {
        if (slot == _presetSlot) return;
        _presetSlot = slot;
        RefreshPresetUI();
    }

    private void PlanStat(int index, int delta)
    {
        var plan = ActivePreset;
        int next = plan.Stats[index] + delta;
        if (next < 0 || next > PlannedStatBudget(index)) return;
        plan.Stats[index] = next;
        StorePresetPlan(PresetStatKind, plan.Stats);
        RefreshPresetUI();
    }

    private void PlanTree(int index, int delta)
    {
        int tree = MasteryPoints.FirstTree + index;
        if (!MasteryPoints.ClassHasTree(_selfClass, tree)) return;

        var plan = ActivePreset;
        int next = plan.Trees[index] + delta;
        if (next < 0
            || next > MasteryPoints.CapInTree(_selfClass, tree, Sheet.Level)
            || next > PlannedTreeBudget(index))
            return;

        plan.Trees[index] = next;
        StorePresetPlan(PresetSkillKind, plan.Trees);
        RefreshPresetUI();
    }

    private int PlannedStatBudget(int index)
    {
        int spent = 0;
        var stats = ActivePreset.Stats;
        for (int i = 0; i < stats.Length; i++) if (i != index) spent += stats[i];
        return System.Math.Min(Sheet.PointsForLevel - spent, PresetStatCap(index));
    }

    private int PresetStatCap(int index) => Sheet.AtBaseStats
        ? CharacterSheet.StatMax - Sheet.StatAtRow(index)
        : CharacterSheet.StatMax - PresetMaxRaceBaseStat;

    private int PlannedTreeBudget(int index)
    {
        int spent = 0;
        var trees = ActivePreset.Trees;
        for (int i = 0; i < trees.Length; i++) if (i != index) spent += trees[i];
        return Mastery.PointsForLevel - spent;
    }

    private void RefreshPresetUI()
    {
        for (int i = 0; i < _presetSlotBtns.Length; i++)
        {
            var btn = _presetSlotBtns[i];
            if (btn == null || !GodotObject.IsInstanceValid(btn)) continue;
            btn.AddThemeStyleboxOverride("normal", UiTheme.TopTab(i == _presetSlot));
            btn.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
            btn.AddThemeStyleboxOverride("hover", UiTheme.TopTab(i == _presetSlot, true));
        }

        var plan = ActivePreset;
        int statSpent = 0;
        for (int i = 0; i < CharacterSheet.StatCount; i++)
        {
            statSpent += plan.Stats[i];
            if (_presetStatLbls[i] == null || !GodotObject.IsInstanceValid(_presetStatLbls[i])) continue;
            _presetStatLbls[i].Text = plan.Stats[i].ToString();
        }
        if (_presetStatPointsLbl != null && GodotObject.IsInstanceValid(_presetStatPointsLbl))
            _presetStatPointsLbl.Text = $"Planned {statSpent} of {Sheet.PointsForLevel} stat point(s)";

        int treeSpent = 0;
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
        {
            int index = tree - MasteryPoints.FirstTree;
            treeSpent += plan.Trees[index];
            if (_presetTreeNameLbls[index] != null && GodotObject.IsInstanceValid(_presetTreeNameLbls[index]))
            {
                bool has = MasteryPoints.ClassHasTree(_selfClass, tree);
                _presetTreeNameLbls[index].Text = has ? SkillData.PageName(_selfClass, tree) : "—";
                _presetTreeNameLbls[index].Modulate = has ? Colors.White : UiTheme.TextDim;
            }
            if (_presetTreeLbls[index] != null && GodotObject.IsInstanceValid(_presetTreeLbls[index]))
                _presetTreeLbls[index].Text = plan.Trees[index].ToString();
        }
        if (_presetTreePointsLbl != null && GodotObject.IsInstanceValid(_presetTreePointsLbl))
            _presetTreePointsLbl.Text = $"Planned {treeSpent} of {Mastery.PointsForLevel} mastery point(s)";
    }

    private void ApplyStatPreset()
    {
        if (!Sheet.AtBaseStats)
        {
            RequestReset(Net.ResetKindStat, thenApplyPlan: true);
            return;
        }
        SendStatPlan();
    }

    private void SendStatPlan()
    {
        var plan = ActivePreset;
        var values = new int[CharacterSheet.StatCount];
        int spent = 0;
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Sheet.StatAtRow(i) + plan.Stats[i];
            spent += plan.Stats[i];
        }
        Net.I.SendStatPreset(values, Sheet.Points - spent);
    }

    private void ApplySkillPreset()
    {
        if (Mastery.Pool < Mastery.PointsForLevel)
        {
            RequestReset(Net.ResetKindSkill, thenApplyPlan: true);
            return;
        }
        SendSkillPlan();
    }

    private void SendSkillPlan()
    {
        var plan = ActivePreset;
        int spent = 0;
        foreach (int v in plan.Trees) spent += v;
        Net.I.SendSkillPreset(plan.Trees, Mastery.Pool - spent);
    }

    private void OnPresetStatResult(int result, PresetStatState state)
    {
        if (result != Net.PresetApplied)
        {
            CombatNotice(result switch
            {
                Net.PresetStatNeedsRedistribution => "Redistribute your stats before applying a plan.",
                Net.PresetStatClassError => "Your class cannot use a stat plan.",
                Net.PresetStatPointsMismatch => "The plan no longer matches your stat points.",
                _ => "The stat plan could not be applied.",
            });
            return;
        }

        Sheet.ApplyReset(state.Stats, state.Points, state.TotalHit);
        Sheet.SetMaxWeight(state.MaxWeight);
        Vitals.ApplyMaxima(state.MaxHp, state.MaxMp);
        System.Array.Clear(ActivePreset.Stats);
        StorePresetPlan(PresetStatKind, ActivePreset.Stats);
        RefreshStatsUI();
        RefreshPresetUI();
        CombatNotice("Stat plan applied.");
    }

    private void OnPresetSkillResult(int result, PresetSkillState state)
    {
        if (result != Net.PresetApplied)
        {
            CombatNotice(result switch
            {
                Net.PresetSkillNeedsRedistribution => "Redistribute your mastery before applying a plan.",
                Net.PresetSkillLevelTooLow => $"Mastery plans need level {MasteryPoints.MinLevel} or above.",
                Net.PresetSkillNeedsFirstJobChange => "Mastery plans unlock after your first class change.",
                Net.PresetSkillNeedsSecondJobChange => "That mastery unlocks after your second class change.",
                Net.PresetSkillMasterFailed => "Your master skill points are above what your level allows.",
                Net.PresetSkillPointsMismatch => "The plan no longer matches your mastery points.",
                _ => "The mastery plan could not be applied.",
            });
            return;
        }

        Mastery.ApplyTrees(state.Trees, state.Pool);
        System.Array.Clear(ActivePreset.Trees);
        StorePresetPlan(PresetSkillKind, ActivePreset.Trees);
        RefreshMasteryUI();
        RefreshPresetUI();
        CombatNotice("Mastery plan applied.");
    }

    private void LoadPresetPlans()
    {
        string character = PresetCharacterKey();
        for (int slot = 0; slot < PresetPlan.SlotCount; slot++)
        {
            _presetPlans[slot].SetStats(
                Config.GetPresetPlan(character, PresetStatKind, slot, CharacterSheet.StatCount));
            _presetPlans[slot].SetTrees(
                Config.GetPresetPlan(character, PresetSkillKind, slot, PresetPlan.TreeCount));
        }
    }

    private void StorePresetPlan(string kind, int[] values)
        => Config.SavePresetPlan(PresetCharacterKey(), kind, _presetSlot, values);

    private string PresetCharacterKey()
    {
        string name = Net.I.LastEnter.Name;
        return string.IsNullOrEmpty(name) ? "default" : name;
    }
}
