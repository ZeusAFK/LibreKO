using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string PetBarLayoutId = "hud_pet_bar";
    private const int PetBarSlotSize = 40;
    private const int PetBarGap = 3;
    private const int TextPetNotSummoned = 14017;
    private const int TextNotPetSkill = 14018;
    private const string PetNoTargetText = "Select a monster for your familiar.";
    private const string PetNoManaText = "Your familiar does not have enough MP.";

    private CanvasLayer _petBarLayer = null!;
    private PanelContainer _petBar = null!;
    private PetSkillCell _petAttackCell = null!;
    private readonly List<PetSkillCell> _petCells = new();
    private Button _petPageBtn = null!;
    private List<int> _petBarSkills = new();
    private int _petBarPage;
    private readonly Dictionary<int, double> _petSkillReadyAt = new();
    private readonly Dictionary<int, (int CasterId, int Serial)> _petSkillCasts = new();
    private int _petSkillGeneration;
    private int _petSkillCastSerial;

    private void PetBarInit()
    {
        _petBarLayer = new CanvasLayer { Layer = 64 };
        AddChild(_petBarLayer);
        BuildPetBar();
        Net.I.PetSummonedEvent += OnPetBarSummoned;
        Net.I.PetGoneEvent += HidePetBar;
        Net.I.PetVitalsEvent += RefreshPetBar;
        Net.I.PetExpEvent += OnPetBarExp;
        Net.I.MagicEvent += OnPetSkillMagic;
        if (Net.I.Pet is { } sheet) OnPetBarSummoned(sheet);
    }

    private void PetBarDispose()
    {
        Net.I.PetSummonedEvent -= OnPetBarSummoned;
        Net.I.PetGoneEvent -= HidePetBar;
        Net.I.PetVitalsEvent -= RefreshPetBar;
        Net.I.PetExpEvent -= OnPetBarExp;
        Net.I.MagicEvent -= OnPetSkillMagic;
        _petSkillGeneration++;
        _petSkillCasts.Clear();
    }

    private void OnPetSkillMagic(int sub, int skillId, int casterId, int targetId, short[] data)
    {
        if (sub is not (MagicSub.Fail or MagicSub.Cancel)) return;
        bool pending = _petSkillCasts.TryGetValue(skillId, out var cast) && cast.CasterId == casterId;
        bool current = MyPetEntity() is { } actor && actor.Id == casterId && _petSkillReadyAt.ContainsKey(skillId);
        if (!pending && !current) return;
        _petSkillCasts.Remove(skillId);
        _petSkillReadyAt.Remove(skillId);
    }

    private void BuildPetBar()
    {
        _petBar = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _petBar.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        _petBarLayer.AddChild(_petBar);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", PetBarGap);
        _petBar.AddChild(row);

        var grip = new HotGrip
        {
            TooltipText = "Drag to move the familiar bar",
            CustomMinimumSize = new Vector2(HotGripThickness, PetBarSlotSize),
        };
        row.AddChild(grip);

        _petAttackCell = new PetSkillCell(PetBarSlotSize) { OnUse = UsePetSkill };
        _petAttackCell.Set(PetSkills.DesignatedAttack, locked: false, dim: false, "Send your familiar at your target");
        row.AddChild(_petAttackCell);
        row.AddChild(UiTheme.Rule(vertical: true));

        for (int i = 0; i < PetSkills.SlotsPerPage; i++)
        {
            var cell = new PetSkillCell(PetBarSlotSize) { OnUse = UsePetSkill };
            _petCells.Add(cell);
            row.AddChild(cell);
        }

        _petPageBtn = UiTheme.SmallButton("1", "Next page of familiar skills");
        _petPageBtn.CustomMinimumSize = new Vector2(22, PetBarSlotSize);
        _petPageBtn.Pressed += () =>
        {
            _petBarPage = (_petBarPage + 1) % PetSkills.Pages;
            RefreshPetBar();
        };
        row.AddChild(_petPageBtn);

        HudLayout.Attach(_petBar, PetBarLayoutId, grip, PetBarDefaultPosition);
    }

    private Vector2 PetBarDefaultPosition()
    {
        var size = _petBar.GetCombinedMinimumSize();
        if (_hotbarBox != null && GodotObject.IsInstanceValid(_hotbarBox))
            return new Vector2(_hotbarBox.Position.X, Mathf.Max(0f, _hotbarBox.Position.Y - size.Y - PetBarGap * 2));
        var viewport = GetViewport().GetVisibleRect().Size;
        return new Vector2((viewport.X - size.X) * 0.5f, viewport.Y - size.Y - HotSlotSize * 2);
    }

    private void OnPetBarSummoned(PetSheet sheet)
    {
        var skills = new List<(int, int)>();
        foreach (var s in SkillData.All)
            if (s.Id is >= PetSkills.FirstSkill and <= PetSkills.LastSkill) skills.Add((s.Id, s.Tree));
        _petBarSkills = PetSkills.BarSkills(skills, sheet.Class);
        _petBarPage = 0;
        _petBar.Visible = true;
        RefreshPetBar();
    }

    private void OnPetBarExp(long _) => RefreshPetBar();

    private void HidePetBar()
    {
        _petSkillGeneration++;
        if (GodotObject.IsInstanceValid(_petBar)) _petBar.Visible = false;
        _petSkillReadyAt.Clear();
        _petSkillCasts.Clear();
    }

    private void RefreshPetBar()
    {
        if (Net.I.Pet is not { } sheet || !GodotObject.IsInstanceValid(_petBar)) return;
        _petPageBtn.Text = (_petBarPage + 1).ToString();
        _petPageBtn.Visible = _petBarSkills.Count > PetSkills.SlotsPerPage;
        int first = _petBarPage * PetSkills.SlotsPerPage;
        for (int i = 0; i < _petCells.Count; i++)
        {
            int index = first + i;
            if (index >= _petBarSkills.Count || SkillData.Get(_petBarSkills[index]) is not { } s)
            {
                _petCells[i].Set(0, locked: false, dim: false, "");
                continue;
            }
            bool locked = !PetSkills.Unlocked(s.Level, sheet.Level);
            string tip = SkillTooltip(s) + (locked ? $"\nFamiliar level {s.Level}" : "");
            _petCells[i].Set(s.Id, locked, dim: locked || sheet.Mp < s.Msp, tip);
        }
    }

    private void PetBarTick(double now)
    {
        if (!GodotObject.IsInstanceValid(_petBar) || !_petBar.Visible) return;
        _petAttackCell.SetCooldown(PetCooldown(PetSkills.DesignatedAttack, now));
        foreach (var cell in _petCells)
            cell.SetCooldown(cell.SkillId == 0 ? 0f : PetCooldown(cell.SkillId, now));
    }

    private float PetCooldown(int skillId, double now)
    {
        if (!_petSkillReadyAt.TryGetValue(skillId, out var readyAt) || readyAt <= now) return 0f;
        float total = SkillData.Get(skillId) is { } s ? Mathf.Max(s.RecastSeconds, s.CastSeconds) : 0f;
        return total <= 0f ? 0f : Mathf.Clamp((float)(readyAt - now) / total, 0f, 1f);
    }

    private Ent? MyPetEntity()
    {
        if (Net.I.Pet is not { } sheet) return null;
        foreach (var e in _ents.Values)
            if (e.IsNpc && e.NpcType == NpcTypes.Pet && !e.Dead && e.Name == sheet.Name) return e;
        return null;
    }

    private void UsePetSkill(int skillId)
    {
        if (!Alive || !IsInsideTree() || _selfDead || !Net.I.Connected) return;
        if (Net.I.Pet is not { } sheet || MyPetEntity() is not { } pet)
        {
            CombatNotice(SystemText(TextPetNotSummoned, "Familiar has not been summoned."));
            return;
        }
        if (SkillData.Get(skillId) is not { } s || !PetSkills.BelongsTo(skillId, s.Tree, sheet.Class))
        {
            CombatNotice(SystemText(TextNotPetSkill, "It is not a Familiar skill."));
            return;
        }
        if (!PetSkills.Unlocked(s.Level, sheet.Level))
        {
            CombatNotice($"Your familiar needs level {s.Level} for {s.Name}.");
            return;
        }
        double now = Now();
        if (_petSkillReadyAt.TryGetValue(skillId, out var readyAt) && readyAt > now) return;
        if (sheet.Mp < s.Msp)
        {
            CombatNotice(PetNoManaText);
            return;
        }

        int target;
        if (s.Moral == PetSkills.OwnerMoral) target = _myId;
        else if (_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var t) && t.IsNpc && t.Attackable && !t.Dead)
            target = _selectedId;
        else
        {
            CombatNotice(PetNoTargetText);
            return;
        }

        int casterId = pet.Id, x = (int)pet.KoX, y = (int)pet.KoY, z = (int)pet.KoZ;
        double readyAgain = now + Mathf.Max(s.RecastSeconds, s.CastSeconds);
        if (s.CastSeconds <= 0f)
        {
            if (Net.I.SendPetSkill(PetSkills.StageEffecting, skillId, casterId, target, x, y, z))
                _petSkillReadyAt[skillId] = readyAgain;
            return;
        }

        if (!Net.I.SendPetSkill(PetSkills.StageCasting, skillId, casterId, target, x, y, z)) return;
        _petSkillReadyAt[skillId] = readyAgain;
        int petIndex = sheet.Index, generation = _petSkillGeneration, serial = ++_petSkillCastSerial;
        _petSkillCasts[skillId] = (casterId, serial);
        GetTree().CreateTimer(s.CastSeconds).Timeout += () =>
        {
            if (!Alive || !_petSkillCasts.TryGetValue(skillId, out var cast) || cast != (casterId, serial)) return;
            _petSkillCasts.Remove(skillId);
            if (CanFinishPetSkill(petIndex, casterId, generation))
                Net.I.SendPetSkill(PetSkills.StageEffecting, skillId, casterId, target, x, y, z);
        };
    }

    private bool CanFinishPetSkill(int petIndex, int casterId, int generation) =>
        Alive && IsInsideTree() && !_selfDead && generation == _petSkillGeneration
        && Net.I.Connected && Net.I.Pet is { } sheet && sheet.Index == petIndex
        && MyPetEntity() is { } caster && caster.Id == casterId;

    private sealed partial class PetSkillCell : PanelContainer
    {
        public int SkillId { get; private set; }
        public System.Action<int>? OnUse;
        private readonly TextureRect _icon;
        private readonly ColorRect _cool;
        private readonly ShaderMaterial _coolMat;
        private readonly StyleBoxFlat _frame;
        private float _coolShown = -1f;
        private static readonly StringName RemainParam = "remain";
        private static readonly Color DimColor = new(0.45f, 0.45f, 0.45f);

        public PetSkillCell(float size)
        {
            CustomMinimumSize = new Vector2(size, size);
            MouseFilter = MouseFilterEnum.Stop;
            ClipContents = true;
            _frame = UiTheme.Slot();
            AddThemeStyleboxOverride("panel", _frame);

            _icon = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(_icon);

            _coolMat = new ShaderMaterial { Shader = Shaders.Get("cooldown") };
            _cool = new ColorRect { Material = _coolMat, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            AddChild(_cool);
        }

        public void Set(int skillId, bool locked, bool dim, string tooltip)
        {
            SkillId = skillId;
            TooltipText = tooltip;
            _icon.Texture = skillId == 0 ? null : locked ? SkillData.EnigmaIcon() : SkillData.Icon(skillId);
            _icon.Modulate = dim ? DimColor : Colors.White;
            AddThemeStyleboxOverride("panel", UiTheme.Slot(locked: locked));
        }

        public void SetCooldown(float frac)
        {
            bool on = frac > 0.001f;
            if (_cool.Visible != on) _cool.Visible = on;
            if (on && Mathf.Abs(frac - _coolShown) > 0.002f)
            {
                _coolMat.SetShaderParameter(RemainParam, frac);
                _coolShown = frac;
            }
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (SkillId == 0 || ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            OnUse?.Invoke(SkillId);
            AcceptEvent();
        }
    }
}
