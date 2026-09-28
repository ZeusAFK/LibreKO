using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const float AnvilInteractRange = 10f;
    private const float AnvilPickRadius = 70f;
    private const float AnvilTagHeight = 4.5f;
    private const float AnvilMinHeight = 3f;

    private sealed class Anvil
    {
        public ObjInfo Obj = null!;
        public Label3D Tag = null!;
        public bool TagPlaced;
    }

    private readonly List<Anvil> _anvils = new();
    private Node3D? _anvilRoot;
    private Anvil? _selectedAnvil;
    private MeshInstance3D? _anvilRing;

    private void RebuildAnvils()
    {
        _anvils.Clear();
        _selectedAnvil = null;
        if (_anvilRing != null) _anvilRing.Visible = false;
        _anvilRoot?.QueueFree();
        _anvilRoot = null;

        foreach (var o in _objects)
        {
            if (o.EventType != Net.ObjectEventAnvil || o.EventId <= 0) continue;
            if (_anvilRoot == null)
            {
                _anvilRoot = new Node3D { Name = "Anvils" };
                AddChild(_anvilRoot);
            }
            var tag = NamePlate.MapObject("Magic Anvil");
            tag.Modulate = UiTheme.GoldBright;
            tag.Position = o.Origin + new Vector3(0f, AnvilTagHeight, 0f);
            _anvilRoot.AddChild(tag);
            _anvils.Add(new Anvil { Obj = o, Tag = tag });
        }
    }

    private Aabb AnvilVolume(Anvil anvil)
    {
        var origin = anvil.Obj.Origin;
        var box = new Aabb(
            origin - new Vector3(TargetSymbol.MinRadius, 0f, TargetSymbol.MinRadius),
            new Vector3(TargetSymbol.MinRadius * 2f, AnvilMinHeight, TargetSymbol.MinRadius * 2f));
        if (anvil.Obj.Mi is { Mesh: not null } mi)
            box = box.Merge(XformAabb(mi.GlobalTransform, mi.Mesh.GetAabb()));
        return box;
    }

    private void AnvilTick()
    {
        foreach (var anvil in _anvils)
        {
            if (anvil.TagPlaced) continue;
            if (anvil.Obj.Mi is { Mesh: not null }) anvil.TagPlaced = true;
            var box = AnvilVolume(anvil);
            var centre = box.GetCenter();
            anvil.Tag.Position = new Vector3(centre.X, box.Position.Y + box.Size.Y + 0.7f, centre.Z);
            if (ReferenceEquals(anvil, _selectedAnvil)) UpdateAnvilRing();
        }
    }

    private Anvil? PickAnvilAt(Vector2 mouse)
    {
        if (_camera == null || _anvils.Count == 0) return null;
        var from = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);

        Anvil? best = null;
        float bestDist = float.MaxValue;
        foreach (var anvil in _anvils)
        {
            var anchor = anvil.Tag.GlobalPosition;
            if (!RayAabbEntry(from, dir, AnvilVolume(anvil), out float hitDist))
            {
                if (_camera.IsPositionBehind(anchor)) continue;
                if (_camera.UnprojectPosition(anchor).DistanceTo(mouse) > AnvilPickRadius) continue;
                hitDist = from.DistanceTo(anchor);
            }
            if (hitDist < bestDist) { bestDist = hitDist; best = anvil; }
        }
        return best;
    }

    private bool TrySelectAnvil(Vector2 mouse)
    {
        if (!_worldReady) return false;
        var anvil = PickAnvilAt(mouse);
        if (anvil == null) return false;
        _selectedId = -1;
        _selfClip = null;
        StopAutoAttack();
        SelectWarpGate(null);
        SelectAnvil(anvil);
        return true;
    }

    private void SelectAnvil(Anvil? anvil)
    {
        if (ReferenceEquals(_selectedAnvil, anvil)) return;
        if (_selectedAnvil != null) _selectedAnvil.Tag.Modulate = UiTheme.GoldBright;
        _selectedAnvil = anvil;
        if (anvil != null) anvil.Tag.Modulate = Colors.White;
        UpdateAnvilRing();
    }

    private void UpdateAnvilRing()
    {
        if (_selectedAnvil == null)
        {
            if (_anvilRing != null) _anvilRing.Visible = false;
            return;
        }
        if (_anvilRing == null)
        {
            _anvilRing = new MeshInstance3D
            {
                Mesh = TargetSymbol.Mesh(),
                MaterialOverride = TargetSymbol.Material(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_anvilRing);
        }
        var box = AnvilVolume(_selectedAnvil);
        var origin = _selectedAnvil.Obj.Origin;
        TargetSymbol.Place(_anvilRing, new Vector3(origin.X, origin.Y, origin.Z),
            Mathf.Max(box.Size.X, box.Size.Z) * 0.5f);
    }

    private bool TryOpenAnvil(Vector2 mouse)
    {
        if (!_worldReady) return false;
        var anvil = PickAnvilAt(mouse);
        if (anvil == null) return false;
        SelectAnvil(anvil);
        OpenAnvil(anvil);
        return true;
    }

    private void OpenAnvil(Anvil anvil)
    {
        if (!_worldReady || _self == null || _selfDead) return;
        if (FlatDistance(_self.Position, anvil.Tag.GlobalPosition) > AnvilInteractRange)
        {
            CombatNotice("Move closer to the anvil.");
            return;
        }
        StopForInteraction();
        TryOperateObject((short)anvil.Obj.EventId, anvil.Obj.NpcId);
    }

    public int AnvilCount => _anvils.Count;

    public string AnvilBounds()
    {
        if (_anvils.Count == 0) return "none";
        var box = AnvilVolume(_anvils[0]);
        return $"origin={_anvils[0].Obj.Origin} aabb pos={box.Position} size={box.Size}";
    }

    private bool HasNearbyAnvil()
    {
        if (!_worldReady || _self == null || _selfDead) return false;
        foreach (var anvil in _anvils)
            if (FlatDistance(_self.Position, anvil.Tag.GlobalPosition) <= AnvilInteractRange)
                return true;
        return false;
    }

    public bool OpenNearestAnvil()
    {
        if (!_worldReady || _self == null || _selfDead) return false;
        Anvil? best = null;
        float bestDist = AnvilInteractRange;
        foreach (var anvil in _anvils)
        {
            float d = FlatDistance(_self.Position, anvil.Tag.GlobalPosition);
            if (d <= bestDist) { bestDist = d; best = anvil; }
        }
        if (best == null) return false;
        SelectAnvil(best);
        OpenAnvil(best);
        return true;
    }

    public bool OperateNearestAnvil()
    {
        if (!_worldReady || _self == null) return false;
        Anvil? best = null;
        float bestDist = float.MaxValue;
        foreach (var anvil in _anvils)
        {
            float d = FlatDistance(_self.Position, anvil.Obj.Origin);
            if (d < bestDist) { bestDist = d; best = anvil; }
        }
        if (best == null) return false;
        SelectAnvil(best);
        return TryOperateObject((short)best.Obj.EventId, best.Obj.NpcId);
    }

    private Node3D? AnvilAnchor(int eventId)
    {
        foreach (var anvil in _anvils)
        {
            if (anvil.Obj.EventId != eventId) continue;
            return anvil.Obj.Mi ?? (Node3D)anvil.Tag;
        }
        return _selectedAnvil?.Obj.Mi ?? (Node3D?)_selectedAnvil?.Tag;
    }
}
