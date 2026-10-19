using Godot;

namespace LibreKO.Presentation;

internal static class PickGeometry
{
    public static Aabb XformAabb(Transform3D t, Aabb a)
    {
        var lo = a.Position;
        var hi = a.Position + a.Size;
        var first = t * lo;
        var box = new Aabb(first, Vector3.Zero);
        for (int i = 1; i < 8; i++)
            box = box.Expand(t * new Vector3(
                (i & 1) != 0 ? hi.X : lo.X,
                (i & 2) != 0 ? hi.Y : lo.Y,
                (i & 4) != 0 ? hi.Z : lo.Z));
        return box;
    }

    public static bool RayAabbEntry(Vector3 o, Vector3 d, Aabb box, out float t)
    {
        t = 0f;
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        Vector3 lo = box.Position, hi = box.Position + box.Size;
        for (int a = 0; a < 3; a++)
        {
            float oa = o[a], da = d[a];
            if (Mathf.Abs(da) < 1e-9f)
            {
                if (oa < lo[a] || oa > hi[a]) return false;
            }
            else
            {
                float inv = 1f / da;
                float t1 = (lo[a] - oa) * inv, t2 = (hi[a] - oa) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
        }
        if (tmax < 0f) return false;
        t = Mathf.Max(tmin, 0f);
        return true;
    }
}
