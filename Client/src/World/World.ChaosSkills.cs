using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int ChaosSkillsPerPage = 6;
    private const int ChaosSkillColumns = 2;
    private const int ChaosSkillIconSize = 32;
    private const int ChaosSkillNameWidth = 120;
    private const int MpUsedText = 4323;
    private const int NoMpText = 4320;

    private CanvasLayer? _chaosSkillLayer;
    private HudWindow? _chaosSkillPage;
    private GridContainer? _chaosSkillGrid;
    private Label? _chaosSkillPageLabel;
    private List<SkillData.Skill>? _chaosSkills;
    private readonly List<ChaosSkillIcon> _chaosSkillIcons = new();
    private int _chaosSkillPageIndex;

    private bool InChaosZone => _zone == ChaosBattle.Zone;

    private bool ChaosBattleLocked => ChaosBattle.BattleLocked(_zone, Net.I.SelfAbnormal);

    private bool ToggleChaosSkillPage()
    {
        if (!InChaosZone) return false;
        if (_chaosSkillPage != null && IsInstanceValid(_chaosSkillPage) && _chaosSkillPage.Visible)
        {
            _chaosSkillPage.Visible = false;
            return true;
        }
        EnsureChaosSkillPage();
        RenderChaosSkillPage();
        ShowOnTop(_chaosSkillPage!);
        return true;
    }

    private List<SkillData.Skill> ChaosSkills() =>
        _chaosSkills ??= SkillData.All.Where(s => s.Tree == ChaosBattle.SkillTree).OrderBy(s => s.Id).ToList();

    private int ChaosSkillPages => Mathf.Max(1, (ChaosSkills().Count + ChaosSkillsPerPage - 1) / ChaosSkillsPerPage);

    private void EnsureChaosSkillPage()
    {
        if (_chaosSkillPage != null && IsInstanceValid(_chaosSkillPage)) return;
        _chaosSkillLayer = new CanvasLayer { Layer = 74 };
        AddChild(_chaosSkillLayer);
        _chaosSkillPage = new HudWindow("chaos_skill", "Chaos skill") { Visible = false };
        _chaosSkillPage.Closed += () => _chaosSkillPage.Visible = false;
        _chaosSkillLayer.AddChild(_chaosSkillPage);

        _chaosSkillGrid = new GridContainer { Columns = ChaosSkillColumns };
        _chaosSkillGrid.AddThemeConstantOverride("h_separation", 18);
        _chaosSkillGrid.AddThemeConstantOverride("v_separation", 10);
        _chaosSkillPage.Body.AddChild(_chaosSkillGrid);

        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pager.AddThemeConstantOverride("separation", 10);
        pager.AddChild(LookActionButton("◀", () => FlipChaosSkillPage(-1)));
        _chaosSkillPageLabel = UiTheme.Text("", 13, UiTheme.TextHi, HorizontalAlignment.Center);
        pager.AddChild(_chaosSkillPageLabel);
        pager.AddChild(LookActionButton("▶", () => FlipChaosSkillPage(1)));
        _chaosSkillPage.Body.AddChild(pager);
    }

    private void FlipChaosSkillPage(int delta)
    {
        _chaosSkillPageIndex = Mathf.Clamp(_chaosSkillPageIndex + delta, 0, ChaosSkillPages - 1);
        RenderChaosSkillPage();
    }

    private void RenderChaosSkillPage()
    {
        if (_chaosSkillGrid == null) return;
        foreach (var child in _chaosSkillGrid.GetChildren()) child.QueueFree();
        _chaosSkillIcons.Clear();
        var skills = ChaosSkills();
        int first = _chaosSkillPageIndex * ChaosSkillsPerPage;
        for (int i = first; i < Mathf.Min(skills.Count, first + ChaosSkillsPerPage); i++)
            _chaosSkillGrid.AddChild(ChaosSkillEntry(skills[i]));
        _chaosSkillPageLabel!.Text = $"Page {_chaosSkillPageIndex + 1}/{ChaosSkillPages}";
        _chaosSkillPage!.ResetSize();
        ChaosSkillPageTick();
    }

    private void ChaosSkillPageTick()
    {
        if (_chaosSkillPage == null || !IsInstanceValid(_chaosSkillPage) || !_chaosSkillPage.Visible) return;
        bool locked = ChaosBattleLocked;
        foreach (var icon in _chaosSkillIcons)
            if (IsInstanceValid(icon))
                icon.SetUsable(!locked && SkillItemHeld(icon.Skill));
    }

    private Control ChaosSkillEntry(SkillData.Skill skill)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var icon = new ChaosSkillIcon(skill, AddToHotbar)
        {
            CustomMinimumSize = new Vector2(ChaosSkillIconSize, ChaosSkillIconSize),
            TooltipText = ChaosSkillTooltip(skill),
        };
        _chaosSkillIcons.Add(icon);
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

    private void ApplyChaosHotbar()
    {
        if (!InChaosZone) return;
        var kept = Net.I.ChaosHotbar;
        System.Array.Clear(_hotbar, 0, HotTotal);
        bool any = false;
        if (kept != null)
            for (int i = 0; i < kept.Length && i < HotTotal; i++)
                if (ChaosBattle.KeptOnEntry(kept[i]))
                {
                    _hotbar[i] = kept[i];
                    any = true;
                }
        if (!any)
            for (int i = 0; i < ChaosBattle.DefaultBar.Length; i++)
                if (SkillData.Get(ChaosBattle.DefaultBar[i]) is { Tree: ChaosBattle.SkillTree })
                    _hotbar[i] = ChaosBattle.DefaultBar[i];
        _hotPage = 0;
        Net.I.ChaosHotbar = (int[])_hotbar.Clone();
        RefreshHotbar();
    }

    private sealed partial class ChaosSkillIcon : TextureRect
    {
        private readonly SkillData.Skill _skill;
        private readonly System.Action<int> _add;
        private bool _usable = true;

        public ChaosSkillIcon(SkillData.Skill skill, System.Action<int> add)
        {
            _skill = skill;
            _add = add;
            Texture = SkillData.Icon(skill.Id);
            ExpandMode = ExpandModeEnum.IgnoreSize;
            StretchMode = StretchModeEnum.KeepAspectCentered;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public SkillData.Skill Skill => _skill;

        public void SetUsable(bool usable)
        {
            if (_usable == usable) return;
            _usable = usable;
            Modulate = usable ? Colors.White : HotSlotCell.DimColor;
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right })
            {
                _add(_skill.Id);
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
            return new Godot.Collections.Dictionary { { "id", _skill.Id } };
        }
    }
}
