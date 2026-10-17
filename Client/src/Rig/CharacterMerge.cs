using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace LibreKO;

public static class CharacterMerge
{
    private const string MergedName = "merged_body";
    private const string PartsMeta = "merged_parts";
    private const string TicketMeta = "merge_ticket";

    private sealed class Plan
    {
        public readonly List<(ArrayMesh Mesh, int Surface)> Sources = new();
        public readonly List<OutfitMaterial.Slot> Slots = new();
        public readonly List<(MeshInstance3D Part, ArrayMesh Mesh)> Used = new();
        public Skin? Skin;
        public Node? Parent;
    }

    private sealed record Surfaces(Vector3[] Verts, Vector3[] Normals, Vector2[] Uvs, Vector2[] SlotUvs,
                                   int[] Bones, float[] Weights, int[] Indices, int Influences);

    private static readonly Dictionary<string, ArrayMesh?> _meshes = Shutdown.Track(new Dictionary<string, ArrayMesh?>());
    private static readonly Dictionary<string, List<(Node3D Body, long Ticket)>> _reading = new();
    private static readonly ConditionalWeakTable<MeshInstance3D, MeshInstance3D> _mergedInto = new();
    private static long _lastTicket;

    public static int CachedMeshes => _meshes.Count;

    public static GeometryInstance3D ShownAs(MeshInstance3D part) =>
        _mergedInto.TryGetValue(part, out var merged) && GodotObject.IsInstanceValid(merged) ? merged : part;

    public static void Apply(Node3D body)
    {
        Remove(body);
        var plan = PlanFor(body);
        if (plan.Used.Count < 2 || plan.Skin == null || plan.Parent == null) return;
        string id = KeyOf(plan.Sources);
        if (_meshes.TryGetValue(id, out var cached))
        {
            if (cached != null) Swap(plan, cached);
            return;
        }

        long ticket = ++_lastTicket;
        body.SetMeta(TicketMeta, ticket);
        if (_reading.TryGetValue(id, out var waiting))
        {
            waiting.Add((body, ticket));
            return;
        }
        _reading[id] = new List<(Node3D Body, long Ticket)> { (body, ticket) };
        var sources = plan.Sources;
        Task.Run(() =>
        {
            Surfaces? surfaces = null;
            try { surfaces = Read(sources); }
            catch (System.Exception ex) { GD.PushWarning($"[merge] surfaces could not be read: {ex.Message}"); }
            Callable.From(() => Finish(id, surfaces)).CallDeferred();
        });
    }

    private static void Finish(string id, Surfaces? surfaces)
    {
        if (!_reading.Remove(id, out var waiting)) return;
        var alive = waiting.FindAll(w => GodotObject.IsInstanceValid(w.Body)
            && w.Body.HasMeta(TicketMeta) && w.Body.GetMeta(TicketMeta).AsInt64() == w.Ticket);
        if (alive.Count == 0) return;
        _meshes[id] = surfaces == null ? null : Build(surfaces);
        foreach (var (body, _) in alive) Apply(body);
    }

    private static Plan PlanFor(Node3D body)
    {
        var plan = new Plan();
        foreach (var part in Ordered(World.BodyParts(body)))
        {
            if (!part.Visible || part.Mesh is not ArrayMesh mesh || part.Skin == null) continue;
            if (part.Transform != Transform3D.Identity || part.MaterialOverlay != null) continue;
            if (plan.Skin != null && (part.Skin != plan.Skin || part.GetParent() != plan.Parent)) continue;
            int count = mesh.GetSurfaceCount();
            if (count == 0 || plan.Slots.Count + count > OutfitMaterial.MaxSlots) continue;
            var (level, shinePart) = ShineOf(part);
            var partSlots = new List<OutfitMaterial.Slot>();
            for (int i = 0; i < count && partSlots.Count == i; i++)
                if (mesh.SurfaceGetPrimitiveType(i) == Mesh.PrimitiveType.Triangles
                    && OutfitMaterial.TryDescribe(part.GetActiveMaterial(i), level, shinePart, out var slot))
                    partSlots.Add(slot);
            if (partSlots.Count != count) continue;
            plan.Skin ??= part.Skin;
            plan.Parent ??= part.GetParent();
            for (int i = 0; i < count; i++) plan.Sources.Add((mesh, i));
            plan.Slots.AddRange(partSlots);
            plan.Used.Add((part, mesh));
        }
        return plan;
    }

    private static void Swap(Plan plan, ArrayMesh combined)
    {
        var first = plan.Used[0].Part;
        var merged = new MeshInstance3D
        {
            Name = MergedName,
            Mesh = combined,
            Skin = plan.Skin,
            Skeleton = first.Skeleton,
            CastShadow = first.CastShadow,
            Transparency = first.Transparency,
            VisibilityRangeEnd = first.VisibilityRangeEnd,
            VisibilityRangeEndMargin = first.VisibilityRangeEndMargin,
            VisibilityRangeFadeMode = first.VisibilityRangeFadeMode,
            Layers = first.Layers,
        };
        merged.SetSurfaceOverrideMaterial(0, OutfitMaterial.For(plan.Slots));
        plan.Parent!.AddChild(merged);
        SkinShare.FixBounds(merged);
        var stored = new Godot.Collections.Array();
        foreach (var (part, mesh) in plan.Used)
        {
            stored.Add(part);
            stored.Add(mesh);
            _mergedInto.AddOrUpdate(part, merged);
            part.Visible = false;
            part.Mesh = null;
        }
        merged.SetMeta(PartsMeta, stored);
    }

    public static void Remove(Node3D body)
    {
        body.RemoveMeta(TicketMeta);
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
                    _mergedInto.Remove(part);
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

    private static string KeyOf(List<(ArrayMesh Mesh, int Surface)> sources)
    {
        var key = new StringBuilder();
        foreach (var s in sources) key.Append(s.Mesh.GetInstanceId()).Append(':').Append(s.Surface).Append(',');
        return key.ToString();
    }

    private static Surfaces? Read(List<(ArrayMesh Mesh, int Surface)> sources)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var slotUvs = new List<Vector2>();
        var bones = new List<int>();
        var weights = new List<float>();
        var indices = new List<int>();
        int influences = 0;
        for (int slot = 0; slot < sources.Count; slot++)
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
                return null;
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
        return new Surfaces(verts.ToArray(), normals.ToArray(), uvs.ToArray(), slotUvs.ToArray(),
                            bones.ToArray(), weights.ToArray(), indices.ToArray(), influences);
    }

    private static ArrayMesh Build(Surfaces s)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = s.Verts;
        arrays[(int)Mesh.ArrayType.Normal] = s.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = s.Uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = s.SlotUvs;
        arrays[(int)Mesh.ArrayType.Bones] = s.Bones;
        arrays[(int)Mesh.ArrayType.Weights] = s.Weights;
        arrays[(int)Mesh.ArrayType.Index] = s.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
            s.Influences > 4 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0);
        return mesh;
    }

    private static void AppendOrFill<T>(List<T> target, T[] values, int count, T fill)
    {
        if (values.Length == count) target.AddRange(values);
        else for (int i = 0; i < count; i++) target.Add(fill);
    }
}
