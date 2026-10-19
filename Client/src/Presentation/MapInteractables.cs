using Godot;

namespace LibreKO.Presentation;

internal sealed class MapInteractables
{
    internal sealed class Item
    {
        public MapObject Obj = null!;
        public Label3D Tag = null!;
        public bool TagPlaced;
    }

    private readonly string rootName;
    private readonly int eventType;
    private readonly Func<MapObject, string> label;
    private readonly float tagHeight;
    private readonly float minHeight;
    private readonly float tagLift;
    private readonly float pickRadius;
    private readonly List<Item> items = new();
    private Node3D? host;
    private Node3D? root;
    private MeshInstance3D? ring;

    public MapInteractables(string rootName, int eventType, Func<MapObject, string> label,
        float tagHeight, float minHeight, float tagLift, float pickRadius)
    {
        this.rootName = rootName;
        this.eventType = eventType;
        this.label = label;
        this.tagHeight = tagHeight;
        this.minHeight = minHeight;
        this.tagLift = tagLift;
        this.pickRadius = pickRadius;
    }

    public IReadOnlyList<Item> Items => items;

    public Item? Selected { get; private set; }

    public void Rebuild(Node3D host, IEnumerable<MapObject> objects)
    {
        this.host = host;
        items.Clear();
        Selected = null;
        if (ring != null) ring.Visible = false;
        root?.QueueFree();
        root = null;

        foreach (var o in objects)
        {
            if (o.EventType != (ObjectEventType)eventType || o.EventId <= 0) continue;
            if (root == null)
            {
                root = new Node3D { Name = rootName };
                host.AddChild(root);
            }
            var tag = NamePlate.MapObject(label(o));
            tag.Modulate = UiTheme.GoldBright;
            tag.Position = o.Origin + new Vector3(0f, tagHeight, 0f);
            root.AddChild(tag);
            items.Add(new Item { Obj = o, Tag = tag });
        }
    }

    public Aabb Volume(Item item)
    {
        var origin = item.Obj.Origin;
        var box = new Aabb(
            origin - new Vector3(TargetSymbol.MinRadius, 0f, TargetSymbol.MinRadius),
            new Vector3(TargetSymbol.MinRadius * 2f, minHeight, TargetSymbol.MinRadius * 2f));
        if (item.Obj.Mi is { Mesh: not null } mi)
            box = box.Merge(PickGeometry.XformAabb(mi.GlobalTransform, mi.Mesh.GetAabb()));
        return box;
    }

    public void Tick()
    {
        foreach (var item in items)
        {
            if (item.TagPlaced) continue;
            if (item.Obj.Mi is { Mesh: not null }) item.TagPlaced = true;
            var box = Volume(item);
            var centre = box.GetCenter();
            item.Tag.Position = new Vector3(centre.X, box.Position.Y + box.Size.Y + tagLift, centre.Z);
            if (ReferenceEquals(item, Selected)) PlaceRing();
        }
    }

    public Item? Pick(Camera3D? camera, Vector2 mouse)
    {
        if (camera == null || items.Count == 0) return null;
        var from = camera.ProjectRayOrigin(mouse);
        var dir = camera.ProjectRayNormal(mouse);

        Item? best = null;
        float bestDist = float.MaxValue;
        foreach (var item in items)
        {
            var anchor = item.Tag.GlobalPosition;
            if (!PickGeometry.RayAabbEntry(from, dir, Volume(item), out float hitDist))
            {
                if (camera.IsPositionBehind(anchor)) continue;
                if (camera.UnprojectPosition(anchor).DistanceTo(mouse) > pickRadius) continue;
                hitDist = from.DistanceTo(anchor);
            }
            if (hitDist < bestDist) { bestDist = hitDist; best = item; }
        }
        return best;
    }

    public void Select(Item? item)
    {
        if (ReferenceEquals(Selected, item)) return;
        if (Selected != null) Selected.Tag.Modulate = UiTheme.GoldBright;
        Selected = item;
        if (item != null) item.Tag.Modulate = Colors.White;
        PlaceRing();
    }

    public bool AnyWithin(Vector3 from, float range)
    {
        foreach (var item in items)
            if (Coord.FlatDistance(from, item.Tag.GlobalPosition) <= range)
                return true;
        return false;
    }

    public Item? Nearest(Vector3 from, float range)
    {
        Item? best = null;
        float bestDist = range;
        foreach (var item in items)
        {
            float d = Coord.FlatDistance(from, item.Tag.GlobalPosition);
            if (d <= bestDist) { bestDist = d; best = item; }
        }
        return best;
    }

    public Item? Closest(Vector3 from)
    {
        Item? best = null;
        float bestDist = float.MaxValue;
        foreach (var item in items)
        {
            float d = Coord.FlatDistance(from, item.Obj.Origin);
            if (d < bestDist) { bestDist = d; best = item; }
        }
        return best;
    }

    public Node3D? Anchor(int eventId)
    {
        foreach (var item in items)
        {
            if (item.Obj.EventId != eventId) continue;
            return item.Obj.Mi ?? (Node3D)item.Tag;
        }
        return Selected?.Obj.Mi ?? (Node3D?)Selected?.Tag;
    }

    private void PlaceRing()
    {
        if (Selected == null)
        {
            if (ring != null) ring.Visible = false;
            return;
        }
        if (ring == null)
        {
            ring = new MeshInstance3D
            {
                Mesh = TargetSymbol.Mesh(),
                MaterialOverride = TargetSymbol.Material(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            host!.AddChild(ring);
        }
        var box = Volume(Selected);
        TargetSymbol.Place(ring, Selected.Obj.Origin, Mathf.Max(box.Size.X, box.Size.Z) * 0.5f);
    }
}
