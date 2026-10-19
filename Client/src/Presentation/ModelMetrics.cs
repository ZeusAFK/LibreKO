using Godot;

namespace LibreKO.Presentation;

public static class ModelMetrics
{
    public static float TopY(Node node)
    {
        float top = 0f;
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var a = mi.Mesh.GetAabb();
            top = a.Position.Y + a.Size.Y;
        }
        foreach (var c in node.GetChildren())
            top = Mathf.Max(top, TopY(c));
        return top;
    }
}
