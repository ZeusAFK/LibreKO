using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private VBoxContainer _skillsContent = null!;
    private readonly SkillCell[] _skillCells = new SkillCell[SkillPage.SlotsPerPage];
    private readonly Dictionary<int, Button> _skillTabBtns = new();
    private List<SkillData.Page> _skillPages = new();
    private int _skillTab, _skillPageIndex, _skillSelected = -1;
    private Label _skillPageLbl = null!;
    private Button _skillPrevBtn = null!, _skillNextBtn = null!;
    private Label _skillDescLbl = null!, _skillMpLbl = null!, _skillPointLbl = null!, _skillLevelLbl = null!;
    private Label _skillItem0Lbl = null!, _skillItem1Lbl = null!, _skillItem2Lbl = null!;

    private const int MasteryTreeCount = MasteryPoints.LastTree - MasteryPoints.FirstTree + 1;

    internal MasteryPoints Mastery => Net.I.Mastery;
    private Label _masteryPoolLbl = null!;
    private readonly Label[] _masteryNameLbls = new Label[MasteryTreeCount];
    private readonly Label[] _masteryValLbls = new Label[MasteryTreeCount];
    private readonly Button[] _masteryBtns = new Button[MasteryTreeCount];

    private void SkillWindowInit()
    {
        BuildSkillWindow();
        Net.I.SkillPointChangeEvent += OnMasteryReject;
        Net.I.SkillResetEvent += OnSkillReset;
    }

    private void SkillWindowDispose()
    {
        Net.I.SkillPointChangeEvent -= OnMasteryReject;
        Net.I.SkillResetEvent -= OnSkillReset;
    }

    private void BuildSkillWindow()
    {
        _skillsContent = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        _skillsContent.AddThemeConstantOverride("separation", 6);
        PopulateSkillWindow();
    }

    // A class change swaps the whole skill set (a Blade gains Attack/Defense/Master on top of the
    // beginner Novice/Passive), so the window has to be rebuilt rather than just re-enabled.
    private void RebuildSkillWindow()
    {
        if (_skillsContent == null) return;

        foreach (var child in _skillsContent.GetChildren())
        {
            _skillsContent.RemoveChild(child);
            child.QueueFree();
        }
        _skillTabBtns.Clear();
        _skillTab = _skillPageIndex = 0;
        _skillSelected = -1;

        PopulateSkillWindow();
        RefreshSkillsEnabled();
        if (_mainWindows.TryGetValue("Skills", out var window))
            Callable.From(window.ResetSize).CallDeferred();
    }

    private void PopulateSkillWindow()
    {
        var root = _skillsContent;

        BuildMasteryBlock(root);

        var tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 4);
        root.AddChild(tabBar);

        _skillPages = SkillData.Pages(_selfClass, SelfTransformModel());
        foreach (var page in _skillPages)
        {
            int tab = page.Category;
            tabBar.AddChild(MakeSubTabButton(page.Label, tab, () => SelectSkillTab(tab), _skillTabBtns));
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
        for (int i = 0; i < _skillCells.Length; i++)
        {
            var cell = new SkillCell { OnAdd = AddToHotbar, OnSelect = SelectSkillCell };
            _skillCells[i] = cell;
            grid.AddChild(cell);
        }

        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pager.AddThemeConstantOverride("separation", 10);
        _skillPrevBtn = new Button { Text = "◀", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 0) };
        _skillPrevBtn.Pressed += () => TurnSkillPage(-1);
        _skillNextBtn = new Button { Text = "▶", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 0) };
        _skillNextBtn.Pressed += () => TurnSkillPage(1);
        _skillPageLbl = HudStyle.Label(12, HorizontalAlignment.Center);
        _skillPageLbl.CustomMinimumSize = new Vector2(70, 0);
        pager.AddChild(_skillPrevBtn);
        pager.AddChild(_skillPageLbl);
        pager.AddChild(_skillNextBtn);
        root.AddChild(pager);

        root.AddChild(new HSeparator());
        BuildSkillInfoBlock(root);

        SelectSkillTab(_skillPages.Count > 0 ? _skillPages[0].Category : SkillPage.Basic);
    }

    private void BuildSkillInfoBlock(VBoxContainer root)
    {
        var info = new VBoxContainer { CustomMinimumSize = new Vector2(0, 96) };
        info.AddThemeConstantOverride("separation", 1);
        root.AddChild(info);

        // ScrollContainer does not propagate child min size — an autowrapping Label would inflate this window.
        var descBox = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 34),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
        };
        _skillDescLbl = HudStyle.Label(12);
        _skillDescLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _skillDescLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _skillDescLbl.AddThemeColorOverride("font_color", UiTheme.Good);
        descBox.AddChild(_skillDescLbl);
        info.AddChild(descBox);

        _skillMpLbl = HudStyle.Label(11);
        info.AddChild(_skillMpLbl);

        var reqRow = new HBoxContainer();
        reqRow.AddThemeConstantOverride("separation", 12);
        _skillPointLbl = HudStyle.Label(11);
        _skillLevelLbl = HudStyle.Label(11);
        reqRow.AddChild(_skillPointLbl);
        reqRow.AddChild(_skillLevelLbl);
        info.AddChild(reqRow);

        _skillItem0Lbl = HudStyle.Label(11);
        _skillItem1Lbl = HudStyle.Label(11);
        _skillItem2Lbl = HudStyle.Label(11);
        info.AddChild(_skillItem0Lbl);
        info.AddChild(_skillItem1Lbl);
        info.AddChild(_skillItem2Lbl);
    }

    private List<SkillData.Skill> CurrentTabSkills()
    {
        foreach (var p in _skillPages)
            if (p.Category == _skillTab) return p.Skills;
        return new List<SkillData.Skill>();
    }

    private int CurrentTabPageCount()
    {
        int n = CurrentTabSkills().Count;
        int pages = (n + SkillPage.SlotsPerPage - 1) / SkillPage.SlotsPerPage;
        return Mathf.Clamp(pages, 1, SkillPage.MaxPages);
    }

    private void SelectSkillTab(int tab)
    {
        _skillTab = tab;
        _skillPageIndex = 0;
        _skillSelected = -1;
        foreach (var (k, b) in _skillTabBtns) b.ButtonPressed = k == tab;
        RefreshSkillPage();
    }

    private void TurnSkillPage(int delta)
    {
        int page = Mathf.Clamp(_skillPageIndex + delta, 0, CurrentTabPageCount() - 1);
        if (page == _skillPageIndex) return;
        _skillPageIndex = page;
        RefreshSkillPage();
    }

    private void RefreshSkillPage()
    {
        var skills = CurrentTabSkills();
        int first = _skillPageIndex * SkillPage.SlotsPerPage;
        if (_skillSelected < 0 && first < skills.Count) _skillSelected = skills[first].Id;
        for (int i = 0; i < _skillCells.Length; i++)
        {
            var s = first + i < skills.Count ? skills[first + i] : null;
            _skillCells[i].Bind(s, s != null && SkillRequirementMet(s));
            _skillCells[i].SetSelected(s != null && s.Id == _skillSelected);
        }
        int pages = CurrentTabPageCount();
        _skillPageLbl.Text = $"{_skillPageIndex + 1} Page";
        _skillPrevBtn.Disabled = _skillPageIndex <= 0;
        _skillNextBtn.Disabled = _skillPageIndex >= pages - 1;
        RefreshSkillInfo();
    }

    private void SelectSkillCell(SkillCell cell)
    {
        if (cell.Skill == null) return;
        _skillSelected = cell.Skill.Id;
        foreach (var c in _skillCells) c.SetSelected(c.Skill != null && c.Skill.Id == _skillSelected);
        RefreshSkillInfo();
    }

    private void RefreshSkillInfo()
    {
        var s = _skillSelected >= 0 ? SkillData.Get(_skillSelected) : null;
        if (s == null)
        {
            _skillDescLbl.Text = "";
            _skillMpLbl.Text = _skillPointLbl.Text = _skillLevelLbl.Text = "";
            _skillItem0Lbl.Text = _skillItem1Lbl.Text = _skillItem2Lbl.Text = "";
            return;
        }

        _skillDescLbl.Text = s.Desc.Replace('|', '\n');
        _skillMpLbl.Text = $"MP consumed : {s.Msp}";
        _skillPointLbl.Visible = SkillData.MasteryType(s.Tree) > 0;
        _skillPointLbl.Text = $"Required Skill Point : {s.Level}";
        _skillLevelLbl.Text = $"Required Level : {s.Level}";

        string weapon = SkillData.WeaponRequirementName(s.NeedWeapon);
        _skillItem0Lbl.Text = weapon.Length > 0 ? $"Basic item : Required Item : {weapon}" : "No basic item";
        _skillItem1Lbl.Text = s.NeedItem != 0
            ? $"Required item : {ItemData.DisplayName(s.NeedItem)}"
            : "No required item";
        _skillItem2Lbl.Text = s.ConsumedItem != 0 && s.ConsumedItem != s.NeedItem
            ? $"Item consumed : {ItemData.DisplayName(s.ConsumedItem)}"
            : "No item consumed";
    }

    private void RefreshSkillsEnabled()
    {
        RefreshSkillPage();
        RefreshMasteryUI();
        PluginNotifySkills();
    }

    private bool SkillRequirementMet(SkillData.Skill s)
    {
        int mastery = SkillData.MasteryType(s.Tree);
        if (mastery > 0) return Mastery.InTree(mastery) >= s.Level;
        return Sheet.Level <= 0 || Sheet.Level >= s.Level;
    }

    private bool SkillAssignable(int id) =>
        SkillData.Get(id) is not { } s || SkillRequirementMet(s);

    private void BuildMasteryBlock(VBoxContainer root)
    {
        root.AddChild(UiTheme.SectionTitle("Mastery"));
        _masteryPoolLbl = HudStyle.Label(14);
        root.AddChild(_masteryPoolLbl);

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        root.AddChild(grid);
        for (int type = MasteryPoints.FirstTree; type <= MasteryPoints.LastTree; type++)
        {
            int t = type, idx = type - MasteryPoints.FirstTree;
            _masteryNameLbls[idx] = HudStyle.Label(13);
            _masteryNameLbls[idx].Text = SkillData.PageName(_selfClass, type);
            _masteryNameLbls[idx].CustomMinimumSize = new Vector2(96, 0);
            grid.AddChild(_masteryNameLbls[idx]);
            _masteryValLbls[idx] = HudStyle.Label(13, HorizontalAlignment.Right);
            _masteryValLbls[idx].CustomMinimumSize = new Vector2(36, 0);
            grid.AddChild(_masteryValLbls[idx]);
            _masteryBtns[idx] = new Button { Text = "+", CustomMinimumSize = new Vector2(28, 22), FocusMode = Control.FocusModeEnum.None };
            _masteryBtns[idx].Pressed += () => OnMasterySpend(t);
            grid.AddChild(_masteryBtns[idx]);
        }
        root.AddChild(new HSeparator());
        RefreshMasteryUI();
    }

    private bool CanSpendMastery(int type) => Mastery.CanSpend(_selfClass, type, Sheet.Level);

    private string MasteryHint(int type)
    {
        if (!MasteryPoints.ClassHasTree(_selfClass, type))
            return "This mastery unlocks with your next class change";
        int cap = MasteryPoints.CapInTree(_selfClass, type, Sheet.Level);
        if (Mastery.InTree(type) >= cap)
            return type == MasteryPoints.MasterTree
                ? $"Capped at {cap} — one more per level above {MasteryPoints.MasterTreeMinLevel}"
                : $"Capped at your level ({cap})";
        return Mastery.Pool > 0 ? "Spend a mastery point" : "No mastery points left";
    }

    private void OnMasterySpend(int type)
    {
        if (!CanSpendMastery(type) || !Mastery.Spend(type)) return;
        Net.I.SendSkillPointChange(type);
        RefreshSkillsEnabled();
    }

    private void OnMasteryReject(int type, int value)
    {
        if (!MasteryPoints.IsTree(type)) return;
        Mastery.ApplyRejection(type, value);
        RefreshSkillsEnabled();
    }

    private void OnSkillReset(bool ok, int money, int pool)
    {
        if (!ok) return;
        Mastery.ResetTrees(pool);
        RefreshSkillsEnabled();
    }

    private void SyncMasteryPool(int pool)
    {
        Mastery.SetPool(pool);
        RefreshSkillsEnabled();
    }

    private void SyncMasteryPoints(byte[]? points)
    {
        if (points == null || points.Length < MasteryPoints.SlotCount) return;
        Mastery.Seed(points);
        RefreshSkillsEnabled();
    }

    private void RefreshMasteryUI()
    {
        if (_masteryPoolLbl == null) return;
        _masteryPoolLbl.Text = $"Mastery points: {Mastery.Pool}";
        for (int type = MasteryPoints.FirstTree; type <= MasteryPoints.LastTree; type++)
        {
            int idx = type - MasteryPoints.FirstTree;
            if (_masteryValLbls[idx] == null) continue;
            _masteryValLbls[idx].Text = Mastery.InTree(type).ToString();
            bool eligibleTree = type != MasteryPoints.MasterTree
                || MasteryPoints.ClassHasTree(_selfClass, type);
            _masteryNameLbls[idx].Visible = eligibleTree;
            _masteryValLbls[idx].Visible = eligibleTree;
            _masteryBtns[idx].Visible = eligibleTree;
            _masteryBtns[idx].Disabled = !CanSpendMastery(type);
            _masteryBtns[idx].TooltipText = MasteryHint(type);
        }

        // Keep the GM panel's Skills tab in sync with points spent here.
        LoadAdminSkillSpins();
    }

    private partial class SkillCell : PanelContainer
    {
        public SkillData.Skill? Skill { get; private set; }
        public System.Action<int>? OnAdd;
        public System.Action<SkillCell>? OnSelect;

        private readonly TextureRect _icon;
        private readonly Label _name;
        private readonly StyleBoxFlat _frame;
        private bool _met;

        public SkillCell()
        {
            CustomMinimumSize = new Vector2(150, 46);
            MouseFilter = MouseFilterEnum.Stop;
            _frame = new StyleBoxFlat { BgColor = new Color(0.07f, 0.08f, 0.10f, 0.92f), BorderColor = new Color(0.32f, 0.34f, 0.40f) };
            _frame.SetBorderWidthAll(1);
            AddThemeStyleboxOverride("panel", _frame);

            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            AddChild(row);

            _icon = new TextureRect
            {
                CustomMinimumSize = new Vector2(40, 40),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            row.AddChild(_icon);

            _name = HudStyle.Label(11);
            _name.ClipText = true;
            _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _name.VerticalAlignment = VerticalAlignment.Center;
            _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _name.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(_name);
        }

        public void Bind(SkillData.Skill? skill, bool met)
        {
            Skill = skill;
            _met = met;
            Visible = skill != null;
            if (skill == null) { TooltipText = ""; return; }

            _icon.Texture = met ? SkillData.Icon(skill.Id) ?? SkillData.EnigmaIcon() : SkillData.EnigmaIcon();
            _name.Text = skill.Name;
            _name.AddThemeColorOverride("font_color", met ? UiTheme.TextHi : UiTheme.TextLo);
            TooltipText = SkillTooltip(skill);
        }

        public void SetSelected(bool selected)
        {
            _frame.BorderColor = selected ? UiTheme.Gold : new Color(0.32f, 0.34f, 0.40f);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (Skill == null || ev is not InputEventMouseButton { Pressed: true } mb) return;
            if (mb.ButtonIndex == MouseButton.Left)
            {
                OnSelect?.Invoke(this);
                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Right && _met)
            {
                OnAdd?.Invoke(Skill.Id);
                AcceptEvent();
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (Skill == null || !_met) return default;
            var preview = new PanelContainer { CustomMinimumSize = new Vector2(48, 48) };
            var icon = SkillData.Icon(Skill.Id);
            if (icon != null)
                preview.AddChild(new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
            else { var l = HudStyle.Label(11, HorizontalAlignment.Center); l.Text = Skill.Name; preview.AddChild(l); }
            SetDragPreview(preview);
            return new Godot.Collections.Dictionary { { "id", Skill.Id } };
        }
    }
}
