using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class SceneryBuilder
{
    public static Node3D? Build(Terrain terrain, Vector3 centreMap, float radius)
    {
        var objs = ZoneObjects.LoadJson(terrain.ZoneStem);
        if (objs == null)
            return null;

        var root = new Node3D { Name = "Scenery" };
        var cache = new Dictionary<string, PackedScene?>();
        float radiusSq = radius * radius;
        int built = 0, missing = 0;

        foreach (var o in objs.Items)
        {
            if (radius > 0f)
            {
                float dx = o.MapPos.X - centreMap.X;
                float dz = o.MapPos.Z - centreMap.Z;
                if (dx * dx + dz * dz > radiusSq)
                    continue;
            }

            string key = ModelNodes.SafeModelName(o.Name);
            if (!cache.TryGetValue(key, out var scene))
            {
                string path = $"res://assets/objects/{key}.glb";
                scene = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
                cache[key] = scene;
            }
            if (scene == null) { missing++; continue; }

            var instance = scene.Instantiate<Node3D>();
            SceneryRig.ConfigureObjectMaterials(instance);
            var rotation = new Quaternion(o.Rot.X, -o.Rot.Y, -o.Rot.Z, o.Rot.W).Normalized();
            instance.Transform = new Transform3D(
                terrain.Transform.Basis * new Basis(rotation) * Basis.FromScale(o.Scale),
                terrain.MapToWorld(o.MapPos.X, o.MapPos.Y, o.MapPos.Z));

            if (FindFirst<AnimationPlayer>(instance) is { } animation)
            {
                var names = animation.GetAnimationList();
                if (names.Length > 0) animation.Play(names[0]);
            }

            root.AddChild(instance);
            built++;
        }

        GD.Print($"[scenery] {terrain.ZoneStem}: {built} objects" + (missing > 0 ? $" ({missing} unbaked)" : ""));
        return root;
    }

    private static T? FindFirst<T>(Node node) where T : class
    {
        if (node is T match) return match;
        foreach (Node child in node.GetChildren())
            if (FindFirst<T>(child) is { } found) return found;
        return null;
    }
}
