using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const float RunThreshold = 3.0f;
    private const double AnimBlend = 0.2;
    private const float TeleportSnap = 30.0f;
    private const float MoveArriveEps = 0.02f;

    private sealed class Ent
    {
        public Node3D Body = null!;
        public bool Infiltrating;
        public StaticBody3D? Collider;
        public StaticBody3D? GateBlocker;
        public Label3D? NameTag;
        public PlateStack? Plate;
        public Node3D? IndicatorFx;
        public string IndicatorName = "";
        public Node3D? RoleFx;
        public AnimationPlayer? Anim;
        public CrowdAnimator? Crowd;
        public AnimationPlayer? RigAnim;
        public Flinch? Flinch;
        public string? Clip;
        public AnimationPlayer?[]? WingAnims;
        public string?[] WingClips = new string?[WingSlotCount];
        public double ActionUntil;
        public int ActionRank;
        public int ActionAnim = NoActionAnim;
        public string? ActionClip;
        public bool Gathering;
        public bool GatherFishing;
        public double CombatStanceUntil;
        public bool CombatStance;
        public bool Dead;
        public double CorpseRemoveAt;
        public Vector3 Target;
        public bool HasTarget;
        public float Speed;
        public float Lift;
        public float KoX, KoZ, KoY;
        public bool IsNpc, IsMonster;
        public bool Attackable;
        public float TargetYaw;
        public bool Backwards;
        public bool Sitting;
        public float OriginalTagY;
        public Aabb? HitFxBox;
        public float Radius = 0.9f;
        public float BoundRadius = 1.0f;
        public string Name = "";
        public int Id;
        public int GateOpen;
        public Node3D? BridgeVisual;
        public StaticBody3D? BridgeFloor;
        public int ObjectType;
        public int Level;
        public int Hp, MaxHp;
        public int ModelId, Size, Nation, NpcId, NpcType;
        public int Race, Face, Hair;
        public int CapeId, CapeR, CapeG, CapeB, KnightsId, ClanGrade, ClanRanking;
        public bool IsGm;
        public bool HelmetHidden;
        public int[] Gear = System.Array.Empty<int>();
        public Dictionary<int, (Mesh? Mesh, Skin? Skin)> DefaultParts = new();
        public float SpawnX, SpawnZ, SpawnY, SpawnDir;
        public string ModelStem = "";
        public bool AnimPaused;
        public bool AnimThrottled;
        public double AnimStep;
        public int AnimEvery = 1;
        public int AnimFrames;
        public int AnimRank;
        public float CamDist2;
        public bool Far;
        public bool OnScreen = true;
        public bool JustEntered;
        public double FarAccum;
        public bool ColliderNear;
        public Vector3 ColliderAt;
        public Node3D? PlateRoot;
        public bool PlateNear = true;
        public int MoveFrames;
        public float MoveAccum;
        public bool? AnimActive;
        public float? Yaw;
        public string? StepClip;
        public double StepPos;
        public int StepPhase;
        public int StrikeTarget = -1;
        public double AnimAccum;
    }

    private Node3D _entities = null!;
    private readonly Dictionary<int, Ent> _ents = new();

    private const float NameTagDist = 10f;
    private void OnSpawn(EntitySnapshot info)
    {
        if (info.Id == _myId)
            return;
        if (info.IsNpc && info.ObjectType != 0) NoteGateState(info.X, info.Z, info.GateOpen != 0);

        if (_ents.TryGetValue(info.Id, out var existing))
        {
            if (info.IsNpc && info.ObjectType == 0)
                ApplyPlainNpcBridgeState(existing, info.GateOpen);

            var rp = EntityGroundPos(info.X, info.Z, info.Y, existing.Lift);
            float jump = existing.Body.Position.DistanceTo(rp);
            if (existing.Dead || info.Dead || jump > TeleportSnap || jump < MoveArriveEps)
            {
                existing.Body.Position = rp;
                existing.Speed = 0f;
            }
            else existing.Speed = Mathf.Max(jump / RelistGlideSeconds, 1f);
            existing.Target = rp;
            existing.HasTarget = true;
            existing.KoX = info.X; existing.KoZ = info.Z; existing.KoY = info.Y;
            if (existing.Attackable != info.Attackable)
            {
                existing.Attackable = info.Attackable;
                RefreshEntityCollision(existing);
            }

            if (!info.IsNpc)
            {
                if (info.Sitting) _sittingIds.Add(info.Id); else _sittingIds.Remove(info.Id);
                ApplyEntitySitVisual(existing, info.Sitting);
                ApplyEntityCombatStance(existing, _stanceIds.Contains(info.Id));
                if (info.Gathering) BeginRemoteGather(info.Id, info.GatherFishing);
                else if (existing.Gathering) EndRemoteGather(info.Id);
            }
            if (info.Dead) LayOutCorpse(existing);
            else if (existing.Dead)
            {
                existing.Dead = false;
                existing.CorpseRemoveAt = 0;
                existing.ActionUntil = 0; existing.ActionRank = 0;
                existing.ActionClip = null; existing.Clip = null;
                existing.Hp = existing.MaxHp;
                RefreshEntityCollision(existing);
            }
            return;
        }

        if (!_pendingSpawns.ContainsKey(info.Id)) _pendingOrder.Enqueue(info.Id);
        _pendingSpawns[info.Id] = info;
    }

    private readonly Dictionary<int, EntitySnapshot> _pendingSpawns = new();
    private readonly Queue<int> _pendingOrder = new();
    private const int SpawnBuildBudget = 1;

    private void ProcessSpawnQueue()
    {
        int built = 0;
        int examined = 0, pending = _pendingOrder.Count;
        while (built < SpawnBuildBudget && examined < pending && _pendingOrder.Count > 0)
        {
            int id = _pendingOrder.Dequeue();
            examined++;
            if (!_pendingSpawns.TryGetValue(id, out var info)) continue;
            if (_ents.ContainsKey(id)) { _pendingSpawns.Remove(id); continue; }
            if (!SceneReadyFor(info)) { _pendingOrder.Enqueue(id); continue; }
            _pendingSpawns.Remove(id);
            BuildEntity(info);
            built++;
        }
    }

    private void BuildEntity(EntitySnapshot info)
    {
        var label = info.Name.Length > 0 ? info.Name : (info.IsNpc ? "NPC" : "Player");

        var watch = Diag.Watch();
        var sceneScope = Perf.Measure(Perf.Section.BuildScene);
        bool mapObject = info.ObjectType == NpcTypes.ObjectType.MapObject;
        var loadWatch = Diag.Watch();
        PackedScene? scene = mapObject ? null
            : info.IsNpc ? ResolveMobScene(info.ModelId)
                         : ResolvePlayerScene(info.Race);
        Diag.Slow($"model load {label} model={info.ModelId} race={info.Race}", loadWatch);
        float lift = scene != null || mapObject ? 0f : CapsuleHalf;
        var pos = EntityGroundPos(info.X, info.Z, info.Y, lift);

        Node3D body;
        AnimationPlayer? anim = null;
        CrowdAnimator? crowd = null;
        if (scene != null)
        {
            (body, anim) = MakeAnimatedEntity(scene, label, info.Size > 0 ? info.Size / 100f : 1f);
            crowd = CrowdAnimator.Create(anim, body, scene.ResourcePath);
            if (crowd != null) anim!.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        }
        else if (mapObject)
            body = MakeMapObjectEntity(label);
        else
            body = MakeEntity(ColorFor(info), label);

        body.Position = pos;
        float spawnYaw = info.Dir != 0 ? 180f - 2f * info.Dir : (info.Id * 137) % 360;
        if (scene != null)
            body.RotationDegrees = new Vector3(0, spawnYaw, 0);
        using (Perf.Measure(Perf.Section.BuildEnter)) _entities.AddChild(body);
        sceneScope.Dispose();
        Dictionary<int, (Mesh? Mesh, Skin? Skin)> defaultParts = new();
        AnimationPlayer?[]? wingAnims = null;
        Flinch? flinch = null;
        if (scene != null)
        {
            using (Perf.Measure(Perf.Section.BuildGraft))
            {
                defaultParts = CapturePartDefaults(body);
                if (!info.IsNpc)
                {
                    flinch = Flinch.Attach(body);
                    GraftEquipment(body, info.Race, info.Face, info.Gear, info.Hair, info.HelmetHidden);
                    DressEntityCape(body, info);
                }
            }
            using (Perf.Measure(Perf.Section.BuildGear))
            {
                AttachWeapons(body, info.Gear, info.NpcType, info.NpcId);
                if (!info.IsNpc) AttachClanGauntlet(body, info.Race, info.ClanGrade, info.ClanRanking);
                if (info.IsNpc)
                {
                    string fxStem = _mobIndex != null && _mobIndex.TryGetValue(info.ModelId, out var fs)
                        ? fs : "";
                    AttachCharacterFxPlugs(body, fxStem);
                }
                else
                {
                    wingAnims = AttachWings(body, info.Gear, info.Race, _zone);
                    AttachHandFx(body, info.Gear, info.Race, _zone);
                }
            }
        }
        using var restScope = Perf.Measure(Perf.Section.BuildRest);
        ApplyEntityRenderCost(body);
        if (scene != null && Config.MergeCharacters) CharacterMerge.Apply(body);

        string modelStem = mapObject ? "(map object)"
            : scene == null ? "(capsule fallback)"
            : info.IsNpc ? (_mobIndex != null && _mobIndex.TryGetValue(info.ModelId, out var ms) ? ms + ".glb" : "(model)")
            : "(race rig)";
        var radii = BodyRadii(body);
        var ent = new Ent
        {
            Id = info.Id,
            Body = body, Anim = anim, Crowd = crowd, WingAnims = wingAnims, Flinch = flinch,
            Target = pos, HasTarget = true, Speed = 0f, Lift = lift,
            KoX = info.X, KoZ = info.Z, KoY = info.Y,
            IsNpc = info.IsNpc, IsMonster = info.IsMonster, Attackable = info.Attackable, ObjectType = info.ObjectType,
            Name = label, Level = info.Level, Radius = radii.Footprint, BoundRadius = radii.Bound,
            ModelId = info.ModelId, Size = info.Size, Nation = info.Nation,
            NpcId = info.NpcId, NpcType = info.NpcType,
            Race = info.Race, Face = info.Face, Hair = info.Hair,
            CapeId = info.CapeId, CapeR = info.CapeR, CapeG = info.CapeG, CapeB = info.CapeB,
            KnightsId = info.KnightsId, ClanGrade = info.ClanGrade, ClanRanking = info.ClanRanking,
            IsGm = info.IsGm, HelmetHidden = info.HelmetHidden,
            Gear = info.Gear.Length > 0 ? (int[])info.Gear.Clone() : System.Array.Empty<int>(),
            DefaultParts = defaultParts,
            SpawnX = info.X, SpawnZ = info.Z, SpawnY = info.Y, SpawnDir = info.Dir,
            ModelStem = modelStem, TargetYaw = spawnYaw,
        };
        _ents[info.Id] = ent;
        ApplyGmFx(info.Id, Net.I.GmFxVisible(info.Id, info.IsGm));

        ent.Collider = AttachBodyCollider(body, info.IsNpc ? ent.Radius : PlayerCapsuleRadius, lift);
        if (IsBreakableGate(info)) ent.GateBlocker = AttachGateBlocker(body);
        RefreshEntityCollision(ent);
        if (info.IsNpc && info.ObjectType == 0)
            ApplyPlainNpcBridgeState(ent, info.GateOpen);

        if (!info.IsNpc)
        {
            ent.TargetYaw = 180f - info.Dir;
            body.RotationDegrees = new Vector3(0, ent.TargetYaw, 0);
            if (info.Sitting) _sittingIds.Add(info.Id);
            ent.Sitting = _sittingIds.Contains(info.Id);
            ent.CombatStance = _stanceIds.Contains(info.Id);
            ent.Gathering = info.Gathering;
            ent.GatherFishing = info.GatherFishing;
        }
        var nameLabel = FindFirst<Label3D>(body);
        if (nameLabel != null)
        {
            nameLabel.Modulate = NameColor(info);
            nameLabel.Visible = false;
            ent.OriginalTagY = nameLabel.Position.Y;
            if (ent.Sitting || _stalls.ContainsKey(info.Id))
            {
                nameLabel.Position = new Vector3(nameLabel.Position.X, SittingNameTagHeight, nameLabel.Position.Z);
            }
            ent.NameTag = nameLabel;
            ent.PlateRoot = nameLabel.GetParent() as Node3D;
            ent.Plate = new PlateStack(nameLabel);
            ent.Plate.SetClan(info.ClanName);
            ent.Plate.SetTitle(TitleTextOf(info.TitleId));
        }
        ent.RoleFx = SpawnNpcRoleFx(ent);
        if (info.Invisible) StealthOnSpawn(info.Id, info.Invisibility);
        if (crowd != null) ent.AnimActive = false;
        if (info.Dead) LayOutCorpse(ent);
        else PlayClip(ent, "idle");
        if (crowd != null && !info.Dead)
        {
            crowd.TakeOver();
            crowd.Step(0);
        }
        if (ent.Gathering && !info.Dead) BeginRemoteGather(info.Id, ent.GatherFishing);
        RefreshNpcQuestMarker(ent);
        AttachPendingStall(info.Id);
        Diag.Slow($"entity {label} model={info.ModelId} npc={info.IsNpc}", watch);
    }

    private void LayOutCorpse(Ent e, bool settled = true)
    {
        e.Dead = true;
        e.HasTarget = false;
        e.Speed = 0f;
        e.CorpseRemoveAt = e.IsNpc ? Now() + CorpseLinger : 0;
        e.Flinch?.Stop();
        if (settled) e.Hp = 0;
        RefreshEntityCollision(e);
        double len = PlayActionOn(e.Anim, DeathClips);
        e.ActionUntil = Now() + Mathf.Max((float)len, 3.0f);
        e.ActionRank = ActionRankDeath;
        e.ActionClip = "dead";
        if (settled && e.Anim != null && len > 0) e.Anim.Advance(len);
    }

    private const float ColliderSyncDist = 8f;
    private const string PlateRootName = "plates";

    private static Node3D AttachNameLabel(Node3D body, string name, float y)
    {
        var root = new Node3D { Name = PlateRootName };
        body.AddChild(root);
        root.AddChild(NameLabel(name, y));
        return root;
    }

    private static void SyncEntityPlates(Ent e, bool near)
    {
        if (e.PlateRoot == null || near == e.PlateNear) return;
        e.PlateNear = near;
        e.PlateRoot.TopLevel = !near;
        if (near) e.PlateRoot.Transform = Transform3D.Identity;
    }

    private static StaticBody3D AttachBodyCollider(Node3D body, float radius, float lift)
    {
        var collider = new StaticBody3D { CollisionMask = 0, CollisionLayer = 0, TopLevel = true };
        collider.AddChild(BodyCapsule(radius, lift));
        body.AddChild(collider);
        collider.Position = body.GlobalPosition;
        return collider;
    }

    private static void SyncEntityCollider(Ent e, Vector3 pos, Vector3 selfPos)
    {
        if (e.Collider == null) return;
        bool near = pos.DistanceSquaredTo(selfPos) <= ColliderSyncDist * ColliderSyncDist;
        if (near != e.ColliderNear)
        {
            e.ColliderNear = near;
            if (near) { e.Collider.Position = pos; e.ColliderAt = pos; }
            RefreshEntityCollision(e);
        }
        else if (near && pos != e.ColliderAt)
        {
            e.Collider.Position = pos;
            e.ColliderAt = pos;
        }
    }

    public System.Collections.Generic.List<string> MapObjectReport()
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            if (!e.IsNpc || e.ModelStem != "(map object)") continue;
            lines.Add($"MAPOBJECT id={kv.Key} proto={e.NpcId} model={e.ModelId} " +
                      $"tNpc={e.NpcType} hit={e.Attackable} \"{e.Name}\"");
        }
        if (lines.Count == 0) lines.Add("MAPOBJECT none in range");
        return lines;
    }

    private static bool BlocksMovement(Ent e) =>
        !e.Dead && (e.IsNpc ? e.NpcType == NpcTypes.Scarecrow : e.Attackable);

    private static void RefreshEntityCollision(Ent e)
    {
        RefreshGateBlocker(e);
        if (e.Collider == null || !GodotObject.IsInstanceValid(e.Collider)) return;
        e.Collider.CollisionLayer = e.ColliderNear && BlocksMovement(e) ? BlockerCollisionLayer : 0u;
    }

    private const float MoveFacingEpsSq = 0.04f;
    private const float MoveFacingWarpSq = 900f;
    private const float MovePlaybackSeconds = 1.5f;
    private const float MovePlaybackDamp = 0.85f;
    private const float NpcMovePlaybackSeconds = 0.3f;
    private const float RelistGlideSeconds = 0.5f;

    private const float EntityTurnDegPerSec = 720f;

    private static void CancelEntityPosture(Ent e, double now)
    {
        if (e.ActionRank != ActionRankPosture || now >= e.ActionUntil) return;
        e.ActionUntil = 0;
        e.ActionClip = null;
        e.Clip = null;
    }

    private static void FaceEntity(Ent e, float yawDegrees, bool immediate)
    {
        e.TargetYaw = yawDegrees;
        if (immediate) SetEntityYaw(e, yawDegrees);
    }

    private static void SetEntityYaw(Ent e, float yawDegrees)
    {
        e.Yaw = yawDegrees;
        e.Body.RotationDegrees = new Vector3(0, yawDegrees, 0);
    }

    private static void TickEntityFacing(Ent e, float dt)
    {
        float current = e.Yaw ?? e.Body.RotationDegrees.Y;
        if (current == e.TargetYaw) { e.Yaw = current; return; }
        float delta = Mathf.RadToDeg(Mathf.AngleDifference(
            Mathf.DegToRad(current), Mathf.DegToRad(e.TargetYaw)));
        if (Mathf.Abs(delta) < 0.5f)
        {
            SetEntityYaw(e, e.TargetYaw);
            return;
        }

        float step = EntityTurnDegPerSec * dt;
        SetEntityYaw(e, current + Mathf.Clamp(delta, -step, step));
    }

    private void OnMove(int id, float x, float z, float y, float velHint, bool travelling)
    {
        if (id == _myId) return;
        if (!_ents.TryGetValue(id, out var e)) return;
        if (e.Dead)
        {
            e.Dead = false; e.CorpseRemoveAt = 0; e.ActionUntil = 0; e.ActionClip = null; e.Clip = null;
            RefreshEntityCollision(e);
        }

        var dest = e.Body.Position.DistanceSquaredTo(_self.Position) <= EntityProbeDist * EntityProbeDist
            ? EntityGroundPos(x, z, y, e.Lift)
            : GroundPos(x, z, y, e.Lift);
        float dxKo = x - e.KoX, dzKo = z - e.KoZ;
        float koMove2 = dxKo * dxKo + dzKo * dzKo;
        if (travelling && koMove2 > MoveFacingEpsSq)
            CancelEntityPosture(e, Now());
        if (travelling && koMove2 > MoveFacingEpsSq && koMove2 < MoveFacingWarpSq)
        {
            bool backwards = velHint < 0f;
            FaceEntity(
                e,
                180f - (backwards ? Coord.KoHeading(-dxKo, -dzKo) : Coord.KoHeading(dxKo, dzKo)),
                immediate: false);
        }

        e.KoX = x; e.KoZ = z; e.KoY = y;

        if (travelling)
            e.Backwards = velHint < 0f;

        if (e.Body.Position.DistanceTo(dest) > TeleportSnap)
        {
            e.Body.Position = dest;
            e.Target = dest;
            e.HasTarget = true;
            e.Speed = 0f;
            return;
        }

        if (!travelling)
        {
            e.Target = dest;
            e.HasTarget = true;
            return;
        }

        var delta = dest - e.Body.Position;
        float dist = new Vector2(delta.X, delta.Z).Length();
        e.Target = dest;
        e.HasTarget = true;
        float playback = dist / MovePlaybackSeconds * MovePlaybackDamp;
        if (e.IsNpc && velHint > 0.01f)
            playback = Mathf.Max(velHint, dist / NpcMovePlaybackSeconds);
        e.Speed = Mathf.Abs(velHint) > 0.01f ? playback : 0f;
    }

    private static bool EntityMoving(Ent e) => EntityMoving(e, e.Body.Position);

    private static bool EntityMoving(Ent e, Vector3 pos) =>
        !e.Dead && e.HasTarget && e.Speed > 0.1f
        && pos.DistanceTo(e.Target) > MoveArriveEps;

    private const float EntityRenderDist = 160f;
    private const float EntityRenderFade = 30f;
    private const float EntityProbeDist = 64f;

    private static void ApplyEntityRenderCost(Node3D body)
    {
        foreach (var mi in FindAll<MeshInstance3D>(body))
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.VisibilityRangeEnd = EntityRenderDist * Config.ViewDistance;
            mi.VisibilityRangeEndMargin = EntityRenderFade;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            SkinShare.FixBounds(mi);
        }
    }

    private const float AnimAlwaysDist = 22f;
    private const int OffScreenMoveEvery = 6;
    private const float AnimViewMargin = 4f;
    private const double FarEntityInterval = 0.1;
    private int _frustumCount;
    private int _animFull, _animMid, _animFar, _animOff;
    private int _closeEntities;

    private void RefreshFrustum()
    {
        if (_camera == null) { _frustumCount = 0; return; }
        var frustum = _camera.GetFrustum();
        _frustumCount = Mathf.Min(frustum.Count, _frustumPlanes.Length);
        for (int j = 0; j < _frustumCount; j++) _frustumPlanes[j] = frustum[j];
    }

    private bool InView(Vector3 pos)
    {
        for (int j = 0; j < _frustumCount; j++)
            if (_frustumPlanes[j].DistanceTo(pos) > AnimViewMargin) return false;
        return true;
    }

    private readonly List<Ent> _animRanking = new();
    private static readonly System.Comparison<Ent> ByCamDist = (a, b) => a.CamDist2.CompareTo(b.CamDist2);

    private void RankEntityAnimation()
    {
        _animRanking.Clear();
        foreach (var e in _ents.Values)
            if (e.Anim != null && !e.Far) _animRanking.Add(e);
        _animRanking.Sort(ByCamDist);
        for (int i = 0; i < _animRanking.Count; i++) _animRanking[i].AnimRank = i;
    }

    private void UpdateEntityAnimLod(Ent e, Vector3 camPos, Vector3 pos)
    {
        float d2 = pos.DistanceSquaredTo(camPos);
        e.CamDist2 = d2;
        if (d2 <= Perf.CloseEntityDist * Perf.CloseEntityDist) _closeEntities++;
        float pause2 = EntityRenderDist * EntityRenderDist;
        float resume2 = (EntityRenderDist - 12f) * (EntityRenderDist - 12f);
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
            if (d2 > Config.AnimMidDist * Config.AnimMidDist) step = far;
            else if (d2 > Config.AnimFullDist * Config.AnimFullDist) step = mid;
            if (e.AnimRank >= Config.AnimFullCount + Config.AnimMidCount) step = Mathf.Max(step, far);
            else if (e.AnimRank >= Config.AnimFullCount) step = Mathf.Max(step, mid);
            every = step == 0 ? 1 : step >= far ? Config.AnimFarEvery : Config.AnimMidEvery;
        }
        e.AnimStep = step;
        e.AnimEvery = every;
        e.AnimThrottled = !e.AnimPaused && (step > 0 || e.Crowd != null);
        if (e.AnimPaused) _animOff++;
        else if (step == 0) _animFull++;
        else if (step <= 1.0 / Config.AnimMidHz) _animMid++;
        else _animFar++;
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

    private void OnRotate(int id, float dir)
    {
        if (id == _myId) return;
        if (!_ents.TryGetValue(id, out var e)) return;
        FaceEntity(e, 180f - dir, immediate: false);
    }

    private void OnLookChange(int id, int lookSlot, int itemId, short durability)
    {
        if (id == _myId) return;
        if (!_ents.TryGetValue(id, out var e) || e.IsNpc) return;
        int gearIndex = GearIndexForLookSlot(lookSlot);
        if (gearIndex < 0) return;

        if (e.Gear.Length < InventoryConstants.VisualSlotCount)
            System.Array.Resize(ref e.Gear, InventoryConstants.VisualSlotCount);
        e.Gear[gearIndex] = itemId;

        RedressEntity(e);
    }

    private void RedressEntity(Ent e)
    {
        if (e.Anim == null || e.DefaultParts.Count == 0)
            return;
        CharacterMerge.Remove(e.Body);
        RestorePartDefaults(e.Body, e.DefaultParts);
        GraftEquipment(e.Body, e.Race, e.Face, e.Gear, e.Hair, e.HelmetHidden);
        AttachWeapons(e.Body, e.Gear);
        e.WingAnims = AttachWings(e.Body, e.Gear, e.Race, _zone);
        System.Array.Clear(e.WingClips);
        AttachHandFx(e.Body, e.Gear, e.Race, _zone);
        RearmWornLook(e.Body, e.Gear);
        ApplyEntityRenderCost(e.Body);
        if (!e.IsNpc && Config.MergeCharacters) CharacterMerge.Apply(e.Body);
        e.HitFxBox = null;
    }

    private static int GearIndexForLookSlot(int lookSlot)
        => System.Array.IndexOf(InventoryConstants.VisualSlots, lookSlot);

    private void OnOut(int id)
    {
        _pendingSpawns.Remove(id);
        if (_ents.TryGetValue(id, out var e))
        {
            if (e.BridgeFloor != null && GodotObject.IsInstanceValid(e.BridgeFloor))
                e.BridgeFloor.QueueFree();
            e.Crowd?.Release();
            e.Body.QueueFree();
            _ents.Remove(id);
            ForgetSkillFx(id);
            StateVisualForgetEntity(id);
            StealthForgetEntity(id);
            ForgetStall(id);
        }
    }

    private void ApplyEntityTitle(Ent ent, int titleId)
    {
        ent.Plate?.SetTitle(TitleTextOf(titleId));
    }

    private static string TitleTextOf(int titleId) =>
        titleId != 0 && AchievementData.TitleOf(titleId) is { } title ? title.Name : "";

    private void OnEntityTitle(int charId, int titleId)
    {
        if (charId == _myId)
        {
            SelfPlate()?.SetTitle(TitleTextOf(titleId));
            RefreshTitleButton();
            if (_titleShown) RebuildTitleList();
            return;
        }

        if (_ents.TryGetValue(charId, out var ent))
            ApplyEntityTitle(ent, titleId);
    }

    private PlateStack? _selfPlate;

    private PlateStack? SelfPlate()
    {
        if (_selfPlate != null) return _selfPlate;
        if (_self == null) return null;

        if (!_selfNameTagFound)
        {
            _selfNameTagFound = true;
            _selfNameTag = FindFirst<Label3D>(_self);
        }

        if (_selfNameTag == null || !GodotObject.IsInstanceValid(_selfNameTag)) return null;
        return _selfPlate = new PlateStack(_selfNameTag);
    }

    private void ApplySelfClan(string clanName) => SelfPlate()?.SetClan(clanName);

    private Label3D? _selfNameTag;
    private bool _selfNameTagFound;

    private float HeadHeightOf(int charId)
    {
        bool sitting = charId == _myId
            ? (_selfSitting || _stalls.ContainsKey(charId))
            : (_ents.TryGetValue(charId, out var sitEnt) && (sitEnt.Sitting || _stalls.ContainsKey(charId)));

        if (charId != _myId)
        {
            float baseH = _ents.TryGetValue(charId, out var tagEnt) && tagEnt.NameTag is { } tag
                ? Mathf.Max(0.4f, tag.Position.Y)
                : StandingNameTagHeight;
            return sitting ? Mathf.Min(baseH, SittingNameTagHeight) : baseH;
        }
        if (!_selfNameTagFound)
        {
            _selfNameTagFound = true;
            _selfNameTag = _self != null ? FindFirst<Label3D>(_self) : null;
        }
        float selfH = Mathf.Max(0.4f, _selfNameTag?.Position.Y ?? StandingSelfNameTagHeight);
        return sitting ? Mathf.Min(selfH, SittingNameTagHeight) : selfH;
    }

    private void OnEntityHp(int id, int hp, int maxHp, int damage)
    {
        if (!_ents.TryGetValue(id, out var e)) return;
        int old = e.MaxHp > 0 ? e.Hp : hp;
        e.Hp = hp; e.MaxHp = maxHp;
        if (e.IsNpc || e.Attackable) { _lastHitId = id; _lastHitAt = Now(); }
        int shown = damage != 0 ? Mathf.Abs(damage) : Mathf.Max(0, old - hp);
        if (shown > 0)
        {
            CombatLogAdd($"You hit {e.Name} for {shown:n0} damage.", CombatLogKind.Damage);
            Floaters?.Damage(id, shown);
            if (hp > 0 && !e.Dead)
            {
                AudioStruck(id);
            }
        }
        else if (hp - old > 0 && old > 0)
        {
            CombatLogAdd($"{e.Name} recovered {hp - old:n0} HP.", CombatLogKind.Recovery);
            Floaters?.Cure(id, hp - old);
        }
    }

    private static (float Footprint, float Bound) BodyRadii(Node3D body)
    {
        Aabb? merged = null;

        void Walk(Node n)
        {
            if (n is Cape or BoneAttachment3D) return;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                var a = mi.GlobalTransform * mi.GetAabb();
                if (a.Size != Vector3.Zero)
                    merged = merged.HasValue ? merged.Value.Merge(a) : a;
            }
            foreach (var child in n.GetChildren()) Walk(child);
        }

        Walk(body);
        if (!merged.HasValue) return (0.9f, 1.0f);
        var s = merged.Value.Size;
        return (Mathf.Clamp(Mathf.Max(s.X, s.Z) * 0.5f, 0.45f, 6f),
                Mathf.Clamp(s.Length() * 0.5f, 0.7f, 10f));
    }

    private Color PlayerNameColor(int nation) =>
        nation == Net.I.Nation ? NamePlate.Ally : NamePlate.Enemy;

    private Color NameColor(EntitySnapshot info)
    {
        if (!info.IsNpc) return PlayerNameColor(info.Nation);
        if (!info.Attackable) return new Color(0.55f, 0.85f, 1f);
        int d = info.Level - Sheet.Level;
        if (d >= 4) return new Color(1f, 0.35f, 0.30f);
        if (d >= 0) return new Color(1f, 0.85f, 0.35f);
        return new Color(0.5f, 1f, 0.5f);
    }

    public const float BridgeLoweredPitch = 110.0f;

    private void ApplyPlainNpcBridgeState(Ent e, int gateOpen)
    {
        e.GateOpen = gateOpen;
        if (e.BridgeVisual == null || !GodotObject.IsInstanceValid(e.BridgeVisual))
            e.BridgeVisual = e.Body.GetNodeOrNull<Node3D>(ModelNodeName) ?? e.Body.FindChild(ModelNodeName, true, false) as Node3D;

        if (gateOpen == 2)
        {
            if (e.BridgeVisual != null)
                e.BridgeVisual.RotationDegrees = new Vector3(BridgeLoweredPitch, 0, 0);

            if (e.BridgeFloor == null || !GodotObject.IsInstanceValid(e.BridgeFloor))
            {
                e.Body.ForceUpdateTransform();
                var faces = new List<Vector3>();
                CollectFaces(e.Body, faces);
                if (faces.Count > 0)
                {
                    var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
                    shape.SetFaces(faces.ToArray());
                    var floor = new StaticBody3D { Name = "BridgeFloor", CollisionLayer = WorldCollisionLayer, CollisionMask = 0 };
                    floor.AddChild(new CollisionShape3D { Shape = shape });
                    _entities.AddChild(floor);
                    e.BridgeFloor = floor;
                }
            }
            else
            {
                e.BridgeFloor.CollisionLayer = WorldCollisionLayer;
            }
        }
        else if (gateOpen == 0)
        {
            if (e.BridgeVisual != null)
                e.BridgeVisual.RotationDegrees = Vector3.Zero;

            if (e.BridgeFloor != null && GodotObject.IsInstanceValid(e.BridgeFloor))
            {
                e.BridgeFloor.CollisionLayer = 0;
            }
        }
    }

    private static void CollectFaces(Node node, List<Vector3> faces)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            var xform = mi.GlobalTransform;
            foreach (var v in mesh.GetFaces()) faces.Add(xform * v);
        }
        foreach (var child in node.GetChildren()) CollectFaces(child, faces);
    }
}
