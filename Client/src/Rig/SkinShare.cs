using System.Collections.Generic;
using System.Text;
using Godot;

namespace LibreKO;

public static class SkinShare
{
    private const float PoseRounding = 1e-4f;
    private const float AabbGrowFraction = 0.6f;
    private const float AabbGrowMin = 0.3f;

    private static readonly Dictionary<string, Skin> _canonical = Shutdown.Track(new Dictionary<string, Skin>());
    private static readonly Dictionary<ulong, Skin> _byInstance = Shutdown.Track(new Dictionary<ulong, Skin>());

    public static int Distinct => _canonical.Count;

    public static Skin? Canonical(Skin? skin)
    {
        if (skin == null) return null;
        ulong id = skin.GetInstanceId();
        if (_byInstance.TryGetValue(id, out var known)) return known;
        string key = KeyOf(skin);
        if (!_canonical.TryGetValue(key, out var shared)) _canonical[key] = shared = skin;
        _byInstance[id] = shared;
        return shared;
    }

    public static void ShareUnder(Node node)
    {
        if (node is MeshInstance3D { Skin: { } skin } mi)
        {
            mi.Skin = Canonical(skin);
            FixBounds(mi);
        }
        foreach (var child in node.GetChildren()) ShareUnder(child);
    }

    public static void FixBounds(MeshInstance3D mi)
    {
        if (mi.Skin == null || mi.Mesh == null) return;
        var rest = mi.Mesh.GetAabb();
        mi.CustomAabb = rest.Grow(Mathf.Max(AabbGrowMin, rest.GetLongestAxisSize() * AabbGrowFraction));
    }

    private static string KeyOf(Skin skin)
    {
        var sb = new StringBuilder();
        int count = skin.GetBindCount();
        sb.Append(count).Append(':');
        for (int i = 0; i < count; i++)
        {
            sb.Append(skin.GetBindName(i)).Append('/').Append(skin.GetBindBone(i)).Append('/');
            var t = skin.GetBindPose(i);
            Append(sb, t.Basis.X); Append(sb, t.Basis.Y); Append(sb, t.Basis.Z); Append(sb, t.Origin);
            sb.Append(';');
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, Vector3 v)
    {
        sb.Append(Mathf.Snapped(v.X, PoseRounding)).Append(',')
          .Append(Mathf.Snapped(v.Y, PoseRounding)).Append(',')
          .Append(Mathf.Snapped(v.Z, PoseRounding)).Append('|');
    }
}
