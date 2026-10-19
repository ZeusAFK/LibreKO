using Godot;

namespace LibreKO.Features.Core;

internal sealed class BuffSystem : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly CombatSession combat;
    private readonly SelfSession self;
    private readonly ZoneSession zone;
    private readonly ICombatRequests requests;
    private readonly ProgressSession progress;
    private readonly EntityStatesSystem entityStates;

    public BuffSystem(IWorldContext context, CombatSession combat, SelfSession self, ZoneSession zone, ICombatRequests requests, ProgressSession progress, EntityStatesSystem entityStates)
    {
        this.context = context;
        this.combat = combat;
        this.self = self;
        this.zone = zone;
        this.requests = requests;
        this.progress = progress;
        this.entityStates = entityStates;
    }

    private readonly List<(int SkillId, double End)> buffRestore = new();

    public void Build()
    {
        entityStates.SelfTransformEnded += DropBuffChip;
    }

    public void Dispose()
    {
        entityStates.SelfTransformEnded -= DropBuffChip;
    }

    public void LateTick(double delta, double nowSec)
    {
        BuffBarTick(nowSec);
        BlindTick(nowSec);
    }

    private sealed class ActiveBuff
    {
        public int SkillId;
        public double End;
        public bool Power;
        public Control Entry = null!;
        public Control Chip = null!;
        public Label Timer = null!;
    }

    private const float BuffChipPointerSize = 32f;
    private const double BuffTickStep = 0.2;

    internal static readonly Vector2 BuffPanelFromCenter = new(-300f, 56f);
    private static readonly Vector2 BuffPowerPanelBelow = new(0f, 44f);
    private static readonly Color BuffBlinkDim = new(1f, 1f, 1f, BuffExpiry.BlinkDimAlpha);
    private static readonly Color ExpiringTimerColour = new(1f, 0.45f, 0.45f);
    private const double SecondsPerMinute = 60;
    private const double ExpiringSeconds = 5;

    private readonly List<ActiveBuff> buffs = new();
    internal Control? BuffPanel { get; private set; }
    private Control? buffPowerPanel;
    private HBoxContainer buffNormalRow = null!;
    private HBoxContainer buffPowerRow = null!;
    private double buffNextTick;
    private readonly List<ActiveBuff> buffOrder = new();

    internal static float BuffChipSize =>
        Platform.TouchUi ? HudPlacement.BuffChipSize : BuffChipPointerSize;

    internal void BuildBuffBar()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);

        buffNormalRow = BuffRow();
        buffPowerRow = BuffRow();
        var panel = BuildBuffRow(layer, buffNormalRow);
        BuffPanel = panel;

        if (Platform.TouchUi)
        {
            HudPlacement.Buffs(context.Hud.VitalsSize, StatusHudFeature.TouchHpBarPos.X).ApplyTo(panel);
            panel.GrowHorizontal = Control.GrowDirection.End;
            return;
        }

        HudLayout.Attach(panel, "hud_buffs", panel, () => BuffPanelDefault(Vector2.Zero));

        var power = BuildBuffRow(layer, buffPowerRow);
        buffPowerPanel = power;
        HudLayout.Attach(power, "hud_buffs_power", power, () => BuffPanelDefault(BuffPowerPanelBelow));
    }

    private static PanelContainer BuildBuffRow(CanvasLayer layer, HBoxContainer row)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        layer.AddChild(panel);
        panel.AddChild(row);
        return panel;
    }

    private Vector2 BuffPanelDefault(Vector2 offset)
    {
        var vp = context.Root.GetViewport().GetVisibleRect().Size;
        return vp * 0.5f + BuffPanelFromCenter + offset;
    }

    private static HBoxContainer BuffRow()
    {
        var row = new HBoxContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", Platform.TouchUi ? 8 : 4);
        return row;
    }

    internal float AttackSpeedMultiplier()
    {
        double now = FrameClock.Now();
        float factor = 1f;
        foreach (var (skillId, end) in self.BuffEnds)
        {
            if (end <= now) continue;
            var s = SkillData.Get(skillId);
            if (s == null || s.AttackSpeedPercent == 100 || s.AttackSpeedPercent <= 0) continue;
            factor *= s.AttackSpeedPercent / 100f;
        }
        return factor;
    }

    public const int FragmentOfManesSkillId = 492063;

    private bool SelfCursed() =>
        buffs.Exists(buff => SkillData.Get(buff.SkillId) is { Type1: MagicType.Buff, IsEnemy: true });

    internal bool IsSelfCursed => SelfCursed();

    public bool HasActiveFragmentOfManes() =>
        self.BuffEnds.TryGetValue(FragmentOfManesSkillId, out var end) && end > FrameClock.Now();

    internal void RegisterBuff(SkillData.Skill s, int targetId, int duration)
    {
        if (BuffPanel == null || targetId != context.Self.CharId || duration <= 0) return;
        if (s.Type1 is not (MagicType.Buff or MagicType.DotHeal or MagicType.Stealth
            or MagicType.Transform)) return;

        if (s.Id == FragmentOfManesSkillId)
        {
            for (int i = buffs.Count - 1; i >= 0; i--)
            {
                var other = SkillData.Get(buffs[i].SkillId);
                if (other != null && BuffRules.IsSpeedBuff(other))
                    DropBuff(i);
            }
        }

        AddBuffChip(s, FrameClock.Now() + duration);
        if (s.MoveSpeedPercent != 100)
            ApplyMoveSpeedBuff(s, duration);
    }

    internal void RestoreBuffs()
    {
        if (BuffPanel == null || self.BuffEnds.Count == 0) return;

        double now = FrameClock.Now();
        buffRestore.Clear();
        foreach (var (skillId, end) in self.BuffEnds) buffRestore.Add((skillId, end));
        foreach (var (skillId, end) in buffRestore)
        {
            var s = SkillData.Get(skillId);
            if (s == null || end <= now) { self.ForgetBuff(skillId); continue; }
            AddBuffChip(s, end);
            ApplyMoveSpeedBuff(s, (int)(end - now));
        }
    }

    private void AddBuffChip(SkillData.Skill s, double end)
    {
        self.SetBuffEnd(s.Id, end);
        foreach (var b in buffs)
        {
            if (b.SkillId != s.Id) continue;
            b.End = end;
            return;
        }

        bool harmful = s.IsEnemy || s.MoveSpeedPercent < 100;
        bool power = !Platform.TouchUi && BuffRules.IsPowerBuff(s);

        var entry = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        entry.AddThemeConstantOverride("separation", 1);

        var chip = new PanelContainer
        {
            CustomMinimumSize = new Vector2(BuffChipSize, BuffChipSize),
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = harmful ? SkillText.BuffTooltip(s) : $"{SkillText.BuffTooltip(s)}\nDouble-click to remove.",
        };
        int chipSkill = s.Id;
        chip.GuiInput += ev => BuffChipInput(ev, chipSkill, harmful);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.85f),
            BorderColor = harmful ? new Color(0.85f, 0.3f, 0.3f) : new Color(0.79f, 0.64f, 0.15f),
        };
        style.SetCornerRadiusAll(Platform.TouchUi ? (int)(BuffChipSize * 0.16f) : 4);
        style.SetBorderWidthAll(Platform.TouchUi ? 2 : 1);
        chip.AddThemeStyleboxOverride("panel", style);
        entry.AddChild(chip);

        if (SkillIcons.Get(s.Id) is { } tex)
            chip.AddChild(new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        else
            chip.AddChild(new Label
            {
                Text = s.Name.Length > 0 ? s.Name[..1] : "?",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        var timer = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        timer.AddThemeFontSizeOverride("font_size",
            Platform.TouchUi ? (int)(BuffChipSize * 0.28f) : 10);
        timer.AddThemeColorOverride("font_outline_color", Colors.Black);
        timer.AddThemeConstantOverride("outline_size", 3);
        if (Platform.TouchUi)
        {
            var cell = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            var plate = new StyleBoxFlat
            {
                BgColor = new Color(0.022f, 0.025f, 0.032f, 0.90f),
                BorderColor = new Color(UiTheme.Edge, 0.34f),
            };
            plate.SetCornerRadiusAll(4);
            plate.SetBorderWidthAll(1);
            plate.ContentMarginTop = plate.ContentMarginBottom = 0f;
            cell.AddThemeStyleboxOverride("panel", plate);
            cell.AddChild(timer);
            entry.AddChild(cell);
        }
        else
        {
            entry.AddChild(timer);
        }

        (power ? buffPowerRow : buffNormalRow).AddChild(entry);
        buffs.Add(new ActiveBuff
        {
            SkillId = s.Id,
            End = end,
            Power = power,
            Entry = entry,
            Chip = chip,
            Timer = timer,
        });
        ShowBuffRows();
    }

    private void BuffChipInput(InputEvent ev, int skillId, bool harmful)
    {
        if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click) return;
        if (ContinuanceClick(skillId)) return;
        if (harmful || skillId == FragmentOfManesSkillId) return;
        if (click.DoubleClick) requests.Magic(MagicSub.Cancel, skillId, context.Self.CharId);
    }

    internal void OnBuffExpired(int buffType)
    {
        if (IsStunBuffType(buffType)) self.ClearStun();
        else if (buffType == BuffRules.Speed2BuffType) ClearMoveSpeedBuff();
        if (BuffPanel == null || buffType == 0) return;

        bool dropped = false;
        for (int i = buffs.Count - 1; i >= 0; i--)
        {
            var s = SkillData.Get(buffs[i].SkillId);
            if (s == null || !BuffExpiry.Removes(buffType, s.BuffType, s.Type1, s.TimeDamage, s.StateChange)) continue;
            DropBuff(i);
            dropped = true;
        }
        if (dropped) ShowBuffRows();
    }

    internal void DropBuffChip(int skillId)
    {
        if (skillId <= 0) return;
        for (int i = buffs.Count - 1; i >= 0; i--)
        {
            if (buffs[i].SkillId != skillId) continue;
            DropBuff(i);
            ShowBuffRows();
            return;
        }
    }

    private void DropBuff(int index)
    {
        var b = buffs[index];
        if (GodotObject.IsInstanceValid(b.Entry)) b.Entry.QueueFree();
        buffs.RemoveAt(index);
        self.ForgetBuff(b.SkillId);
        ForgetExtendedBuff(b.SkillId);
        if (SkillData.Get(b.SkillId) is { } s && s.MoveSpeedPercent != 100) ClearMoveSpeedBuff();
    }

    internal void BuffBarTick(double now)
    {
        if (BuffPanel == null || now < buffNextTick) return;
        buffNextTick = now + BuffTickStep;

        bool removed = false;
        for (int i = buffs.Count - 1; i >= 0; i--)
        {
            var b = buffs[i];
            double left = b.End - now;
            if (left <= 0)
            {
                DropBuff(i);
                removed = true;
                continue;
            }
            b.Timer.Text = left >= SecondsPerMinute ? $"{left / SecondsPerMinute:0}M" : $"{left:0}S";
            b.Chip.Modulate = BuffExpiry.BlinkDimmed(left) ? BuffBlinkDim : Colors.White;
            UiTheme.ApplyFontColor(b.Timer, left <= ExpiringSeconds ? ExpiringTimerColour : Colors.White);
        }
        if (removed) ShowBuffRows();
        if (Platform.TouchUi) SortBuffsByRemaining();
    }

    private void SortBuffsByRemaining()
    {
        buffOrder.Clear();
        foreach (var b in buffs)
            if (b.Entry.GetParent() == buffNormalRow) buffOrder.Add(b);
        buffOrder.Sort((a, b) => a.End.CompareTo(b.End));
        for (int i = 0; i < buffOrder.Count; i++)
            if (buffOrder[i].Entry.GetIndex() != i)
                buffNormalRow.MoveChild(buffOrder[i].Entry, i);
    }

    private void ShowBuffRows()
    {
        int normal = groundChip != null ? 1 : 0, power = 0;
        foreach (var b in buffs)
            if (b.Power) power++;
            else normal++;
        buffNormalRow.Visible = normal > 0;
        buffPowerRow.Visible = power > 0;
    }

    internal void ClearAllBuffs()
    {
        foreach (var b in buffs)
            if (GodotObject.IsInstanceValid(b.Entry)) b.Entry.QueueFree();
        buffs.Clear();
        self.ForgetBuffs();
        if (BuffPanel != null) ShowBuffRows();
        ClearMoveSpeedBuff();
    }

    public const byte FreezeBuffType = 22;
    public const byte StunBuffType = 47;

    public static bool IsStunBuffType(int buffType) => buffType is FreezeBuffType or StunBuffType;

    internal event Action? SelfStunned;

    private void Stun(double seconds)
    {
        if (seconds <= 0) return;
        double until = FrameClock.Now() + seconds;
        self.ExtendStun(until);
        context.Movement.CancelClickMove();
        SelfStunned?.Invoke();
    }

    private const byte SilenceBuffType = 152;
    private const byte DisarmBuffType = 32;
    private const byte DrakeBlessingBuffType = 181;
    private const byte DrakeBlessingRenewBuffType = 182;
    private const double SilenceSeconds = 3.0;
    private const int DisarmedHandSlot = 7;
    private const int TextFrozen = 3035;
    private const int TextDrakeBlessing = 42340;
    internal const int TextCastingFailed = 4002;

    private double skillLockUntil;
    private int disarmToken;

    internal bool SkillsLocked() => FrameClock.Now() < skillLockUntil || ChaosBattle.BattleLocked(context.Zone, progress.SelfAbnormal);

    internal void ApplySelfStatus(int buffType, int speedPercent, double seconds)
    {
        if (IsStunBuffType(buffType)) Stun(seconds);
        else if (BuffRules.IsSpeedBuffType(buffType)) ApplyMoveSpeedBuff(speedPercent, (int)seconds);
        StartBlind(buffType, seconds);

        switch (buffType)
        {
            case SilenceBuffType:
                skillLockUntil = FrameClock.Now() + SilenceSeconds;
                break;
            case FreezeBuffType:
                context.Log.Notice(GameText.System(TextFrozen, "You are Frozen"));
                break;
            case DrakeBlessingBuffType or DrakeBlessingRenewBuffType:
                context.Log.Notice(GameText.System(TextDrakeBlessing, "Drake's Blessing has been used"));
                break;
            case DisarmBuffType:
                Disarm(seconds);
                break;
        }
    }

    internal void Disarm(double seconds)
    {
        if (seconds <= 0) return;
        SetSelfWeaponVisible(DisarmedHandSlot, false);
        int token = ++disarmToken;
        context.Root.After(seconds, () =>
        {
            if (token == disarmToken) SetSelfWeaponVisible(DisarmedHandSlot, true);
        });
    }

    private void SetSelfWeaponVisible(int slot, bool visible)
    {
        if (context.Self.Body == null || !GodotObject.IsInstanceValid(context.Self.Body) || NodeSearch.FindFirst<Godot.Skeleton3D>(context.Self.Body) is not { } skeleton) return;
        if (skeleton.GetNodeOrNull<Godot.Node3D>($"{WeaponMounts.WeaponNodePrefix}{slot}") is { } weapon) weapon.Visible = visible;
    }

    private ColorRect? blindRect;
    private BlindEffect blind;

    internal bool BlindHidesOthers(double now) => blind.HidesOthers(now);
    private float blindAlpha;
    internal bool OthersHidden { get; private set; }

    private void StartBlind(int buffType, double seconds)
    {
        var effect = BlindEffect.Start(buffType, FrameClock.Now(), seconds);
        if (effect.Mode == BlindMode.None) return;
        blind = effect;
        EnsureBlindLayer();
        context.Selection.Deselect();
    }

    private void EnsureBlindLayer()
    {
        if (blindRect != null) return;
        var layer = new CanvasLayer { Layer = UiLayers.BlindOverlay };
        context.Root.AddChild(layer);
        blindRect = new ColorRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        blindRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(blindRect);
    }

    internal void BlindTick(double now)
    {
        if (blindRect == null) return;
        HideOthersWhileBlind(blind.HidesOthers(now));
        float alpha = blind.Alpha(now);
        if (Mathf.IsEqualApprox(alpha, blindAlpha)) return;
        blindAlpha = alpha;
        blindRect.Visible = alpha > 0f;
        float grey = blind.Grey;
        blindRect.Color = new Color(grey, grey, grey, alpha);
    }

    private void HideOthersWhileBlind(bool hide)
    {
        if (!hide && !OthersHidden) return;
        OthersHidden = hide;
        foreach (var e in context.Entities.Views.Values)
            if (e.Body != null && GodotObject.IsInstanceValid(e.Body) && e.Body.Visible == hide
                && !(e.GateBlocker != null && e.Dead))
                e.Body.Visible = !hide;
    }

    internal void OnSecondaryBuffLanded(SkillData.Skill s, int affected, int seconds, int speedPercent)
    {
        if (affected != context.Self.CharId || BuffPanel == null || seconds <= 0) return;
        AddBuffChip(s, FrameClock.Now() + seconds);
        ApplySelfStatus(s.Buff2Type, speedPercent, seconds);
    }

    private static readonly Color ContinuanceNoticeColour = new("ffff00");
    private static readonly Color ContinuanceRefusalColour = new("ff3b3b");

    internal int ContinuanceSkill { get; private set; }
    private readonly HashSet<int> extendedBuffs = new();

    internal void ContinuanceInit() => combat.BuffExtendedEvent += OnBuffExtended;

    internal void ContinuanceDispose() => combat.BuffExtendedEvent -= OnBuffExtended;

    internal bool BeginContinuance(SkillData.Skill item)
    {
        if (!Continuance.IsContinuanceItem(item.Moral)) return false;
        ContinuanceSkill = item.Id;
        context.Log.Notice(GameText.System(Continuance.UsingText, "Using %s", item.Name), ContinuanceNoticeColour);
        return true;
    }

    internal void CancelContinuance() => ContinuanceSkill = 0;

    private bool ContinuanceClick(int buffSkillId)
    {
        if (ContinuanceSkill == 0) return false;
        if (SkillData.Get(ContinuanceSkill) == null)
        {
            CancelContinuance();
            context.Log.Notice(GameText.System(Continuance.NoItemText, "Skill failed - Improper item"), ContinuanceRefusalColour);
            return true;
        }
        var buff = SkillData.Get(buffSkillId);
        var check = buff == null
            ? ContinuanceCheck.Cannot
            : Continuance.Check(buff.Id, buff.Type1, buff.Type2, buff.Moral, extendedBuffs.Contains(buff.Id));
        switch (check)
        {
            case ContinuanceCheck.Cannot:
                context.Log.Notice(GameText.System(Continuance.CannotText, "Cannot use Continuance Item for this skill."), ContinuanceRefusalColour);
                break;
            case ContinuanceCheck.Already:
                context.Log.Notice(GameText.System(Continuance.AlreadyText, "This skill is already using the Continuance Item."), ContinuanceRefusalColour);
                break;
            case ContinuanceCheck.Send:
                requests.Magic(MagicSub.ExtendDuration, buffSkillId, context.Self.CharId);
                CancelContinuance();
                break;
        }
        return true;
    }

    private void OnBuffExtended(int skillId)
    {
        CancelContinuance();
        if (SkillData.Get(skillId) is not { } s) return;
        extendedBuffs.Add(skillId);
        if (s.Duration > 0 && self.BuffEnds.TryGetValue(skillId, out double end))
            AddBuffChip(s, end + s.Duration);
        context.Log.Notice(GameText.System(Continuance.UsedText, "Continuance Item has been used on the %s skill.", s.Name), ContinuanceRefusalColour);
    }

    private void ForgetExtendedBuff(int skillId) => extendedBuffs.Remove(skillId);

    private PanelContainer? groundChip;
    private Node3D? groundAura;

    internal void GroundEffectInit()
    {
        zone.GroundEffectEvent += OnGroundEffect;
        OnGroundEffect(zone.GroundEffect);
    }

    internal void GroundEffectDispose() => zone.GroundEffectEvent -= OnGroundEffect;

    private void OnGroundEffect(GroundEffect effect)
    {
        ShowGroundChip(effect);
        SetGroundAura(GroundEffects.HasAura(effect));
    }

    private void ShowGroundChip(GroundEffect effect)
    {
        if (groundChip != null && GodotObject.IsInstanceValid(groundChip)) groundChip.QueueFree();
        groundChip = null;
        if (GroundEffects.IconPath(effect) is { } path && GodotObject.IsInstanceValid(buffNormalRow))
        {
            var (textId, fallback) = GroundEffects.Text(effect);
            var chip = new PanelContainer
            {
                CustomMinimumSize = new Vector2(BuffChipSize, BuffChipSize),
                MouseFilter = Control.MouseFilterEnum.Pass,
                TooltipText = TextWrap.Wrap(GameText.System(textId, fallback), SkillText.BuffTooltipWidth),
            };
            chip.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            chip.AddChild(new TextureRect
            {
                Texture = ResourceLoader.Load<Texture2D>(path),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            buffNormalRow.AddChild(chip);
            buffNormalRow.MoveChild(chip, 0);
            groundChip = chip;
        }
        ShowBuffRows();
    }

    private void SetGroundAura(bool on)
    {
        bool running = groundAura != null && GodotObject.IsInstanceValid(groundAura);
        if (!on)
        {
            if (running) groundAura!.QueueFree();
            groundAura = null;
            return;
        }
        if (running || !GodotObject.IsInstanceValid(context.Self.Body) || Fx.NameForId(GroundEffects.SwampAuraFx) is not { } name) return;
        groundAura = Fx.Spawn(name, context.Self.Body, Vector3.Zero);
    }

    private double moveBuffUntil;

    private float moveBuffPercent = 100f;

    internal float MoveSpeedMultiplier()
    {
        if (FrameClock.Now() >= moveBuffUntil)
        {
            moveBuffPercent = 100f;
            return zone.GroundSpeedFactor;
        }
        return moveBuffPercent / 100f * zone.GroundSpeedFactor;
    }

    internal void ApplyMoveSpeedBuff(SkillData.Skill s, int duration) =>
        ApplyMoveSpeedBuff(s.MoveSpeedPercent, duration);

    internal void ApplyMoveSpeedBuff(int pct, int duration)
    {
        if (pct <= 0 || pct == 100 || duration <= 0) return;
        if (HasActiveFragmentOfManes() && pct > 100) return;

        moveBuffPercent = pct;
        moveBuffUntil = FrameClock.Now() + duration;
    }

    internal void ClearMoveSpeedBuff()
    {
        moveBuffPercent = 100f;
        moveBuffUntil = 0;
    }
}
