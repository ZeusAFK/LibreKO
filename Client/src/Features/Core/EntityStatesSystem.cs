using Godot;

namespace LibreKO.Features.Core;

internal sealed class EntityStatesSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly AchievementSession achievement;
    private readonly ClanSession clan;
    private readonly GenieSession genie;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly IPlayerRequests playerRequests;
    private readonly ISoccerRequests soccerRequests;
    private readonly EntityLooksSystem entityLooks;
    private readonly EntityActionSystem entityAction;
    private readonly StealthSystem stealth;
    private readonly EntityPlatesSystem entityPlates;

    public EntityStatesSystem(IWorldContext context, AchievementSession achievement, ClanSession clan, GenieSession genie, ProgressSession progress, SelfSession self, IPlayerRequests playerRequests, ISoccerRequests soccerRequests, SelfAvatar avatar, EntityLooksSystem entityLooks, EntityActionSystem entityAction, StealthSystem stealth, EntityPlatesSystem entityPlates)
    {
        this.context = context;
        this.achievement = achievement;
        this.clan = clan;
        this.genie = genie;
        this.progress = progress;
        this.self = self;
        this.playerRequests = playerRequests;
        this.soccerRequests = soccerRequests;
        this.avatar = avatar;
        this.entityLooks = entityLooks;
        this.entityAction = entityAction;
        this.stealth = stealth;
        this.entityPlates = entityPlates;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    internal HashSet<int> SittingIds { get; } = new();
    internal HashSet<int> StanceIds { get; } = new();

    internal void NoteSitting(int charId, bool sitting)
    {
        if (sitting) SittingIds.Add(charId);
        else SittingIds.Remove(charId);
    }

    private readonly Dictionary<int, Node3D> sleepFx = new();

    private readonly Dictionary<int, Node3D> warPointFx = new();

    private bool selfSitVisual;
    private bool sitRequestPending;

    internal void StateVisualForgetEntity(int id)
    {
        SittingIds.Remove(id);
        StanceIds.Remove(id);
        ApplySleepState(id, false);
        ApplyWarPoint(id, false);
        DevilForms.Remove(id);
    }

    private const float SitDrop = 0.42f;
    private const float SitTiltDeg = -14f;
    internal const int SleepFxId = 13042;
    private const int WarPointFxId = 34001;
    internal const float SleepFxLift = 0.4f;

    internal void StateVisualInit()
    {
        progress.StateChangeEvent += OnStateChange;
    }

    internal void StateVisualDispose()
    {
        progress.StateChangeEvent -= OnStateChange;
    }

    private void OnStateChange(int charId, int type, int value)
    {
        switch (type)
        {
            case StateChange.Pose:
                if (value is UserPose.Asleep or UserPose.Awake)
                    ApplySleepState(charId, value == UserPose.Asleep);
                else
                    ApplySitState(charId, value == UserPose.Sitting);
                break;
            case StateChange.Transformation: ApplyTransformState(charId, value); break;
            case StateChange.NeedParty: ApplySeekingParty(charId, (byte)value); break;
            case StateChange.PartyLeader: ApplyPartyLeader(charId, value != 0); break;
            case StateChange.CombatStance: ApplyCombatStanceState(charId, value != StateChange.StanceRelaxed); break;
            case StateChange.Action: ApplyGesture(charId, value); break;
            case StateChange.WarPoint: ApplyWarPoint(charId, value != 0); break;
            case StateChange.DevilForm: ApplyDevilForm(charId, value != 0); break;
            case StateChange.ExpeditionSide: ApplyExpeditionName(); break;
        }
    }

    private void ApplyWarPoint(int charId, bool on)
    {
        if (warPointFx.Remove(charId, out var running) && GodotObject.IsInstanceValid(running))
            running.QueueFree();
        Node3D? body = charId == context.Self.CharId ? context.Self.Body : context.Entities.Views.TryGetValue(charId, out var e) ? e.Body : null;
        if (!on || body == null || !GodotObject.IsInstanceValid(body) || Fx.NameForId(WarPointFxId) is not { } fxName) return;
        if (Fx.Spawn(fxName, body, Vector3.Zero) is { } fx) warPointFx[charId] = fx;
    }

    private void ApplyExpeditionName()
    {
        if (self.LastEnter.Zone != StatePlates.ExpeditionZone || progress.SelfExpeditionSide == ProgressSession.NoExpeditionSide) return;
        int text = StatePlates.ExpeditionNameText(progress.SelfExpeditionSide);
        entityPlates.SelfPlate()?.SetNameText(text > 0 ? ItemData.Text(text, "") : "");
    }

    internal void ApplySelfPlateState()
    {
        entityPlates.SelfPlate()?.SetTitle(EntityRig.TitleTextOf(achievement.DisplayTitleId, context.Zone));
        ApplyExpeditionName();
        entityPlates.ApplySelfZoneName();
    }

    internal static readonly Color SeekingPartyColour = new(0f, 1f, 0f);

    private void ApplyPartyLeader(int charId, bool leader)
    {
        var plate = charId == context.Self.CharId ? entityPlates.SelfPlate() : context.Entities.Views.TryGetValue(charId, out var e) ? e.Plate : null;
        plate?.SetLeader(leader);
    }

    private void ApplySeekingParty(int charId, byte state)
    {
        bool isSelf = charId == context.Self.CharId;
        var plate = isSelf ? entityPlates.SelfPlate() : context.Entities.Views.TryGetValue(charId, out var e) ? e.Plate : null;
        int level = isSelf ? self.Sheet.Level : context.Entities.Views.TryGetValue(charId, out var who) ? who.Level : 0;
        plate?.SetSeeking(SeekingParty.Line((SeekingPartyState)state, level, ItemData.Text) ?? "", SeekingPartyColour);
    }

    private void ApplySleepState(int charId, bool asleep)
    {
        if (sleepFx.Remove(charId, out var running) && GodotObject.IsInstanceValid(running))
            running.QueueFree();

        if (!asleep
            || !context.Entities.Views.TryGetValue(charId, out var e)
            || !GodotObject.IsInstanceValid(e.Body)
            || Fx.NameForId(SleepFxId) is not { } fxName)
            return;

        var fx = Fx.Spawn(fxName, e.Body, new Vector3(0, entityPlates.HeadHeightOf(charId) + SleepFxLift, 0));
        if (fx != null)
            sleepFx[charId] = fx;
    }

    private void ApplyCombatStanceState(int charId, bool ready)
    {
        if (charId == context.Self.CharId) return;
        if (ready ? !StanceIds.Add(charId) : !StanceIds.Remove(charId)) return;
        if (!context.Entities.Views.TryGetValue(charId, out var e) || e.IsNpc) return;
        StateLooks.ApplyEntityCombatStance(e, ready);
    }

    private const double SitToggleInterval = 3.0;
    private double sitToggledAt = double.NegativeInfinity;

    internal void SendSitToggle()
    {
        if (context.Self.Dead || sitRequestPending) return;
        bool sitDown = !avatar.Sitting;
        double now = context.Now;
        if (now - sitToggledAt < SitToggleInterval) return;
        sitToggledAt = now;
        sitRequestPending = true;
        playerRequests.Sitting(sitDown);
    }

    private void ApplySitState(int charId, bool sitting)
    {
        bool isSelf = charId == context.Self.CharId;

        if (sitting ? !SittingIds.Add(charId) : !SittingIds.Remove(charId))
        {
            return;
        }

        if (isSelf)
        {
            sitRequestPending = false;
            avatar.Sitting = sitting;
            if (sitting)
            {
                context.Movement.StopForInteraction();
            }
            ApplySelfSitVisual(sitting);
        }
        else if (context.Entities.Views.TryGetValue(charId, out var ent) && GodotObject.IsInstanceValid(ent.Body))
        {
            ApplyEntitySitVisual(ent, sitting);
        }

        if (isSelf)
            context.Log.Add(
                sitting ? "You sit down." : "You stand up.",
                CombatLogKind.Status);
    }

    internal void ApplyEntitySitVisual(EntityView e, bool sitting)
    {
        if (e.Sitting == sitting) return;
        e.Sitting = sitting;
        e.Clip = null;
        if (sitting) e.Hover = WingRig.ResolveHoverClips(e.Body, e.Anim);

        if (e.NameTag != null && GodotObject.IsInstanceValid(e.NameTag) && !e.Head.Follows)
        {
            var tagPos = e.NameTag.Position;
            tagPos.Y = sitting && e.Hover == null
                ? NamePlate.SittingHeight
                : (e.OriginalTagY > 0 ? e.OriginalTagY : NamePlate.StandingHeight);
            if (e.Plate != null) e.Plate.MoveTo(tagPos);
            else e.NameTag.Position = tagPos;
        }

        var posture = sitting ? e.Hover?.Start ?? ModelNodes.SitDownClips : e.Hover?.End ?? ModelNodes.StandUpClips;
        if (e.Anim != null && ModelAnimation.Pick(e.Anim, posture) != null)
        {
            entityAction.PlayEntityAction(e, posture, ModelAnimation.ActionRankPosture);
            return;
        }

        var pos = e.Body.Position;
        pos.Y += sitting ? -SitDrop : SitDrop;
        e.Body.Position = pos;
    }

    private void ApplySelfSitVisual(bool sitting)
    {
        if (sitting && selfSitVisual != sitting) avatar.Hover = WingRig.ResolveHoverClips(context.Self.Body, avatar.Anim);

        if (avatar.NameTag is { } nameTag && !entityPlates.SelfHead.Follows)
        {
            var tagPos = nameTag.Position;
            tagPos.Y = sitting && avatar.Hover == null ? NamePlate.SittingHeight : NamePlate.StandingSelfHeight;
            if (entityPlates.SelfPlate() is { } plate) plate.MoveTo(tagPos);
            else nameTag.Position = tagPos;
        }

        if (!GodotObject.IsInstanceValid(context.Self.Visual)) return;
        if (selfSitVisual == sitting) return;
        selfSitVisual = sitting;

        context.Self.Visual.Transform = avatar.StandingVisualTransform;
        if (!sitting)
        {
            if (avatar.Anim != null)
            {
                double len = ActionAnimation.PlayActionOn(avatar.Anim, avatar.Hover?.End ?? ModelNodes.StandUpClips);
                if (len > 0)
                {
                    entityAction.BeginSelfAction(len, ModelAnimation.ActionRankPosture, context.Now);
                }
            }
            return;
        }

        var sitDown = avatar.Hover?.Start ?? ModelNodes.SitDownClips;
        if (avatar.Anim != null && ModelAnimation.Pick(avatar.Anim, sitDown) != null)
        {
            double len = ActionAnimation.PlayActionOn(avatar.Anim, sitDown);
            entityAction.BeginSelfAction(len, ModelAnimation.ActionRankPosture, context.Now);
            return;
        }

        var seated = avatar.StandingVisualTransform;
        seated.Origin += Vector3.Down * SitDrop;
        seated.Basis = seated.Basis.Rotated(Vector3.Right, Mathf.DegToRad(SitTiltDeg));
        context.Self.Visual.Transform = seated;
    }

    internal void StandUp()
    {
        avatar.Sitting = false;
        selfSitVisual = false;
        sitRequestPending = true;
        if (GodotObject.IsInstanceValid(context.Self.Visual))
            context.Self.Visual.Transform = avatar.StandingVisualTransform;
        avatar.ActionUntil = 0;
        avatar.Clip = null;
        playerRequests.Sitting(false);
    }

    private void ApplyTransformState(int charId, int skillId)
    {
        bool ending = skillId <= 0;
        if (ZoneLookHolds(charId))
        {
            if (charId == context.Self.CharId) HoldSelfTransform(ending ? 0 : skillId);
            return;
        }

        if (ending)
        {
            RestoreLookOrZoneLook(charId);
            return;
        }

        WearMonsterLook(charId, skillId);
    }

    internal Node3D? TransformHost(int charId) =>
        charId == context.Self.CharId ? avatar.Body : context.Entities.Views.TryGetValue(charId, out var e) ? e.Body : null;

    internal bool IsWearingTransformLook(int charId) =>
        TransformHost(charId)?.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) != null;

    private void DressSelfLook()
    {
        entityLooks.RerenderSelfEquipment(force: true);
        entityLooks.DressSelf();
        StateLooks.DressClanGauntlet(context.Self.Body, self.Race, clan.MyClan.InClan ? clan.MyClan.Grade : 0, clan.MyClan.InClan ? clan.MyClan.Ranking : 0);
    }

    private void DressEntityLook(EntityView e)
    {
        entityLooks.RedressEntity(e, force: true);
        entityLooks.Dress(e.Body, e.CapeId, e.CapeR, e.CapeG, e.CapeB, e.IsGm, e.Race);
        StateLooks.DressClanGauntlet(e.Body, e.Race, e.ClanGrade, e.ClanRanking);
    }

    internal void WearMonsterLook(int charId, int skillId, float? scale = null, int fixedWeapon = 0)
    {
        var skill = SkillData.Get(skillId);
        if (skill == null || skill.TransformModelId <= 0) return;
        WearLook(charId, skill.TransformModelId, scale ?? skill.TransformScale, skillId, fixedWeapon, skill);
    }

    internal Node3D? WearLook(int charId, int modelId, float scale, int skillId, int fixedWeapon, SkillData.Skill? skill)
    {
        var host = TransformHost(charId);
        if (host == null) return null;

        var scene = entityLooks.ResolveMobScene(modelId);
        if (scene == null)
        {
            GD.Print($"[transform] no baked model for {modelId} (skill {skillId})");
            return null;
        }

        RestoreOwnLook(charId, endingTransform: false);

        int race = charId == context.Self.CharId ? self.Race : context.Entities.Views.TryGetValue(charId, out var wearer) ? wearer.Race : 0;
        var (monster, anim) = entityLooks.MakeAnimatedEntity(scene, "", scale);
        monster.Name = StateLooks.TransformNodeName;
        PlateFollow.AdoptWornPlate(monster);
        monster.SetMeta(WornLook.WornAccessoriesMeta, skill != null && entityLooks.WearsAccessories(skill, host, race, monster));
        if (fixedWeapon > 0) monster.SetMeta(EntityLooksSystem.FixedWeaponMeta, fixedWeapon);
        host.AddChild(monster);

        if (charId == context.Self.CharId)
        {
            selfRigAnim ??= avatar.Anim;
            avatar.Anim = anim;
            avatar.Clip = null;
            self.WearTransform(skillId);
            DressSelfLook();
        }
        else if (context.Entities.Views.TryGetValue(charId, out var e))
        {
            e.TransformSkill = skillId;
            e.RigAnim ??= e.Anim;
            e.Anim = anim;
            e.Clip = null;
            DressEntityLook(e);
        }

        StateLooks.SetOwnLookVisible(host, false);
        ReapplySizeState(charId);
        entityPlates.FitPlateToLook(charId);
        return monster;
    }

    internal void RestoreOwnLook(int charId, bool endingTransform = true)
    {
        var host = TransformHost(charId);
        if (host == null) return;

        if (host.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) is not { } worn)
            return;

        host.RemoveChild(worn);
        worn.QueueFree();
        StateLooks.SetOwnLookVisible(host, true);
        ReapplySizeState(charId);
        entityPlates.FitPlateToLook(charId);

        if (charId == context.Self.CharId)
        {
            if (selfRigAnim != null) { avatar.Anim = selfRigAnim; selfRigAnim = null; }
            avatar.Clip = null;
            if (endingTransform) SelfTransformEnded?.Invoke(self.TransformSkill);
            self.EndTransform();
            DressSelfLook();
        }
        else if (context.Entities.Views.TryGetValue(charId, out var e))
        {
            e.TransformSkill = 0;
            if (e.RigAnim != null) { e.Anim = e.RigAnim; e.RigAnim = null; }
            e.Clip = null;
            DressEntityLook(e);
        }
    }

    private AnimationPlayer? selfRigAnim;

    private sealed class SizeTween
    {
        public Node3D Model = null!;
        public Vector3 From;
        public Vector3 To;
        public double Start;
    }

    private readonly Dictionary<int, SizeTween> sizeTweens = new();

    private readonly Dictionary<int, Node3D> sizeFx = new();

    private readonly Dictionary<int, int> sizeStates = new();

    private const int TransformedRace = 0;
    private readonly List<int> sizeDone = new();

    internal void SizeStateInit()
    {
        progress.StateChangeEvent += OnSizeState;
        ApplySizeState(context.Self.CharId, SizeState.OnSpawn(progress.SelfAbnormal, self.Race, context.Zone), instant: true);
    }

    internal void SizeStateDispose() => progress.StateChangeEvent -= OnSizeState;

    private void OnSizeState(int charId, int type, int value)
    {
        if (type == StateChange.Abnormal) ApplySizeState(charId, value, instant: false);
    }

    internal void SizeStateOnSpawn(int charId, int recorded, int race) =>
        ApplySizeState(charId, SizeState.OnSpawn(recorded, race, context.Zone), instant: true);

    internal void SizeStateForgetEntity(int id)
    {
        sizeTweens.Remove(id);
        sizeStates.Remove(id);
        if (sizeFx.Remove(id, out var fx) && GodotObject.IsInstanceValid(fx)) Fx.Free(fx);
    }

    private Node3D? SizeBodyOf(int charId) =>
        charId == context.Self.CharId ? context.Self.Visual : context.Entities.Views.TryGetValue(charId, out var e) && !e.IsNpc ? e.Body : null;

    private Node3D? SizeModelOf(Node3D body, int charId, out bool transformed)
    {
        transformed = false;
        if (TransformHost(charId)?.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) is { } worn)
        {
            transformed = true;
            return worn;
        }
        return body.GetNodeOrNull<Node3D>(ModelAnimation.ModelNodeName);
    }

    private void ReapplySizeState(int charId)
    {
        if (sizeStates.TryGetValue(charId, out int state)) ApplySizeState(charId, state, instant: true);
    }

    private void ApplySizeState(int charId, int state, bool instant)
    {
        if (SizeBodyOf(charId) is not { } body || !GodotObject.IsInstanceValid(body)) return;
        var model = SizeModelOf(body, charId, out bool transformed);
        int race = transformed ? TransformedRace : charId == context.Self.CharId ? self.Race : context.Entities.Views.TryGetValue(charId, out var e) ? e.Race : 0;
        if (SizeState.Target(state, race, context.Zone) != null) sizeStates[charId] = state;
        int shown = ZoneLookHolds(charId) ? SizeState.Normal : state;
        if (SizeState.Target(shown, race, context.Zone) is { } target && model != null)
        {
            var to = new Vector3(target.X, target.Y, target.Z);
            if (instant)
            {
                sizeTweens.Remove(charId);
                model.Scale = to;
            }
            else
            {
                sizeTweens[charId] = new SizeTween { Model = model, From = model.Scale, To = to, Start = context.Now };
            }
        }
        if (SizeState.AddsFx(shown) && !sizeFx.ContainsKey(charId)
            && Fx.NameForId(SizeState.GiantTargetFxId) is { } fxName
            && Fx.Spawn(fxName, body, Vector3.Zero, oneShot: false) is { } fx)
            sizeFx[charId] = fx;
        else if (SizeState.RemovesFx(shown) && sizeFx.Remove(charId, out var running) && GodotObject.IsInstanceValid(running))
            Fx.Free(running);
    }

    internal void SizeStateTick(double now)
    {
        if (sizeTweens.Count == 0) return;
        sizeDone.Clear();
        foreach (var (id, tween) in sizeTweens)
        {
            if (!GodotObject.IsInstanceValid(tween.Model))
            {
                sizeDone.Add(id);
                continue;
            }
            float t = SizeState.Progress(now - tween.Start);
            tween.Model.Scale = tween.From.Lerp(tween.To, t);
            if (t >= 1f) sizeDone.Add(id);
        }
        foreach (int id in sizeDone) sizeTweens.Remove(id);
    }

    private sealed class BlinkState
    {
        public double Since;
        public bool? Dimmed;
    }

    private readonly Dictionary<int, BlinkState> blinking = new();

    private readonly List<int> blinkGone = new();

    internal void RespawnBlinkInit()
    {
        progress.StateChangeEvent += OnRespawnBlinkState;
        if (progress.SelfAbnormal == RespawnBlink.Blinking) StartRespawnBlink(context.Self.CharId);
    }

    internal void RespawnBlinkDispose() => progress.StateChangeEvent -= OnRespawnBlinkState;

    private void OnRespawnBlinkState(int charId, int type, int value)
    {
        if (type != StateChange.Abnormal) return;
        if (value == RespawnBlink.Blinking) StartRespawnBlink(charId);
        else if (RespawnBlink.Ends(value) && blinking.Remove(charId)) EndRespawnBlink(charId);
    }

    internal void StartRespawnBlink(int charId) => blinking[charId] = new BlinkState { Since = context.Now };

    private void EndRespawnBlink(int charId)
    {
        if (stealth.StealthBodyOf(charId) is { } body) SetBlinkAlpha(body, 1f);
        if (context.Entities.StealthIds.Contains(charId)) stealth.ApplyStealthFade(charId, stealthed: true);
    }

    internal void RespawnBlinkTick(double now)
    {
        if (blinking.Count == 0) return;
        blinkGone.Clear();
        foreach (var (id, state) in blinking)
        {
            if (stealth.StealthBodyOf(id) is not { } body)
            {
                blinkGone.Add(id);
                continue;
            }
            bool dim = RespawnBlink.Dimmed(now - state.Since);
            if (dim == state.Dimmed) continue;
            state.Dimmed = dim;
            SetBlinkAlpha(body, RespawnBlink.AlphaAt(now - state.Since));
        }
        foreach (int id in blinkGone) blinking.Remove(id);
    }

    private static void SetBlinkAlpha(Node3D body, float alpha)
    {
        float transparency = Mathf.Clamp(1f - alpha, 0f, 1f);
        foreach (var node in StealthLook.StealthDescendants(body))
            if (node is GeometryInstance3D gi)
                Fx.SetTransparency(gi, transparency);
    }

    internal void SendChatGesture(int action)
    {
        if (context.Self.Dead || ZoneLooks.MutesChatGestures(context.Zone)) return;
        soccerRequests.Emote(action);
    }

    private void ApplyGesture(int charId, int action)
    {
        int clip = Gestures.ClipFor(action);
        if (clip < 0) return;
        entityAction.PlaySkillAction(charId, clip, Array.Empty<string>(), ModelAnimation.ActionRankPosture, restart: true);
    }

    internal void PlayCorpseJiggle(int id)
    {
        if (id == context.Self.CharId)
        {
            if (!context.Self.Dead || avatar.Anim == null) return;
            double own = ActionAnimation.PlayActionOn(avatar.Anim, Gestures.CorpseJiggleClip, Array.Empty<string>());
            if (own > 0) avatar.ActionUntil = context.Now + own;
            return;
        }

        if (!context.Entities.Views.TryGetValue(id, out var e) || !e.Dead || EntityActionSystem.Dying(e) || e.IsNpc || e.Anim == null) return;
        double len = ActionAnimation.PlayActionOn(e.Anim, Gestures.CorpseJiggleClip, Array.Empty<string>());
        if (len > 0) e.ActionUntil = context.Now + len;
    }

    private const int NpcGestureFirst = 12;
    private const int NpcGestureLast = 15;
    private const uint NpcGestureOneIn = 10;
    private const string IdleState = "idle";

    internal void TickNpcGesture(EntityView e, double now)
    {
        if (!e.IsNpc || e.IsMonster || e.Statue || e.Anim == null || e.Clip != IdleState)
        {
            e.IdleLoopAt = 0;
            return;
        }
        double loop = e.Anim.CurrentAnimationLength;
        if (loop <= 0) return;
        if (e.IdleLoopAt <= 0)
        {
            e.IdleLoopAt = now + loop;
            return;
        }
        if (now < e.IdleLoopAt) return;
        e.IdleLoopAt = now + loop;
        if (GD.Randi() % NpcGestureOneIn != 0) return;
        int gesture = NpcGestureFirst + (int)(GD.Randi() % (uint)(NpcGestureLast - NpcGestureFirst + 1));
        if (ModelAnimation.AnimationNameAt(e.Anim, gesture) == null) return;
        e.IdleLoopAt = 0;
        entityAction.PlaySkillAction(e.Id, gesture, System.Array.Empty<string>(), ModelAnimation.ActionRankStruck);
        e.NpcGesturing = true;
    }

    internal event Action<int>? SelfTransformEnded;

    internal event Action<int, bool>? GenieAuraChanged;

    internal HashSet<int> GenieUsers { get; } = new();

    internal void GenieAuraInit() => genie.GenieAuraEvent += OnGenieAura;

    internal void GenieAuraDispose() => genie.GenieAuraEvent -= OnGenieAura;

    private void OnGenieAura(int entityId, bool on)
    {
        if (entityId != context.Self.CharId) ShowGenieAura(entityId, on);
    }

    internal void ShowGenieAura(int entityId, bool on)
    {
        if (on) GenieUsers.Add(entityId);
        else GenieUsers.Remove(entityId);
        GenieAuraChanged?.Invoke(entityId, on);
    }

    internal HashSet<int> DevilForms { get; } = new();

    private int heldSelfTransform;

    private int ZoneLookSkill(int charId)
    {
        if (charId == context.Self.CharId) return ZoneLooks.SelfLook(context.Zone, self.Nation);
        int nation = context.Entities.Views.TryGetValue(charId, out var e) ? e.Nation : 0;
        return ZoneLooks.OtherLook(context.Zone, self.Nation, nation);
    }

    internal bool ZoneLookHolds(int charId) => ZoneLookSkill(charId) > 0;

    internal void HoldSelfTransform(int skillId)
    {
        if (skillId <= 0) SelfTransformEnded?.Invoke(heldSelfTransform);
        heldSelfTransform = skillId;
    }

    internal void ApplyZoneLook(int charId)
    {
        bool isSelf = charId == context.Self.CharId;
        int nation = isSelf ? self.Nation : context.Entities.Views.TryGetValue(charId, out var e) ? e.Nation : 0;
        int skill = ZoneLookSkill(charId);
        if (skill <= 0) return;
        float scale = ZoneLooks.LookScale(context.Zone, self.Nation, skill);
        int weapon = isSelf ? ZoneLooks.SelfWeapon(context.Zone, self.Nation) : ZoneLooks.OtherWeapon(context.Zone, self.Nation, nation);
        WearMonsterLook(charId, skill, scale > 0f ? scale : null, weapon);
    }

    internal void RestoreLookOrZoneLook(int charId)
    {
        RestoreOwnLook(charId);
        ApplyZoneLook(charId);
    }

    internal void ApplyDevilForm(int charId, bool on)
    {
        if (!on)
        {
            if (!DevilForms.Remove(charId)) return;
            RestoreOwnLook(charId, endingTransform: false);
            ApplyZoneLook(charId);
            return;
        }
        int race = charId == context.Self.CharId ? self.Race : context.Entities.Views.TryGetValue(charId, out var e) ? e.Race : 0;
        int model = ZoneLooks.DevilModel(race);
        if (model <= 0 || WearLook(charId, model, ZoneLooks.DevilScale, 0, 0, null) is not { } worn) return;
        DevilForms.Add(charId);
        if (worn.GetNodeOrNull<Node3D>(ModelAnimation.ModelNodeName) is not { } visual) return;
        visual.Scale = Vector3.One * ZoneLooks.DevilStartScale;
        visual.CreateTween().TweenProperty(visual, "scale", Vector3.One * ZoneLooks.DevilScale, ZoneLooks.DevilGrowSeconds);
    }
}
