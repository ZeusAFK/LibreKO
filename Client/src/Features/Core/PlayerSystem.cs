using Godot;

namespace LibreKO.Features.Core;

internal sealed class PlayerSystem : IFeature, IMovement, ISelfView
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly AdminSession admin;
    private readonly ClanSession clan;
    private readonly SelfSession selfSession;
    private readonly INpcQuestRequests npcQuestRequests;
    private readonly IPlayerRequests requests;
    private readonly CosmeticsSession cosmetics;
    private readonly CombatSystem combat;
    private readonly EntityLooksSystem entityLooks;
    private readonly ScenerySystem scenery;
    private readonly EntitySystem entities;
    private readonly EntityPlatesSystem entityPlates;
    private readonly SkillMovementSystem skillMovement;
    private readonly EntityStatesSystem entityStates;
    private readonly EntityActionSystem entityAction;
    private readonly ZoneSession zone;
    private readonly CameraSystem camera;
    private readonly BuffSystem buff;
    private readonly ISoccerRequests soccerRequests;
    private readonly SkillCastSystem skillCast;
    private readonly BasicAttackSystem basicAttack;

    public PlayerSystem(IWorldContext context, AdminSession admin, ClanSession clan, SelfSession self, INpcQuestRequests npcQuestRequests, IPlayerRequests requests, CosmeticsSession cosmetics, SelfAvatar avatar, CombatSystem combat, EntityLooksSystem entityLooks, ScenerySystem scenery, EntitySystem entities, EntityPlatesSystem entityPlates, SkillMovementSystem skillMovement, EntityStatesSystem entityStates, EntityActionSystem entityAction, ZoneSession zone, CameraSystem camera, BuffSystem buff, ISoccerRequests soccerRequests, SkillCastSystem skillCast, BasicAttackSystem basicAttack)
    {
        this.context = context;
        this.admin = admin;
        this.clan = clan;
        selfSession = self;
        this.npcQuestRequests = npcQuestRequests;
        this.requests = requests;
        this.cosmetics = cosmetics;
        this.avatar = avatar;
        this.combat = combat;
        this.entityLooks = entityLooks;
        this.scenery = scenery;
        this.entities = entities;
        this.entityPlates = entityPlates;
        this.skillMovement = skillMovement;
        this.entityStates = entityStates;
        this.entityAction = entityAction;
        this.zone = zone;
        this.camera = camera;
        this.buff = buff;
        this.soccerRequests = soccerRequests;
        this.skillCast = skillCast;
        this.basicAttack = basicAttack;
    }

    private IMoveGates? moveGates;

    internal void AttachMoveGates(IMoveGates moveGates) => this.moveGates = moveGates;

    internal void DetachMoveGates() => moveGates = null;

    internal event Action? SelfMoving;

    public void Build()
    {
        zone.WarpEvent += OnWarp;
    }

    internal void TickSelfAnimation(double nowSec)
    {
        if (avatar.Anim != null && !avatar.Dead)
        {
            bool clickMoveEdge = clickMovePressedEdge;
            clickMovePressedEdge = false;
            if (nowSec < avatar.ActionUntil
                && !skillCast.IsRootedByCast()
                && ((walkKeyHeld && (avatar.ActionRank != ModelAnimation.ActionRankSkill || walkPressedEdge)) || clickMoveEdge))
            {
                avatar.ActionUntil = 0;
                avatar.Clip = null;
            }

            if (nowSec < avatar.ActionUntil) {  }
            else
            {
                if (avatar.ActionUntil != 0) { avatar.ActionUntil = 0; avatar.Clip = null; }
                string locomotion = !avatar.Moving
                    ? avatar.Sitting ? "sit" : "idle"
                    : avatar.MovingBackward
                        ? "walk_reverse"
                        : avatar.Running ? "run" : "walk";
                bool combatStance = locomotion == "idle" && combat.SelfStanceReady();
                var stanceGear = combatStance ? entityLooks.SelfGear() : null;
                if (locomotion == "sit" && avatar.Hover != null)
                    ModelAnimation.PlayClipOn(avatar.Anim, ref avatar.Clip, WingRig.WingSitLocomotion, avatar.Hover.Loop);
                else
                    ModelAnimation.PlayClipOn(
                        avatar.Anim,
                        ref avatar.Clip,
                        locomotion,
                        combatStance ? ModelAnimation.StanceIdleClips(stanceGear) : null,
                        combatStance ? ModelAnimation.StanceIdleSlot(stanceGear) : -1);
            }
        }
    }

    internal void BuildSelf(MyInfo info)
    {
        string selfName = info.Name.Length > 0 ? info.Name : "You";
        GD.Print("[load] character: body scene");
        int selfLookRace = PlayerRig.LookRace(info.Race, info.Class);
        PackedScene? selfScene = entityLooks.ResolvePlayerScene(selfLookRace);
        Node3D selfVisual;
        if (selfScene != null)
        {
            GD.Print("[load] character: rig");
            (selfVisual, avatar.Anim) = entityLooks.MakeAnimatedEntity(selfScene, selfName, 1f);
            avatar.Flinch = Flinch.Attach(selfVisual);
            entityLooks.CaptureSelfDefaults(selfVisual);
            GD.Print("[load] character: equipment");
            entityLooks.GraftEquipment(selfVisual, selfLookRace, info.Face, info.Gear, info.Hair, cosmetics.HelmetHidden, cosmetics.CostumeHidden);
            GD.Print("[load] character: complete");
        }
        else
        {
            selfVisual = ModelNodes.MakeEntity(new Color(1f, 0.82f, 0.3f), selfName);
            selfVisual.Position = new Vector3(0, CapsuleHalf, 0);
        }
        avatar.Lift = 0f;
        avatar.Body = MakeBody(selfVisual);
        ApplyCollisionPolicy();
        var selfCs = NodeSearch.FindFirst<CollisionShape3D>(avatar.Body);
        avatar.Capsule = selfCs?.Shape as CapsuleShape3D;
        avatar.CapsuleOffsetY = selfCs?.Position.Y ?? 0.85f;
        avatar.Body.Position = scenery.FloorGroundPos(info.X, info.Z, info.Y, avatar.Lift, login: true);
        avatar.LastFreePosition = avatar.Body.Position;
        entities.EntityLayer.AddChild(avatar.Body);
        entityPlates.RefreshSelfClanLine();
        if (selfScene != null)
        {
            entityLooks.AttachWeapons(avatar.Body, info.Gear, new HandWearer(info.Race));
            StateLooks.DressClanGauntlet(avatar.Body, info.Race, clan.MyClan.InClan ? clan.MyClan.Grade : 0, clan.MyClan.InClan ? clan.MyClan.Ranking : 0);
            entityLooks.AttachSelfWings(avatar.Body, info.Gear, info.Race, context.Zone);
            HandFxRig.AttachHandFx(avatar.Body, info.Gear, info.Race, context.Zone);
        }
        avatar.Visual = selfVisual;
        avatar.StandingVisualTransform = selfVisual.Transform;
    }

    void IMovement.MoveTo(Vector3 target) => SetMoveTarget(target);
    void IMovement.CancelMoveTo() => CancelMoveTarget();
    void IMovement.StopForInteraction() => StopForInteraction();
    void IMovement.SetCollisions(bool on) => SetCollisions(on);
    void IMovement.Face(Vector3 direction) => avatar.Facing = direction;
    void IMovement.StopMoving() => StopMoving();

    internal void ToggleSitting()
    {
        if (!avatar.Sitting && (avatar.Moving || skillCast.SelfCasting(context.Now))) return;
        entityStates.SendSitToggle();
    }

    private bool gesturePending;

    internal void PressInteract()
    {
        if (context.Self.Dead || selfSession.Stunned(context.Now) || skillCast.SelfCasting(context.Now)) return;
        if (context.Selection.SelectedId >= 0 && context.Entities.Views.ContainsKey(context.Selection.SelectedId)) entityAction.FaceToward(context.Self.CharId, context.Selection.SelectedId);
        gesturePending = true;
    }

    internal void GestureTick()
    {
        if (!gesturePending) return;
        if (context.Self.Dead)
        {
            gesturePending = false;
            return;
        }

        if (context.Selection.SelectedId >= 0 && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var target) && target.Attackable)
        {
            if (!basicAttack.InBasicAttackRange(context.Selection.SelectedId)) return;
            if (target.Dead && !EntityActionSystem.Dying(target))
            {
                gesturePending = false;
                skillCast.CastSkill(Gestures.CorpseSkillFor((int)(GD.Randi() % Gestures.CorpseSkillChoices), selfSession.Nation));
                return;
            }
        }

        gesturePending = false;
        StopMoving();
        soccerRequests.Emote(Gestures.TauntFor((int)(GD.Randi() % Gestures.TauntChoices)));
    }
    void IMovement.SendMoveStop() => SendMoveStop();
    void IMovement.CancelClickMove() => CancelClickMove();
    void IMovement.CancelAutoRun() => CancelAutoRun();

    int ISelfView.CharId => avatar.CharId;
    bool ISelfView.IsGm => IsGm;
    bool ISelfView.Dead => avatar.Dead;
    Node3D ISelfView.Body => avatar.Body;
    Node3D ISelfView.Visual => avatar.Visual;
    PlateStack? ISelfView.Plate() => entityPlates.SelfPlate();
    bool ISelfView.Sitting => avatar.Sitting;
    bool ISelfView.Cursed => buff.IsSelfCursed;
    int ISelfView.TransformModel() => skillCast.SelfTransformModel();
    int[] ISelfView.Gear() => entityLooks.SelfGear();
    void ISelfView.Redress(bool force) => entityLooks.RerenderSelfEquipment(force);
    bool ISelfView.Moving => avatar.Moving;
    float ISelfView.Lift => avatar.Lift;
    Vector3 ISelfView.Facing => avatar.Facing;
    (float X, float Z) ISelfView.MapXZ => (avatar.MapX, avatar.MapZ);

    public void Dispose()
    {
        zone.WarpEvent -= OnWarp;
    }

    private const float WalkSpeed = MoveSpeeds.Walk;
    private const float RunSpeed = MoveSpeeds.Run;
    private const float GmSpeedMult = 5.0f;

    internal const float CapsuleHalf = 1.0f;
    internal const uint WorldCollisionLayer = CollisionLayers.World;
    private const uint BlockerCollisionLayer = CollisionLayers.Blocker;
    private const float CapsuleRadius = 0.4f;
    private const float CapsuleHeight = 1.3f;
    private const float CapsuleOffsetY = 1.15f;
    private const float BlockedProgressFraction = 0.25f;
    private readonly MoveSendClock moveClock = new();
    private bool moveSent;
    private float lastSentHeading = float.NaN;
    internal bool IsGm { get; set; }
    private bool collisionsOff;

    private bool GmSpeedHeld => (IsGm || admin.GmSpeedGranted) && Held(KeyAction.GmSpeed);

    private Vector3 moveWish;
    internal void ApplyAuthority(bool isGm, bool collisionsOff)
    {
        this.IsGm = isGm;
        this.collisionsOff = isGm && collisionsOff;
        avatar.NoClip = IsGm && this.collisionsOff;
    }

    internal void HandleInput(double delta)
    {
        avatar.MoveKeyPressed = false;
        walkPressedEdge = false;
        walkKeyHeld = false;

        if (avatar.Dead) { avatar.Moving = false; moveSent = false; return; }
        if (skillMovement.TickSelfDash()) return;
        if (moveGates?.InputHeld == true)
        {
            avatar.Moving = false;
            hasMoveTarget = false;
            terrainMoveHeld = false;
            autoMoveForward = false;
            SendMoveStop();
            return;
        }
        bool typing = context.Root.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
        if (typing && moveGates?.WhisperHasFocus != true && !context.Chat.IsActive) { avatar.Moving = false; SendMoveStop(); return; }

        if (avatar.Sitting)
        {
            if (!SeatedMoveIntent())
            {
                avatar.Moving = false;
                moveWish = Vector3.Zero;
                if (avatar.Body != null) avatar.Body.Velocity = Vector3.Zero;
                SendMoveStop();
                return;
            }
            entityStates.StandUp();
        }

        var wish = ProfileMoveWish(delta, typing);
        moveWish = wish.LengthSquared() > 0.01f ? wish.Normalized() : Vector3.Zero;

        avatar.Moving = wish != Vector3.Zero;
        if (avatar.Moving) SelfMoving?.Invoke();
        if (wish == Vector3.Zero && !turning)
        {
            SettleSelfFall(delta);
            SendMoveStop();
            return;
        }

        wish = moveWish;
        float speed = avatar.MovingBackward ? WalkSpeed : avatar.Running ? RunSpeed : WalkSpeed;
        speed *= buff.MoveSpeedMultiplier();
        if (GmSpeedHeld)
            speed *= GmSpeedMult;
        if (skillCast.IsRootedByCast() || selfSession.Stunned(context.Now) || TouchLocked()) speed = 0f;

        var wasAt = avatar.Body.Position;
        avatar.Body.Velocity = wish * speed * MoveAndSlideScale(delta);
        avatar.Body.MoveAndSlide();

        var resolved = avatar.Body.Position;
        var grounded = SelfGroundAt(resolved);
        grounded.Y = FallToward(resolved.Y, grounded.Y, delta);
        avatar.Body.Position = entities.BlockedByUnits(wasAt, grounded) ? wasAt : grounded;

        if (scenery.CapsuleOverlaps() && !StepOffLedge(grounded, resolved.Y))
            avatar.Body.Position = avatar.LastFreePosition;
        else
            avatar.LastFreePosition = avatar.Body.Position;

        var (mapX, mapZ) = scenery.WorldToMap(avatar.Body.Position);
        avatar.MapX = mapX; avatar.MapZ = mapZ; avatar.MapY = avatar.Body.Position.Y - avatar.Lift;

        if (!skillCast.IsRootedByCast() && (avatar.Moving || turning))
            entityAction.SnapSelfToFaceDir();

        if (TrackBlockedProgress(wasAt, speed, delta))
        {
            avatar.Moving = false;
            speed = 0f;
        }

        bool travelling = avatar.Moving && speed > 0f;
        moveClock.Tick((float)delta);
        if (!travelling && moveSent)
            SendMoveStop();
        else if (travelling)
        {
            float signedSpeed = avatar.MovingBackward ? -speed : speed;
            var mapPos = new Vector2(mapX, mapZ);
            var facing = MapFacing(mapX, mapZ);
            if (!moveSent || moveClock.MoveDue(mapPos, avatar.MapY, avatar.MovingBackward ? -facing : facing))
            {
                var lead = MoveSendClock.Lead(mapPos, facing, signedSpeed);
                requests.Move(lead.X, lead.Y, avatar.MapY, signedSpeed,
                    moveSent ? MoveEcho.Move : MoveEcho.Start, mapX, mapZ, avatar.MapY);
                moveClock.Sent(mapPos, lead, avatar.MapY);
                moveSent = true;
            }
        }
        SendRotateWhenDue(mapX, mapZ);
    }

    private Vector2 MapFacing(float mapX, float mapZ)
    {
        var (faceX, faceZ) = scenery.WorldToMap(avatar.Body.Position + avatar.Facing);
        return new Vector2(faceX - mapX, faceZ - mapZ);
    }

    private void SendRotateWhenDue(float mapX, float mapZ)
    {
        var facing = MapFacing(mapX, mapZ);
        float mapHeading = Coord.MapHeading(facing.X, facing.Y);
        if (!moveClock.RotateDue(mapHeading, lastSentHeading)) return;
        requests.Rotate(mapHeading);
        lastSentHeading = mapHeading;
        moveClock.Rotated();
    }

    private float MoveAndSlideScale(double step)
    {
        double frame = context.Root.GetProcessDeltaTime();
        return frame > 0 ? (float)(step / frame) : 1f;
    }

    internal void SendMoveStop()
    {
        if (!moveSent) return;
        moveSent = false;
        moveClock.Stopped();
        requests.Move(avatar.MapX, avatar.MapZ, avatar.MapY, 0f, MoveEcho.Finish);
    }

    private float selfFallSpeed;

    private Vector3 SelfGroundAt(Vector3 resolved)
    {
        var (mapX, mapZ) = scenery.WorldToMap(resolved);
        var grounded = context.Terrain.GroundPos(mapX, mapZ, resolved.Y - avatar.Lift, avatar.Lift);
        if (scenery.StandingFloor(grounded, resolved.Y - avatar.Lift) is { } floor) grounded.Y = floor + avatar.Lift;
        return grounded;
    }

    private void SettleSelfFall(double delta)
    {
        if (selfFallSpeed <= 0f || avatar.Body == null) return;
        var at = avatar.Body.Position;
        at.Y = FallToward(at.Y, SelfGroundAt(at).Y, delta);
        avatar.Body.Position = at;
        avatar.LastFreePosition = at;
        avatar.MapY = at.Y - avatar.Lift;
    }

    private float FallToward(float y, float groundY, double delta) =>
        Fall.Step(ref selfFallSpeed, y, groundY, (float)delta);

    private bool StepOffLedge(Vector3 below, float edgeY)
    {
        if (edgeY <= below.Y + ScenerySystem.StepUp) return false;
        avatar.Body.Position = new Vector3(below.X, edgeY, below.Z);
        return !scenery.CapsuleOverlaps();
    }

    private bool TrackBlockedProgress(Vector3 wasAt, float speed, double delta)
    {
        var moved = new Vector2(avatar.Body.Position.X - wasAt.X, avatar.Body.Position.Z - wasAt.Z);
        if (!avatar.Moving || speed <= 0f
            || moved.Length() >= speed * (float)delta * BlockedProgressFraction)
            return false;

        hasMoveTarget = false;
        autoMoveForward = false;
        return true;
    }

    private const float HeadingResendRadians = 0.2f;

    internal void SendHeadingIfChanged(float mapX, float mapZ)
    {
        var (faceX, faceZ) = scenery.WorldToMap(avatar.Body.Position + avatar.Facing);
        float mapHeading = Coord.MapHeading(faceX - mapX, faceZ - mapZ);
        if (!float.IsNaN(lastSentHeading) && Mathf.Abs(Mathf.AngleDifference(
                Mathf.DegToRad(mapHeading), Mathf.DegToRad(lastSentHeading))) <= HeadingResendRadians)
            return;
        requests.Rotate(mapHeading);
        lastSentHeading = mapHeading;
        moveClock.Rotated();
    }

    private bool SeatedMoveIntent() =>
        hasMoveTarget || terrainMoveHeld || autoMoveForward
        || Held(KeyAction.MoveForward) || Held(KeyAction.MoveBackward);

    private void OnWarp(float mapX, float mapZ)
    {
        if (avatar.Body == null) return;
        var pos = scenery.EntityGroundPos(mapX, mapZ, ScenerySystem.NoServerHeight, avatar.Lift);
        if (scenery.StandingFloor(pos, pos.Y - avatar.Lift) is { } floor) pos.Y = floor + avatar.Lift;
        avatar.Body.Position = pos;
        avatar.LastFreePosition = pos;
        avatar.MapX = mapX; avatar.MapZ = mapZ; avatar.MapY = pos.Y - avatar.Lift;
        if (context.Now < avatar.QuietWarpsUntil)
        {
            avatar.QuietWarpsUntil = 0;
            return;
        }
        Fx.Spawn(selfSession.LastEnter.Nation == Nations.Karus ? "warp_ka" : "warp_el", avatar.Body, Vector3.Zero,
            oneShot: true);
        Audio.Play(Sfx.WarpZone, pos);
    }

    private static CollisionShape3D BodyCapsule(float radius, float lift) => new()
    {
        Shape = new CapsuleShape3D { Radius = radius, Height = CapsuleHeight },
        Position = new Vector3(0, CapsuleOffsetY - lift, 0),
    };

    internal static CharacterBody3D MakeBody(Node3D visual)
    {
        var body = new CharacterBody3D { MotionMode = CharacterBody3D.MotionModeEnum.Floating };
        body.AddChild(BodyCapsule(CapsuleRadius, 0f));
        body.AddChild(visual);
        return body;
    }

    internal void ApplyCollisionPolicy()
    {
        if (avatar.Body == null) return;
        avatar.Body.CollisionMask = avatar.NoClip ? 0u : WorldCollisionLayer | BlockerCollisionLayer;
    }

    internal event Action? CollisionsChanged;

    internal bool CollisionCommand(string? argument)
    {
        if (!IsGm) return false;
        bool on = argument == null ? collisionsOff : argument.Equals("on", System.StringComparison.OrdinalIgnoreCase);
        SetCollisions(on);
        return true;
    }

    internal void SetCollisions(bool on)
    {
        if (!IsGm) return;
        collisionsOff = !on;
        avatar.NoClip = IsGm && collisionsOff;
        selfSession.SetGmCollisionsOff(collisionsOff);
        ApplyCollisionPolicy();
        CollisionsChanged?.Invoke();
        context.Log.Notice(on ? "Collision enabled." : "Collision disabled — you now walk through everything.");
    }

    internal void StopForInteraction()
    {
        StopMoving();
        basicAttack.StopAutoAttack();
    }

    internal void StopMoving()
    {
        CancelClickMove();
        CancelAutoRun();
    }

    internal void CancelClickMove()
    {
        hasMoveTarget = false;
        terrainMoveHeld = false;
    }

    internal void CancelAutoRun() => autoMoveForward = false;

    internal void SetMoveTarget(Vector3 target)
    {
        moveTarget = target;
        hasMoveTarget = true;
    }

    internal void CancelMoveTarget() => hasMoveTarget = false;

    private Vector3 moveTarget;
    private bool hasMoveTarget;
    private MoveKeys movePrevMask;
    private bool walkPressedEdge;
    private bool clickMovePressedEdge;
    private bool walkKeyHeld;
    private bool terrainMoveHeld;
    private bool skySteering;
    private Vector3? lastGroundTarget;
    private const float ClickMoveMinDistance = 1.5f;
    private const float SkySteerDistance = 20f;
    private bool autoMoveForward;
    private Vector2 terrainMovePointer;
    private double terrainMoveRetargetAccum;
    private bool turning;
    private Node3D? moveIndicator;
    private static readonly float TurnSpeed = Mathf.DegToRad(Turning.KeyboardDegreesPerSecond);
    private const float PadTurnSpeed = 7.0f;
    private const double TerrainMoveRetargetInterval = 1.0 / 30.0;
    private const double MoveIndicatorLife = 2.0;
    private const string MoveIndicatorFx = "target_pointer";

    internal static bool Held(KeyAction action) => KeyBinds.Held(action);

    private Vector3 CameraRelative(Vector2 stick)
    {
        var forward = new Vector3(-Mathf.Sin(camera.CamYaw), 0f, -Mathf.Cos(camera.CamYaw));
        var right = forward.Cross(Vector3.Up);
        return (forward * -stick.Y + right * stick.X).Normalized();
    }

    internal void NoteMoveKey(InputEventKey key)
    {
        if (key.Keycode == KeyBinds.PolledKey(KeyAction.MoveForward)
            || key.Keycode == KeyBinds.PolledKey(KeyAction.MoveBackward))
            autoMoveForward = false;
    }

    internal void ToggleAutoRun()
    {
        autoMoveForward = !autoMoveForward;
        if (!autoMoveForward) return;
        hasMoveTarget = false;
        terrainMoveHeld = false;
        basicAttack.StopAutoAttack();
    }

    internal void ToggleRunMode()
    {
        if (buff.HasActiveFragmentOfManes())
        {
            context.Log.Notice("You cannot run while carrying the Fragment of Manes.");
            return;
        }
        avatar.Running = !avatar.Running;
    }


    internal void UpdateHeldMoveTarget(double delta)
    {
        if (!terrainMoveHeld)
            return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            EndHeldMove();
            return;
        }

        terrainMoveRetargetAccum += delta;
        if (terrainMoveRetargetAccum < TerrainMoveRetargetInterval)
            return;
        terrainMoveRetargetAccum %= TerrainMoveRetargetInterval;

        if (context.Root.GetViewport().GuiGetHoveredControl() != null)
            return;

        terrainMovePointer = context.Root.GetViewport().GetMousePosition();
        SteerHeldMove(terrainMovePointer);
    }

    private void SteerHeldMove(Vector2 pointer)
    {
        if (TrySetTerrainMoveTarget(pointer))
        {
            skySteering = false;
            return;
        }
        if (context.Camera == null || avatar.Body == null || moveGates?.StallBlocksMove() == true) return;
        if (context.Camera.IsPositionBehind(avatar.Body.GlobalPosition)) return;
        var toCursor = pointer - context.Camera.UnprojectPosition(avatar.Body.GlobalPosition);
        if (toCursor.LengthSquared() <= 0f) return;
        moveTarget = avatar.Body.Position + SkySteer.Heading(camera.CamYaw, toCursor) * SkySteerDistance;
        hasMoveTarget = true;
        skySteering = true;
    }

    internal bool HeldMoveActive => terrainMoveHeld;

    internal void ResetHeldMove()
    {
        terrainMoveHeld = false;
        skySteering = false;
        lastGroundTarget = null;
    }

    internal void BeginClickMove(Vector2 pointer)
    {
        bool ground = TrySetTerrainMoveTarget(pointer);
        if (!ground && TryPickTerrainPoint(pointer, avatar.Lift, out _)) return;
        StartHeldMove(pointer);
        if (!ground) SteerHeldMove(pointer);
    }

    internal bool TryStartHeldMove(Vector2 pointer)
    {
        if (!TrySetTerrainMoveTarget(pointer)) return false;
        StartHeldMove(pointer);
        return true;
    }

    private void StartHeldMove(Vector2 pointer)
    {
        clickMovePressedEdge = true;
        terrainMoveHeld = true;
        terrainMovePointer = pointer;
        terrainMoveRetargetAccum = 0.0;
    }

    internal bool DragHeldMove(Vector2 pointer, bool overUi)
    {
        if (!terrainMoveHeld) return false;
        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            EndHeldMove();
            return false;
        }
        if (!overUi)
        {
            terrainMovePointer = pointer;
            SteerHeldMove(pointer);
            terrainMoveRetargetAccum = 0.0;
        }
        return true;
    }

    internal void EndHeldMove()
    {
        terrainMoveHeld = false;
        if (!skySteering) return;
        skySteering = false;
        if (lastGroundTarget is { } ground) moveTarget = ground;
        else hasMoveTarget = false;
    }

    private Vector3 ProfileMoveWish(double delta, bool keyboardBlocked)
    {
        if (terrainMoveHeld
            && !Input.IsMouseButtonPressed(MouseButton.Left))
            terrainMoveHeld = false;

        turning = false;
        avatar.MovingBackward = false;

        var stick = keyboardBlocked ? Vector2.Zero : KeyBinds.MoveStick();
        if (stick != Vector2.Zero && moveGates?.StallBlocksMove() == true) stick = Vector2.Zero;
        if (stick != Vector2.Zero)
        {
            autoMoveForward = false;
            hasMoveTarget = false;
            terrainMoveHeld = false;
            avatar.MoveKeyPressed = movePrevMask == MoveKeys.None;
            walkPressedEdge = avatar.MoveKeyPressed;
            movePrevMask = MoveKeys.Forward;
            walkKeyHeld = false;
            skillCast.NoteMoveInput(true);
            if (avatar.MoveKeyPressed && RangedAutoAttack()) basicAttack.StopAutoAttack();
            var wish = CameraRelative(stick);
            float toTurn = avatar.Facing.SignedAngleTo(wish, Vector3.Up);
            float limit = PadTurnSpeed * (float)delta;
            avatar.Facing = avatar.Facing.Rotated(Vector3.Up, Mathf.Clamp(toTurn, -limit, limit)).Normalized();
            turning = !Mathf.IsZeroApprox(toTurn);
            return avatar.Facing;
        }

        bool moveForward = !keyboardBlocked && Held(KeyAction.MoveForward);
        bool moveBackward = !keyboardBlocked && Held(KeyAction.MoveBackward);
        bool turnLeft = !keyboardBlocked && Held(KeyAction.TurnLeft);
        bool turnRight = !keyboardBlocked && Held(KeyAction.TurnRight);
        bool keyboardControl = moveForward || moveBackward || turnLeft || turnRight;

        var moveMask = (moveForward ? MoveKeys.Forward : MoveKeys.None)
                       | (moveBackward ? MoveKeys.Backward : MoveKeys.None)
                       | (turnLeft ? MoveKeys.TurnLeft : MoveKeys.None)
                       | (turnRight ? MoveKeys.TurnRight : MoveKeys.None);
        avatar.MoveKeyPressed = MoveInput.AnyKeyPressed(movePrevMask, moveMask);
        walkPressedEdge = MoveInput.WalkKeyPressed(movePrevMask, moveMask);
        walkKeyHeld = (moveMask & MoveKeys.Walk) != MoveKeys.None;
        skillCast.NoteMoveInput(keyboardControl);
        movePrevMask = moveMask;
        if (keyboardControl)
        {
            if (moveForward || moveBackward)
                autoMoveForward = false;
            hasMoveTarget = false;
            if (avatar.MoveKeyPressed && RangedAutoAttack()) basicAttack.StopAutoAttack();
        }

        float turn = (turnLeft ? 1f : 0f) - (turnRight ? 1f : 0f);
        if (!Mathf.IsZeroApprox(turn))
        {
            avatar.Facing = avatar.Facing.Rotated(Vector3.Up, turn * TurnSpeed * (float)delta).Normalized();
            turning = true;
        }

        moveForward |= autoMoveForward;
        if ((moveForward || moveBackward) && moveGates?.StallBlocksMove() == true)
        {
            autoMoveForward = false;
            return Vector3.Zero;
        }

        if (moveForward != moveBackward)
        {
            avatar.MovingBackward = moveBackward;
            return moveBackward ? -avatar.Facing : avatar.Facing;
        }

        if (AutoAttackTarget() is { } autoTarget) return AutoAttackWish(autoTarget);

        if (!hasMoveTarget)
            return Vector3.Zero;

        Vector3 toward = moveTarget - avatar.Body.Position;
        toward.Y = 0;
        if (toward.LengthSquared() <= 0.12f * 0.12f)
        {
            hasMoveTarget = false;
            return Vector3.Zero;
        }
        avatar.Facing = toward.Normalized();
        return avatar.Facing;
    }

    private EntityView? AutoAttackTarget() =>
        basicAttack.AutoAttack
        && basicAttack.AutoTargetId >= 0
        && context.Entities.Views.TryGetValue(basicAttack.AutoTargetId, out var target)
    && target.Attackable
    && !target.Dead
        ? target
        : null;

    private bool RangedAutoAttack() => skillCast.BasicRangedAttackSkill() != null;

    private Vector3 AutoAttackWish(EntityView autoTarget)
    {
        if (RangedAutoAttack()) return Vector3.Zero;
        Vector3 towardTarget = autoTarget.Body.GlobalPosition - avatar.Body.Position;
        towardTarget.Y = 0;
        float closeTo = basicAttack.BasicAttackRange(autoTarget) * BasicAttackSystem.ApproachFraction;
        if (towardTarget.LengthSquared() <= closeTo * closeTo) return Vector3.Zero;
        avatar.Facing = towardTarget.Normalized();
        return avatar.Facing;
    }

    private bool TrySetTerrainMoveTarget(Vector2 screenPosition)
    {
        if (moveGates?.StallBlocksMove() == true) return false;
        if (!TryPickTerrainPoint(screenPosition, avatar.Lift, out Vector3 destination))
            return false;
        if (avatar.Body != null && avatar.Body.Position.DistanceTo(destination) <= ClickMoveMinDistance)
            return false;

        moveTarget = destination;
        lastGroundTarget = destination;
        hasMoveTarget = true;
        skillCast.InterruptSelfCast();
        basicAttack.StopAutoAttack();
        ShowMoveIndicator(moveTarget);
        return true;
    }

    internal bool TryPickTerrainPoint(Vector2 screenPosition, float lift, out Vector3 worldPosition)
    {
        worldPosition = Vector3.Zero;
        if (context.Camera == null || avatar.Body == null)
            return false;

        Vector3 from = context.Camera.ProjectRayOrigin(screenPosition);
        Vector3 direction = context.Camera.ProjectRayNormal(screenPosition).Normalized();

        if (scenery.Terrain == null)
        {
            float groundY = avatar.Body.Position.Y;
            if (Mathf.Abs(direction.Y) < 0.0001f)
                return false;
            float distance = (groundY - from.Y) / direction.Y;
            if (distance <= 0)
                return false;
            worldPosition = from + direction * distance;
            return true;
        }

        const float maxRayDistance = 1200f;
        const float rayStep = 4f;
        float previousDistance = 0f;
        float previousHeightDelta = HeightAboveTerrain(from);
        for (float distance = rayStep; distance <= maxRayDistance; distance += rayStep)
        {
            Vector3 point = from + direction * distance;
            float heightDelta = HeightAboveTerrain(point);
            if (!float.IsFinite(heightDelta))
                continue;
            if (heightDelta <= 0f && previousHeightDelta > 0f)
            {
                float low = previousDistance;
                float high = distance;
                for (int i = 0; i < TerrainCrossingSteps; i++)
                {
                    float middle = (low + high) * 0.5f;
                    if (HeightAboveTerrain(from + direction * middle) > 0f)
                        low = middle;
                    else
                        high = middle;
                }

                Vector3 hit = from + direction * high;
                var (mapX, mapZ) = scenery.WorldToMap(hit);
                worldPosition = context.Terrain.GroundPos(mapX, mapZ, hit.Y, lift);
                return true;
            }

            previousDistance = distance;
            previousHeightDelta = heightDelta;
        }

        return false;
    }

    internal void ShowMoveIndicator(Vector3 destination)
    {
        if (moveIndicator != null
            && GodotObject.IsInstanceValid(moveIndicator))
        {
            moveIndicator.GlobalPosition = destination + Vector3.Up * 0.04f;
            return;
        }

        var marker = Fx.Spawn(
            MoveIndicatorFx,
            context.Root,
            destination + Vector3.Up * 0.04f);
        if (marker == null)
        {
            GD.PushWarning($"[controls] missing movement marker FX: {MoveIndicatorFx}");
            moveIndicator = null;
            return;
        }

        marker.Name = "MoveIndicator";
        moveIndicator = marker;
        ScheduleMoveIndicatorExpiry(marker);
    }

    private void ScheduleMoveIndicatorExpiry(Node3D marker)
    {
        context.Root.After(MoveIndicatorLife, () =>
        {
            if (!GodotObject.IsInstanceValid(marker))
                return;
            if (terrainMoveHeld)
            {
                ScheduleMoveIndicatorExpiry(marker);
                return;
            }

            marker.QueueFree();
            if (ReferenceEquals(moveIndicator, marker))
                moveIndicator = null;
        });
    }

    private const int TerrainCrossingSteps = 12;

    private float HeightAboveTerrain(Vector3 worldPoint) =>
        scenery.GroundHeight(worldPoint) is { } ground ? worldPoint.Y - ground : float.NaN;

    private double touchCooldown;
    private double tremorClock;
    private double touchLockUntil;

    private bool TouchLocked() => context.Now < touchLockUntil;

    internal void NpcTouchTick(double delta)
    {
        if (touchCooldown > 0)
        {
            touchCooldown -= delta;
            return;
        }
        if (avatar.Body == null) return;

        var selfPosition = avatar.Body.GlobalPosition;
        float selfRadius = context.Entities.Views.TryGetValue(context.Self.CharId, out var me) ? me.Radius : SelfAvatar.DefaultBodyRadius;
        Tremor strongest = default;
        Node3D? source = null;
        bool touched = false;
        foreach (var (id, e) in context.Entities.Views)
        {
            if (!e.IsNpc || e.Dead || !GodotObject.IsInstanceValid(e.Body) || NpcTouch.Ignored(e.ModelId) || !SelectionFeature.Restriction.Touch.Contains(e.NpcId))
                continue;
            float distance = e.Body.GlobalPosition.DistanceTo(selfPosition);
            var tremor = NpcTouch.TremorFrom(e.ModelId, distance, EntityRig.EntityMoving(e));
            if (tremor.Power > strongest.Power)
            {
                strongest = tremor;
                source = e.Body;
            }
            float radius = e.Radius > 0f ? e.Radius : NpcTouch.UnknownRadius;
            if (avatar.Dead || distance >= NpcTouch.ContactDistance(e.ModelId, selfRadius, radius)) continue;
            if (!NpcTouch.HurtsOnContact(e.ModelId, e.Nation == selfSession.Nation)) continue;

            npcQuestRequests.NpcTouch(id);
            touchCooldown = NpcTouch.Cooldown;
            touched = true;
            if (NpcTouch.PicksUpCube(e.ModelId, context.Zone))
            {
                StopSelfMoving();
                npcQuestRequests.NpcEvent(id);
            }
            if (e.ModelId == NpcTouch.SawBladeModel)
            {
                StopSelfMoving();
                touchLockUntil = context.Now + NpcTouch.SawBladeLockSeconds;
            }
        }

        tremorClock += delta;
        if (strongest.Power <= 0f || (tremorClock <= NpcTouch.TremorInterval && !touched)) return;
        tremorClock = 0;
        camera.ShakeCamera(NpcTouch.TremorSeconds, NpcTouch.Amplitude(strongest, touched), NpcTouch.TremorRate);
        Audio.PlayAt(NpcTouch.TremorSound, source);
    }

    private void StopSelfMoving()
    {
        hasMoveTarget = false;
        terrainMoveHeld = false;
        autoMoveForward = false;
    }
}
