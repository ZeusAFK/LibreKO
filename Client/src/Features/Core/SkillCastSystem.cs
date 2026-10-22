using Godot;

namespace LibreKO.Features.Core;

internal sealed class SkillCastSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly AudioSystem audio;
    private readonly SelfAvatar avatar;
    private readonly BuffSystem buff;
    private readonly CombatFxSystem combatFx;
    private readonly EntityActionSystem entityAction;
    private readonly EntityLooksSystem entityLooks;
    private readonly EntityStatesSystem entityStates;
    private readonly GateSystem gate;
    private readonly PartySession party;
    private readonly PvpSession pvp;
    private readonly ICombatRequests requests;
    private readonly ScenerySystem scenery;
    private readonly SelfSession self;
    private readonly SiegeTowerSystem siegeTower;
    private readonly SkillTimers skillTimers;
    private readonly SoccerSystem soccer;
    private readonly EntitySystem entities;

    public SkillCastSystem(IWorldContext context, AudioSystem audio, SelfAvatar avatar, BuffSystem buff, CombatFxSystem combatFx, EntityActionSystem entityAction, EntityLooksSystem entityLooks, EntityStatesSystem entityStates, GateSystem gate, PartySession party, PvpSession pvp, ICombatRequests requests, ScenerySystem scenery, SelfSession self, SiegeTowerSystem siegeTower, SkillTimers skillTimers, SoccerSystem soccer, EntitySystem entities)
    {
        this.context = context;
        this.audio = audio;
        this.avatar = avatar;
        this.buff = buff;
        this.combatFx = combatFx;
        this.entityAction = entityAction;
        this.entityLooks = entityLooks;
        this.entityStates = entityStates;
        this.gate = gate;
        this.party = party;
        this.pvp = pvp;
        this.requests = requests;
        this.scenery = scenery;
        this.self = self;
        this.siegeTower = siegeTower;
        this.skillTimers = skillTimers;
        this.soccer = soccer;
        this.entities = entities;
    }

    public void Build()
    {
        entities.EntityRemoved += ForgetRemoteCast;
        buff.SelfStunned += CancelActionCasts;
    }

    public void Dispose()
    {
        entities.EntityRemoved -= ForgetRemoteCast;
        buff.SelfStunned -= CancelActionCasts;
    }

    internal void Tick(double now)
    {
        TickSkillFxStops(now);
        combatFx.SweepSkillFx(now);
        launches.Clear();
        for (int i = PendingCasts.Count - 1; i >= 0; i--)
        {
            var pc = PendingCasts[i];
            if (pc.Stage == PendingAwaitServer)
            {
                if (now < pc.ReplyDeadline) continue;
                PendingCasts.RemoveAt(i);
                EndCast(pc.SkillId);
                continue;
            }
            if (now < pc.EffectTime) continue;
            if (pc.Stage == PendingFlying)
            {
                requests.Magic(2, pc.SkillId, pc.TargetId, pc.Data);
                launches.Add(pc);
            }
            else
            {
                if (pc.Straight)
                {
                    pc.TargetId = combatFx.FirstHostileAlong(pc.From, pc.To);
                    if (pc.TargetId < 0)
                    {
                        requests.Magic(MagicSub.Fail, pc.SkillId, CombatFxSystem.AreaImpactTarget, ActionAnimation.ArrowMissedData());
                        PendingCasts.RemoveAt(i);
                        if (!HasPendingCast(pc.SkillId)) EndCast(pc.SkillId);
                        continue;
                    }
                }
                requests.Magic(MagicSub.Effecting, pc.SkillId, pc.TargetId, movementSkills?.DashRequestData(pc.SkillId, pc.TargetId) ?? soccer.KickRequestData(pc.SkillId, pc.TargetId) ?? pc.Data);
            }
            pc.Stage = PendingAwaitServer;
            pc.EffectTime = double.PositiveInfinity;
            pc.ReplyDeadline = now + PendingReplyTimeout;
            PendingCasts[i] = pc;
        }
        foreach (var launch in launches)
            if (SkillData.Get(launch.SkillId) is { } launched)
                LaunchOwnFlight(launch.TargetId, launched, launch.Data ?? new short[MagicData.Length]);
    }

    private void ForgetRemoteCast(int id) => RemoteCastingSkill.Remove(id);

    private const int PendingAwaitServer = 0;
    internal const int PendingFlying = 2;
    internal const int PendingEffecting = 3;
    private const double PendingReplyTimeout = 5.0;

    private readonly HashSet<int> castLogged = new();

    private const double VolleyHitGapSeconds = 0.12;

    internal struct PendingCast
    {
        public double EffectTime, ReplyDeadline;
        public int SkillId, TargetId, Stage;
        public short[]? Data;
        public bool Straight;
        public Vector3 From, To;
    }

    internal List<PendingCast> PendingCasts { get; } = new();

    internal void ForgetPendingCasts() => PendingCasts.Clear();

    internal bool SelfBuffActive(SkillData.Skill skill, double now)
    {
        if (PendingSelfCastLike(skill)) return true;
        foreach (var (id, end) in self.BuffEnds)
        {
            if (end <= now) continue;
            var active = SkillData.Get(id);
            if (id == skill.Id || (skill.BuffType > 0 && active?.BuffType == skill.BuffType)) return true;
        }
        return false;
    }

    internal bool PendingSelfCastLike(SkillData.Skill skill)
    {
        foreach (var pending in PendingCasts)
        {
            var casting = SkillData.Get(pending.SkillId);
            if (pending.TargetId == context.Self.CharId && (pending.SkillId == skill.Id
                || (skill.BuffType > 0 && casting?.BuffType == skill.BuffType))) return true;
        }
        return false;
    }

    private readonly List<PendingCast> launches = new();

    private readonly record struct SkillFxStop(double At, int Caster, int Skill, int Phase);

    private readonly List<SkillFxStop> skillFxStops = new();
    private const double CastFxGraceSeconds = 3.0;

    private void ScheduleSkillFxStop(int casterId, int skillId, int phase, double at) =>
        skillFxStops.Add(new SkillFxStop(at, casterId, skillId, phase));

    private void TickSkillFxStops(double now)
    {
        for (int i = skillFxStops.Count - 1; i >= 0; i--)
        {
            var stop = skillFxStops[i];
            if (now < stop.At) continue;
            skillFxStops.RemoveAt(i);
            combatFx.StopSkillFx(stop.Caster, stop.Skill, stop.Phase);
        }
    }

    private void LaunchOwnFlight(int targetId, SkillData.Skill s, short[] data)
    {
        var arrivals = combatFx.LaunchFlight(context.Self.CharId, targetId, s, data, own: true);
        ClearPendingCast(s.Id);
        if (arrivals == null)
        {
            EndCast(s.Id);
            return;
        }
        for (int k = 0; k < arrivals.Count; k++)
        {
            var arrival = arrivals[k];
            PendingCasts.Add(new PendingCast
            {
                EffectTime = FrameClock.Now() + arrival.Travel + k * VolleyHitGapSeconds,
                SkillId = s.Id,
                TargetId = arrival.TargetId,
                Stage = PendingEffecting,
                Data = arrival.Area ? data : null,
                Straight = arrival.Straight,
                From = arrival.From,
                To = arrival.To,
            });
        }
    }

    private readonly RateBudget strangerFxBudget = new();

    internal void OnMagic(MagicReply reply)
    {
        bool suppress = !StrangerSkillFxAllowed(reply.CasterId, reply.TargetId);
        Fx.SuppressOneShots = suppress;
        try { HandleMagic(reply.Sub, reply.SkillId, reply.CasterId, reply.TargetId, reply.Data); }
        finally { if (suppress) Fx.SuppressOneShots = false; }
    }

    private bool StrangerSkillFxAllowed(int casterId, int targetId)
    {
        if (casterId == context.Self.CharId || targetId == context.Self.CharId || casterId == context.Selection.SelectedId || targetId == context.Selection.SelectedId
            || party.IsMember(casterId) || party.IsMember(targetId))
            return true;
        var limits = CrowdModes.Of(Config.Crowd);
        if (limits.StrangerFxPerSecond == CrowdModes.Unlimited) return true;
        if (!context.Entities.Views.TryGetValue(casterId, out var caster) || !caster.OnScreen || context.Self.Body == null) return false;
        if (caster.IsNpc) return true;
        if (Coord.FlatDistance(caster.Body.GlobalPosition, context.Self.Body.GlobalPosition) > limits.StrangerFxRange) return false;
        return strangerFxBudget.Take(limits.StrangerFxPerSecond, FrameClock.Now());
    }

    private void HandleMagic(int sub, int skillId, int casterId, int targetId, short[] data)
    {
        var s = SkillData.Get(skillId);
        switch (sub)
        {
            case MagicSub.Casting:
                if (Diag.SlowLog) GD.Print($"[fx] casting skill={skillId} caster={casterId} selfFx1={s?.SelfFx1} part={s?.SelfPart1}");
                SkillMessage?.Invoke(SkillMsgOut.Cast(skillId));
                if (targetId < 0) FaceTowardImpact(casterId, data);
                else entityAction.FaceToward(casterId, targetId);
                if (casterId != context.Self.CharId) combatFx.StopSkillFx(casterId, skillId);
                if (casterId == context.Self.CharId && s != null)
                {
                    if (!instantCasts.Remove(s.Id)) skillTimers.Ensure(s, FrameClock.Now());
                    BeginCast(s);
                    LogSkillUse(s);
                }
                if (s != null)
                {
                    bool remoteUse = casterId != context.Self.CharId;
                    if (remoteUse) RemoteCastingSkill[casterId] = skillId;
                    entityAction.PlaySkillAction(casterId, entityAction.SkillAnim(casterId, s, false), ActionAnimation.ClipsForCast(s), ModelAnimation.ActionRankSkill, restart: remoteUse);
                    if (remoteUse) HoldEntityCastPose(casterId, s);
                    entityAction.LatchStrikeTarget(casterId, s.IsMelee ? targetId : -1);
                }
                if (casterId != context.Self.CharId && s != null) StartCastFx(casterId, s);
                break;

            case MagicSub.Flying:
                if (casterId != context.Self.CharId && s != null) combatFx.LaunchFlight(casterId, targetId, s, data, own: false);
                break;

            case MagicSub.Effecting:
                SkillMessage?.Invoke(SkillMsgOut.Effect(skillId));
                if (s != null && SecondaryBuff.IsEcho(s, data))
                {
                    buff.OnSecondaryBuffLanded(s, targetId == 0 ? casterId : targetId, SecondaryBuff.Seconds(data),
                        SecondaryBuff.SpeedPercent(data));
                    break;
                }
                bool miss = MagicFail.IsMiss(data);
                bool dash = s != null && movementSkills?.OnDashEffecting(s, casterId, targetId, data) == true;
                int affected = targetId == 0 ? casterId : targetId;
                if (Diag.SlowLog) GD.Print($"[fx] effecting skill={skillId} caster={casterId} target={targetId} miss={miss} targetFx={s?.TargetFx} part={s?.TargetPart} data3={MagicFail.Code(data)}");
                combatFx.StopSkillFx(casterId, skillId, MagicSub.Casting);
                bool castFollowUp = RemoteCastingSkill.Remove(casterId, out int castingSkill) && castingSkill == skillId;
                if (s != null && !s.HasFlyingStage && (s.IsMelee || s.SelfAnim2 != 0))
                {
                    entityAction.PlaySkillAction(casterId, entityAction.SkillAnim(casterId, s, true), ActionAnimation.ClipsForCast(s), ModelAnimation.ActionRankSkill,
                                    restart: casterId != context.Self.CharId && !castFollowUp);
                    entityAction.LatchStrikeTarget(casterId, !miss && s.IsMelee ? targetId : -1);
                }
                else if (s != null && casterId != context.Self.CharId && !castFollowUp && s.Cast <= 0 && !s.IsNonAction)
                    entityAction.PlaySkillAction(casterId, entityAction.SkillAnim(casterId, s, false), ActionAnimation.ClipsForCast(s), ModelAnimation.ActionRankSkill, restart: true);
                if (s?.SelfFx2 != null)
                {
                    double buffSeconds = ActionAnimation.BuffSeconds(s, data);
                    bool loops = Fx.LoopsForever(s.SelfFx2) && buffSeconds > 0;
                    combatFx.SpawnOwnedFx(casterId, skillId, MagicSub.Flying, s.SelfFx2, s.SelfPart2, oneShot: !loops);
                    if (loops) ScheduleSkillFxStop(casterId, skillId, 2, FrameClock.Now() + buffSeconds);
                    audio.FxAt(s.SelfFx2Id, casterId);
                }
                if (!miss && !dash && s?.TargetFx != null)
                {
                    if (combatFx.SpawnFxAtImpact(casterId, targetId, s.TargetFx, s.TargetPart, data, s.IsAreaCast, s.IsMelee)
                        && !(casterId == context.Self.CharId && s.IsPotion && !s.HasCastPhase))
                        audio.FxAt(s.TargetFxId, targetId > 0 ? targetId : casterId);
                }
                if (!miss && s != null && s.TargetAnim != 0 && targetId >= 0)
                    entityAction.PlaySkillAction(affected, s.TargetAnim, ActionAnimation.StruckClips, ModelAnimation.ActionRankStruck);
                if (!miss && targetId >= 0 && Gestures.IsCorpseSkill(skillId)) entityStates.PlayCorpseJiggle(targetId);
                if (casterId == context.Self.CharId)
                {
                    bool requested = ResolvePendingReply(skillId);
                    if (!HasPendingCast(skillId)) EndCast(skillId);
                    if (requested && s != null && !HasPendingCast(skillId) && !dash)
                        LogSkillOutcome(s, miss);
                    if (s != null)
                    {
                        if (requested) skillTimers.Ensure(s, FrameClock.Now());
                        if (!miss && affected == context.Self.CharId) buff.ApplyMoveSpeedBuff(s, ActionAnimation.BuffSeconds(s, data));
                    }
                }
                if (!miss && s != null && affected == context.Self.CharId && casterId != context.Self.CharId && CastBreak.LightningHurts(CastBreakSkill.Of(s)))
                    BreakCastByChance();
                if (!miss && s != null)
                {
                    buff.RegisterBuff(s, affected, ActionAnimation.BuffSeconds(s, data));
                    if (affected == context.Self.CharId && s.Type1 == MagicType.Buff)
                        buff.ApplySelfStatus(s.BuffType, SecondaryBuff.SpeedPercent(data), ActionAnimation.BuffSeconds(s, data));
                }
                if (!miss && s?.Revives == true && !ResurrectOfferFeature.ResurrectionOnlyOffered(s, data)) SkillRevived?.Invoke(affected);
                break;

            case MagicSub.Fail:
            case MagicSub.Cancel:
                if (sub == MagicSub.Fail && targetId == context.Self.CharId && casterId != context.Self.CharId && s != null && CastBreak.InFlightFailBreaks(CastBreakSkill.Of(s), data))
                    BreakCastByChance();
                combatFx.StopSkillFx(casterId, skillId);
                if (casterId != context.Self.CharId) EndEntityCastPose(casterId);
                if (casterId == context.Self.CharId)
                {
                    bool awaited = HasPendingCast(skillId);
                    ClearPendingCast(skillId);
                    instantCasts.Remove(skillId);
                    EndCast(skillId);
                    if (s != null && (sub != MagicSub.Fail || MagicFail.RefundsCooldown(MagicFail.Code(data), s.FlyingFxId > 0)))
                        skillTimers.Cancel(s);
                    if (sub == MagicSub.Fail) SelfSkillRefused?.Invoke(skillId);
                    if (s != null && sub == MagicSub.Fail && MagicFail.Code(data) != 0) LogSkillFail(s, data);
                    else if (awaited && s != null) LogSkillRefused(s, sub);
                    castLogged.Remove(skillId);
                }
                break;

            case MagicSub.DurationExpired:
                if (targetId == context.Self.CharId || (targetId == 0 && casterId == context.Self.CharId))
                    buff.DropBuffChip(skillId);
                break;
        }
    }

    private void LogSkillUse(SkillData.Skill s)
    {
        if (BasicRangedAttackSkill()?.Id == s.Id || !castLogged.Add(s.Id)) return;
        context.Log.Add(s.IsPotion
            ? $"You used {ItemData.DisplayName(s.PotionItem)}."
            : GameText.System(GameText.UsingSkill, "Using %s", s.Name), CombatLogKind.Status);
    }

    private void LogSkillOutcome(SkillData.Skill s, bool miss)
    {
        LogSkillUse(s);
        castLogged.Remove(s.Id);
        if (BasicRangedAttackSkill()?.Id == s.Id || s.IsPotion) return;
        if (miss) context.Log.Add(GameText.System(GameText.Missed, "%s Missed.", s.Name), CombatLogKind.Incoming);
        else context.Log.Add($"{s.Name} succeeded.", CombatLogKind.Outgoing);
    }

    private void LogSkillFail(SkillData.Skill s, short[] data)
    {
        string text = MagicFail.TextFor(data) switch
        {
            MagicFailText.CastingFailed => GameText.System(BuffSystem.TextCastingFailed, "Casting failed"),
            MagicFailText.SkillFailed => $"{s.Name} failed.",
            MagicFailText.Missed => GameText.System(GameText.Missed, "%s Missed.", s.Name),
            MagicFailText.NotEnoughMp => GameText.System(GameText.NotEnoughMp, "Skill Failed - Not enough MP"),
            _ => "",
        };
        context.Log.Notice(text);
    }

    private void LogSkillRefused(SkillData.Skill s, int sub)
    {
        if (sub == MagicSub.Cancel)
        {
            if (castLogged.Contains(s.Id)) context.Log.Add($"{s.Name} was cancelled.", CombatLogKind.Status);
            return;
        }
        if (s is { UseItem: not 0, IsRanged: false })
            context.Log.Notice($"You couldn't use {ItemData.DisplayName(s.ConsumedItem)} right now.");
        else if (ConflictingBuff(s) is { } blocker)
            context.Log.Notice($"{blocker.Name} is already active.");
        else
            context.Log.Add($"{s.Name} failed.", CombatLogKind.Incoming);
    }

    private SkillData.Skill? ConflictingBuff(SkillData.Skill s)
    {
        if (buff.HasActiveFragmentOfManes() && (BuffRules.IsSpeedBuff(s) || s.IsBlink))
            return SkillData.Get(BuffSystem.FragmentOfManesSkillId);

        if (s.Type1 != MagicType.Buff || s.BuffType == 0) return null;
        double now = FrameClock.Now();
        foreach (var (skillId, end) in self.BuffEnds)
        {
            if (skillId == s.Id || end <= now) continue;
            var other = SkillData.Get(skillId);
            if (other != null && other.BuffType == s.BuffType) return other;
        }
        return null;
    }

    private IAreaCasts? areaCasts;
    private IMovementSkills? movementSkills;
    private IPartyPick? partyPick;
    private IMoveGates? moveGates;

    internal void AttachAreaCasts(IAreaCasts areaCasts) => this.areaCasts = areaCasts;

    internal void DetachAreaCasts() => areaCasts = null;

    internal void AttachMovementSkills(IMovementSkills movementSkills) => this.movementSkills = movementSkills;

    internal void DetachMovementSkills() => movementSkills = null;

    internal void AttachPartyPick(IPartyPick partyPick) => this.partyPick = partyPick;

    internal void DetachPartyPick() => partyPick = null;

    internal void AttachMoveGates(IMoveGates moveGates) => this.moveGates = moveGates;

    internal void DetachMoveGates() => moveGates = null;

    internal event Action<string?>? SkillMessage;

    internal event Action<int>? SkillRevived;

    internal event Action<int>? SelfSkillRefused;

    internal void CastSkill(int id)
    {
        if (context.Self.Dead) return;
        var s = SkillData.Get(id);
        if (s == null) return;
        if (!ZoneSkillTrees.Allowed(context.Zone, s.Tree, s.Id)) return;
        if (buff.BeginContinuance(s)) return;
        if (buff.SkillsLocked())
        {
            context.Log.Notice(GameText.System(BuffSystem.TextCastingFailed, "Casting failed"));
            return;
        }

        if (SkillData.IsOtherClassSkill(s.Id, self.Class))
        {
            context.Log.Notice($"You cannot use {s.Name}.");
            return;
        }
        if (!self.SkillLearned(s))
        {
            context.Log.Notice($"You have not learned {s.Name}. {SkillText.Requirement(s)}.");
            return;
        }
        if (!SkillData.IsGranted(s.Tree, SelfTransformModel()))
        {
            context.Log.Notice($"You cannot use {s.Name}.");
            return;
        }
        if (!siegeTower.SiegeSkillAllowed(s)) return;

        if (buff.HasActiveFragmentOfManes() && (BuffRules.IsSpeedBuff(s) || s.IsBlink))
        {
            context.Log.Notice("You cannot use movement skills while carrying the Fragment of Manes.");
            return;
        }

        double now = FrameClock.Now();
        if (skillTimers.OnCooldown(s, now)) return;
        if (skillTimers.GroupBlocker(s, now) is { } blocker)
        {
            context.Log.Notice(GroupCooldownNotice(s, blocker, now), CooldownRefusalColour);
            return;
        }
        if (!s.IsNonAction && SelfCasting(now)) return;
        if (HasPendingCast(id)) return;
        if (moveGates?.InputHeld == true) return;
        if (!self.Vitals.HasMana(s.Msp))
        {
            context.Log.Notice(SkillCost.ManaShortText(self.Class) == SkillCost.TextNotEnoughMp
                ? GameText.System(SkillCost.TextNotEnoughMp, "Skill Failed - Not enough MP")
                : GameText.System(SkillCost.TextNotEnoughSp, "Skill failed - Not enough SP"));
            return;
        }
        if (!self.Vitals.HasStamina(s.Sp))
        {
            context.Log.Notice(GameText.System(SkillCost.TextNotEnoughVp, "Skill Failed - Insufficient VP"));
            return;
        }
        if (!SkillUsable.AngerAllows(s.Id, pvp.AngerFull))
        {
            context.Log.Notice(GameText.System(TextRageModeOnly, "This can only be used when rage mode has been activated."), RageRefusalColour);
            return;
        }
        if (SkillCost.HpShort(self.Vitals.Hp, s.Hp))
        {
            context.Log.Notice(GameText.System(SkillCost.TextNotEnoughHp, "Skill failed - Not enough HP"));
            return;
        }
        if (s.Id == ZoneBoundSkill && context.Zone != ZoneBoundSkillZone)
        {
            context.Log.Notice(GameText.System(TextSkillWrongZone, "You cannot use %s from this zone.", s.Name));
            return;
        }
        if (!soccer.SoccerSkillAllowed(s))
        {
            context.Log.Notice(GameText.System(TextSkillWrongZone, "You cannot use %s from this zone.", s.Name));
            return;
        }
        if (s.Id is CoinSkills.A or CoinSkills.B && self.Sheet.Gold < CoinSkills.Floor)
        {
            context.Log.Notice(ItemData.Text(TextSkillNeedsCoins, "Skill failed - You need to have more than %d Noah.")
                .Replace("%d", CoinSkills.Floor.ToString()));
            return;
        }
        if (!CanCastWithGear(s, announce: true)) return;
        if (s.IsRanged && s.NeedArrow > 0 && SelfTransformedNonHumanoid())
        {
            context.Log.Notice(ItemData.Text(TextBowWhileTransformed, "You cannot use your bow while transforming."));
            return;
        }
        if (s.Id is >= RepairHammerFirst and <= RepairHammerLast && !WearsHammerRepairable())
        {
            context.Log.Notice(ItemData.Text(TextNoHammerTarget, "You are not equipped with a 0 durability irreparable item."));
            return;
        }
        if (!s.IsNonAction && avatar.Sitting) entityStates.StandUp();

        if (s.IsMeleeArea)
        {
            if (MeleeAreaCentre(s) is { } centre) areaCasts?.SendAreaCast(s, centre);
            return;
        }
        if (s.IsGroundArea && areaCasts?.BeginAreaCast(s) == true) return;
        if (s.IsCasterArea) { areaCasts?.SendAreaCast(s, context.Self.Body.GlobalPosition); return; }
        if (s.IsBlink) { movementSkills?.CastBlink(s); return; }

        int target;
        if (s.IsEnemy)
        {
            bool selectedValid =
                context.Selection.SelectedId >= 0
                && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var te)
                && te.Attackable
                && (!te.Dead || Gestures.IsCorpseSkill(s.Id));
            if (!selectedValid) return;
            if (!AnnounceReach(SkillReachTo(context.Selection.SelectedId, s)) || !AnnounceSight(s, context.Selection.SelectedId)) return;
            target = context.Selection.SelectedId;
            entityAction.FaceToward(context.Self.CharId, target);
        }
        else if (s.IsDeadFriend)
        {
            target = ResurrectTarget(s);
            if (target < 0) return;
            entityAction.FaceToward(context.Self.CharId, target);
        }
        else
        {
            target = FriendlyCastTarget(s);
            if (target < 0) return;
            if (target != context.Self.CharId) entityAction.FaceToward(context.Self.CharId, target);
        }

        bool skipCooldown = SpendInstantMagic(s);
        if (BattleMusic.Starts(s.Moral)) audio.EnterBattleMusic();
        SendCastingStage(s, target);
        if (s.Cast <= 0) avatar.AttackChain.Record(AttackChain.InstantSkill, FrameClock.Now());
        QueuePendingStage(id, target, s.HasFlyingStage ? PendingFlying : PendingEffecting, CastDelay(s));
        BeginLocalCast(s, skipCooldown);
    }

    private const int TextBowWhileTransformed = 7650;
    private const int TextNoHammerTarget = 15152;
    private const int RepairHammerFirst = 490215;
    private const int RepairHammerLast = 490217;
    private const byte HammerRepairableFlag = 2;

    private static readonly int[] HammerArmourSlots =
    {
        InventoryConstants.Head, InventoryConstants.Breast, InventoryConstants.Leg,
        InventoryConstants.Glove, InventoryConstants.Foot,
    };

    private bool SelfTransformedNonHumanoid() =>
        self.TransformSkill != 0
        && avatar.Body?.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) is { } worn
        && !(worn.HasMeta(WornLook.WornAccessoriesMeta) && worn.GetMeta(WornLook.WornAccessoriesMeta).AsBool());

    private bool WearsHammerRepairable()
    {
        foreach (int slot in HammerArmourSlots)
            if (slot < self.Inventory.Length && !self.Inventory[slot].IsEmpty && self.Inventory[slot].Durability <= 0 && self.Inventory[slot].Flag == HammerRepairableFlag)
                return true;
        return false;
    }

    internal void SendCastingStage(SkillData.Skill s, int target, short[]? data = null)
    {
        if (s.Cast > 0)
        {
            requests.Magic(MagicSub.Casting, s.Id, target, data);
            return;
        }
        instantCasts.Remove(s.Id);
        LogSkillUse(s);
    }

    private const int InstantMagicBuffType = 23;
    private readonly HashSet<int> instantCasts = new();

    internal bool SpendInstantMagic(SkillData.Skill s)
    {
        if (s is { Type1: MagicType.Buff, BuffType: InstantMagicBuffType }) return false;
        double now = FrameClock.Now();
        foreach (var (buffSkillId, end) in self.BuffEnds)
        {
            if (end <= now || SkillData.Get(buffSkillId) is not { Type1: MagicType.Buff, BuffType: InstantMagicBuffType }) continue;
            buff.DropBuffChip(buffSkillId);
            self.ForgetBuff(buffSkillId);
            instantCasts.Add(s.Id);
            return true;
        }
        return false;
    }

    private const string TransformModelKey = "TransformId";

    internal int SelfTransformModel() =>
        SkillData.Get(self.TransformSkill) is { } form && form.Effect.TryGetValue(TransformModelKey, out var model)
        ? model : 0;

    private const int TextSkillTooFar = 4009;
    private const int TextSkillTooNear = 4014;
    private const int TextSkillBlocked = 4010;
    private const int TextSkillNoItem = 4007;
    private const int TextSkillImproperItem = 4005;
    private const int TextEquipWeapon = 7658;
    private const int TextNoClassStoneFirst = 7606;
    private const int TextSkillNeedsCoins = 4012;
    private const int TextSkillWrongZone = 4331;

    private const int ZoneBoundSkill = 500073;
    private const int ZoneBoundSkillZone = 84;
    private const int FirstStoneFamily = 1;
    private const int LastStoneFamily = 4;

    private Vector3? MeleeAreaCentre(SkillData.Skill s)
    {
        if (context.Selection.SelectedId < 0 || !context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var target) || !target.Attackable || target.Dead)
        {
            context.Log.Notice("Select a target first.");
            return null;
        }
        if (!AnnounceReach(SkillReachTo(context.Selection.SelectedId, s))) return null;
        entityAction.FaceToward(context.Self.CharId, context.Selection.SelectedId);
        return target.Body.GlobalPosition;
    }

    private int FriendlyCastTarget(SkillData.Skill s)
    {
        if (!s.IsFriendly) return context.Self.CharId;
        if (s.Moral == SkillTarget.FriendWithMe && self.HealStrikesEnemies
            && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var foe) && foe.Attackable && !foe.Dead)
        {
            if (!AnnounceReach(SkillReachTo(context.Selection.SelectedId, s))) return -1;
            return HealStrike.StrikesWith(s.Type1, s.DirectType, s.FirstDamage, s.TimeDamage) ? context.Selection.SelectedId : context.Self.CharId;
        }
        bool isSelf = context.Selection.SelectedId == context.Self.CharId;
        bool hasSelection = isSelf || (context.Selection.SelectedId >= 0 && context.Entities.Views.ContainsKey(context.Selection.SelectedId));
        bool valid = isSelf || (context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e) && !e.IsNpc && !e.Dead && !e.Attackable);
        bool inParty = isSelf || party.IsMember(context.Selection.SelectedId);
        int pick = PartyPick();
        return FriendlyTarget.Choose(s.Moral, hasSelection, valid, inParty, pick >= 0) switch
        {
            FriendlyPick.Self => context.Self.CharId,
            FriendlyPick.PartyPick => pick,
            FriendlyPick.Selected when isSelf => context.Self.CharId,
            FriendlyPick.Selected => AnnounceReach(SkillReachTo(context.Selection.SelectedId, s)) ? context.Selection.SelectedId : -1,
            _ => -1,
        };
    }

    private int PartyPick()
    {
        int pick = partyPick?.Pick ?? -1;
        return pick >= 0 && party.IsMember(pick) ? pick : -1;
    }

    private int ResurrectTarget(SkillData.Skill s)
    {
        if (context.Selection.SelectedId < 0 || context.Selection.SelectedId == context.Self.CharId) return -1;
        if (!context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e)) return -1;
        if (e.IsNpc || !e.Dead || e.Attackable) return -1;
        return AnnounceReach(SkillReachTo(context.Selection.SelectedId, s)) && AnnounceSight(s, context.Selection.SelectedId) ? context.Selection.SelectedId : -1;
    }

    internal bool CanCastWithGear(SkillData.Skill s, bool announce = false)
    {
        var gear = entityLooks.SelfGear();
        int right = SkillAnimation.HeldItemClass(gear, InventoryConstants.VisRightHand), left = SkillAnimation.HeldItemClass(gear, InventoryConstants.VisLeftHand);
        if (!SkillUsable.GearAllows(s.NeedWeapon, right, left))
        {
            if (announce)
                context.Log.Notice(WeaponAnimation.IsWeapon(right) || WeaponAnimation.IsWeapon(left)
                    ? GameText.System(TextSkillImproperItem, "Skill failed - Improper item")
                    : GameText.System(TextEquipWeapon, "Please equip your weapon."));
            return false;
        }
        if (s.UseItem != 0 && !s.IsResurrect && ItemData.Get(s.UseItem) != null)
        {
            if (self.Inventory.CountInBackpack(s.UseItem) < 1)
            {
                if (announce) context.Log.Notice(GameText.System(TextSkillNoItem, "Skill failed - Not enough item"));
                return false;
            }
            int need = s.IsRanged ? Mathf.Max(1, s.NeedArrow) : 1;
            if (self.Inventory.CountInBackpack(s.ConsumedItem) < need)
            {
                int family = CharacterClassCatalog.Family(self.Class);
                if (announce)
                    context.Log.Notice(GameText.System(s.IsMasterScrollSkill && family is >= FirstStoneFamily and <= LastStoneFamily
                        ? TextNoClassStoneFirst + family - FirstStoneFamily
                        : TextSkillNoItem, "Skill failed - Not enough item"));
                return false;
            }
        }
        return true;
    }

    internal bool InSkillRange(int id, SkillData.Skill s) => SkillReachTo(id, s) == SkillReachVerdict.InRange;

    private SkillReachVerdict SkillReachTo(int id, SkillData.Skill s) =>
        context.Entities.Views.TryGetValue(id, out var e)
        ? SkillReachToPoint(s, e.Body.GlobalPosition, e.Radius, !e.IsNpc)
        : SkillReachVerdict.TooFar;

    internal SkillReachVerdict SkillReachToPoint(SkillData.Skill s, Vector3 point, float targetRadius, bool targetIsPlayer) =>
        SkillReach.Check(new SkillReachQuery
        {
            SkillId = s.Id,
            Type1 = s.Type1,
            Type2 = s.Type2,
            Range = s.Range,
            SelfAnim2 = s.SelfAnim2,
            Distance = point.DistanceTo(context.Self.Body.GlobalPosition),
            CasterRadius = context.Entities.Views.TryGetValue(context.Self.CharId, out var me) ? me.Radius : SelfAvatar.DefaultBodyRadius,
            TargetRadius = targetRadius,
            TargetIsPlayer = targetIsPlayer,
            Zone = context.Zone,
            MeleeWeaponReach = entityLooks.HandWeaponReach(rightFirst: true),
            RangedWeaponReach = entityLooks.HandWeaponReach(rightFirst: false),
            RangedAddRangePercent = s.AddRange,
            MeleeReachBonus = ActiveReachBonus(),
        });

    internal bool AnnounceReach(SkillReachVerdict verdict)
    {
        switch (verdict)
        {
            case SkillReachVerdict.InRange:
                return true;
            case SkillReachVerdict.TooFar:
                context.Log.Notice(GameText.System(TextSkillTooFar, "Skill failed - Too far"));
                break;
            case SkillReachVerdict.TooNear:
                context.Log.Notice(GameText.System(TextSkillTooNear, "Skill Failed - Too near"));
                break;
        }
        return false;
    }

    private const float SightShortfall = 0.2f;

    private bool AnnounceSight(SkillData.Skill s, int targetId)
    {
        if (!context.Entities.Views.TryGetValue(targetId, out var target) || !SkillSightBlocked(s, target)) return true;
        context.Log.Notice(GameText.System(TextSkillBlocked, "Skill failed - Blocked by an object"));
        return false;
    }

    private bool SkillSightBlocked(SkillData.Skill s, EntityView target)
    {
        if (avatar.NoClip || scenery.ObjCollision == null || !context.Entities.Views.TryGetValue(context.Self.CharId, out var me)) return false;
        if (!SkillReach.SightChecked(s.Moral, target.Attackable, s.Id)) return false;

        var from = MidBody(me);
        var to = MidBody(target);
        var span = to - from;
        float length = span.Length();
        if (length > SightShortfall) to -= span / length * SightShortfall;

        var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionLayers.World);
        query.Exclude = SightExclusions();
        return context.Root.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }

    internal Godot.Collections.Array<Rid> SightExclusions()
    {
        var excluded = new Godot.Collections.Array<Rid>();
        if (avatar.Body != null) excluded.Add(avatar.Body.GetRid());
        foreach (var e in context.Entities.Views.Values)
            if (e.BridgeFloor != null && GodotObject.IsInstanceValid(e.BridgeFloor)) excluded.Add(e.BridgeFloor.GetRid());
        if (!SkillReach.ObjectsBlockSight(context.Zone))
            foreach (var collider in gate.GateColliders)
                if (collider.Body != null) excluded.Add(collider.Body.GetRid());
        return excluded;
    }

    private static Vector3 MidBody(EntityView e) => e.Body.GlobalPosition + Vector3.Up * (SkillFxRig.EntityLocalBox(e).Size.Y * 0.5f);

    private const int ReachBuffType = 43;
    private const string ReachBuffAmountKey = "SpecialAmount";

    private float ActiveReachBonus()
    {
        double now = FrameClock.Now();
        foreach (var (buffSkillId, end) in self.BuffEnds)
        {
            if (end <= now || SkillData.Get(buffSkillId) is not { Type1: MagicType.Buff, BuffType: ReachBuffType } reachBuff) continue;
            return reachBuff.Effect.TryGetValue(ReachBuffAmountKey, out var amount) ? amount : 0;
        }
        return 0f;
    }

    internal static double CastDelay(SkillData.Skill s) => Mathf.Max(0f, s.CastSeconds);

    internal void QueuePendingStage(int skillId, int targetId, int stage, double delay, short[]? data = null, bool replace = true)
    {
        double when = FrameClock.Now() + delay;
        var pending = new PendingCast
        { EffectTime = when, SkillId = skillId, TargetId = targetId, Stage = stage, Data = data };
        for (int i = 0; replace && i < PendingCasts.Count; i++)
        {
            if (PendingCasts[i].SkillId != skillId) continue;
            PendingCasts[i] = pending;
            return;
        }
        PendingCasts.Add(pending);
    }

    private bool ResolvePendingReply(int skillId)
    {
        int fallback = -1;
        for (int i = 0; i < PendingCasts.Count; i++)
        {
            if (PendingCasts[i].SkillId != skillId) continue;
            if (PendingCasts[i].Stage == PendingAwaitServer)
            {
                PendingCasts.RemoveAt(i);
                return true;
            }
            if (fallback < 0) fallback = i;
        }
        if (fallback < 0) return false;
        PendingCasts.RemoveAt(fallback);
        return true;
    }

    internal bool HasPendingCast(int skillId)
    {
        foreach (var pc in PendingCasts)
            if (pc.SkillId == skillId)
                return true;
        return false;
    }

    internal bool PendingCastPreEffect(int skillId)
    {
        foreach (var pc in PendingCasts)
            if (pc.SkillId == skillId)
                return pc.Stage != PendingAwaitServer;
        return false;
    }

    private void ClearPendingCast(int skillId)
    {
        for (int i = PendingCasts.Count - 1; i >= 0; i--)
            if (PendingCasts[i].SkillId == skillId)
                PendingCasts.RemoveAt(i);
    }

    private void StartCastFx(int casterId, SkillData.Skill s)
    {
        combatFx.StopSkillFx(casterId, s.Id);
        if (s.SelfFx1 == null) return;
        combatFx.SpawnOwnedFx(casterId, s.Id, MagicSub.Casting, s.SelfFx1, s.SelfPart1);
        ScheduleSkillFxStop(casterId, s.Id, 1, FrameClock.Now() + s.CastSeconds + CastFxGraceSeconds);
        if (s.HasCastPhase && !SameSound(s.SelfFx1Id, s.FlyingFxId)) audio.FxAt(s.SelfFx1Id, casterId);
    }

    private static bool SameSound(int castFx, int flyingFx) =>
        flyingFx != 0 && SoundCatalog.FxSound(castFx) is var sound and not 0 && sound == SoundCatalog.FxSound(flyingFx);

    internal void BeginLocalCast(SkillData.Skill s, bool skipCooldown = false)
    {
        if (!skipCooldown) skillTimers.Start(s, FrameClock.Now());
        StartCastFx(context.Self.CharId, s);
        if (s.IsPotion && !s.HasCastPhase) audio.FxAt(s.TargetFxId, context.Self.CharId);
        BeginCast(s);
        if (s.IsNonAction) return;
        entityAction.PlaySkillAction(context.Self.CharId, entityAction.SkillAnim(context.Self.CharId, s, false), ActionAnimation.ClipsForCast(s), ModelAnimation.ActionRankSkill);
        StretchCastAnimation(s);
    }

    internal bool SelfCasting(double now) =>
        IsRootedByCast() || self.Stunned(FrameClock.Now()) || (avatar.ActionRank == ModelAnimation.ActionRankSkill && now < avatar.ActionUntil);

    private static readonly Color CooldownRefusalColour = new("ff3b3b");
    private static readonly Color RageRefusalColour = new(1f, 1f, 0f);
    private const int TextRageModeOnly = 18601;

    private string GroupCooldownNotice(SkillData.Skill pressed, SkillData.Skill blocker, double now) =>
        TextTemplate.Fill(GameText.System(CooldownGroups.RefusalText, "%s is available in %d seconds"),
            pressed.Name, CooldownGroups.SecondsShown(skillTimers.ReadyAt(blocker.Id) - now));

    internal bool SkillReady(SkillData.Skill s, double now) =>
        !HasPendingCast(s.Id)
    && !skillTimers.OnCooldown(s, now)
    && skillTimers.GroupBlocker(s, now) == null
    && self.Vitals.HasMana(s.Msp);

    internal SkillData.Skill? BasicRangedAttackSkill()
    {
        if (RangedWeaponItem() == 0) return null;
        int id = BasicRangedSkillId();
        return id != 0 ? SkillData.Get(id) : null;
    }

    private const int BasicRangedSkillIndex = 3;
    private const int KnightRoyaleArchery = 490356;

    private int BasicRangedSkillId()
    {
        if (KnightRoyale.IsZone(context.Zone)) return SkillData.Get(KnightRoyaleArchery) != null ? KnightRoyaleArchery : 0;
        if (CharacterClassCatalog.Family(self.Class) != CharacterClassCatalog.FamilyRogue) return 0;
        int id = SkillData.ClassSkillId(self.Class, BasicRangedSkillIndex);
        return SkillData.Get(id) != null ? id : 0;
    }

    internal int RangedWeaponItem()
    {
        int right = self.Inventory.ItemIdAt(InventoryConstants.RightHand), left = self.Inventory.ItemIdAt(InventoryConstants.LeftHand);
        if (ItemData.Get(left)?.Kind is WeaponAnimation.Bow or WeaponAnimation.LongBow) return left;
        if (ItemData.Get(right)?.Kind is WeaponAnimation.Crossbow) return right;
        return 0;
    }

    private System.Collections.Generic.Dictionary<int, int> RemoteCastingSkill { get; } = new();

    private void HoldEntityCastPose(int casterId, SkillData.Skill s)
    {
        if (!s.RootsCaster || s.CastSeconds <= CastStretchMinSeconds) return;
        if (!context.Entities.Views.TryGetValue(casterId, out var e) || e.Anim == null || e.Dead) return;
        double now = FrameClock.Now();
        double clipLeft = e.ActionUntil - now;
        if (clipLeft <= 0) return;
        e.ActionUntil = now + s.CastSeconds;
        e.ActionTimeScale = (float)(clipLeft / s.CastSeconds);
        if (!e.AnimThrottled) e.Anim.SpeedScale = e.ActionTimeScale;
    }

    private void EndEntityCastPose(int casterId)
    {
        if (!context.Entities.Views.TryGetValue(casterId, out var e) || e.ActionTimeScale == 1f) return;
        ActionAnimation.ResetEntityActionScale(e);
        e.ActionUntil = 0;
    }

    private void FaceTowardImpact(int casterId, short[] data)
    {
        if (data.Length <= 2 || (data[0] == 0 && data[2] == 0)) return;
        Vector3 impact = context.Terrain.GroundPos(data[0], data[2], 0f, 0f);
        entityAction.FaceTowardPoint(casterId, impact);
    }

    private int castingSkillId;
    private double castingUntil;
    private float castAnimScale = 1f;

    private bool moveInputHeld;

    internal void NoteMoveInput(bool held) => moveInputHeld = held;
    private const double CastHoldCancelGraceSeconds = 0.15;

    private const float AnimSpeedDeltaMin = 0.01f;
    private const float AnimSpeedDeltaMax = 10.0f;

    internal void ApplySelfAnimSpeed()
    {
        if (avatar.Anim == null) return;
        float delta = IsRootedByCast() ? castAnimScale
            : audio.SelfSwinging() ? buff.AttackSpeedMultiplier()
            : context.Self.Moving ? buff.MoveSpeedMultiplier() : 1f;
        if (delta < AnimSpeedDeltaMin || delta >= AnimSpeedDeltaMax) return;
        if (!Mathf.IsEqualApprox(avatar.Anim.SpeedScale, delta)) avatar.Anim.SpeedScale = delta;
    }

    internal bool IsRootedByCast() => castingSkillId != 0 && FrameClock.Now() < castingUntil;

    private void BeginCast(SkillData.Skill s)
    {
        if (!s.RootsCaster) return;
        context.Movement.CancelMoveTo();
        if (castingSkillId == s.Id && FrameClock.Now() < castingUntil) return;
        castingSkillId = s.Id;
        castingUntil = FrameClock.Now() + s.CastSeconds;
    }

    internal void EndCast(int skillId)
    {
        if (castingSkillId == skillId || skillId == 0)
        {
            castingSkillId = 0;
            castingUntil = 0;
            castAnimScale = 1f;
        }
    }

    private const float CastStretchMinSeconds = 0.1f;

    private void StretchCastAnimation(SkillData.Skill s)
    {
        castAnimScale = 1f;
        if (s.CastSeconds <= CastStretchMinSeconds || avatar.Anim == null) return;
        var clip = avatar.Anim.GetAnimation(avatar.Anim.CurrentAnimation);
        if (clip == null || clip.Length <= 0f) return;
        castAnimScale = (float)(clip.Length / s.CastSeconds);
        avatar.ActionUntil = FrameClock.Now() + s.CastSeconds;
    }

    private const double RangedCommitSeconds = 0.4;

    internal void CastMoveCancelTick()
    {
        if (castingSkillId == 0) return;
        var s = SkillData.Get(castingSkillId);
        if (s is not { HasCastPhase: true }) return;
        if (avatar.MoveKeyPressed) { InterruptSelfCast(); return; }
        if (!moveInputHeld) return;
        if (!s.NeedsFlying && FrameClock.Now() - (castingUntil - s.CastSeconds) >= CastHoldCancelGraceSeconds)
            InterruptSelfCast();
    }

    internal void InterruptSelfCast()
    {
        int skillId = castingSkillId;
        if (skillId == 0) return;
        var s = SkillData.Get(skillId);
        if (s == null) return;
        bool pastCommit = FrameClock.Now() >= castingUntil - s.CastSeconds + RangedCommitSeconds;
        bool released = CastInterrupt.IsReleased(FrameClock.Now(), castingUntil, PendingCastPreEffect(skillId));
        switch (CastInterrupt.OnMove(s.HasCastPhase, released, s.NeedsFlying, pastCommit))
        {
            case CastInterruptAction.ReleaseEarly: ReleaseSelfCastEarly(skillId); break;
            case CastInterruptAction.Cancel: CancelSelfCast(skillId); break;
        }
    }

    private void ReleaseSelfCastEarly(int skillId)
    {
        double now = FrameClock.Now();
        for (int i = 0; i < PendingCasts.Count; i++)
        {
            var pc = PendingCasts[i];
            if (pc.SkillId != skillId || pc.Stage != PendingFlying) continue;
            pc.EffectTime = now;
            PendingCasts[i] = pc;
        }
        combatFx.StopSkillFx(context.Self.CharId, skillId, MagicSub.Casting);
        EndCast(skillId);
        avatar.ActionUntil = 0;
        avatar.Clip = null;
    }

    private void CancelActionCasts()
    {
        var casting = PendingCasts.Select(c => c.SkillId).Append(castingSkillId);
        foreach (int skill in CastInterrupt.OnStun(casting, id => SkillData.Get(id)?.IsNonAction).ToArray()) CancelSelfCast(skill);
    }

    internal void CancelAllSelfCasts()
    {
        foreach (int skill in PendingCasts.Select(c => c.SkillId).Distinct().ToArray()) CancelSelfCast(skill);
        if (castingSkillId != 0) CancelSelfCast(castingSkillId);
    }

    internal void CancelCastsAtTheDead()
    {
        foreach (var pending in PendingCasts.ToArray())
            if (pending.TargetId >= 0 && pending.TargetId != context.Self.CharId
                && context.Entities.Views.TryGetValue(pending.TargetId, out var target) && target.Dead
                && SkillData.Get(pending.SkillId) is { IsEnemy: true } && !Gestures.IsCorpseSkill(pending.SkillId))
                CancelSelfCast(pending.SkillId);
    }

    internal void CancelSelfCast(int skillId)
    {
        combatFx.StopSkillFx(context.Self.CharId, skillId);
        EndCast(skillId);
        ClearPendingCast(skillId);
        var s = SkillData.Get(skillId);
        if (s != null)
            skillTimers.Cancel(s);
        requests.CastFailed(skillId);
        avatar.ActionUntil = 0;
        avatar.Clip = null;
    }

    private const int CastBreakRoll = 100;

    internal void BreakCastByChance()
    {
        int skillId = castingSkillId;
        if (skillId == 0 || SkillData.Get(skillId) is not { HasCastPhase: true } s) return;
        if (CastInterrupt.IsReleased(FrameClock.Now(), castingUntil, PendingCastPreEffect(skillId))) return;
        if ((int)(GD.Randi() % CastBreakRoll) < s.Success) return;
        CancelSelfCast(skillId);
        context.Log.Notice(GameText.System(BuffSystem.TextCastingFailed, "Casting failed"));
    }
}
