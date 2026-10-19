using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class ChaosSkillWindowFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly HotbarFeature hotbar;

    public ChaosSkillWindowFeature(IWorldContext context, HotbarFeature hotbar)
    {
        this.context = context;
        this.hotbar = hotbar;
    }

    public void LateTick(double delta, double nowSec) => ChaosSkillPageTick();

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const int ChaosSkillsPerPage = 6;
    private const int ChaosSkillColumns = 2;
    private const int ChaosSkillIconSize = 32;
    private const int ChaosSkillNameWidth = 120;
    private const int MpUsedText = 4323;
    private const int NoMpText = 4320;

    private CanvasLayer? chaosSkillLayer;
    internal HudWindow? ChaosSkillPage { get; private set; }
    private GridContainer? chaosSkillGrid;
    private Label? chaosSkillPageLabel;
    private List<SkillData.Skill>? chaosSkills;
    private readonly List<ChaosSkillIcon> chaosSkillIcons = new();
    private int chaosSkillPageIndex;

    internal bool ToggleChaosSkillPage()
    {
        if (!hotbar.InChaosZone) return false;
        if (ChaosSkillPage != null && GodotObject.IsInstanceValid(ChaosSkillPage) && ChaosSkillPage.Visible)
        {
            ChaosSkillPage.Visible = false;
            return true;
        }
        EnsureChaosSkillPage();
        RenderChaosSkillPage();
        UiKit.ShowOnTop(ChaosSkillPage!);
        return true;
    }

    private List<SkillData.Skill> ChaosSkills() =>
        chaosSkills ??= SkillData.All.Where(s => s.Tree == ChaosBattle.SkillTree).OrderBy(s => s.Id).ToList();

    private int ChaosSkillPages => Mathf.Max(1, (ChaosSkills().Count + ChaosSkillsPerPage - 1) / ChaosSkillsPerPage);

    internal void EnsureChaosSkillPage()
    {
        if (ChaosSkillPage != null && GodotObject.IsInstanceValid(ChaosSkillPage)) return;
        chaosSkillLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(chaosSkillLayer);
        ChaosSkillPage = new HudWindow("chaos_skill", "Chaos skill") { Visible = false };
        ChaosSkillPage.Closed += () => ChaosSkillPage.Visible = false;
        chaosSkillLayer.AddChild(ChaosSkillPage);

        chaosSkillGrid = new GridContainer { Columns = ChaosSkillColumns };
        chaosSkillGrid.AddThemeConstantOverride("h_separation", 18);
        chaosSkillGrid.AddThemeConstantOverride("v_separation", 10);
        ChaosSkillPage.Body.AddChild(chaosSkillGrid);

        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pager.AddThemeConstantOverride("separation", 10);
        pager.AddChild(GenderChangeFeature.LookActionButton("◀", () => FlipChaosSkillPage(-1)));
        chaosSkillPageLabel = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        pager.AddChild(chaosSkillPageLabel);
        pager.AddChild(GenderChangeFeature.LookActionButton("▶", () => FlipChaosSkillPage(1)));
        ChaosSkillPage.Body.AddChild(pager);
    }

    private void FlipChaosSkillPage(int delta)
    {
        chaosSkillPageIndex = Mathf.Clamp(chaosSkillPageIndex + delta, 0, ChaosSkillPages - 1);
        RenderChaosSkillPage();
    }

    internal void RenderChaosSkillPage()
    {
        if (chaosSkillGrid == null) return;
        foreach (var child in chaosSkillGrid.GetChildren()) child.QueueFree();
        chaosSkillIcons.Clear();
        var skills = ChaosSkills();
        int first = chaosSkillPageIndex * ChaosSkillsPerPage;
        for (int i = first; i < Mathf.Min(skills.Count, first + ChaosSkillsPerPage); i++)
            chaosSkillGrid.AddChild(ChaosSkillEntry(skills[i]));
        chaosSkillPageLabel!.Text = $"Page {chaosSkillPageIndex + 1}/{ChaosSkillPages}";
        ChaosSkillPage!.ResetSize();
        ChaosSkillPageTick();
    }

    internal void ChaosSkillPageTick()
    {
        if (ChaosSkillPage == null || !GodotObject.IsInstanceValid(ChaosSkillPage) || !ChaosSkillPage.Visible) return;
        bool locked = hotbar.ChaosBattleLocked;
        foreach (var icon in chaosSkillIcons)
            if (GodotObject.IsInstanceValid(icon))
                icon.SetUsable(!locked && hotbar.SkillItemHeld(icon.Skill));
    }

    private Control ChaosSkillEntry(SkillData.Skill skill)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var icon = new ChaosSkillIcon(skill, hotbar.AddTo)
        {
            CustomMinimumSize = new Vector2(ChaosSkillIconSize, ChaosSkillIconSize),
            TooltipText = ChaosSkillTooltip(skill),
        };
        chaosSkillIcons.Add(icon);
        row.AddChild(icon);
        var name = UiTheme.Text(skill.Name, 12, UiTheme.TextHi);
        name.CustomMinimumSize = new Vector2(ChaosSkillNameWidth, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.ClipText = true;
        row.AddChild(name);
        return row;
    }

    private static string ChaosSkillTooltip(SkillData.Skill skill)
    {
        string mp = skill.Msp > 0
            ? string.Format(ItemData.Text(MpUsedText, "MP consumed : %d").Replace("%d", "{0}"), skill.Msp)
            : ItemData.Text(NoMpText, "Doesn't use MP");
        return string.IsNullOrEmpty(skill.Desc) ? $"{skill.Name}\n{mp}" : $"{skill.Name}\n{skill.Desc}\n{mp}";
    }

    private sealed partial class ChaosSkillIcon : TextureRect
    {
        private readonly SkillData.Skill skill;
        private readonly System.Action<int> add;
        private bool usable = true;

        public ChaosSkillIcon(SkillData.Skill skill, System.Action<int> add)
        {
            this.skill = skill;
            this.add = add;
            Texture = SkillIcons.Get(skill.Id);
            ExpandMode = ExpandModeEnum.IgnoreSize;
            StretchMode = StretchModeEnum.KeepAspectCentered;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public SkillData.Skill Skill => skill;

        public void SetUsable(bool usable)
        {
            if (this.usable == usable) return;
            this.usable = usable;
            Modulate = usable ? Colors.White : HotSlotCell.DimColor;
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
            {
                add(skill.Id);
                AcceptEvent();
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            var preview = new TextureRect
            {
                Texture = Texture,
                CustomMinimumSize = new Vector2(48, 48),
                ExpandMode = ExpandModeEnum.IgnoreSize,
                StretchMode = StretchModeEnum.KeepAspectCentered,
            };
            DragLayer.Show(this, preview);
            return new Godot.Collections.Dictionary { { "id", skill.Id } };
        }
    }
}
