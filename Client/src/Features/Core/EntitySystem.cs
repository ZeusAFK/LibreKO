using Godot;

namespace LibreKO.Features.Core;

internal sealed class EntitySystem : IFeature, IEntityAccess
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ClanSession clan;
    private readonly GmFxSession gmFx;
    private readonly SelfSession self;
    private readonly EntitiesSession entitiesSession;
    private readonly ItemsSession items;
    private readonly AchievementSession achievement;
    private readonly EntityPlatesSystem entityPlates;
    private readonly AudioSystem audio;
    private readonly EntityActionSystem entityAction;
    private readonly EntityStatesSystem entityStates;
    private readonly EntityLooksSystem entityLooks;
    private readonly StealthSystem stealth;
    private readonly ScenerySystem scenery;

    public EntitySystem(IWorldContext context, ClanSession clan, GmFxSession gmFx, SelfSession self, EntitiesSession entitiesSession, ItemsSession items, AchievementSession achievement, SelfAvatar avatar, EntityPlatesSystem entityPlates, AudioSystem audio, EntityActionSystem entityAction, EntityStatesSystem entityStates, EntityLooksSystem entityLooks, StealthSystem stealth, ScenerySystem scenery)
    {
        this.context = context;
        this.clan = clan;
        this.gmFx = gmFx;
        this.self = self;
        this.entitiesSession = entitiesSession;
        this.items = items;
        this.achievement = achievement;
        this.avatar = avatar;
        this.entityPlates = entityPlates;
        this.audio = audio;
        this.entityAction = entityAction;
        this.entityStates = entityStates;
        this.entityLooks = entityLooks;
        this.stealth = stealth;
        this.scenery = scenery;
    }

    public void Build()
    {
    }

    internal void Attach()
    {
        entitiesSession.EntitySpawnEvent += OnSpawn;
        entitiesSession.EntityMoveEvent += OnMove;
        entitiesSession.EntityRotateEvent += OnRotate;
        entitiesSession.EntityOutEvent += OnOut;
        entitiesSession.EntityHpEvent += OnEntityHp;
        items.LookChangeEvent += OnLookChange;
        achievement.TitleChangedEvent += OnEntityTitle;
    }

    internal string NameFor(int id)
    {
        if (id == context.Self.CharId) return "You";
        return Ents.TryGetValue(id, out var e) && !string.IsNullOrWhiteSpace(e.Name)
            ? entityPlates.ShownName(e)
            : "Unknown";
    }

    internal void TickEntities(double delta, double nowSec, Vector3 selfPos, Vector3 camPos)
    {
        float dt = (float)delta;
        float nameDist2 = NameTagDist * NameTagDist;
        animFull = animMid = animFar = animOff = closeEntities = 0;
        foreach (var e in Ents.Values)
        {
            float step = dt;
            if (e.Far)
            {
                e.FarAccum += delta;
                if (e.FarAccum < FarEntityInterval) continue;
                step = (float)e.FarAccum;
                e.FarAccum = 0;
            }
            Vector3 pos = e.Body.Position;
            var moveScope = Perf.Measure(Perf.Section.EntMove);
            float moveStep = step;
            if (e.JustEntered)
            {
                moveStep += e.MoveAccum;
                e.MoveAccum = 0f;
            }
            else if (!e.Far && (!e.OnScreen || e.AnimEvery >= Config.AnimFarEvery))
            {
                int moveEvery = e.OnScreen ? Config.MoveFarEvery : OffScreenMoveEvery;
                e.MoveAccum += step;
                bool due = ++e.MoveFrames % moveEvery == 0 || (e.OnScreen && e.MoveAccum >= OnScreenMoveFloor);
                if (!due) moveStep = 0f;
                else { moveStep = e.MoveAccum; e.MoveAccum = 0f; }
            }
            if (moveStep > 0f && !Perf.SkipMove && e.HasTarget && e.Speed > 0f && !e.Dead)
            {
                var moved = pos.MoveToward(e.Target, e.Speed * moveStep);
                if (moved != pos) { pos = moved; e.Body.Position = pos; }
            }

            moveScope.Dispose();
            using (Perf.Measure(Perf.Section.EntFacing)) EntityRig.TickEntityFacing(e, step);

            using (Perf.Measure(Perf.Section.EntLod)) UpdateEntityAnimLod(e, camPos, pos);
            using (Perf.Measure(Perf.Section.EntAudio)) audio.EntityStepTick(e, nowSec, selfPos, pos);
            if (e.MoveSwingAt >= 0 || e.MoveStrikeAt >= 0) audio.MoveAttackTick(e, nowSec);

            var plateScope = Perf.Measure(Perf.Section.EntPlates);
            if (e.NameTag != null)
            {
                bool near = !Perf.SkipPlates && pos.DistanceSquaredTo(selfPos) <= nameDist2 && !StealthLook.HiddenFromMe(e);
                EntityRig.SyncEntityPlates(e, near);
                if (near) PlateFollow.FollowHeadJoint(e.Body, e.PlateRoot, e.Head);
                if (e.Plate != null) e.Plate.SetVisible(near && !entityPlates.PlatesHidden);
                else if ((near && !entityPlates.PlatesHidden) != e.NameTag.Visible) e.NameTag.Visible = near && !entityPlates.PlatesHidden;
            }

            plateScope.Dispose();
            if (EntityActionSystem.Dying(e)) entityAction.TickDying(e, nowSec);
            else if (e.Dead && (e.PushStart >= 0 || e.DeathCryAt > 0)) entityAction.TickCorpse(e, nowSec, delta);
            if (e.Anim != null && !e.AnimPaused)
            {
                if (e.Dead) {  }
                else if (nowSec < e.ActionUntil && !(e.NpcGesturing && EntityRig.EntityMoving(e, pos))) {  }
                else
                {
                    if (e.NpcGesturing) { e.NpcGesturing = false; e.ActionUntil = 0; }
                    if (e.ActionClip != null) { e.ActionClip = null; e.Clip = null; ActionAnimation.ResetEntityActionScale(e); }
                    bool moving = Domain.NpcStride.Continues(EntityRig.EntityMoving(e, pos), nowSec, e.NpcStepAt);
                    ModelAnimation.PlayClip(e, moving
                        ? e.Backwards ? "walk_reverse" : e.Speed >= EntityRig.RunThresholdOf(e) ? "run" : "walk"
                        : e.Sitting ? "sit" : "idle");
                    entityStates.TickNpcGesture(e, nowSec);
                }
                if (e.AnimThrottled)
                {
                    e.AnimAccum += dt;
                    e.AnimFrames++;
                    if (e.JustEntered || (e.AnimFrames >= e.AnimEvery && e.AnimAccum >= e.AnimStep))
                    {
                        using (Perf.Measure(Perf.Section.EntStep))
                        {
                            double animStep = nowSec < e.ActionUntil ? e.AnimAccum * e.ActionTimeScale : e.AnimAccum;
                            if (e.Crowd != null) e.Crowd.Step(animStep, e.OnScreen);
                            else e.Anim.Advance(animStep);
                        }
                        if (e.WingAnims != null && e.OnScreen)
                            using (Perf.Measure(Perf.Section.EntWings))
                                foreach (var wing in e.WingAnims)
                                    wing?.Advance(e.AnimAccum);
                        e.AnimAccum = 0;
                        e.AnimFrames = 0;
                    }
                }
            }
        }
        Perf.EntityTiers = (Ents.Count, animFull, animMid, animFar, animOff);
        Perf.CloseEntities = closeEntities;
    }

    Node3D? IEntityAccess.BodyOf(int charId) => charId == context.Self.CharId ? avatar.Body : Ents.TryGetValue(charId, out var e) ? e.Body : null;
    float IEntityAccess.HeadHeight(int charId) => entityPlates.HeadHeightOf(charId);
    PlateStack? IEntityAccess.PlateOf(int charId) => entityPlates.PlateOf(charId);
    string IEntityAccess.NameOf(int charId) => NameFor(charId);
    void IEntityAccess.RefreshNameColours() => entityPlates.RefreshPlayerNameColours();
    void IEntityAccess.SpawnFxOn(int charId, string fxName, float y) => SpawnFxOn(charId, fxName, y);
    Node3D IEntityAccess.Layer => EntityLayer;
    void IEntityAccess.ReleaseCorpse(EntityView e) => entityAction.ReleaseCorpse(e);
    void IEntityAccess.GenieAura(int entityId, bool on) => entityStates.ShowGenieAura(entityId, on);
    void IEntityAccess.RefreshClanLine(int charId) => entityPlates.RefreshClanLine(charId);
    void IEntityAccess.RefreshSelfClanLine() => entityPlates.RefreshSelfClanLine();
    Vector3? IEntityAccess.WorldPosOf(int charId) => WorldPosOf(charId);

    internal Vector3? WorldPosOf(int id) =>
        id == context.Self.CharId ? context.Self.Body?.Position :
        Ents.TryGetValue(id, out var e) ? e.Body.Position : null;
    void IEntityAccess.RefreshPartyPlates() => entityPlates.RefreshPartyPlates();
    IEnumerable<EntityInfo> IEntityAccess.All() => EntityInfos();
    EntityInfo? IEntityAccess.Find(int charId) => FindEntity(charId);
    EntityRegistry IEntityAccess.Views => Ents;
    void IEntityAccess.Redress(EntityView e, bool force) => entityLooks.RedressEntity(e, force);
    IReadOnlySet<int> IEntityAccess.StealthIds => stealth.StealthIds;
    IEnumerable<EntityCape> IEntityAccess.PlayerCapes() => PlayerCapes();
    void IEntityAccess.SetCape(int charId, int capeId, int r, int g, int b) => SetEntityCape(charId, capeId, r, g, b);
    void IEntityAccess.SetClanMark(int clanId, int version, Texture2D? mark) => SetClanMark(clanId, version, mark);

    public void Dispose()
    {
        entitiesSession.EntitySpawnEvent -= OnSpawn;
        entitiesSession.EntityMoveEvent -= OnMove;
        entitiesSession.EntityRotateEvent -= OnRotate;
        entitiesSession.EntityOutEvent -= OnOut;
        entitiesSession.EntityHpEvent -= OnEntityHp;
        items.LookChangeEvent -= OnLookChange;
        achievement.TitleChangedEvent -= OnEntityTitle;
    }

    private const float TeleportSnap = 30.0f;

    internal Node3D EntityLayer { get; private set; } = null!;

    internal void AttachLayer(Node3D entityLayer) => EntityLayer = entityLayer;
    internal EntityRegistry Ents { get; } = new();

    internal IEnumerable<EntityInfo> EntityInfos() => Ents.Select(kv => EntityRig.EntityInfoOf(kv.Key, kv.Value));

    internal Node3D? BodyOf(int charId) => Ents.TryGetValue(charId, out var e) ? e.Body : null;

    internal EntityInfo? FindEntity(int charId) => Ents.TryGetValue(charId, out var e) ? EntityRig.EntityInfoOf(charId, e) : null;

    internal IEnumerable<EntityCape> PlayerCapes() =>
        Ents.Where(kv => !kv.Value.IsNpc).Select(kv => new EntityCape(kv.Key, kv.Value.Body, kv.Value.CapeId, kv.Value.CapeR, kv.Value.CapeG, kv.Value.CapeB, kv.Value.IsGm, kv.Value.Race, kv.Value.KnightsId));

    internal void SetEntityCape(int charId, int capeId, int r, int g, int b)
    {
        if (!Ents.TryGetValue(charId, out var e)) return;
        e.CapeId = capeId; e.CapeR = r; e.CapeG = g; e.CapeB = b;
    }

    internal void SetClanMark(int clanId, int version, Texture2D? mark)
    {
        foreach (var e in Ents.Values)
            if (e.KnightsId == clanId)
            {
                e.ClanMarkVersion = version;
                e.Plate?.SetMark(mark);
            }
    }

    internal const float NameTagDist = 10f;

    internal event Action<EntityView, EntitySnapshot>? EntitySpawned;

    internal event Action<EntityView, EntitySnapshot>? EntityRefreshed;

    internal event Action<int>? EntityRevived;

    internal event Action<int>? EntityRemoved;

    internal void OnSpawn(EntitySnapshot info)
    {
        if (info.Id == context.Self.CharId)
        {
            self.ApplyNationRanks(info.KnightsRank, info.PersonalRank);
            return;
        }
        if (!info.IsNpc && ZoneLooks.HidesOtherPlayers(context.Zone)) return;

        if (Ents.TryGetValue(info.Id, out var existing))
        {
            if (info.IsNpc && info.ObjectType == 0)
                ApplyPlainNpcBridgeState(existing, info.GateOpen);

            var rp = existing.Statue ? StatueSpot(info.X, info.Z, info.Y) : scenery.InOutGroundPos(info.IsNpc, info.X, info.Z, info.Y, existing.Lift, existing.BridgeFloor);
            float jump = existing.Body.Position.DistanceTo(rp);
            if (existing.Dead || info.Dead || jump > TeleportSnap || jump < EntityRig.MoveArriveEps)
            {
                existing.Body.Position = rp;
                existing.Speed = 0f;
            }
            else existing.Speed = Mathf.Max(jump / RelistGlideSeconds, 1f);
            existing.Target = rp;
            existing.HasTarget = true;
            existing.MapX = info.X; existing.MapZ = info.Z; existing.MapY = info.Y;
            if (existing.Attackable != info.Attackable)
            {
                existing.Attackable = info.Attackable;
                NpcGatesFeature.RefreshGateBlocker(existing);
            }

            if (!info.IsNpc)
            {
                entityStates.NoteSitting(info.Id, info.Sitting);
                entityStates.ApplyEntitySitVisual(existing, info.Sitting);
                StateLooks.ApplyEntityCombatStance(existing, entityStates.StanceIds.Contains(info.Id));
                existing.PersonalRank = info.PersonalRank;
                existing.KnightsRank = info.KnightsRank;
                existing.ClanFlag = info.ClanFlag;
                existing.ClanFame = info.ClanFame;
                entityPlates.RefreshClanLine(existing, info.Id);
                EntityRefreshed?.Invoke(existing, info);
            }
            if (info.Dead) entityAction.LayOutCorpse(existing);
            else if (existing.Dead)
            {
                EntityRevived?.Invoke(info.Id);
                existing.Dead = false;
                existing.DeathDueAt = 0;
                existing.CorpseRemoveAt = 0;
                existing.ActionUntil = 0; existing.ActionRank = 0;
                existing.ActionClip = null; existing.Clip = null;
                existing.Hp = existing.MaxHp;
                NpcGatesFeature.RefreshGateBlocker(existing);
            }
            return;
        }

        if (!pendingSpawns.ContainsKey(info.Id)) pendingOrder.Enqueue(info.Id);
        pendingSpawns[info.Id] = info;
    }

    private readonly Dictionary<int, EntitySnapshot> pendingSpawns = new();

    private readonly Queue<int> pendingOrder = new();
    private const int SpawnBuildBudget = 1;

    internal void ProcessSpawnQueue()
    {
        int built = 0;
        int examined = 0, pending = pendingOrder.Count;
        while (built < SpawnBuildBudget && examined < pending && pendingOrder.Count > 0)
        {
            int id = pendingOrder.Dequeue();
            examined++;
            if (!pendingSpawns.TryGetValue(id, out var info)) continue;
            if (Ents.ContainsKey(id)) { pendingSpawns.Remove(id); continue; }
            if (!entityLooks.SceneReadyFor(info)) { pendingOrder.Enqueue(id); continue; }
            pendingSpawns.Remove(id);
            BuildEntity(info);
            built++;
        }
    }

    private void BuildEntity(EntitySnapshot info)
    {
        var label = info.Name.Length > 0 ? info.Name : (info.IsNpc ? "NPC" : "Player");
        bool statue = info.IsNpc && RankerStatue.Is(info.NpcType);

        var watch = Diag.Watch();
        var sceneScope = Perf.Measure(Perf.Section.BuildScene);
        bool mapObject = info.ObjectType == NpcTypes.ObjectType.MapObject;
        var loadWatch = Diag.Watch();
        PackedScene? scene = mapObject ? null
            : info.IsNpc && !statue ? entityLooks.ResolveMobScene(info.ModelId)
                                    : entityLooks.ResolvePlayerScene(PlayerRig.LookRace(info.Race, info.Class));
        Diag.Slow($"model load {label} model={info.ModelId} race={info.Race}", loadWatch);
        float lift = scene != null || mapObject ? 0f : PlayerSystem.CapsuleHalf;
        var pos = statue ? StatueSpot(info.X, info.Z, info.Y) : scenery.InOutGroundPos(info.IsNpc, info.X, info.Z, info.Y, lift);

        Node3D body;
        AnimationPlayer? anim = null;
        CrowdAnimator? crowd = null;
        if (scene != null)
        {
            (body, anim) = statue
                ? entityLooks.MakeAnimatedEntity(scene, RankerStatueFeature.StatueOverheadName, RankerStatue.Scale)
                : entityLooks.MakeAnimatedEntity(scene, label, WeaponMounts.SizeScale(info.Size));
            crowd = CrowdAnimator.Create(anim, body, scene.ResourcePath);
            if (crowd != null) anim!.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        }
        else if (mapObject)
            body = ModelNodes.MakeMapObjectEntity(label);
        else
            body = ModelNodes.MakeEntity(ModelNodes.ColorFor(info), label);

        body.Position = pos;
        bool walks = anim != null && ModelAnimation.Pick(anim, ModelNodes.WalkClips) != null;
        float spawnYaw = NpcTypes.KeepsSpawnFacing(info.Dir, info.NpcType, walks) ? 180f - 2f * info.Dir : (info.Id * 137) % 360;
        if (scene != null)
            body.RotationDegrees = new Vector3(0, spawnYaw, 0);
        using (Perf.Measure(Perf.Section.BuildEnter)) EntityLayer.AddChild(body);
        sceneScope.Dispose();
        Dictionary<int, (Mesh? Mesh, Skin? Skin)> defaultParts = new();
        AnimationPlayer?[]? wingAnims = null;
        Flinch? flinch = null;
        if (scene != null)
        {
            using (Perf.Measure(Perf.Section.BuildGraft))
            {
                defaultParts = entityLooks.CapturePartDefaults(body);
                int lookRace = PlayerRig.LookRace(info.Race, info.Class);
                if (statue)
                    entityLooks.GraftEquipment(body, lookRace, info.Face, info.Gear, info.Hair, false);
                else if (!info.IsNpc)
                {
                    flinch = Flinch.Attach(body);
                    entityLooks.GraftEquipment(body, lookRace, info.Face, info.Gear, info.Hair, info.HelmetHidden, info.CostumeHidden);
                    entityLooks.DressEntity(body, info);
                }
            }
            using (Perf.Measure(Perf.Section.BuildGear))
            {
                string lookStem = info.IsNpc && !statue && entityLooks.MobIndex != null
                    && entityLooks.MobIndex.TryGetValue(info.ModelId, out var fs) ? fs : "";
                ModelNodes.AttachBuiltInPlugs(body, lookStem);
                entityLooks.AttachWeapons(body, info.Gear,
                    WeaponMounts.WeaponWearer(info.IsNpc, statue, info.Race, info.NpcId, info.Size, info.ModelId), info.NpcType);
                if (!info.IsNpc) ClanGauntletRig.AttachClanGauntlet(body, info.Race, info.ClanGrade, info.ClanRanking);
                if (info.IsNpc && !statue)
                    ModelNodes.AttachCharacterFxPlugs(body, lookStem);
                else if (!info.IsNpc)
                {
                    wingAnims = WingRig.AttachWings(body, CosmeticsFeature.ShownCospre(info.Gear), info.Race, context.Zone);
                    HandFxRig.AttachHandFx(body, CosmeticsFeature.ShownCospre(info.Gear), info.Race, context.Zone);
                }
            }
        }
        using var restScope = Perf.Measure(Perf.Section.BuildRest);
        EntityRig.ApplyEntityRenderCost(body);
        if (scene != null && Config.MergeCharacters) CharacterMerge.Apply(body);

        string modelStem = mapObject ? "(map object)"
            : scene == null ? "(capsule fallback)"
            : statue ? "(ranker statue)"
            : info.IsNpc ? (entityLooks.MobIndex != null && entityLooks.MobIndex.TryGetValue(info.ModelId, out var ms) ? ms + ".glb" : "(model)")
            : "(race rig)";
        var radii = EntityRig.BodyRadii(body);
        var ent = new EntityView
        {
            Id = info.Id,
            Body = body, Anim = anim, Crowd = crowd, WingAnims = wingAnims, Flinch = flinch,
            Target = pos, HasTarget = true, Speed = 0f, Lift = lift,
            MapX = info.X, MapZ = info.Z, MapY = info.Y,
            IsNpc = info.IsNpc, IsMonster = info.IsMonster, Statue = statue, Attackable = info.Attackable, ObjectType = info.ObjectType,
            Name = label, Level = info.Level, Radius = radii.Footprint, BoundRadius = radii.Bound,
            ModelId = info.ModelId, Size = info.Size, Nation = entityPlates.ShownNation(info),
            NpcId = info.NpcId, NpcType = info.NpcType,
            Race = info.Race, LookRace = PlayerRig.LookRace(info.Race, info.Class), Face = info.Face, Hair = info.Hair,
            CapeId = info.CapeId, CapeR = info.CapeR, CapeG = info.CapeG, CapeB = info.CapeB,
            KnightsId = info.KnightsId, AllianceId = info.AllianceId, ClanGrade = info.ClanGrade, ClanRanking = info.ClanRanking,
            ClanMarkVersion = info.ClanMarkVersion, ClanName = info.ClanName, ClanFlag = info.ClanFlag, ClanFame = info.ClanFame,
            IsGm = info.IsGm, HelmetHidden = info.HelmetHidden, CostumeHidden = info.CostumeHidden, PersonalRank = info.PersonalRank, KnightsRank = info.KnightsRank,
            Gear = info.Gear.Length > 0 ? (int[])info.Gear.Clone() : System.Array.Empty<int>(),
            DefaultParts = defaultParts,
            SpawnX = info.X, SpawnZ = info.Z, SpawnY = info.Y, SpawnDir = info.Dir,
            ModelStem = modelStem, TargetYaw = spawnYaw,
        };
        Ents[info.Id] = ent;
        if (info.GenieActive) entityStates.ShowGenieAura(info.Id, true);
        if (info.TransformSkill > 0 && !info.IsNpc && !entityStates.ZoneLookHolds(info.Id)) entityStates.WearMonsterLook(info.Id, info.TransformSkill);
        else if (!info.IsNpc) entityStates.ApplyZoneLook(info.Id);

        if (NpcGatesFeature.IsBreakableGate(info)) ent.GateBlocker = NpcGatesFeature.AttachGateBlocker(body);
        NpcGatesFeature.RefreshGateBlocker(ent);
        if (info.IsNpc && info.ObjectType == 0)
            ApplyPlainNpcBridgeState(ent, info.GateOpen);

        if (!info.IsNpc)
        {
            ent.TargetYaw = 180f - info.Dir;
            body.RotationDegrees = new Vector3(0, ent.TargetYaw, 0);
            if (info.Sitting) entityStates.NoteSitting(info.Id, true);
            ent.Sitting = entityStates.SittingIds.Contains(info.Id);
            if (ent.Sitting) ent.Hover = WingRig.ResolveHoverClips(body, anim);
            ent.CombatStance = entityStates.StanceIds.Contains(info.Id);
            ent.Gathering = info.Gathering;
            ent.GatherFishing = info.GatherFishing;
        }
        var nameLabel = NodeSearch.FindFirst<Label3D>(body);
        if (nameLabel != null)
        {
            NamePlate.Tint(nameLabel, entityPlates.NameColor(info));
            if (!info.IsNpc && entityPlates.ZoneNameText(info) is > 0 and var hidden)
                nameLabel.Text = ItemData.Text(hidden, nameLabel.Text);
            nameLabel.Visible = false;
            ent.OriginalTagY = nameLabel.Position.Y;
            PlateFollow.SeekHeadJoint(body, ent.Head);
            if ((ent.Sitting || entityPlates.HasStall(info.Id)) && !ent.Head.Follows)
            {
                nameLabel.Position = new Vector3(nameLabel.Position.X, NamePlate.SittingHeight, nameLabel.Position.Z);
            }
            ent.NameTag = nameLabel;
            if (entityStates.IsWearingTransformLook(info.Id)) entityPlates.FitPlateToLook(info.Id);
            ent.PlateRoot = nameLabel.GetParent() as Node3D;
            ent.Plate = new PlateStack(nameLabel);
            if (info.IsNpc) ent.Plate.SetClan(info.ClanName);
            else entityPlates.RefreshClanLine(ent, info.Id);
            ent.Plate.SetTitle(EntityRig.TitleTextOf(info.TitleId, context.Zone));
            ent.Plate.SetSeeking(SeekingParty.Line((SeekingPartyState)info.NeedParty, info.Level, ItemData.Text) ?? "", EntityStatesSystem.SeekingPartyColour);
            ent.Plate.SetLeader(info.PartyLeader);
            if (!info.IsNpc && entityPlates.AtSiegeWar) entityPlates.ApplySiegeName(ent);
        }
        ent.RoleFx = NpcRoleFx.Spawn(ent);
        if (info.Invisible) stealth.StealthOnSpawn(info.Id, (InvisibilityType)info.Invisibility);
        if (!info.IsNpc && info.Appearance == RespawnBlink.Blinking) entityStates.StartRespawnBlink(info.Id);
        if (!info.IsNpc) entityStates.SizeStateOnSpawn(info.Id, info.Appearance, info.Race);
        if (crowd != null) ent.AnimActive = false;
        if (info.Dead) entityAction.LayOutCorpse(ent);
        else ModelAnimation.PlayClip(ent, "idle");
        if (crowd != null && !info.Dead)
        {
            crowd.TakeOver();
            crowd.Step(0);
        }
        EntitySpawned?.Invoke(ent, info);
        Diag.Slow($"entity {label} model={info.ModelId} npc={info.IsNpc}", watch);
    }

    public System.Collections.Generic.List<string> MapObjectReport()
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var kv in Ents)
        {
            var e = kv.Value;
            if (!e.IsNpc || e.ModelStem != "(map object)") continue;
            lines.Add($"MAPOBJECT id={kv.Key} proto={e.NpcId} model={e.ModelId} " +
                      $"tNpc={e.NpcType} hit={e.Attackable} \"{e.Name}\"");
        }
        if (lines.Count == 0) lines.Add("MAPOBJECT none in range");
        return lines;
    }

    internal bool BlockedByUnits(Vector3 from, Vector3 to)
    {
        if (self.IsGm) return false;
        var before = new Vector2(from.X, from.Z);
        var after = new Vector2(to.X, to.Z);
        foreach (var e in Ents.Values)
        {
            if (e.Dead || !e.Attackable || !GodotObject.IsInstanceValid(e.Body)) continue;
            var at = new Vector2(e.Body.Position.X, e.Body.Position.Z);
            if (at.DistanceSquaredTo(after) > UnitBlocking.Reach * UnitBlocking.Reach) continue;
            float height = e.IsNpc ? SkillFxRig.EntityLocalBox(e).Size.Y * e.Body.Scale.Y : UnitBlocking.PlayerHeight;
            if (UnitBlocking.Blocks(before, after, at, UnitBlocking.PlayerHeight, height)) return true;
        }
        return false;
    }

    private const float MoveFacingEpsSq = 0.04f;
    private const float MoveFacingWarpSq = 900f;
    private const float MovePlaybackSeconds = 1.5f;
    private const float MovePlaybackDamp = 0.85f;
    private const float NpcMovePlaybackSeconds = 0.3f;
    private const float RelistGlideSeconds = 0.5f;

    internal void OnMove(EntityMove move)
    {
        if (move.Id == context.Self.CharId) return;
        if (!Ents.TryGetValue(move.Id, out var e)) return;
        if (e.Dead && e.IsNpc) return;
        if (e.Dead)
        {
            e.Dead = false; e.DeathDueAt = 0; e.CorpseRemoveAt = 0; e.ActionUntil = 0; e.ActionClip = null; e.Clip = null;
            e.PushStart = -1; e.DeathCryAt = 0; e.KillStriker = -1;
            NpcGatesFeature.RefreshGateBlocker(e);
        }

        var dest = e.Body.Position.DistanceSquaredTo(context.Self.Body.Position) <= EntityProbeDist * EntityProbeDist
            ? scenery.GroundPosFor(e.IsNpc, move.X, move.Z, move.Y, e.Lift)
            : context.Terrain.GroundPos(move.X, move.Z, move.Y, e.Lift);
        float dxMap = move.X - e.MapX, dzMap = move.Z - e.MapZ;
        float mapMove2 = dxMap * dxMap + dzMap * dzMap;
        if (move.Travelling && mapMove2 > MoveFacingEpsSq)
            EntityRig.CancelEntityPosture(e, context.Now);
        if (move.Travelling && mapMove2 > MoveFacingEpsSq && mapMove2 < MoveFacingWarpSq)
        {
            bool backwards = move.Speed < 0f;
            EntityRig.FaceEntity(
                e,
                180f - (backwards ? Coord.MapHeading(-dxMap, -dzMap) : Coord.MapHeading(dxMap, dzMap)),
                immediate: false);
        }

        e.MapX = move.X; e.MapZ = move.Z; e.MapY = move.Y;

        if (move.Travelling)
            e.Backwards = move.Speed < 0f;

        if (e.Body.Position.DistanceTo(dest) > TeleportSnap)
        {
            e.Body.Position = dest;
            e.Target = dest;
            e.HasTarget = true;
            e.Speed = 0f;
            e.NpcStepAt = NpcStride.NoStep;
            return;
        }

        if (!move.Travelling)
        {
            e.Target = dest;
            e.HasTarget = true;
            return;
        }

        var delta = dest - e.Body.Position;
        float dist = new Vector2(delta.X, delta.Z).Length();
        e.Target = dest;
        e.HasTarget = true;
        if (e.IsNpc && e.NpcId == Domain.Soccer.BallProto)
        {
            e.Speed = dist / Domain.Soccer.BallTravelSeconds;
            return;
        }
        float playback = dist / MovePlaybackSeconds * MovePlaybackDamp;
        if (e.IsNpc && move.Speed > 0.01f)
        {
            playback = Mathf.Max(move.Speed, dist / NpcMovePlaybackSeconds);
            e.NpcStepAt = context.Now;
        }
        e.Speed = Mathf.Abs(move.Speed) > 0.01f ? playback : 0f;
    }

    private const float EntityProbeDist = 64f;

    private const float AnimAlwaysDist = 22f;
    internal const int OffScreenMoveEvery = 6;
    internal const float OnScreenMoveFloor = 1f / 30f;
    private const float AnimViewMargin = 4f;
    internal const double FarEntityInterval = 0.1;
    private readonly ViewFrustum frustum = new();
    private int animFull;
    private int animMid;
    private int animFar;
    private int animOff;
    private int closeEntities;

    internal void RefreshFrustum() => frustum.Refresh(context.Camera);

    private bool InView(Vector3 pos) => frustum.Contains(pos, AnimViewMargin);

    private readonly List<EntityView> animRanking = new();

    private static readonly System.Comparison<EntityView> ByCamDist = (a, b) => a.CamDist2.CompareTo(b.CamDist2);

    internal void RankEntityAnimation()
    {
        animRanking.Clear();
        foreach (var e in Ents.Values)
            if (e.Anim != null && !e.Far) animRanking.Add(e);
        animRanking.Sort(ByCamDist);
        for (int i = 0; i < animRanking.Count; i++) animRanking[i].AnimRank = i;
    }

    internal void UpdateEntityAnimLod(EntityView e, Vector3 camPos, Vector3 pos)
    {
        float d2 = pos.DistanceSquaredTo(camPos);
        e.CamDist2 = d2;
        if (d2 <= Perf.CloseEntityDist * Perf.CloseEntityDist) closeEntities++;
        float pause2 = EntityRig.EntityRenderDist * EntityRig.EntityRenderDist;
        float resume2 = (EntityRig.EntityRenderDist - 12f) * (EntityRig.EntityRenderDist - 12f);
        e.Far = e.Far ? d2 > resume2 : d2 > pause2;
        bool inView = InView(pos);
        e.JustEntered = inView && !e.OnScreen;
        e.OnScreen = inView;
        if (e.Anim == null) return;
        bool offScreen = d2 > AnimAlwaysDist * AnimAlwaysDist && !inView;
        e.AnimPaused = !Config.EntityAnim || Perf.SkipPoses || e.Far || offScreen;
        double step = 0;
        int every = 1;
        if (!e.AnimPaused)
        {
            double mid = 1.0 / Config.AnimMidHz, far = 1.0 / Config.AnimThrottleHz;
            float reach = Mathf.Clamp(Config.ViewDistance, Config.ViewDistanceMin, Config.ViewDistanceMax);
            float midDist = Config.AnimMidDist * reach, fullDist = Config.AnimFullDist * reach;
            if (d2 > midDist * midDist) step = far;
            else if (d2 > fullDist * fullDist) step = mid;
            if (e.AnimRank >= Config.AnimFullCount + Config.AnimMidCount) step = Mathf.Max(step, far);
            else if (e.AnimRank >= Config.AnimFullCount) step = Mathf.Max(step, mid);
            every = step == 0 ? 1 : step >= far ? Config.AnimFarEvery : Config.AnimMidEvery;
        }
        e.AnimStep = step;
        e.AnimEvery = every;
        e.AnimThrottled = !e.AnimPaused && (step > 0 || e.Crowd != null);
        if (e.AnimPaused) animOff++;
        else if (step == 0) animFull++;
        else if (step <= 1.0 / Config.AnimMidHz) animMid++;
        else animFar++;
        bool active = !e.AnimPaused && !e.AnimThrottled;
        if (e.AnimActive != active)
        {
            e.AnimActive = active;
            e.Anim.CallbackModeProcess = active
                ? AnimationMixer.AnimationCallbackModeProcess.Idle
                : AnimationMixer.AnimationCallbackModeProcess.Manual;
            if (!active) e.Crowd?.TakeOver();
            else e.Crowd?.Resume();
        }
    }

    internal void OnRotate(int id, float dir)
    {
        if (id == context.Self.CharId) return;
        if (!Ents.TryGetValue(id, out var e) || e.Dead) return;
        EntityRig.FaceEntity(e, 180f - dir, immediate: false);
    }

    internal void OnLookChange(int id, int lookSlot, int itemId, short durability)
    {
        if (id == context.Self.CharId) return;
        if (!Ents.TryGetValue(id, out var e) || e.IsNpc) return;
        int gearIndex = EntityRig.GearIndexForLookSlot(lookSlot);
        if (gearIndex < 0) return;

        if (e.Gear.Length < InventoryConstants.VisualSlotCount)
            System.Array.Resize(ref e.Gear, InventoryConstants.VisualSlotCount);
        if (e.Gear[gearIndex] == itemId) return;
        e.Gear[gearIndex] = itemId;

        entityLooks.RedressWhenGearLoaded(e);
    }

    private const double PlayerCorpseFade = 10.0;

    internal void OnOut(int id)
    {
        pendingSpawns.Remove(id);
        if (Ents.TryGetValue(id, out var corpse) && corpse.Dead && !corpse.IsNpc)
        {
            if (corpse.CorpseRemoveAt <= 0) corpse.CorpseRemoveAt = context.Now + PlayerCorpseFade;
            return;
        }
        RemoveEntity(id);
    }

    internal void RemoveEntity(int id)
    {
        if (!Ents.TryGetValue(id, out var e)) return;
        if (e.BridgeFloor != null && GodotObject.IsInstanceValid(e.BridgeFloor))
            e.BridgeFloor.QueueFree();
        e.Crowd?.Release();
        e.Body.QueueFree();
        Ents.Remove(id);
        entityStates.StateVisualForgetEntity(id);
        entityStates.SizeStateForgetEntity(id);
        stealth.StealthForgetEntity(id);
        EntityRemoved?.Invoke(id);
    }

    private void ApplyEntityTitle(EntityView ent, int titleId)
    {
        ent.Plate?.SetTitle(EntityRig.TitleTextOf(titleId, context.Zone));
    }

    private void OnEntityTitle(int charId, int titleId)
    {
        if (charId == context.Self.CharId)
        {
            entityPlates.SelfPlate()?.SetTitle(EntityRig.TitleTextOf(titleId, context.Zone));
            return;
        }

        if (Ents.TryGetValue(charId, out var ent))
            ApplyEntityTitle(ent, titleId);
    }

    internal event Action<EntityView>? EntityHpChanged;

    internal event Action<int>? EntityStruck;

    internal void OnEntityHp(int id, int hp, int maxHp, int damage)
    {
        if (!Ents.TryGetValue(id, out var e)) return;
        int old = e.MaxHp > 0 ? e.Hp : hp;
        e.Hp = hp; e.MaxHp = maxHp;
        EntityHpChanged?.Invoke(e);
        int shown = damage != 0 ? Mathf.Abs(damage) : Mathf.Max(0, old - hp);
        if (shown > 0)
        {
            context.Log.Add(TextTemplate.Fill(GameText.System(CombatLogLines.TargetDamageText, "%s received %d damage"), entityPlates.ShownName(e), shown),
                CombatLogKind.Damage);
            context.Floaters?.Damage(id, shown);
            if (hp > 0 && !e.Dead)
            {
                audio.Struck(id);
                EntityStruck?.Invoke(id);
            }
        }
        else if (hp - old > 0 && old > 0)
        {
            context.Log.Add($"{e.Name} recovered {hp - old:n0} HP.", CombatLogKind.Recovery);
            context.Floaters?.Cure(id, hp - old);
        }
    }

    public const float BridgeLoweredPitch = -110.0f;
    private const float BridgeTiltDegreesPerSecond = 45f;

    private void TiltBridge(Node3D? visual, float fromPitch, float toPitch)
    {
        if (visual == null) return;
        visual.RotationDegrees = new Vector3(fromPitch, 0, 0);
        float span = Mathf.Abs(toPitch - fromPitch);
        if (span <= 0f) return;
        var tween = visual.CreateTween();
        tween.TweenProperty(visual, "rotation_degrees:x", toPitch, span / BridgeTiltDegreesPerSecond);
    }

    private void ApplyPlainNpcBridgeState(EntityView e, int gateOpen)
    {
        e.GateOpen = gateOpen;
        if (e.BridgeVisual == null || !GodotObject.IsInstanceValid(e.BridgeVisual))
            e.BridgeVisual = e.Body.GetNodeOrNull<Node3D>(ModelAnimation.ModelNodeName) ?? e.Body.FindChild(ModelAnimation.ModelNodeName, true, false) as Node3D;

        if (gateOpen == 2)
        {
            float startPitch = e.BridgeVisual?.RotationDegrees.X ?? 0f;
            if (e.BridgeVisual != null)
                e.BridgeVisual.RotationDegrees = new Vector3(BridgeLoweredPitch, 0, 0);

            if (e.BridgeFloor == null || !GodotObject.IsInstanceValid(e.BridgeFloor))
            {
                e.Body.ForceUpdateTransform();
                e.BridgeVisual?.ForceUpdateTransform();
                var faces = new List<Vector3>();
                EntityRig.CollectFaces(e.Body, faces);
                if (faces.Count > 0)
                {
                    var toBody = e.Body.GlobalTransform.AffineInverse();
                    for (int i = 0; i < faces.Count; i++) faces[i] = toBody * faces[i];
                    var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
                    shape.SetFaces(faces.ToArray());
                    var floor = new StaticBody3D { Name = "BridgeFloor", CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
                    floor.AddChild(new CollisionShape3D { Shape = shape });
                    e.Body.AddChild(floor);
                    e.BridgeFloor = floor;
                }
            }
            else
            {
                e.BridgeFloor.CollisionLayer = CollisionLayers.World;
            }
            TiltBridge(e.BridgeVisual, startPitch, BridgeLoweredPitch);
        }
        else if (gateOpen == 0)
        {
            TiltBridge(e.BridgeVisual, e.BridgeVisual?.RotationDegrees.X ?? 0f, 0f);

            if (e.BridgeFloor != null && GodotObject.IsInstanceValid(e.BridgeFloor))
            {
                e.BridgeFloor.CollisionLayer = 0;
            }
        }
    }

    internal Vector3 StatueSpot(float mapX, float mapZ, float serverY) =>
        context.Terrain.GroundPos(mapX, mapZ, serverY, RankerStatue.PedestalLift);

    internal void SpawnFxOn(int id, string fxName, float y)
    {
        Node3D? parent = id == context.Self.CharId ? context.Self.Body : (context.Entities.Views.TryGetValue(id, out var e) ? e.Body : null);
        if (parent == null) return;
        Fx.Spawn(fxName, parent, new Vector3(0, y, 0), oneShot: true, deferParts: true);
    }
}
