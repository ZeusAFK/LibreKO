using Godot;

namespace LibreKO.Features.Windows;

internal sealed class SkillPresetFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly PresetSession preset;
    private readonly IPresetRequests presetRequests;
    private readonly SelfSession self;

    public SkillPresetFeature(IWorldContext context, PresetSession preset, IPresetRequests presetRequests, SelfSession self)
    {
        this.context = context;
        this.preset = preset;
        this.presetRequests = presetRequests;
        this.self = self;
    }

    public void Build()
    {
        for (int i = 0; i < presetPlans.Length; i++) presetPlans[i] = new PresetPlan();
        LoadPresetPlans();

        presetLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(presetLayer);
        presetPanel = new HudWindow("presets", "Presets", new Vector2(200, 140), bodyMinWidth: 260)
        { Visible = false };
        presetPanel.Closed += ClosePreset;
        presetLayer.AddChild(presetPanel);

        var root = presetPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        BuildPresetSlotBar(root);
        BuildPresetStatBlock(root);
        BuildPresetSkillBlock(root);
        RefreshPresetUI();

        preset.PresetStatResultEvent += OnPresetStatResult;
        preset.PresetSkillResultEvent += OnPresetSkillResult;
    }

    public void Dispose()
    {
        preset.PresetStatResultEvent -= OnPresetStatResult;
        preset.PresetSkillResultEvent -= OnPresetSkillResult;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!presetShown) return false;
        ClosePreset();
        return true;
    }

    private const string PresetStatKind = "stat";
    private const string PresetSkillKind = "skill";

    private CanvasLayer presetLayer = null!;
    private HudWindow presetPanel = null!;
    private bool presetShown;
    private int presetSlot;

    private readonly PresetPlan[] presetPlans = new PresetPlan[PresetPlan.SlotCount];
    private readonly Button[] presetSlotBtns = new Button[PresetPlan.SlotCount];
    private readonly Label[] presetStatLbls = new Label[CharacterSheet.StatCount];
    private readonly Button[] presetStatMinusBtns = new Button[CharacterSheet.StatCount];
    private readonly Button[] presetStatPlusBtns = new Button[CharacterSheet.StatCount];
    private readonly Label[] presetTreeLbls = new Label[PresetPlan.TreeCount];
    private readonly Label[] presetTreeNameLbls = new Label[PresetPlan.TreeCount];
    private Label presetStatPointsLbl = null!;
    private Label presetTreePointsLbl = null!;

    private PresetPlan ActivePreset => presetPlans[presetSlot];

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
            presetSlotBtns[i] = btn;
            bar.AddChild(btn);
        }
    }

    private void BuildPresetStatBlock(VBoxContainer root)
    {
        root.AddChild(UiTheme.SectionTitle("Stats (base + allocated)"));
        presetStatPointsLbl = HudStyle.Label(13);
        root.AddChild(presetStatPointsLbl);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 3);
        root.AddChild(grid);

        for (int i = 0; i < CharacterSheet.StatCount; i++)
        {
            int index = i;
            var name = HudStyle.Label(14);
            name.Text = CharacterSheet.StatLabels[i];
            name.CustomMinimumSize = new Vector2(40, 0);
            grid.AddChild(name);

            presetStatLbls[i] = HudStyle.Label(14, HorizontalAlignment.Right);
            presetStatLbls[i].CustomMinimumSize = new Vector2(74, 0);
            grid.AddChild(presetStatLbls[i]);

            grid.AddChild(presetStatMinusBtns[i] = PresetStepButton("-", () => PlanStat(index, -1)));
            grid.AddChild(presetStatPlusBtns[i] = PresetStepButton("+", () => PlanStat(index, 1)));
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
        presetTreePointsLbl = HudStyle.Label(13);
        root.AddChild(presetTreePointsLbl);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 3);
        root.AddChild(grid);

        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
        {
            int index = tree - MasteryPoints.FirstTree;
            presetTreeNameLbls[index] = HudStyle.Label(13);
            presetTreeNameLbls[index].CustomMinimumSize = new Vector2(96, 0);
            grid.AddChild(presetTreeNameLbls[index]);

            presetTreeLbls[index] = HudStyle.Label(13, HorizontalAlignment.Right);
            presetTreeLbls[index].CustomMinimumSize = new Vector2(36, 0);
            grid.AddChild(presetTreeLbls[index]);

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
        HoldRepeat.Attach(btn, pressed);
        return btn;
    }

    internal void TogglePreset()
    {
        if (presetShown) { ClosePreset(); return; }
        presetPanel.Visible = true;
        presetShown = true;
        RefreshPresetUI();
    }

    private void ClosePreset()
    {
        if (!presetShown) return;
        presetShown = false;
        presetPanel.Visible = false;
    }

    private void SelectPresetSlot(int slot)
    {
        if (slot == presetSlot) return;
        presetSlot = slot;
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
        if (!MasteryPoints.ClassHasTree(self.Class, tree)) return;

        var plan = ActivePreset;
        int next = plan.Trees[index] + delta;
        if (next < 0
            || next > MasteryPoints.CapInTree(self.Class, tree, self.Sheet.Level)
            || next > PlannedTreeBudget(index))
            return;

        plan.Trees[index] = next;
        StorePresetPlan(PresetSkillKind, plan.Trees);
        RefreshPresetUI();
    }

    private int PlannedStatBudget(int index) => ActivePreset.StatBudget(self.Class, index, self.Sheet.PointsForLevel);

    private int PlannedTreeBudget(int index)
    {
        int spent = 0;
        var trees = ActivePreset.Trees;
        for (int i = 0; i < trees.Length; i++) if (i != index) spent += trees[i];
        return self.Mastery.PointsForLevel - spent;
    }

    private void RefreshPresetUI()
    {
        for (int i = 0; i < presetSlotBtns.Length; i++)
        {
            var btn = presetSlotBtns[i];
            if (btn == null || !GodotObject.IsInstanceValid(btn)) continue;
            btn.AddThemeStyleboxOverride("normal", UiTheme.TopTab(i == presetSlot));
            btn.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
            btn.AddThemeStyleboxOverride("hover", UiTheme.TopTab(i == presetSlot, true));
        }

        var plan = ActivePreset;
        int statSpent = 0;
        for (int i = 0; i < CharacterSheet.StatCount; i++)
        {
            statSpent += plan.Stats[i];
            if (presetStatLbls[i] == null || !GodotObject.IsInstanceValid(presetStatLbls[i])) continue;
            presetStatLbls[i].Text = plan.StatValue(self.Class, i).ToString();
            presetStatLbls[i].TooltipText =
                $"Base: {StarterStats.BaseForClass(self.Class).StatAtRow(i)}\nAllocated: {plan.Stats[i]}";
            presetStatMinusBtns[i].Disabled = plan.Stats[i] <= 0;
            presetStatPlusBtns[i].Disabled = plan.Stats[i] >= PlannedStatBudget(i);
        }
        if (presetStatPointsLbl != null && GodotObject.IsInstanceValid(presetStatPointsLbl))
            presetStatPointsLbl.Text = $"Planned {statSpent} of {self.Sheet.PointsForLevel} stat point(s)";

        int treeSpent = 0;
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
        {
            int index = tree - MasteryPoints.FirstTree;
            treeSpent += plan.Trees[index];
            if (presetTreeNameLbls[index] != null && GodotObject.IsInstanceValid(presetTreeNameLbls[index]))
            {
                bool has = MasteryPoints.ClassHasTree(self.Class, tree);
                presetTreeNameLbls[index].Text = has ? SkillData.PageName(self.Class, tree) : "—";
                presetTreeNameLbls[index].Modulate = has ? Colors.White : UiTheme.TextDim;
            }
            if (presetTreeLbls[index] != null && GodotObject.IsInstanceValid(presetTreeLbls[index]))
                presetTreeLbls[index].Text = plan.Trees[index].ToString();
        }
        if (presetTreePointsLbl != null && GodotObject.IsInstanceValid(presetTreePointsLbl))
            presetTreePointsLbl.Text = $"Planned {treeSpent} of {self.Mastery.PointsForLevel} mastery point(s)";
    }

    private const string RedistributeAtKaishan =
    "Your points are already spent. [Grand Merchant] Kaishan in Moradon redistributes them.";

    private const string StatPlanOutOfRange = "The plan exceeds your available stat points or the stat limit.";

    private void ApplyStatPreset()
    {
        if (!PresetPlan.IsRedistributed(self.Class, self.Sheet))
        {
            context.Log.Notice(RedistributeAtKaishan);
            return;
        }
        SendStatPlan();
    }

    private void SendStatPlan()
    {
        if (!ActivePreset.TryStatValues(self.Class, self.Sheet.Points, out var values, out int remaining))
        {
            context.Log.Notice(StatPlanOutOfRange);
            return;
        }
        presetRequests.StatPreset(values, remaining);
    }

    private void ApplySkillPreset()
    {
        if (self.Mastery.Pool < self.Mastery.PointsForLevel)
        {
            context.Log.Notice(RedistributeAtKaishan);
            return;
        }
        SendSkillPlan();
    }

    private void SendSkillPlan()
    {
        var plan = ActivePreset;
        int spent = 0;
        foreach (int v in plan.Trees) spent += v;
        presetRequests.SkillPreset(plan.Trees, self.Mastery.Pool - spent);
    }

    internal event System.Action? StatPlanApplied;

    internal event System.Action? SkillPlanApplied;

    private static readonly Color PresetResultColour = new(1f, 0f, 0f);

    private void PresetResultLine(int kind, PresetResult result)
    {
        var (id, fallback, withCode) = PresetTexts.For(kind, (int)result);
        string text = ItemData.Text(id, fallback);
        context.Log.Notice(withCode ? $"{text}{(int)result}" : text, PresetResultColour);
    }

    private void OnPresetStatResult(PresetResult result, PresetStatState state)
    {
        PresetResultLine(PresetTexts.StatKind, result);
        if (result != PresetResult.Applied) return;

        self.Sheet.ApplyReset(state.Stats, state.Points, state.TotalHit);
        self.Sheet.SetMaxWeight(state.MaxWeight);
        self.Vitals.ApplyMaxima(state.MaxHp, state.MaxMp);
        System.Array.Clear(ActivePreset.Stats);
        StorePresetPlan(PresetStatKind, ActivePreset.Stats);
        StatPlanApplied?.Invoke();
        RefreshPresetUI();
    }

    private void OnPresetSkillResult(PresetResult result, PresetSkillState state)
    {
        PresetResultLine(PresetTexts.SkillKind, result);
        if (result != PresetResult.Applied) return;

        self.Mastery.ApplyTrees(state.Trees, state.Pool);
        System.Array.Clear(ActivePreset.Trees);
        StorePresetPlan(PresetSkillKind, ActivePreset.Trees);
        SkillPlanApplied?.Invoke();
        RefreshPresetUI();
    }

    private void LoadPresetPlans()
    {
        string character = PresetCharacterKey();
        for (int slot = 0; slot < PresetPlan.SlotCount; slot++)
        {
            presetPlans[slot].SetStats(
                Config.GetPresetPlan(character, PresetStatKind, slot, CharacterSheet.StatCount));
            presetPlans[slot].SetTrees(
                Config.GetPresetPlan(character, PresetSkillKind, slot, PresetPlan.TreeCount));
        }
    }

    private void StorePresetPlan(string kind, int[] values)
    => Config.SavePresetPlan(PresetCharacterKey(), kind, presetSlot, values);

    private string PresetCharacterKey()
    {
        string name = self.LastEnter.Name;
        return string.IsNullOrEmpty(name) ? "default" : name;
    }
}
