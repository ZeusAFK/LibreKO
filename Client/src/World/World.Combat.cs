using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const float SwingInterval = 1.2f;
    private const float SwingCommitFallback = 0.3f;
    private const float ActionClipCap = 2.5f;
    private const float ProjectileFxSpeed = 18f;
    private const double CorpseLinger = 5.0;

    private const int PendingAwaitServer = 0;
    private const int PendingFlying = 2;
    private const int PendingEffecting = 3;
    private const double PendingReplyTimeout = 5.0;

    internal Vitals Vitals => Net.I.Vitals;
    private bool _autoAttack;
    private int _autoTargetId = -1;
    private double _nextSwing;
    private int _swingTarget = -1;
    private double _swingCommitAt;
    private double _nextSwingIfCancelled;

    private const double VolleyHitGapSeconds = 0.12;
    private const float VolleyLateralSpacing = 0.35f;
    private const double ComboLen = 1.0;
    private const double ComboGrace = 1.0;
    private double _comboStepLen = ComboLen;
    private int _comboStep;
    private double _comboPlayingUntil;
    private double _comboExpireAt;
    private bool _comboBuffered;

    private Dictionary<int, double> _skillReady => Net.I.SkillCooldowns;

    private struct PendingCast
    {
        public double EffectTime, ReplyDeadline;
        public int SkillId, TargetId, Stage;
        public short[]? Data;
    }
    private readonly List<PendingCast> _pendingCasts = new();
    private readonly Dictionary<(int Caster, int Skill, int Phase), List<Node3D>> _skillFx = new();

    private static double Now() => Time.GetTicksMsec() / 1000.0;

    private void CombatInit()
    {
        SkillData.EnsureLoaded();
        Net.I.ItemStatsEvent += OnItemStats;
        _selfCombatStance = false;
        Net.I.SendCombatStance(false);
        RestoreBuffs();
    }

    private void CombatTick(double now)
    {
        if (!_selfDead && Vitals.Known && Vitals.Hp <= 0) EnterSelfDeath();

        for (int i = _pendingCasts.Count - 1; i >= 0; i--)
        {
            var pc = _pendingCasts[i];
            if (pc.Stage == PendingAwaitServer)
            {
                if (now < pc.ReplyDeadline) continue;
                _pendingCasts.RemoveAt(i);
                EndCast(pc.SkillId);
                continue;
            }
            if (now < pc.EffectTime) continue;
            if (pc.Stage == PendingFlying)
                Net.I.SendMagic(2, pc.SkillId, pc.TargetId, pc.Data);
            else
                Net.I.SendMagic(3, pc.SkillId, pc.TargetId, pc.Data);
            pc.Stage = PendingAwaitServer;
            pc.EffectTime = double.PositiveInfinity;
            pc.ReplyDeadline = now + PendingReplyTimeout;
            _pendingCasts[i] = pc;
        }

        if (_comboBuffered && !_selfDead && now >= _comboPlayingUntil) ComboHit(now);
        TickSwingCommit(now);
        TickAutoAttack(now);

        ReapCorpses(now);
        UpdateHotbarReady(now);
        BuffBarTick(now);
        PotionBarTick(now);
        _touchActions?.SetAutoAttack(CanAutoAttack(), _autoAttack);
        ExpBarStatsTick(now);
    }

    private readonly List<int> _corpseScratch = new();

    private void ReapCorpses(double now)
    {
        _corpseScratch.Clear();
        foreach (var (id, e) in _ents)
            if (e.Dead && e.CorpseRemoveAt > 0 && now >= e.CorpseRemoveAt)
                _corpseScratch.Add(id);
        foreach (var id in _corpseScratch)
        {
            if (!_ents.TryGetValue(id, out var e)) continue;
            if (_selectedId == id) Deselect();
            e.Body.QueueFree();
            _ents.Remove(id);
            StateVisualForgetEntity(id);
        }
    }

    private bool CanAutoAttack() =>
        _selectedId >= 0
        && _ents.TryGetValue(_selectedId, out var target)
        && target.Attackable
        && !target.Dead;

    private void ToggleAutoAttack()
    {
        if (_autoAttack)
        {
            StopAutoAttack();
            return;
        }
        if (!CanAutoAttack())
        {
            CombatNotice(_selectedId < 0
                ? "Select a target first."
                : "That target cannot be attacked.");
            return;
        }

        StartAutoAttack(_selectedId);
    }

    private void StartAutoAttack(int targetId)
    {
        _autoAttack = true;
        _autoTargetId = targetId;
        _hasMoveTarget = false;
        _terrainMoveHeld = false;
    }

    private void StopAutoAttack()
    {
        _autoAttack = false;
        _autoTargetId = -1;
        CancelSwing();
    }

    private void ArmSwing(int targetId, double now)
    {
        _swingTarget = targetId;
        if (targetId < 0) return;
        float windUp = FirstStrikeTime(_selfAnim);
        _swingCommitAt = now + (windUp > 0f ? windUp : SwingCommitFallback);
    }

    private void TickSwingCommit(double now)
    {
        if (_swingTarget < 0 || now < _swingCommitAt) return;
        int target = _swingTarget;
        _swingTarget = -1;
        if (_selfDead) return;
        if (!_ents.TryGetValue(target, out var t) || t.Dead || !t.Attackable) return;
        Net.I.SendAttack(target, SwingDelay());
    }

    private short SwingDelay()
    {
        var gear = SelfGear();
        int right = gear.Length > 6 ? gear[6] : 0;
        int left = gear.Length > 7 ? gear[7] : 0;
        int hand = right != 0 ? right : left;
        int delay = hand == 0 ? 0 : ItemData.Get(hand)?.Delay ?? 0;
        return (short)Mathf.Max(BareHandedSwingDelay, delay);
    }

    private const int BareHandedSwingDelay = 100;

    private void CancelSwing()
    {
        _comboBuffered = false;
        if (_selfActionRank == ActionRankBasic && Now() < _selfActionUntil)
        {
            _selfActionUntil = 0;
            _selfClip = null;
            _strikeTargetSelf = -1;
        }
        bool uncommitted = CancelBasicRangedShot();
        if (_swingTarget >= 0)
        {
            uncommitted = true;
            _swingTarget = -1;
        }
        if (!uncommitted) return;
        _nextSwing = _nextSwingIfCancelled;
        _comboPlayingUntil = 0;
        _comboExpireAt = 0;
        _comboStep = 0;
    }

    private bool CancelBasicRangedShot()
    {
        if (_selfDead || _pendingCasts.Count == 0) return false;
        if (BasicRangedAttackSkill() is not { } ranged) return false;
        if (!PendingCastPreEffect(ranged.Id)) return false;
        CancelSelfCast(ranged.Id);
        return true;
    }

    private void TickAutoAttack(double now)
    {
        if (!_autoAttack)
            return;
        if (_selfDead
            || _selectedId != _autoTargetId
            || !_ents.TryGetValue(_autoTargetId, out var target)
            || !target.Attackable
            || target.Dead)
        {
            StopAutoAttack();
            return;
        }

        if (!InBasicAttackRange(_autoTargetId) || now < _nextSwing)
            return;

        var rangedSkill = BasicRangedAttackSkill();
        _nextSwingIfCancelled = _nextSwing;
        _nextSwing = now + (rangedSkill != null ? rangedSkill.CastSeconds + RangedSwingPad : SwingInterval / AttackSpeedMultiplier());
        BasicAttack();
    }

    private float BasicAttackRange(Ent? target)
    {
        int ranged = RangedWeaponItem();
        float weapon = ranged != 0
            ? (ItemData.Get(ranged)?.Range ?? 0) / 10f
            : MeleeWeaponReach();
        return Mathf.Max(MeleeReach, weapon) + (target?.Radius ?? 0f);
    }

    private float MeleeWeaponReach()
    {
        var gear = SelfGear();
        int right = gear.Length > 6 ? gear[6] : 0;
        int left = gear.Length > 7 ? gear[7] : 0;
        int hand = right != 0 ? right : left;
        return hand == 0 ? 0f : (ItemData.Get(hand)?.Range ?? 0) / 10f;
    }

    private const float MeleeReach = 3.0f;
    private const float ApproachFraction = 0.8f;
    private const float RangedSwingPad = 0.15f;
    private const float DefaultSkillRange = 7.0f;

    private void AimAttack(double now)
    {
        if (_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var e))
            FaceSelfToward(e.Body.Position);
        else if (_faceDir.LengthSquared() >= 0.01f)
            _self.RotationDegrees = new Vector3(0, 180f - Coord.KoHeading(-_faceDir.X, _faceDir.Z), 0);
        _attackLungeDir = Vector3.Zero;
        _attackLungeUntil = 0;
    }

    private void BasicAttack()
    {
        if (_selfDead) return;
        if (BasicRangedAttackSkill() is { } ranged)
        {
            if (!HasPendingCast(ranged.Id)) CastSkill(ranged.Id);
            return;
        }
        double now = Now();
        if (now < _comboPlayingUntil) { _comboBuffered = true; return; }
        ComboHit(now);
    }

    private void ComboHit(double now)
    {
        if (now > _comboExpireAt) _comboStep = 0;
        _comboStep = (_comboStep + 1) % 3;
        _comboStepLen = ComboLen;

        AimAttack(now);

        int target =
            _selectedId >= 0
            && _ents.TryGetValue(_selectedId, out var t)
            && t.Attackable
            && !t.Dead
            && InBasicAttackRange(_selectedId)
                ? _selectedId
                : -1;
        _strikeTargetSelf = target;
        SelfAction(BasicAttackClips, ActionRankBasic);
        ArmSwing(target, now);
        _nextSwing = Mathf.Max((float)_nextSwing, (float)now + LastStrikeTime(_selfAnim) + StrikeTailPad);
        _comboPlayingUntil = now + _comboStepLen;
        _comboExpireAt = _comboPlayingUntil + ComboGrace;
        _comboBuffered = false;
    }

    private int SelfWeaponItem()
    {
        var g = SelfGear();
        return g.Length > 6 ? g[6] : 0;
    }

    private bool InRange(int id, float radius) =>
        _ents.TryGetValue(id, out var e) && e.Body.GlobalPosition.DistanceTo(_self.GlobalPosition) <= radius;

    private bool InBasicAttackRange(int id) =>
        _ents.TryGetValue(id, out var e)
        && e.Body.GlobalPosition.DistanceTo(_self.GlobalPosition) <= BasicAttackRange(e);

    private const int AttackResultHit = 1;

    private void OnAttack(int attackType, int result, int attackerId, int targetId)
    {
        if (result == AttackResultHit) PlayStruck(targetId);
        if (attackerId != _myId && _ents.TryGetValue(attackerId, out var a))
        {
            a.StrikeTarget = result == AttackResultHit ? targetId : -1;
            PlayEntityAction(a, BasicAttackClips, ActionRankBasic);
        }
        FaceToward(attackerId, targetId);
    }

    private void OnMagic(int sub, int skillId, int casterId, int targetId, short[] data)
    {
        var s = SkillData.Get(skillId);
        switch (sub)
        {
            case 1:
                if (Diag.SlowLog) GD.Print($"[fx] casting skill={skillId} caster={casterId} selfFx1={s?.SelfFx1} part={s?.SelfPart1}");
                if (targetId < 0) FaceTowardImpact(casterId, data);
                else FaceToward(casterId, targetId);
                StopSkillFx(casterId, skillId);
                if (casterId == _myId && s != null)
                {
                    EnsureSkillCooldown(s);
                    BeginCast(s);
                }
                if (s != null)
                {
                    PlaySkillAction(casterId, SkillAnim(casterId, s, false), ClipsForCast(s), ActionRankSkill);
                    LatchStrikeTarget(casterId, s.IsMelee ? targetId : -1);
                }
                if (s?.SelfFx1 != null)
                {
                    SpawnOwnedFx(casterId, skillId, 1, s.SelfFx1, s.SelfPart1);
                    if (s.HasCastPhase) AudioFxAt(s.SelfFx1Id, casterId);
                }
                break;

            case 2:
                if (s?.NeedsFlying == true) StopSkillFx(casterId, skillId, 1);
                int arrows = s is { NeedsFlying: true } ? Mathf.Max(1, s.NeedArrow) : 1;
                if (s?.FlyingFx != null)
                {
                    for (int k = 0; k < arrows; k++)
                        SpawnFxProjectile(casterId, targetId, s.FlyingFx, (k - (arrows - 1) * 0.5f) * VolleyLateralSpacing);
                    AudioFxAt(s.FlyingFxId, casterId);
                }
                if (casterId == _myId && s?.NeedsFlying == true)
                {
                    double travel = ProjectileTravelTime(casterId, targetId, s.FlyingFx);
                    for (int k = 0; k < arrows; k++)
                        QueuePendingStage(skillId, targetId, PendingEffecting, travel + k * VolleyHitGapSeconds, replace: k == 0);
                }
                break;

            case 3:
                bool miss = data.Length > 3 && data[3] <= -100;
                int affected = targetId == 0 ? casterId : targetId;
                if (Diag.SlowLog) GD.Print($"[fx] effecting skill={skillId} caster={casterId} target={targetId} miss={miss} targetFx={s?.TargetFx} part={s?.TargetPart} data3={(data.Length > 3 ? data[3] : 0)}");
                StopSkillFx(casterId, skillId, 1);
                if (s != null && (s.IsMelee || s.SelfAnim2 != 0))
                {
                    PlaySkillAction(casterId, SkillAnim(casterId, s, true), ClipsForCast(s), ActionRankSkill);
                    LatchStrikeTarget(casterId, !miss && s.IsMelee ? targetId : -1);
                }
                if (s?.SelfFx2 != null)
                {
                    SpawnOwnedFx(casterId, skillId, 2, s.SelfFx2, s.SelfPart2);
                    AudioFxAt(s.SelfFx2Id, casterId);
                }
                if (!miss && s?.TargetFx != null)
                {
                    if (SpawnFxAtImpact(casterId, targetId, s.TargetFx, s.TargetPart, data)
                        && !(casterId == _myId && s.IsPotion))
                        AudioFxAt(s.TargetFxId, targetId > 0 ? targetId : casterId);
                }
                if (!miss && s != null && s.TargetAnim != 0 && targetId >= 0)
                    PlaySkillAction(affected, s.TargetAnim, StruckClips, ActionRankStruck);
                if (casterId == _myId)
                {
                    bool requested = ResolvePendingReply(skillId);
                    if (!HasPendingCast(skillId)) EndCast(skillId);
                    if (requested && s != null && !HasPendingCast(skillId) && BasicRangedAttackSkill()?.Id != skillId)
                        CombatLogAdd(s.IsPotion
                            ? $"You used {ItemData.DisplayName(s.UseItem)}."
                            : $"You used {s.Name}.", CombatLogKind.Status);
                    if (s != null)
                    {
                        if (requested) EnsureSkillCooldown(s);
                        if (!miss && affected == _myId) ApplyMoveSpeedBuff(s, BuffSeconds(s, data));
                    }
                }
                if (!miss && s != null)
                    RegisterBuff(s, affected, BuffSeconds(s, data));
                if (!miss && s?.IsResurrect == true) Revive(affected);
                break;

            case 4:
            case 6:
                StopSkillFx(casterId, skillId);
                if (casterId == _myId)
                {
                    bool awaited = HasPendingCast(skillId);
                    ClearPendingCast(skillId);
                    EndCast(skillId);
                    if (s != null) CancelSkillCooldown(s);   // a refused cast must not eat the cooldown
                    if (sub == 4 && awaited && s is { UseItem: not 0, IsRanged: false })
                        CombatNotice($"You couldn't use {ItemData.DisplayName(s.UseItem)} right now.");
                    else if (sub == 4 && awaited && s != null && ConflictingBuff(s) is { } blocker)
                        CombatNotice($"{blocker.Name} is already active.");
                }
                break;
        }
    }

    private static int BuffSeconds(SkillData.Skill s, short[] data) =>
        s.Type1 is MagicType.Buff or MagicType.Transform or MagicType.Stealth
        && data.Length > 3 && data[3] > 0
            ? data[3]
            : s.Duration;

    private SkillData.Skill? ConflictingBuff(SkillData.Skill s)
    {
        if (s.Type1 != MagicType.Buff || s.BuffType == 0) return null;
        double now = Now();
        foreach (var (skillId, end) in Net.I.BuffEnds)
        {
            if (skillId == s.Id || end <= now) continue;
            var other = SkillData.Get(skillId);
            if (other != null && other.BuffType == s.BuffType) return other;
        }
        return null;
    }

    private void OnDead(int victimId, int killerId)
    {
        AudioDeath(victimId);
        if (victimId == _myId)
        {
            CombatLogAdd(
                killerId >= 0 ? $"You were defeated by {CombatEntityName(killerId)}." : "You died.",
                CombatLogKind.Incoming);
            EnterSelfDeath();
            return;
        }
        if (_ents.TryGetValue(victimId, out var e))
        {
            if (killerId == _myId)
                CombatLogAdd($"You defeated {e.Name}.", CombatLogKind.Outgoing);
            LayOutCorpse(e, settled: false);
            if (_autoAttack && _autoTargetId == victimId) StopAutoAttack();
        }
    }

    private void EnterSelfDeath()
    {
        if (_selfDead) return;
        _selfDead = true;
        StopAutoAttack();
        Deselect();
        _pendingCasts.Clear();
        _selfFlinch?.Stop();
        double len = PlayActionOn(_selfAnim, DeathClips);
        _selfActionUntil = Now() + Mathf.Max((float)len, 2.5f);
        _selfActionRank = ActionRankDeath;
        ShowDeathDialog();
    }

    private void OnSelfHp(int hp, int maxHp, int attackerId)
    {
        var (dmg, recovered) = Vitals.ApplyHp(hp, maxHp);
        _hpBar?.Set(hp, maxHp);
        if (_selfDead && hp > 0) OnSelfResurrected();
        if (dmg > 0)
            CombatLogAdd($"{CombatEntityName(attackerId)} hit you for {dmg:n0} damage.", CombatLogKind.Incoming);
        else if (recovered > 0)
            CombatLogAdd($"You recovered {recovered:n0} HP.", CombatLogKind.Recovery);
        if (dmg > 0 && _self != null)
        {
            Floaters?.Wound(dmg);
            if (!_selfDead)
            {
                if (_selfSitting) StandUp();
                AudioStruck(_myId);
            }
        }
        else if (recovered > 0 && _self != null)
        {
            if (attackerId >= 0) Floaters?.Cure(_myId, recovered);
            else Floaters?.RegenHp(recovered);
        }
    }

    private void OnSelfMp(int mp, int maxMp)
    {
        int delta = Vitals.ApplyMp(mp, maxMp);
        _mpBar?.Set(mp, maxMp);
        if (delta > 0)
        {
            CombatLogAdd($"You recovered {delta:n0} MP.", CombatLogKind.Resource);
            Floaters?.RegenMp(delta);
        }
        else if (delta < 0)
            CombatLogAdd($"You used {-delta:n0} MP.", CombatLogKind.Resource);
    }

    private void OnItemStats(DerivedStats stats)
    {
        Vitals.ApplyMaxima(stats.MaxHp, stats.MaxMp);
        _hpBar?.Set(Vitals.Hp, Vitals.MaxHp);
        _mpBar?.Set(Vitals.Mp, Vitals.MaxMp);
    }

    private void Revive(int id)
    {
        if (id == _myId) { OnSelfResurrected(); return; }
        if (!_ents.TryGetValue(id, out var e) || !e.Dead) return;
        e.Dead = false;
        e.CorpseRemoveAt = 0;
        e.ActionUntil = 0;
        e.ActionRank = 0;
        e.ActionClip = null;
        e.Clip = null;
        RefreshEntityCollision(e);
    }

    private void OnSelfResurrected()
    {
        if (!_selfDead) return;
        _selfDead = false;
        _selfActionUntil = 0;
        _selfClip = null;
        HideDeathDialog();
        CombatLogAdd("You have been resurrected.", CombatLogKind.Recovery);
    }

    private void OnRegene(float koX, float koZ)
    {
        if (_self == null) return;
        float koY = _myKoY;
        var pos = GroundPos(koX, koZ, koY, _selfLift);
        _self.Position = pos;
        _lastFreePos = pos;
        _myKoX = koX; _myKoZ = koZ; _myKoY = koY;
        _lastKoX = koX; _lastKoZ = koZ;
        _selfDead = false;
        _selfActionUntil = 0;
        EndCast(0);
        ClearAllBuffs();
        _selfClip = null;
        HideDeathDialog();
        CombatLogAdd("You returned to the battlefield.", CombatLogKind.Status);
    }

    private static readonly string[] BasicAttackClips =
        { "attack_sword0_A", "attack_sword1_A", "attack0", "attack1", "attack",
          "attack_blunt0_A", "attack_Axe0_A", "attack_dual0_A", "attack_TwoHand0_A", "skill_sword01_A" };
    private static readonly string[] UnarmedAttackClips =
        { "attack_Punch0_A", "attack_Kick0_A", "attack_Punch0_B", "attack_Kick0_B", "attack0", "attack" };
    private static readonly string[] DaggerAttackClips =
        { "attack_Dagger0_A", "attack_Dagger1_A", "attack0", "attack" };
    private static readonly string[] DualAttackClips =
        { "attack_dual0_A", "attack_dual1_A", "attack0", "attack" };
    private static readonly string[] TwoHandAttackClips =
        { "attack_TwoHand0_A", "attack_TwoHand1_A", "attack0", "attack" };
    private static readonly string[] BluntAttackClips =
        { "attack_blunt0_A", "attack_blunt1_A", "attack0", "attack" };
    private static readonly string[] TwoBluntAttackClips =
        { "attack_Twoblunt0_A", "attack_Twoblunt1_A", "attack0", "attack" };
    private static readonly string[] AxeAttackClips =
        { "attack_Axe0_A", "attack_Axe1_A", "attack0", "attack" };
    private static readonly string[] SpearAttackClips =
        { "attack_Spear0_A", "attack0", "attack" };
    private static readonly string[] PolearmAttackClips =
        { "attack_Polearm0_A", "attack0", "attack" };
    private static readonly string[] StaffAttackClips =
        { "attack_Bash0_A", "magic_attack", "attack0", "attack" };
    private static readonly string[] JamadarAttackClips =
        { "attack_jamadar_A", "attack_Kick0_A", "attack0", "attack" };

    private static (int Right, int Left, int Weight) HandLoadout(int[]? gear)
    {
        int rightId = gear != null && gear.Length > 6 ? gear[6] : 0;
        int leftId = gear != null && gear.Length > 7 ? gear[7] : 0;
        var right = rightId > 0 ? ItemData.Get(rightId) : null;
        var left = leftId > 0 ? ItemData.Get(leftId) : null;
        return (right?.Kind ?? WeaponAnimation.NoItem,
                left?.Kind ?? WeaponAnimation.NoItem,
                right?.Weight ?? 0);
    }

    private static readonly string[] BowAttackClips =
        { "Shoot_Arrow_A", "Shoot_Arrow_B",
          "shoot_arrow_a", "shoot_arrow_b", "attack_Bow0_A", "attack_bow0_A", "attack0", "attack" };
    private static readonly string[] CrossbowAttackClips =
        { "Shoot_Quarrel_A", "Shoot_Quarrel_B", "attack_Bow0_A", "Shoot_Arrow_A", "attack0", "attack" };
    private static readonly string[] MeleeSkillClips =
        { "skill_sword01_A", "skill_sword02_A", "attack_sword0_A", "attack_sword1_A", "attack0", "attack",
          "attack_TwoHand0_A", "attack_blunt0_A", "attack_Axe0_A", "attack_dual0_A" };
    private static readonly string[] BuffClips =
        { "warcry0", "warcry1", "magic_ability", "magic_abil", "magic0", "greeting1", "breath", "basic" };
    private static readonly string[] MagicCastClips =
        { "magic_attack", "magic0", "magic1", "magic_heal", "breath", "basic" };
    private static readonly string[] StruckClips = { "struck0", "struck", "struck1", "struck2" };
    private static readonly string[] DeathClips = { "dead2", "dead0", "dead3", "dead1", "dead4", "dead" };

    private static int HeldItemClass(int[]? gear, int slot)
    {
        if (gear == null || slot >= gear.Length || gear[slot] <= 0) return SkillAnimation.NoItem;
        return ItemData.Get(gear[slot])?.Kind ?? SkillAnimation.NoItem;
    }

    private int[]? GearOf(int entityId) =>
        entityId == _myId ? SelfGear() : _ents.TryGetValue(entityId, out var e) ? e.Gear : null;

    private static double PlayActionOn(AnimationPlayer? anim, int action, string[] fallback)
    {
        if (anim == null) return 0;
        string? indexed = AnimationNameAt(anim, action);
        if (indexed != null && anim.HasAnimation(indexed))
            return StartAction(anim, indexed, AnimationMetaAt(anim, action)?.Blend ?? ActionClip.DefaultBlend);
        return PlayActionOn(anim, fallback);
    }

    private const float ProjectileFxHeight = 1.2f;
}

