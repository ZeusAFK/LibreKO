using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class SkillWindowFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly DevelopSession develop;
    private readonly IDevelopRequests developRequests;
    private readonly HotbarFeature hotbar;
    private readonly SelfSession self;
    private readonly SkillPresetFeature skillPreset;

    public SkillWindowFeature(IWorldContext context, DevelopSession develop, IDevelopRequests developRequests, HotbarFeature hotbar, SelfSession self, SkillPresetFeature skillPreset)
    {
        this.context = context;
        this.develop = develop;
        this.developRequests = developRequests;
        this.hotbar = hotbar;
        this.self = self;
        this.skillPreset = skillPreset;
    }

    public void Build()
    {
        BuildSkillWindow();
        develop.SkillPointChangeEvent += OnMasteryReject;
        skillPreset.SkillPlanApplied += RefreshMasteryUI;
        develop.SkillResetEvent += OnSkillReset;
    }

    public void Dispose()
    {
        skillPreset.SkillPlanApplied -= RefreshMasteryUI;
        develop.SkillPointChangeEvent -= OnMasteryReject;
        develop.SkillResetEvent -= OnSkillReset;
    }

    internal event System.Action? SkillsRebuilt;
    internal VBoxContainer SkillsContent { get; private set; } = null!;
    private readonly SkillCell[] skillCells = new SkillCell[SkillPage.SlotsPerPage];

    private readonly Dictionary<int, Button> skillTabBtns = new();

    private List<SkillData.Page> skillPages = new();
    private int skillTab;
    private int skillPageIndex;
    private int skillSelected = -1;
    private Label skillPageLbl = null!;
    private Button skillPrevBtn = null!;
    private Button skillNextBtn = null!;
    private Label skillDescLbl = null!;
    private Label skillMpLbl = null!;
    private Label skillPointLbl = null!;
    private Label skillLevelLbl = null!;
    private Label skillItem0Lbl = null!;
    private Label skillItem1Lbl = null!;
    private Label skillItem2Lbl = null!;
    private Label skillStateLbl = null!;
    private Godot.Timer? masteryRepeat;
    private int masteryRepeatType;

    private const int MasteryTreeCount = MasteryPoints.LastTree - MasteryPoints.FirstTree + 1;

    private Label masteryPoolLbl = null!;
    private readonly Label[] masteryNameLbls = new Label[MasteryTreeCount];
    private readonly Label[] masteryValLbls = new Label[MasteryTreeCount];
    private readonly Button[] masteryBtns = new Button[MasteryTreeCount];

    internal void BuildSkillWindow()
    {
        SkillsContent = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        SkillsContent.AddThemeConstantOverride("separation", 6);
        PopulateSkillWindow();
    }

    // A class change swaps the whole skill set (a Blade gains Attack/Defense/Master on top of the
    // beginner Novice/Passive), so the window has to be rebuilt rather than just re-enabled.
    internal void RebuildSkillWindow()
    {
        if (SkillsContent == null) return;

        foreach (var child in SkillsContent.GetChildren())
        {
            SkillsContent.RemoveChild(child);
            child.QueueFree();
        }
        skillTabBtns.Clear();
        skillTab = skillPageIndex = 0;
        skillSelected = -1;

        PopulateSkillWindow();
        RefreshSkillsEnabled();
        if (context.MainWindows.SkillsWindow() is { } window)
            Callable.From(window.ResetSize).CallDeferred();
    }

    private void PopulateSkillWindow()
    {
        var root = SkillsContent;

        BuildMasteryBlock(root);

        var tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 4);
        root.AddChild(tabBar);

        skillPages = SkillData.Pages(self.Class, context.Self.TransformModel());
        foreach (var page in skillPages)
        {
            int tab = page.Category;
            tabBar.AddChild(UiKit.SubTabButton(page.Label, tab, () => SelectSkillTab(tab), skillTabBtns));
        }

        var grid = new GridContainer { Columns = SkillPage.Columns, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 4);
        grid.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true } wheel) return;
            if (wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown)) return;
            TurnSkillPage(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            grid.AcceptEvent();
        };
        root.AddChild(grid);
        for (int i = 0; i < skillCells.Length; i++)
        {
            var cell = new SkillCell { OnAdd = hotbar.AddTo, OnSelect = SelectSkillCell };
            skillCells[i] = cell;
            grid.AddChild(cell);
        }

        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pager.AddThemeConstantOverride("separation", 10);
        skillPrevBtn = new Button { Text = "◀", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 0) };
        skillPrevBtn.Pressed += () => TurnSkillPage(-1);
        skillNextBtn = new Button { Text = "▶", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 0) };
        skillNextBtn.Pressed += () => TurnSkillPage(1);
        skillPageLbl = HudStyle.Label(12, HorizontalAlignment.Center);
        skillPageLbl.CustomMinimumSize = new Vector2(70, 0);
        pager.AddChild(skillPrevBtn);
        pager.AddChild(skillPageLbl);
        pager.AddChild(skillNextBtn);
        root.AddChild(pager);

        root.AddChild(new HSeparator());
        BuildSkillInfoBlock(root);

        SelectSkillTab(skillPages.Count > 0 ? skillPages[0].Category : SkillPage.Basic);
    }

    private void BuildSkillInfoBlock(VBoxContainer root)
    {
        var info = new VBoxContainer { CustomMinimumSize = new Vector2(0, 96) };
        info.AddThemeConstantOverride("separation", 1);
        root.AddChild(info);

        skillStateLbl = HudStyle.Label(12);
        info.AddChild(skillStateLbl);

        // ScrollContainer does not propagate child min size — an autowrapping Label would inflate this window.
        var descBox = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 34),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        skillDescLbl = HudStyle.Label(12);
        skillDescLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        skillDescLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        skillDescLbl.AddThemeColorOverride("font_color", UiTheme.Good);
        descBox.AddChild(skillDescLbl);
        info.AddChild(descBox);

        skillMpLbl = HudStyle.Label(11);
        info.AddChild(skillMpLbl);

        var reqRow = new HBoxContainer();
        reqRow.AddThemeConstantOverride("separation", 12);
        skillPointLbl = HudStyle.Label(11);
        skillLevelLbl = HudStyle.Label(11);
        reqRow.AddChild(skillPointLbl);
        reqRow.AddChild(skillLevelLbl);
        info.AddChild(reqRow);

        skillItem0Lbl = HudStyle.Label(11);
        skillItem1Lbl = HudStyle.Label(11);
        skillItem2Lbl = HudStyle.Label(11);
        info.AddChild(skillItem0Lbl);
        info.AddChild(skillItem1Lbl);
        info.AddChild(skillItem2Lbl);
    }

    private List<SkillData.Skill> CurrentTabSkills()
    {
        foreach (var p in skillPages)
            if (p.Category == skillTab) return p.Skills;
        return new List<SkillData.Skill>();
    }

    private int CurrentTabPageCount()
    {
        int n = CurrentTabSkills().Count;
        int pages = (n + SkillPage.SlotsPerPage - 1) / SkillPage.SlotsPerPage;
        return Mathf.Clamp(pages, 1, SkillPage.MaxPages);
    }

    internal void SelectSkillTab(int tab)
    {
        skillTab = tab;
        skillPageIndex = 0;
        skillSelected = -1;
        foreach (var (k, b) in skillTabBtns) b.ButtonPressed = k == tab;
        RefreshSkillPage();
        RefreshMasteryUI();
    }

    private void TurnSkillPage(int delta)
    {
        int page = Mathf.Clamp(skillPageIndex + delta, 0, CurrentTabPageCount() - 1);
        if (page == skillPageIndex) return;
        skillPageIndex = page;
        RefreshSkillPage();
    }

    private void RefreshSkillPage()
    {
        var skills = CurrentTabSkills();
        int first = skillPageIndex * SkillPage.SlotsPerPage;
        if (skillSelected < 0 && first < skills.Count) skillSelected = skills[first].Id;
        for (int i = 0; i < skillCells.Length; i++)
        {
            var s = first + i < skills.Count ? skills[first + i] : null;
            skillCells[i].Bind(s, s != null ? self.SkillLearnOf(s).State : SkillLearnState.Locked);
            skillCells[i].SetSelected(s != null && s.Id == skillSelected);
        }
        int pages = CurrentTabPageCount();
        skillPageLbl.Text = $"{skillPageIndex + 1} Page";
        skillPrevBtn.Disabled = skillPageIndex <= 0;
        skillNextBtn.Disabled = skillPageIndex >= pages - 1;
        RefreshSkillInfo();
    }

    private void SelectSkillCell(SkillCell cell)
    {
        if (cell.Skill == null) return;
        skillSelected = cell.Skill.Id;
        foreach (var c in skillCells) c.SetSelected(c.Skill != null && c.Skill.Id == skillSelected);
        RefreshSkillInfo();
    }

    private void RefreshSkillInfo()
    {
        var s = skillSelected >= 0 ? SkillData.Get(skillSelected) : null;
        if (s == null)
        {
            skillStateLbl.Text = "";
            skillDescLbl.Text = "";
            skillMpLbl.Text = skillPointLbl.Text = skillLevelLbl.Text = "";
            skillItem0Lbl.Text = skillItem1Lbl.Text = skillItem2Lbl.Text = "";
            return;
        }

        var learn = self.SkillLearnOf(s);
        skillStateLbl.Text = SkillLearnText(s, learn);
        skillStateLbl.AddThemeColorOverride("font_color", learn.State switch
        {
            SkillLearnState.Learned => UiTheme.TextHi,
            SkillLearnState.Learnable => UiTheme.Neutral,
            _ => UiTheme.Bad,
        });
        skillDescLbl.Text = s.Desc.Replace('|', '\n');
        skillMpLbl.Text = $"MP consumed : {s.Msp}";
        skillPointLbl.Visible = SkillData.MasteryType(s.Tree) > 0;
        skillPointLbl.Text = $"Required Skill Point : {s.Level}";
        skillLevelLbl.Text = $"Required Level : {s.Level}";

        string weapon = SkillData.WeaponRequirementName(s.NeedWeapon);
        skillItem0Lbl.Text = weapon.Length > 0 ? $"Basic item : Required Item : {weapon}" : "No basic item";
        skillItem1Lbl.Text = s.NeedItem != 0
            ? $"Required item : {ItemData.DisplayName(s.NeedItem)}"
            : "No required item";
        skillItem2Lbl.Text = s.ConsumedItem != 0 && s.ConsumedItem != s.NeedItem
            ? $"Item consumed : {ItemData.DisplayName(s.ConsumedItem)}"
            : "No item consumed";
    }

    internal void RefreshSkillsEnabled()
    {
        RefreshSkillPage();
        RefreshMasteryUI();
        context.Plugins.Skills();
    }

    private string SkillLearnText(SkillData.Skill s, SkillLearnInfo learn) => learn.State switch
    {
        SkillLearnState.Learned => "Learned",
        SkillLearnState.Learnable =>
            $"Spend {learn.PointsShort} more {(learn.PointsShort == 1 ? "point" : "points")} in {SkillData.PageName(self.Class, SkillData.MasteryType(s.Tree))}",
        _ when learn.NeedsClassChange => "Unlocks with your next class change",
        _ => $"Reach level {learn.LevelNeeded}",
    };

    private void BuildMasteryBlock(VBoxContainer root)
    {
        root.AddChild(UiTheme.SectionTitle("Mastery"));
        masteryPoolLbl = HudStyle.Label(14);
        masteryPoolLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var poolRow = new HBoxContainer();
        poolRow.AddChild(masteryPoolLbl);
        var presetButton = new Button
        {
            Text = "Skill Preset",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Plan a mastery spread",
        };
        presetButton.AddThemeFontSizeOverride("font_size", 12);
        presetButton.Pressed += skillPreset.TogglePreset;
        poolRow.AddChild(presetButton);
        root.AddChild(poolRow);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        root.AddChild(grid);
        for (int type = MasteryPoints.FirstTree; type <= MasteryPoints.LastTree; type++)
        {
            int t = type, idx = type - MasteryPoints.FirstTree;
            masteryNameLbls[idx] = HudStyle.Label(13);
            masteryNameLbls[idx].Text = SkillData.PageName(self.Class, type);
            masteryNameLbls[idx].CustomMinimumSize = new Vector2(96, 0);
            grid.AddChild(masteryNameLbls[idx]);
            masteryValLbls[idx] = HudStyle.Label(13, HorizontalAlignment.Right);
            masteryValLbls[idx].CustomMinimumSize = new Vector2(36, 0);
            grid.AddChild(masteryValLbls[idx]);
            masteryBtns[idx] = new Button
            {
                Text = "+", CustomMinimumSize = new Vector2(28, 22), FocusMode = Control.FocusModeEnum.None,
                ActionMode = BaseButton.ActionModeEnum.Press,
            };
            masteryBtns[idx].Pressed += () => OnMasterySpend(t);
            masteryBtns[idx].ButtonDown += () => StartMasteryRepeat(t);
            masteryBtns[idx].ButtonUp += StopMasteryRepeat;
            grid.AddChild(masteryBtns[idx]);
        }
        root.AddChild(new HSeparator());
        RefreshMasteryUI();
    }

    internal bool CanSpendMastery(int type) => self.Mastery.CanSpend(self.Class, type, self.Sheet.Level);

    private void StartMasteryRepeat(int type)
    {
        if (masteryRepeat == null)
        {
            masteryRepeat = new Godot.Timer { OneShot = false };
            masteryRepeat.Timeout += OnMasteryRepeat;
            SkillsContent.AddChild(masteryRepeat);
        }
        masteryRepeatType = type;
        masteryRepeat.Start(SkillLearn.RepeatDelaySeconds);
    }

    private void StopMasteryRepeat() => masteryRepeat?.Stop();

    private void OnMasteryRepeat()
    {
        if (masteryRepeat == null) return;
        if (!CanSpendMastery(masteryRepeatType))
        {
            StopMasteryRepeat();
            return;
        }
        OnMasterySpend(masteryRepeatType);
        masteryRepeat.Start(SkillLearn.RepeatEverySeconds);
    }

    internal string MasteryHint(int type)
    {
        if (!MasteryPoints.ClassHasTree(self.Class, type))
            return "This mastery unlocks with your next class change";
        int cap = MasteryPoints.CapInTree(self.Class, type, self.Sheet.Level);
        if (self.Mastery.InTree(type) >= cap)
            return type == MasteryPoints.MasterTree
                ? $"Capped at {cap} — one more per level above {MasteryPoints.MasterTreeMinLevel}"
                : $"Capped at your level ({cap})";
        return self.Mastery.Pool > 0 ? "Spend a mastery point" : "No mastery points left";
    }

    internal void OnMasterySpend(int type)
    {
        if (!CanSpendMastery(type) || !self.Mastery.Spend(type)) return;
        developRequests.SkillPointChange(type);
        RefreshSkillsEnabled();
    }

    private void OnMasteryReject(int type, int value)
    {
        if (!MasteryPoints.IsTree(type)) return;
        self.Mastery.ApplyRejection(type, value);
        RefreshSkillsEnabled();
    }

    private void OnSkillReset(bool ok, int money, int pool)
    {
        if (!ok) return;
        self.Mastery.ResetTrees(pool);
        RefreshSkillsEnabled();
        hotbar.Clear();
    }

    private void RefreshMasteryUI()
    {
        if (masteryPoolLbl == null) return;
        masteryPoolLbl.Text = $"Mastery points: {self.Mastery.Pool}";
        for (int type = MasteryPoints.FirstTree; type <= MasteryPoints.LastTree; type++)
        {
            int idx = type - MasteryPoints.FirstTree;
            if (masteryValLbls[idx] == null) continue;
            masteryValLbls[idx].Text = self.Mastery.InTree(type).ToString();
            bool eligibleTree = type != MasteryPoints.MasterTree
                || MasteryPoints.ClassHasTree(self.Class, type);
            masteryNameLbls[idx].Visible = eligibleTree;
            masteryNameLbls[idx].AddThemeColorOverride("font_color",
                type == SkillData.MasteryType(skillTab) ? UiTheme.GoldBright : UiTheme.TextHi);
            masteryValLbls[idx].Visible = eligibleTree;
            masteryBtns[idx].Visible = eligibleTree;
            masteryBtns[idx].Disabled = !CanSpendMastery(type);
            masteryBtns[idx].TooltipText = MasteryHint(type);
        }

        // Keep the GM panel's Skills tab in sync with points spent here.
        SkillsRebuilt?.Invoke();
    }

    private partial class SkillCell : PanelContainer
    {
        public SkillData.Skill? Skill { get; private set; }
        public System.Action<int>? OnAdd;
        public System.Action<SkillCell>? OnSelect;

        private readonly TextureRect iconRect;
        private readonly Label name;
        private readonly StyleBoxFlat frame;
        private bool met;
        private const float LearnableIconShade = 0.55f;

        public SkillCell()
        {
            CustomMinimumSize = new Vector2(150, 46);
            MouseFilter = MouseFilterEnum.Stop;
            frame = new StyleBoxFlat { BgColor = new Color(0.07f, 0.08f, 0.10f, 0.92f), BorderColor = new Color(0.32f, 0.34f, 0.40f) };
            frame.SetBorderWidthAll(1);
            AddThemeStyleboxOverride("panel", frame);

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            AddChild(row);

            iconRect = new TextureRect
            {
                CustomMinimumSize = new Vector2(40, 40),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(iconRect);

            name = HudStyle.Label(11);
            name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(name);
        }

        public void Bind(SkillData.Skill? skill, SkillLearnState state)
        {
            Skill = skill;
            met = state == SkillLearnState.Learned;
            Visible = skill != null;
            if (skill == null) { TooltipText = ""; return; }

            iconRect.Texture = state == SkillLearnState.Locked
                ? SkillIcons.Enigma()
                : SkillIcons.Get(skill.Id) ?? SkillIcons.Enigma();
            iconRect.SelfModulate = state == SkillLearnState.Learnable
                ? new Color(LearnableIconShade, LearnableIconShade, LearnableIconShade)
                : Colors.White;
            name.Text = skill.Name;
            name.AddThemeColorOverride("font_color", state switch
            {
                SkillLearnState.Learned => UiTheme.TextHi,
                SkillLearnState.Learnable => UiTheme.Neutral,
                _ => UiTheme.TextLo,
            });
            TooltipText = SkillText.Tooltip(skill);
        }

        public void SetSelected(bool selected)
        {
            frame.BorderColor = selected ? UiTheme.Gold : new Color(0.32f, 0.34f, 0.40f);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (Skill == null || ev is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Left)
            {
                OnSelect?.Invoke(this);
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Right && met && HotbarAssign.CanAdd(Skill.Id, Skill.Type1))
            {
                OnAdd?.Invoke(Skill.Id);
                AcceptEvent();
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (Skill == null || !met || !HotbarAssign.CanDrag(Skill.Type1, Skill.Type2)) return default;
            var preview = new PanelContainer { CustomMinimumSize = new Vector2(48, 48) };
            var icon = SkillIcons.Get(Skill.Id);
            if (icon != null)
                preview.AddChild(new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            else { var l = HudStyle.Label(11, HorizontalAlignment.Center); l.Text = Skill.Name; preview.AddChild(l); }
            DragLayer.Show(this, preview);
            return new Godot.Collections.Dictionary { { "id", Skill.Id } };
        }
    }
}
