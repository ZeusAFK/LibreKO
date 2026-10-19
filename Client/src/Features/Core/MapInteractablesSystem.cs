using Godot;

namespace LibreKO.Features.Core;

internal sealed class MapInteractablesSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly IObjectEventRequests objectEventRequests;
    private readonly ScenerySystem scenery;

    public MapInteractablesSystem(IWorldContext context, SelfAvatar avatar, IObjectEventRequests objectEventRequests, ScenerySystem scenery)
    {
        this.context = context;
        this.avatar = avatar;
        this.objectEventRequests = objectEventRequests;
        this.scenery = scenery;
    }

    public void Build()
    {
        scenery.ObjectsPlaced += RebuildPlaces;
    }

    public void Dispose()
    {
        scenery.ObjectsPlaced -= RebuildPlaces;
    }

    private void RebuildPlaces(IReadOnlyList<MapObject> objects)
    {
        WarpGates.Rebuild(context.Root, objects);
        Anvils.Rebuild(context.Root, objects);
    }

    private const float AnvilInteractRange = 10f;
    private const float AnvilPickRadius = 70f;
    private const float AnvilTagHeight = 4.5f;
    private const float AnvilMinHeight = 3f;
    private const float AnvilTagLift = 0.7f;
    private const string AnvilLabel = "Magic Anvil";

    internal MapInteractables Anvils { get; } = new("Anvils", (int)ObjectEventType.Anvil, _ => AnvilLabel,
        AnvilTagHeight, AnvilMinHeight, AnvilTagLift, AnvilPickRadius);

    internal bool TrySelectAnvil(Vector2 mouse)
    {
        if (!context.Ready) return false;
        var anvil = Anvils.Pick(context.Camera, mouse);
        if (anvil == null) return false;
        context.Selection.Deselect();
        Anvils.Select(anvil);
        return true;
    }

    internal bool TryOpenAnvil(Vector2 mouse)
    {
        if (!context.Ready) return false;
        var anvil = Anvils.Pick(context.Camera, mouse);
        if (anvil == null) return false;
        Anvils.Select(anvil);
        OpenAnvil(anvil);
        return true;
    }

    private void OpenAnvil(MapInteractables.Item anvil)
    {
        if (!context.Ready || avatar.Body == null || avatar.Dead) return;
        if (Coord.FlatDistance(avatar.Body.Position, anvil.Tag.GlobalPosition) > AnvilInteractRange)
        {
            context.Log.Notice("Move closer to the anvil.");
            return;
        }
        context.Movement.StopForInteraction();
        Operate((short)anvil.Obj.EventId, anvil.Obj.NpcId);
    }

    internal event Action<int, string>? WarpGateOpened;

    private bool Operate(short objectIndex, int npcId)
    {
        if (!context.Ready || avatar.Dead) return false;
        objectEventRequests.Send(objectIndex, npcId);
        return true;
    }

    public int AnvilCount => Anvils.Items.Count;

    public string AnvilBounds()
    {
        if (Anvils.Items.Count == 0) return "none";
        var first = Anvils.Items[0];
        var box = Anvils.Volume(first);
        return $"origin={first.Obj.Origin} aabb pos={box.Position} size={box.Size}";
    }

    internal bool HasNearbyAnvil() =>
        context.Ready && avatar.Body != null && !avatar.Dead && Anvils.AnyWithin(avatar.Body.Position, AnvilInteractRange);

    public bool OpenNearestAnvil()
    {
        if (!context.Ready || avatar.Body == null || avatar.Dead) return false;
        var best = Anvils.Nearest(avatar.Body.Position, AnvilInteractRange);
        if (best == null) return false;
        Anvils.Select(best);
        OpenAnvil(best);
        return true;
    }

    public bool OperateNearestAnvil()
    {
        if (!context.Ready || avatar.Body == null) return false;
        var best = Anvils.Closest(avatar.Body.Position);
        if (best == null) return false;
        Anvils.Select(best);
        return Operate((short)best.Obj.EventId, best.Obj.NpcId);
    }

    private const float WarpGateInteractRange = 10f;
    private const float WarpGatePickRadius = 90f;
    private const float WarpGateTagHeight = 6.5f;
    private const float WarpGateMinHeight = 6f;
    private const float WarpGateTagLift = 0.8f;

    internal MapInteractables WarpGates { get; } = new("WarpGates", (int)ObjectEventType.WarpGate, o => WarpGateName(o.Belong),
        WarpGateTagHeight, WarpGateMinHeight, WarpGateTagLift, WarpGatePickRadius);

    private static string WarpGateName(int belong) => belong switch
    {
        Nations.Karus => "Karus Warp Gate",
        Nations.ElMorad => "El Morad Warp Gate",
        _ => "Warp Gate",
    };

    internal bool TrySelectWarpGate(Vector2 mouse)
    {
        var gate = WarpGates.Pick(context.Camera, mouse);
        if (gate == null) return false;
        context.Selection.Deselect();
        WarpGates.Select(gate);
        return true;
    }

    internal bool TryOpenWarpGate(Vector2 mouse)
    {
        var gate = WarpGates.Pick(context.Camera, mouse);
        if (gate == null) return false;
        OpenWarpGate(gate);
        return true;
    }

    private void OpenWarpGate(MapInteractables.Item gate)
    {
        if (avatar.Dead) return;
        if (Coord.FlatDistance(avatar.Body.Position, gate.Tag.GlobalPosition) > WarpGateInteractRange) return;
        context.Movement.StopForInteraction();
        WarpGateOpened?.Invoke(gate.Obj.NpcId, gate.Tag.Text);
        Operate((short)gate.Obj.EventId, (short)gate.Obj.NpcId);
    }

    internal bool HasNearbyWarpGate() =>
        context.Ready && avatar.Body != null && !avatar.Dead && WarpGates.AnyWithin(avatar.Body.Position, WarpGateInteractRange);

    public bool OpenNearestWarpGate()
    {
        if (!context.Ready || avatar.Body == null || avatar.Dead) return false;
        var best = WarpGates.Nearest(avatar.Body.Position, WarpGateInteractRange);
        if (best == null) return false;
        OpenWarpGate(best);
        return true;
    }
}
