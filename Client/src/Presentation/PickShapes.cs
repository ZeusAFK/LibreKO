using Godot;

namespace LibreKO.Presentation;

internal static class PickShapes
{
    internal static ArrayMesh BuildWireBoxMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        Vector3 C(int i) => new((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
        for (int i = 0; i < 8; i++)
            foreach (int bit in new[] { 1, 2, 4 })
                if ((i & bit) == 0) { st.AddVertex(C(i)); st.AddVertex(C(i | bit)); }
        return st.Commit();
    }

    private const float FxBoxMinSize = 0.7f;

    internal static Aabb FxBounds(FxRegistry.Entry fx)
    {
        fx.LocalBounds ??= MeasureFxLocalBounds(fx.Node);
        var box = PickGeometry.XformAabb(fx.Node.GlobalTransform,
            fx.LocalBounds ?? new Aabb(new Vector3(-0.35f, -0.35f, -0.35f), Vector3.One * 0.7f));
        var size = box.Size;
        float gx = Mathf.Max(0f, FxBoxMinSize - size.X) * 0.5f;
        float gy = Mathf.Max(0f, FxBoxMinSize - size.Y) * 0.5f;
        float gz = Mathf.Max(0f, FxBoxMinSize - size.Z) * 0.5f;
        return new Aabb(box.Position - new Vector3(gx, gy, gz), size + new Vector3(gx, gy, gz) * 2f);
    }

    private const float FxBoxMaxHalf = 12f;

    private static Aabb? MeasureFxLocalBounds(Node3D root)
    {
        var toLocal = root.GlobalTransform.AffineInverse();
        Aabb? acc = null;
        Walk(root);
        if (!acc.HasValue) return null;
        var lo = Clamp(acc.Value.Position);
        var hi = Clamp(acc.Value.Position + acc.Value.Size);
        return new Aabb(lo, hi - lo);

        static Vector3 Clamp(Vector3 v) => new(
            Mathf.Clamp(v.X, -FxBoxMaxHalf, FxBoxMaxHalf),
            Mathf.Clamp(v.Y, -FxBoxMaxHalf, FxBoxMaxHalf),
            Mathf.Clamp(v.Z, -FxBoxMaxHalf, FxBoxMaxHalf));

        void Walk(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is Node3D n3)
                {
                    var local = toLocal * n3.GlobalTransform;
                    Aabb? box = child switch
                    {
                        Light3D => null,
                        GpuParticles3D => new Aabb(local.Origin - Vector3.One * 0.4f, Vector3.One * 0.8f),
                        VisualInstance3D vi => PickGeometry.XformAabb(local, vi.GetAabb()),
                        _ => null,
                    };
                    if (box.HasValue) acc = acc.HasValue ? acc.Value.Merge(box.Value) : box.Value;
                }
                Walk(child);
            }
        }
    }

    private const int PickTriLimit = 300_000;

    internal static (Vector3[] Verts, int[] Idx) BuildTriSoup(Mesh mesh)
    {
        var verts = new List<Vector3>();
        var idx = new List<int>();
        int surfaces = mesh.GetSurfaceCount();
        for (int s = 0; s < surfaces; s++)
        {
            if (mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
            var arrays = mesh.SurfaceGetArrays(s);
            var pv = arrays[(int)Mesh.ArrayType.Vertex];
            if (pv.VariantType != Variant.Type.PackedVector3Array) continue;
            var sv = pv.AsVector3Array();
            int baseIdx = verts.Count;
            verts.AddRange(sv);
            var iv = arrays[(int)Mesh.ArrayType.Index];
            if (iv.VariantType != Variant.Type.Nil)
                foreach (int i in iv.AsInt32Array()) idx.Add(baseIdx + i);
            else
                for (int i = 0; i < sv.Length; i++) idx.Add(baseIdx + i);
            if (idx.Count / 3 > PickTriLimit) return (System.Array.Empty<Vector3>(), System.Array.Empty<int>());
        }
        return (verts.ToArray(), idx.ToArray());
    }

    internal static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 e1 = b - a, e2 = c - a, p = d.Cross(e2);
        float det = e1.Dot(p);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = s.Dot(p) * inv;
        if (u < -1e-6f || u > 1f + 1e-6f) return false;
        Vector3 q = s.Cross(e1);
        float v = d.Dot(q) * inv;
        if (v < -1e-6f || u + v > 1f + 1e-6f) return false;
        t = e2.Dot(q) * inv;
        return t >= 0f;
    }
}
