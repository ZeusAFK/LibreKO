using Godot;

namespace LibreKO.Features.Core;

internal sealed class GateSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ObjectEventSession objectEvent;
    private readonly IObjectEventRequests objectEventRequests;
    private readonly ScenerySystem scenery;
    private readonly CameraSystem camera;
    private readonly MapInteractablesSystem mapInteractables;
    private readonly EntitiesSession entitiesSession;

    public GateSystem(IWorldContext context, ObjectEventSession objectEvent, IObjectEventRequests objectEventRequests, SelfAvatar avatar, ScenerySystem scenery, CameraSystem camera, MapInteractablesSystem mapInteractables, EntitiesSession entitiesSession)
    {
        this.context = context;
        this.objectEvent = objectEvent;
        this.objectEventRequests = objectEventRequests;
        this.avatar = avatar;
        this.scenery = scenery;
        this.camera = camera;
        this.mapInteractables = mapInteractables;
        this.entitiesSession = entitiesSession;
    }

    private readonly List<(Vector2 MapXZ, bool Open)> gateStates = new();

    public void Build()
    {
        entitiesSession.EntitySpawnEvent += OnEntitySpawn;
        scenery.ObjectsReset += ClearObjects;
        scenery.ObjectBuilt += AddGateCollider;
    }

    public void Dispose()
    {
        entitiesSession.EntitySpawnEvent -= OnEntitySpawn;
        scenery.ObjectsReset -= ClearObjects;
        scenery.ObjectBuilt -= AddGateCollider;
    }

    private void OnEntitySpawn(EntitySnapshot info)
    {
        if (info.Id != context.Self.CharId && info.IsNpc && info.ObjectType != 0) NoteGateState(info.X, info.Z, info.GateOpen != 0);
    }

    private void ClearObjects()
    {
        GateColliders.Clear();
        KeyGates.Clear();
        GasShapes.Clear();
    }

    internal List<GateRig.GateCollider> GateColliders { get; } = new();

    private void AddGateCollider(MapObject o, Node3D placed)
    {
        if (scenery.ObjRoot == null) return;
        if (o.EventType == ObjectEventType.KeyGate)
        {
            RegisterKeyGate(o, placed);
            return;
        }
        if (o.EventType == ObjectEventType.PoisonGas)
        {
            RegisterGasShape(o, placed);
            return;
        }
        if (o.EventType is ObjectEventType.GateLever or ObjectEventType.LeverFlip)
        {
            RegisterGate(new GateRig.GateCollider { MapXZ = new Vector2(o.MapPos.X, o.MapPos.Z), EventType = o.EventType }, placed);
            return;
        }
        if (o.EventType is not (ObjectEventType.Gate or ObjectEventType.Gate2)) return;
        var faces = new List<Vector3>();
        GateRig.CollectGateFaces(placed, faces);
        if (faces.Count == 0) return;

        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces.ToArray());
        var body = new StaticBody3D { Name = $"Gate{o.EventId}", CollisionLayer = 0, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        scenery.ObjRoot.AddChild(body);

        RegisterGate(new GateRig.GateCollider { MapXZ = new Vector2(o.MapPos.X, o.MapPos.Z), Body = body, EventType = o.EventType }, placed);
    }

    private void RegisterGate(GateRig.GateCollider gate, Node3D placed)
    {
        GateRig.CollectGateWings(placed, gate.Wings);
        if (gate.Body == null && gate.Wings.Count == 0) return;
        gate.Wings.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        GateColliders.Add(gate);
        foreach (var (at, open) in gateStates)
            if (at.DistanceTo(gate.MapXZ) <= GateRig.GateMatchRadius) GateRig.SetGateClosed(gate, !open);
    }

    internal void NoteGateState(float mapX, float mapZ, bool open, bool flip = false, bool animate = false)
    {
        var at = new Vector2(mapX, mapZ);
        foreach (var gate in GateColliders)
        {
            if (gate.MapXZ.DistanceTo(at) > GateRig.GateMatchRadius) continue;
            if (flip && gate.Wings.Count > 0 && GodotObject.IsInstanceValid(gate.Wings[0])) open = gate.Wings[0].Rotation.X > 0f;
            GateRig.SetGateClosed(gate, !open);
        }
        NoteMapObjectState(at, open, animate);
        gateStates.RemoveAll(s => s.MapXZ.DistanceTo(at) <= GateRig.GateMatchRadius);
        gateStates.Add((at, open));
    }

    private const int KeyGateEffect = 20029;
    private const int KeyGateOpenSound = 30002;
    private const int KeyGateMoveSound = 30003;
    private const int KeyGateLandSound = 6151;
    private const int TextObjectTooFar = 1914;
    private const float KeyGateReachFactor = 2f;
    private const float RadiusFromDiagonal = 0.5f;
    private static readonly Color KeyGateTextColour = new(0f, 1f, 0f);
    private static readonly Color ObjectTooFarColour = new(1f, 0.5f, 0.5f);

    internal sealed class KeyGate
    {
        public Node3D Node = null!;
        public Vector3 Base;
        public Vector2 MapXZ;
        public int EventId;
        public float Radius;
        public readonly GateSlide Slide = new();
    }

    internal sealed class GasShape
    {
        public Node3D Node = null!;
        public Vector2 MapXZ;
    }

    internal List<KeyGate> KeyGates { get; } = new();
    internal List<GasShape> GasShapes { get; } = new();

    private void RegisterKeyGate(MapObject o, Node3D placed)
    {
        var gate = new KeyGate
        {
            Node = placed,
            Base = placed.Position,
            MapXZ = new Vector2(o.MapPos.X, o.MapPos.Z),
            EventId = o.EventId,
            Radius = ShapeRadius(placed),
        };
        AttachKeyGateCollider(placed);
        KeyGates.Add(gate);
        foreach (var (at, open) in gateStates)
            if (at.DistanceTo(gate.MapXZ) <= GateRig.GateMatchRadius) gate.Slide.Place(open);
        PlaceKeyGate(gate);
    }

    private void RegisterGasShape(MapObject o, Node3D placed)
    {
        var gas = new GasShape { Node = placed, MapXZ = new Vector2(o.MapPos.X, o.MapPos.Z) };
        GasShapes.Add(gas);
        foreach (var (at, on) in gateStates)
            if (at.DistanceTo(gas.MapXZ) <= GateRig.GateMatchRadius) placed.Visible = on;
    }

    private void AttachKeyGateCollider(Node3D placed)
    {
        var faces = new List<Vector3>();
        GateRig.CollectGateFaces(placed, faces);
        if (faces.Count == 0) return;
        var toLocal = placed.GlobalTransform.AffineInverse();
        for (int i = 0; i < faces.Count; i++) faces[i] = toLocal * faces[i];
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces.ToArray());
        var body = new StaticBody3D { Name = "KeyGateCollider", CollisionLayer = PlayerSystem.WorldCollisionLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        placed.AddChild(body);
    }

    private static float ShapeRadius(Node3D placed)
    {
        if (NpcGatesFeature.LocalMeshBounds(placed, placed) is not { } bounds) return 0f;
        var scale = placed.Basis.Scale;
        float grow = Mathf.Max(Mathf.Abs(scale.X), Mathf.Max(Mathf.Abs(scale.Y), Mathf.Abs(scale.Z)));
        return bounds.Size.Length() * RadiusFromDiagonal * grow;
    }

    private void NoteMapObjectState(Vector2 at, bool open, bool animate)
    {
        foreach (var gate in KeyGates)
        {
            if (gate.MapXZ.DistanceTo(at) > GateRig.GateMatchRadius) continue;
            if (animate) gate.Slide.SetOpen(open);
            else gate.Slide.Place(open);
            PlaceKeyGate(gate);
        }
        foreach (var gas in GasShapes)
            if (gas.MapXZ.DistanceTo(at) <= GateRig.GateMatchRadius && GodotObject.IsInstanceValid(gas.Node))
                gas.Node.Visible = open;
    }

    private static void PlaceKeyGate(KeyGate gate)
    {
        if (!GodotObject.IsInstanceValid(gate.Node)) return;
        gate.Node.Position = gate.Base + Vector3.Up * gate.Slide.Current;
    }

    internal void KeyGateTick(double delta)
    {
        foreach (var gate in KeyGates)
        {
            if (!gate.Slide.Moving) continue;
            if (gate.Slide.Step((float)delta)) Audio.PlayAt(KeyGateLandSound, gate.Node);
            PlaceKeyGate(gate);
        }
    }

    private void OnKeyGateState(int uniqueId, bool open)
    {
        if (!context.Entities.Views.TryGetValue(uniqueId, out var ent)) return;
        camera.ShakeCamera(CameraShake.GateSeconds, CameraShake.GateAmplitude, CameraShake.GateRate);
        if (Fx.NameForId(KeyGateEffect) is { } fx) Fx.Spawn(fx, context.Root, ent.Body.GlobalPosition, oneShot: true);
        context.Log.Notice(open
            ? GameText.System(TextGateOpened, "The Castle Gate has opened")
            : GameText.System(TextGateClosed, "The Castle Gate has been closed"), KeyGateTextColour);
        if (open) Audio.PlayAt(KeyGateOpenSound, ent.Body);
        NoteGateState(ent.MapX, ent.MapZ, open, animate: true);
        Audio.PlayAt(KeyGateMoveSound, ent.Body);
    }

    internal bool TryOperateKeyGate(EntityView e, int npcUniqueId)
    {
        if (e.NpcType != NpcTypes.KeyGate) return false;
        var at = new Vector2(e.MapX, e.MapZ);
        KeyGate? gate = null;
        foreach (var candidate in KeyGates)
            if (candidate.MapXZ.DistanceTo(at) <= GateRig.GateMatchRadius) { gate = candidate; break; }
        if (gate == null) return true;

        float self = context.Entities.Views.TryGetValue(context.Self.CharId, out var me) ? me.Radius : SelfAvatar.DefaultBodyRadius;
        if (Coord.FlatDistance(avatar.Body.Position, gate.Node.GlobalPosition) > KeyGateReachFactor * (self + gate.Radius))
        {
            context.Log.Notice(GameText.System(TextObjectTooFar, "You are too far away from the object."), ObjectTooFarColour);
            return true;
        }
        TryOperateObject((short)gate.EventId, npcUniqueId);
        return true;
    }


    internal void ObjectEventInit()
    {
        objectEvent.ObjectEventResultEvent += OnObjectEventResult;
        objectEvent.ObjectEventGateStateEvent += OnObjectGateState;
        objectEvent.ObjectEffectEvent += OnObjectEffect;
        objectEvent.ObjectTurnEvent += OnObjectTurn;
    }

    internal void ObjectEventDispose()
    {
        objectEvent.ObjectEventResultEvent -= OnObjectEventResult;
        objectEvent.ObjectEventGateStateEvent -= OnObjectGateState;
        objectEvent.ObjectEffectEvent -= OnObjectEffect;
        objectEvent.ObjectTurnEvent -= OnObjectTurn;
    }

    private static readonly StringName ObjectTurnAngleMeta = "object_turn_angle";
    private static readonly StringName ObjectTurnBaseMeta = "object_turn_base";

    private void OnObjectTurn(int objectId, byte direction, short degrees)
    {
        if (!context.Entities.Views.TryGetValue(objectId, out var ent) || !GodotObject.IsInstanceValid(ent.Body)) return;
        var part = ent.Body.GetNodeOrNull<Node3D>(ModelAnimation.ModelNodeName) ?? ent.Body;
        if (!part.HasMeta(ObjectTurnBaseMeta)) part.SetMeta(ObjectTurnBaseMeta, part.Rotation.Y);
        float baseYaw = part.GetMeta(ObjectTurnBaseMeta).AsSingle();
        float current = part.HasMeta(ObjectTurnAngleMeta) ? part.GetMeta(ObjectTurnAngleMeta).AsSingle() : 0f;
        float target = ObjectTurn.TargetRadians(degrees);
        float from = ObjectTurn.StartFrom(current, target, direction);
        part.SetMeta(ObjectTurnAngleMeta, target);
        part.CreateTween().TweenMethod(Callable.From<float>(angle =>
        {
            if (GodotObject.IsInstanceValid(part)) part.Rotation = new Vector3(part.Rotation.X, baseYaw - angle, part.Rotation.Z);
        }), from, target, ObjectTurn.Seconds(from, target));
        Audio.PlayAt(ObjectTurn.SoundId, ent.Body);
    }

    private const float ObjectEffectHeight = 1.0f;

    private void OnObjectEffect(int entityId, int effectId)
    {
        string? fx = Fx.NameForId(effectId) ?? SkillData.Get(effectId)?.TargetFx;
        if (string.IsNullOrEmpty(fx)) return;
        context.Entities.SpawnFxOn(entityId, fx, ObjectEffectHeight);
    }

    private const string AnvilSuccessFx = "item_success";
    private const string AnvilFailFx = "item_fail";

    public bool TryOperateObject(short objectIndex, int npcId)
    {
        if (!context.Ready || avatar.Dead) return false;
        objectEventRequests.Send(objectIndex, npcId);
        return true;
    }

    private const int TextGateClosed = 1801;
    private const int TextGateOpened = 1802;
    private const int TextLeverActivated = 2801;
    private const int TextLeverDeactivated = 2802;
    private const int TextLeverFailed = 1005;
    private const int LeverSound = 340101;
    private const int TextBindFailed = 1001;
    private const int TextBindDone = 1003;
    private const int TextWrongWarpGate = 1004;

    private void OnObjectEventResult(ObjectEventType type, bool success, int objectId)
    {
        if (!success && ObjectEventWire.IsGateObject(type))
        {
            context.Log.Notice(GameText.System(TextLeverFailed, "Failed turning the lever"));
            return;
        }
        switch (type)
        {
            case ObjectEventType.Anvil:
                SpawnAnvilFx(objectId, success ? AnvilSuccessFx : AnvilFailFx);
                break;
            case ObjectEventType.Bind:
            case ObjectEventType.RemoveBind:
                context.Log.Notice(success
                    ? GameText.System(TextBindDone, "Successfully designated a re-spawn point.")
                    : GameText.System(TextBindFailed, "Failed designating re-spawn point."));
                break;
            case ObjectEventType.WarpGate when !success:
                context.Log.Notice(GameText.System(TextWrongWarpGate, "You've selected the wrong Warp Gate."));
                break;
        }
    }

    private static string? GateStateText(ObjectEventType type, bool open) => type switch
    {
        ObjectEventType.Gate or ObjectEventType.Gate2 => open
            ? GameText.System(TextGateOpened, "The Castle Gate has opened")
            : GameText.System(TextGateClosed, "The Castle Gate has been closed"),
        ObjectEventType.GateLever => open
            ? GameText.System(TextLeverActivated, "The lever has been activated.")
            : GameText.System(TextLeverDeactivated, "The lever has been deactivated."),
        ObjectEventType.LeverFlip => GameText.System(TextLeverActivated, "The lever has been activated."),
        _ => null,
    };

    private void SpawnAnvilFx(int anvilId, string fx)
    {
        var anchor = mapInteractables.Anvils.Anchor(anvilId);
        if (anchor != null) Fx.Spawn(fx, anchor, Vector3.Zero, oneShot: true);
    }

    private void OnObjectGateState(ObjectEventType type, int uniqueId, bool gateOpen)
    {
        if (type == ObjectEventType.KeyGate)
        {
            OnKeyGateState(uniqueId, gateOpen);
            return;
        }
        if (!context.Entities.Views.TryGetValue(uniqueId, out var ent)) return;
        NoteGateState(ent.MapX, ent.MapZ, gateOpen, flip: type == ObjectEventType.LeverFlip);
        if (type == ObjectEventType.GateLever) Audio.PlayAt(LeverSound, ent.Body);
        if (GateStateText(type, gateOpen) is { } text) context.Log.Notice(text);
    }
}
