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
        public StaticBody3D? Collider;
        public Label3D? NameTag;
        public PlateStack? Plate;
        public Node3D? HpBar;
        public MeshInstance3D? HpFill;
        public Node3D? IndicatorFx;
        public string IndicatorName = "";
        public Node3D? RoleFx;
        public AnimationPlayer? Anim;
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
        public Aabb? HitFxBox;
        public float Radius = 0.9f;
        public float BoundRadius = 1.0f;
        public string Name = "";
        public int Level;
        public int Hp, MaxHp;
        public int ModelId, Size, Nation, NpcId, NpcType;
        public int Race, Face, Hair;
        public int CapeId, CapeR, CapeG, CapeB, KnightsId;
        public bool IsGm;
        public bool HelmetHidden;
        public int[] Gear = System.Array.Empty<int>();
        public Dictionary<int, (Mesh? Mesh, Skin? Skin)> DefaultParts = new();
        public float SpawnX, SpawnZ, SpawnY, SpawnDir;
        public string ModelStem = "";
        public bool AnimPaused;
        public bool AnimThrottled;
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

        if (_ents.TryGetValue(info.Id, out var existing))
        {
            var rp = GroundPos(info.X, info.Z, info.Y, existing.Lift);
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
        bool mapObject = info.ObjectType == NpcTypes.ObjectType.MapObject;
        var loadWatch = Diag.Watch();
        PackedScene? scene = mapObject ? null
            : info.IsNpc ? ResolveMobScene(info.ModelId)
                         : ResolvePlayerScene(info.Race);
        Diag.Slow($"model load {label} model={info.ModelId} race={info.Race}", loadWatch);
        float lift = scene != null || mapObject ? 0f : CapsuleHalf;
        var pos = GroundPos(info.X, info.Z, info.Y, lift);

        Node3D body;
        AnimationPlayer? anim = null;
        if (scene != null)
            (body, anim) = MakeAnimatedEntity(scene, label, info.Size > 0 ? info.Size / 100f : 1f);
        else if (mapObject)
            body = MakeMapObjectEntity(label);
        else
            body = MakeEntity(ColorFor(info), label);

        body.Position = pos;
        float spawnYaw = info.Dir != 0 ? 180f - 2f * info.Dir : (info.Id * 137) % 360;
        if (scene != null)
            body.RotationDegrees = new Vector3(0, spawnYaw, 0);
        _entities.AddChild(body);
        Dictionary<int, (Mesh? Mesh, Skin? Skin)> defaultParts = new();
        AnimationPlayer?[]? wingAnims = null;
        Flinch? flinch = null;
        if (scene != null)
        {
            defaultParts = CapturePartDefaults(body);
            if (!info.IsNpc)
            {
                flinch = Flinch.Attach(body);
                GraftEquipment(body, info.Race, info.Face, info.Gear, info.Hair, info.HelmetHidden);
                DressEntityCape(body, info);
            }
            AttachWeapons(body, info.Gear, info.NpcType, info.NpcId);
            if (!info.IsNpc) AttachClanGauntlet(body, info.Race, info.ClanGrade);
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
        ApplyEntityRenderCost(body);

        string modelStem = mapObject ? "(map object)"
            : scene == null ? "(capsule fallback)"
            : info.IsNpc ? (_mobIndex != null && _mobIndex.TryGetValue(info.ModelId, out var ms) ? ms + ".glb" : "(model)")
            : "(race rig)";
        var radii = BodyRadii(body);
        var ent = new Ent
        {
            Body = body, Anim = anim, WingAnims = wingAnims, Flinch = flinch,
            Target = pos, HasTarget = true, Speed = 0f, Lift = lift,
            KoX = info.X, KoZ = info.Z, KoY = info.Y,
            IsNpc = info.IsNpc, IsMonster = info.IsMonster, Attackable = info.Attackable,
            Name = label, Level = info.Level, Radius = radii.Footprint, BoundRadius = radii.Bound,
            ModelId = info.ModelId, Size = info.Size, Nation = info.Nation,
            NpcId = info.NpcId, NpcType = info.NpcType,
            Race = info.Race, Face = info.Face, Hair = info.Hair,
            CapeId = info.CapeId, CapeR = info.CapeR, CapeG = info.CapeG, CapeB = info.CapeB,
            KnightsId = info.KnightsId, IsGm = info.IsGm, HelmetHidden = info.HelmetHidden,
            Gear = info.Gear.Length > 0 ? (int[])info.Gear.Clone() : System.Array.Empty<int>(),
            DefaultParts = defaultParts,
            SpawnX = info.X, SpawnZ = info.Z, SpawnY = info.Y, SpawnDir = info.Dir,
            ModelStem = modelStem, TargetYaw = spawnYaw,
        };
        _ents[info.Id] = ent;
        ApplyGmFx(info.Id, Net.I.GmFxVisible(info.Id, info.IsGm));
        ent.Collider = AttachBodyCollider(body, info.IsNpc ? ent.Radius : PlayerCapsuleRadius, lift);
        RefreshEntityCollision(ent);
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
            ent.NameTag = nameLabel;
            ent.Plate = new PlateStack(nameLabel);
            ent.Plate.SetClan(info.ClanName);
            ent.Plate.SetTitle(TitleTextOf(info.TitleId));
        }
        ent.RoleFx = SpawnNpcRoleFx(ent);
        if (info.Invisible) StealthOnSpawn(info.Id);
        if (info.Dead) LayOutCorpse(ent);
        else PlayClip(ent, "idle");
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

    private static StaticBody3D AttachBodyCollider(Node3D body, float radius, float lift)
    {
        var collider = new StaticBody3D { CollisionMask = 0 };
        collider.AddChild(BodyCapsule(radius, lift));
        body.AddChild(collider);
        return collider;
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
        if (e.Collider == null || !GodotObject.IsInstanceValid(e.Collider)) return;
        e.Collider.CollisionLayer = BlocksMovement(e) ? BlockerCollisionLayer : 0u;
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
        if (immediate)
            e.Body.RotationDegrees = new Vector3(0, yawDegrees, 0);
    }

    private static void TickEntityFacing(Ent e, float dt)
    {
        float current = e.Body.RotationDegrees.Y;
        float delta = Mathf.RadToDeg(Mathf.AngleDifference(
            Mathf.DegToRad(current), Mathf.DegToRad(e.TargetYaw)));
        if (Mathf.Abs(delta) < 0.5f)
        {
            if (current != e.TargetYaw)
                e.Body.RotationDegrees = new Vector3(0, e.TargetYaw, 0);
            return;
        }

        float step = EntityTurnDegPerSec * dt;
        e.Body.RotationDegrees = new Vector3(0, current + Mathf.Clamp(delta, -step, step), 0);
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

        var dest = GroundPos(x, z, y, e.Lift);
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

    private static bool EntityMoving(Ent e) =>
        !e.Dead && e.HasTarget && e.Speed > 0.1f
        && e.Body.Position.DistanceTo(e.Target) > MoveArriveEps;

    private const float EntityRenderDist = 160f;
    private const float EntityRenderFade = 30f;

    private static void ApplyEntityRenderCost(Node3D body)
    {
        foreach (var mi in FindAll<MeshInstance3D>(body))
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.VisibilityRangeEnd = EntityRenderDist * Config.ViewDistance;
            mi.VisibilityRangeEndMargin = EntityRenderFade;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
    }

    private const float EntityAnimFullDist = 45f;
    private const double AnimLodStep = 0.1;

    private void UpdateEntityAnimLod(Ent e, Vector3 camPos)
    {
        if (e.Anim == null) return;
        float d2 = e.Body.GlobalPosition.DistanceSquaredTo(camPos);
        float pause2 = EntityRenderDist * EntityRenderDist;
        float resume2 = (EntityRenderDist - 12f) * (EntityRenderDist - 12f);
        e.AnimPaused = e.AnimPaused ? d2 > resume2 : d2 > pause2;
        e.AnimThrottled = !e.AnimPaused && d2 > EntityAnimFullDist * EntityAnimFullDist;
        bool active = !e.AnimPaused && !e.AnimThrottled;
        if (e.Anim.Active != active) e.Anim.Active = active;
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
        RestorePartDefaults(e.Body, e.DefaultParts);
        GraftEquipment(e.Body, e.Race, e.Face, e.Gear, e.Hair, e.HelmetHidden);
        AttachWeapons(e.Body, e.Gear);
        e.WingAnims = AttachWings(e.Body, e.Gear, e.Race, _zone);
        System.Array.Clear(e.WingClips);
        AttachHandFx(e.Body, e.Gear, e.Race, _zone);
        RearmWornLook(e.Body, e.Gear);
        ApplyEntityRenderCost(e.Body);
        e.HitFxBox = null;
    }

    private static int GearIndexForLookSlot(int lookSlot)
        => System.Array.IndexOf(InventoryConstants.VisualSlots, lookSlot);

    private void OnOut(int id)
    {
        _pendingSpawns.Remove(id);
        if (_ents.TryGetValue(id, out var e))
        {
            e.Body.QueueFree();
            _ents.Remove(id);
            StateVisualForgetEntity(id);
            StealthForgetEntity(id);
            ForgetStall(id);
        }
    }

    private const float EntHpBarW = 0.9f, EntHpBarH = 0.075f;

    private static MeshInstance3D HpBarQuad(Color c, float w, float z) => new()
    {
        Mesh = new QuadMesh { Size = new Vector2(w, EntHpBarH), CenterOffset = new Vector3(0, 0, z) },
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = c,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
        },
    };

    private void UpdateEntHpBar(Ent e)
    {
        if (e.NameTag == null || e.MaxHp <= 0) return;
        if (e.IsNpc && !e.Attackable) return;
        if (e.HpBar == null)
        {
            e.HpBar = new Node3D { Position = e.NameTag.Position + new Vector3(0, 0.18f, 0) };
            e.HpBar.AddChild(HpBarQuad(new Color(0, 0, 0, 0.55f), EntHpBarW, 0));
            e.HpFill = HpBarQuad(new Color(0.85f, 0.16f, 0.14f), EntHpBarW, 0.01f);
            e.HpBar.AddChild(e.HpFill);
            e.Body.AddChild(e.HpBar);
        }
        float frac = Mathf.Clamp(e.Hp / (float)e.MaxHp, 0f, 1f);
        float w = Mathf.Max(0.001f, EntHpBarW * frac);
        var q = (QuadMesh)e.HpFill!.Mesh;
        q.Size = new Vector2(w, EntHpBarH);
        q.CenterOffset = new Vector3((w - EntHpBarW) / 2, 0, 0.01f);
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
        if (charId != _myId)
            return _ents.TryGetValue(charId, out var e) && e.NameTag is { } tag
                ? Mathf.Max(0.4f, tag.Position.Y)
                : 1.7f;
        if (!_selfNameTagFound)
        {
            _selfNameTagFound = true;
            _selfNameTag = _self != null ? FindFirst<Label3D>(_self) : null;
        }
        return Mathf.Max(0.4f, _selfNameTag?.Position.Y ?? 1.9f);
    }

    private void OnEntityHp(int id, int hp, int maxHp, int damage)
    {
        if (!_ents.TryGetValue(id, out var e)) return;
        int old = e.MaxHp > 0 ? e.Hp : hp;
        e.Hp = hp; e.MaxHp = maxHp;
        UpdateEntHpBar(e);
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
}
