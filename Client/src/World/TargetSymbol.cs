using Godot;

namespace LibreKO;

public static class TargetSymbol
{
    public const int PlaneRing = 0;
    public const int PlaneSwirl = 1;
    public const int PlaneStar = 2;
    public const int PlaneElMorad = 3;
    public const int PlaneKarus = 4;

    public const float MinRadius = 3.5f;

    private const float GroundLift = 0.12f;

    private static QuadMesh? _mesh;

    public static Mesh Mesh() => _mesh ??= new QuadMesh
    {
        Size = new Vector2(2f, 2f),
        Orientation = PlaneMesh.OrientationEnum.Y,
    };

    public static ShaderMaterial Material(int plane = PlaneRing)
    {
        var mat = Shaders.Material("target_symbol");
        mat.SetShaderParameter("symbol", ResourceLoader.Load<Texture2D>(
            $"res://assets/ui/target/target_symbol_plane_{plane}.png"));
        return mat;
    }

    public static void Tint(ShaderMaterial mat, Color colour) => mat.SetShaderParameter("tint", colour);

    public static void Place(Node3D marker, Vector3 groundCentre, float radius, Vector3 up)
    {
        var side = Mathf.Abs(up.X) < 0.99f ? Vector3.Right : Vector3.Forward;
        var fwd = up.Cross(side).Normalized();
        var right = fwd.Cross(up).Normalized();
        marker.Transform = new Transform3D(new Basis(right * radius, up, fwd * radius), groundCentre + up * GroundLift);
        marker.Visible = true;
    }

    public static void Place(Node3D marker, Vector3 groundCentre, float radius)
    {
        marker.Position = groundCentre + new Vector3(0f, GroundLift, 0f);
        marker.Scale = new Vector3(radius, 1f, radius);
        marker.Visible = true;
    }
}
