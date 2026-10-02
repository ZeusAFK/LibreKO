using System.Collections.Generic;
using System.Text;
using Godot;

namespace LibreKO;

public static class CharacterMerge
{
    private const string MergedName = "merged_body";
    private const string PartsMeta = "merged_parts";
    private const string MergedIntoMeta = "merged_into";

    private static readonly Dictionary<string, ArrayMesh?> _meshes = Shutdown.Track(new Dictionary<string, ArrayMesh?>());

    public static int CachedMeshes => _meshes.Count;

    public static GeometryInstance3D ShownAs(MeshInstance3D part) =>
        part.HasMeta(MergedIntoMeta) && part.GetMeta(MergedIntoMeta).AsGodotObject() is MeshInstance3D merged
            && GodotObject.IsInstanceValid(merged)
            ? merged
            : part;

    public static void Apply(Node3D body)
    {
        Remove(body);
        var sources = new List<(ArrayMesh Mesh, int Surface)>();
        var slots = new List<OutfitMaterial.Slot>();
        var used = new List<(MeshInstance3D Part, ArrayMesh Mesh)>();
        Skin? skin = null;
        Node? parent = null;
        foreach (var part in Ordered(World.BodyParts(body)))
        {
            if (!part.Visible || part.Mesh is not ArrayMesh mesh || part.Skin == null) continue;
            if (part.Transform != Transform3D.Identity || part.MaterialOverlay != null) continue;
            if (skin != null && (part.Skin != skin || part.GetParent() != parent)) continue;
            int count = mesh.GetSurfaceCount();
            if (count == 0 || slots.Count + count > OutfitMaterial.MaxSlots) continue;
            var (level, shinePart) = ShineOf(part);
            var partSlots = new List<OutfitMaterial.Slot>();
            for (int i = 0; i < count && partSlots.Count == i; i++)
                if (mesh.SurfaceGetPrimitiveType(i) == Mesh.PrimitiveType.Triangles
                    && OutfitMaterial.TryDescribe(part.GetActiveMaterial(i), level, shinePart, out var slot))
                    partSlots.Add(slot);
            if (partSlots.Count != count) continue;
            skin ??= part.Skin;
            parent ??= part.GetParent();
            for (int i = 0; i < count; i++) sources.Add((mesh, i));
            slots.AddRange(partSlots);
            used.Add((part, mesh));
        }
        if (used.Count < 2 || skin == null || parent == null) return;
        if (Combined(sources) is not { } combined) return;

        var first = used[0].Part;
        var merged = new MeshInstance3D
        {
            Name = MergedName,
            Mesh = combined,
            Skin = skin,
            Skeleton = first.Skeleton,
            CastShadow = first.CastShadow,
            Transparency = first.Transparency,
            VisibilityRangeEnd = first.VisibilityRangeEnd,
            VisibilityRangeEndMargin = first.VisibilityRangeEndMargin,
            VisibilityRangeFadeMode = first.VisibilityRangeFadeMode,
            Layers = first.Layers,
        };
        merged.SetSurfaceOverrideMaterial(0, OutfitMaterial.For(slots));
        parent.AddChild(merged);
        SkinShare.FixBounds(merged);
        var stored = new Godot.Collections.Array();
        foreach (var (part, mesh) in used)
        {
            stored.Add(part);
            stored.Add(mesh);
            part.SetMeta(MergedIntoMeta, merged);
            part.Visible = false;
            part.Mesh = null;
        }
        merged.SetMeta(PartsMeta, stored);
    }

    public static void Remove(Node3D body)
    {
        foreach (var merged in FindMerged(body))
        {
            if (merged.HasMeta(PartsMeta))
            {
                var stored = merged.GetMeta(PartsMeta).AsGodotArray();
                for (int i = 0; i + 1 < stored.Count; i += 2)
                {
                    if (stored[i].AsGodotObject() is not MeshInstance3D part || !GodotObject.IsInstanceValid(part)) continue;
                    part.Mesh = stored[i + 1].AsGodotObject() as Mesh;
                    part.Visible = true;
                    part.RemoveMeta(MergedIntoMeta);
                }
            }
            merged.GetParent()?.RemoveChild(merged);
            merged.QueueFree();
        }
    }

    private static (int Level, int Part) ShineOf(MeshInstance3D part)
    {
        foreach (var child in part.GetChildren())
            if (child is ItemShineDriver driver && driver.Level > 0) return (driver.Level, driver.PartIndex);
        return (0, 0);
    }

    private static List<MeshInstance3D> FindMerged(Node node)
    {
        var found = new List<MeshInstance3D>();
        void Walk(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is MeshInstance3D mi && mi.Name == MergedName) found.Add(mi);
                else Walk(child);
            }
        }
        Walk(node);
        return found;
    }

    private static IEnumerable<MeshInstance3D> Ordered(Dictionary<int, MeshInstance3D> parts)
    {
        var keys = new List<int>(parts.Keys);
        keys.Sort();
        foreach (int key in keys) yield return parts[key];
    }

    private static ArrayMesh? Combined(List<(ArrayMesh Mesh, int Surface)> sources)
    {
        var key = new StringBuilder();
        foreach (var s in sources) key.Append(s.Mesh.GetInstanceId()).Append(':').Append(s.Surface).Append(',');
        string id = key.ToString();
        if (_meshes.TryGetValue(id, out var cached)) return cached;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var slotUvs = new List<Vector2>();
        var bones = new List<int>();
        var weights = new List<float>();
        var indices = new List<int>();
        int influences = 0;
        ArrayMesh? mesh = null;
        bool compatible = true;
        for (int slot = 0; slot < sources.Count && compatible; slot++)
        {
            var a = sources[slot].Mesh.SurfaceGetArrays(sources[slot].Surface);
            var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var b = a[(int)Mesh.ArrayType.Bones].VariantType == Variant.Type.Nil
                ? System.Array.Empty<int>() : a[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var w = a[(int)Mesh.ArrayType.Weights].VariantType == Variant.Type.Nil
                ? System.Array.Empty<float>() : a[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int per = v.Length > 0 ? b.Length / v.Length : 0;
            if (influences == 0) influences = per;
            if (per == 0 || per != influences || w.Length != b.Length
                || a[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil)
            {
                compatible = false;
                break;
            }
            int start = verts.Count;
            verts.AddRange(v);
            var n = a[(int)Mesh.ArrayType.Normal];
            AppendOrFill(normals, n.VariantType == Variant.Type.Nil ? System.Array.Empty<Vector3>() : n.AsVector3Array(), v.Length, Vector3.Up);
            var uv = a[(int)Mesh.ArrayType.TexUV];
            AppendOrFill(uvs, uv.VariantType == Variant.Type.Nil ? System.Array.Empty<Vector2>() : uv.AsVector2Array(), v.Length, Vector2.Zero);
            for (int i = 0; i < v.Length; i++) slotUvs.Add(new Vector2(slot, 0f));
            bones.AddRange(b);
            weights.AddRange(w);
            foreach (int index in a[(int)Mesh.ArrayType.Index].AsInt32Array()) indices.Add(start + index);
        }
        if (compatible)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = slotUvs.ToArray();
            arrays[(int)Mesh.ArrayType.Bones] = bones.ToArray();
            arrays[(int)Mesh.ArrayType.Weights] = weights.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
                influences > 4 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0);
        }
        _meshes[id] = mesh;
        return mesh;
    }

    private static void AppendOrFill<T>(List<T> target, T[] values, int count, T fill)
    {
        if (values.Length == count) target.AddRange(values);
        else for (int i = 0; i < count; i++) target.Add(fill);
    }
}
