using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace LibreKO;

internal static class SceneCensus
{
    private const int TopClasses = 40;
    private const int TopCanvasOwners = 25;
    private const int OwnerDepth = 5;
    private const int TopDrawGroups = 30;

    private sealed class Tally
    {
        public int Nodes, Visible, Process, PhysicsProcess, InternalProcess, InternalPhysics;
    }

    internal static string Write(SceneTree tree)
    {
        var byClass = new Dictionary<string, Tally>();
        var canvasOwners = new Dictionary<string, int>();
        int visibleCanvas = 0, visibleVisual = 0;
        Walk(tree.Root, "", 0, byClass, canvasOwners, ref visibleCanvas, ref visibleVisual);
        var draws = new Dictionary<string, (int Instances, int Surfaces)>();
        var camera = tree.Root.GetCamera3D();
        if (camera != null) CountDraws(tree.Root, "(root)", camera, camera.GlobalPosition, draws);

        var sb = new StringBuilder();
        sb.AppendLine($"census frame {Engine.GetProcessFrames()}  nodes {byClass.Values.Sum(t => t.Nodes)}"
                      + $"  visible canvas items {visibleCanvas}  visible 3D visuals {visibleVisual}");
        sb.AppendLine();
        sb.AppendLine("class                               nodes  visible  process  physics  int.proc  int.phys");
        foreach (var (name, t) in byClass.OrderByDescending(kv => kv.Value.Nodes).Take(TopClasses))
            sb.AppendLine($"{name,-34} {t.Nodes,6} {t.Visible,8} {t.Process,8} {t.PhysicsProcess,8} {t.InternalProcess,9} {t.InternalPhysics,9}");
        sb.AppendLine();
        sb.AppendLine("processing classes (any kind)");
        foreach (var (name, t) in byClass
                     .Where(kv => kv.Value.Process + kv.Value.PhysicsProcess + kv.Value.InternalProcess + kv.Value.InternalPhysics > 0)
                     .OrderByDescending(kv => kv.Value.Process + kv.Value.InternalProcess + kv.Value.PhysicsProcess + kv.Value.InternalPhysics))
            sb.AppendLine($"{name,-34} process {t.Process,6}  physics {t.PhysicsProcess,6}  internal {t.InternalProcess,6}  internal physics {t.InternalPhysics,6}");
        sb.AppendLine();
        sb.AppendLine("estimated draw surfaces in view (instances / surfaces incl. next passes and label outlines)");
        sb.AppendLine($"total {draws.Values.Sum(d => d.Instances)} instances / {draws.Values.Sum(d => d.Surfaces)} surfaces");
        foreach (var (group, d) in draws.OrderByDescending(kv => kv.Value.Surfaces).Take(TopDrawGroups))
            sb.AppendLine($"{d.Surfaces,7} surfaces {d.Instances,6} instances  {group}");
        sb.AppendLine();
        sb.AppendLine("visible canvas items by owner path");
        foreach (var (path, n) in canvasOwners.OrderByDescending(kv => kv.Value).Take(TopCanvasOwners))
            sb.AppendLine($"{n,6}  {path}");

        if (EngineProfile.Enabled)
        {
            sb.AppendLine();
            sb.AppendLine("engine passes (smoothed)");
            sb.Append(EngineProfile.Report());
        }
        string file = CaptureFiles.Path("scene_census.txt");
        using (var f = Godot.FileAccess.Open(file, Godot.FileAccess.ModeFlags.Write))
            f?.StoreString(sb.ToString());
        return file;
    }

    private static void CountDraws(Node node, string category, Camera3D camera, Vector3 eye,
        Dictionary<string, (int Instances, int Surfaces)> draws)
    {
        if (node is Node3D { Visible: false } || node is CanvasItem) return;
        if (node.GetParent() is { } parent && parent.Name == "World") category = node.Name;
        if (node is GeometryInstance3D gi)
        {
            var pos = gi.GlobalPosition;
            bool inRange = gi.VisibilityRangeEnd <= 0f || pos.DistanceTo(eye) <= gi.VisibilityRangeEnd;
            if (inRange && camera.IsPositionInFrustum(pos))
            {
                int surfaces = SurfacesOf(gi);
                if (surfaces > 0)
                {
                    string group = $"{category} / {Kind(gi)}";
                    (int Instances, int Surfaces) d = draws.TryGetValue(group, out var cur) ? cur : (0, 0);
                    draws[group] = (d.Instances + 1, d.Surfaces + surfaces);
                }
            }
        }
        foreach (var child in node.GetChildren()) CountDraws(child, category, camera, eye, draws);
    }

    private static string Kind(GeometryInstance3D gi) => gi switch
    {
        MeshInstance3D { Skin: not null } => "skinned mesh",
        Label3D => "label",
        FxSharedEmitter => "particles (shared)",
        MultiMeshInstance3D => "multimesh",
        _ => gi.GetScript().VariantType != Variant.Type.Nil ? gi.GetType().Name : gi.GetClass(),
    };

    private static int SurfacesOf(GeometryInstance3D gi)
    {
        switch (gi)
        {
            case Label3D label:
                return label.Text.Length == 0 ? 0 : label.OutlineSize > 0 ? 2 : 1;
            case MultiMeshInstance3D mm:
                return mm.Multimesh is { } m && m.VisibleInstanceCount != 0 ? 1 : 0;
            case MeshInstance3D mi when mi.Mesh != null:
                int total = 0;
                for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                {
                    var material = mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i);
                    total++;
                    for (var next = material?.NextPass; next != null; next = next.NextPass) total++;
                    if (mi.MaterialOverlay != null) total++;
                }
                return total;
            case GpuParticles3D:
                return 1;
            default:
                return 1;
        }
    }

    private static void Walk(Node node, string owner, int depth, Dictionary<string, Tally> byClass,
        Dictionary<string, int> canvasOwners, ref int visibleCanvas, ref int visibleVisual)
    {
        string cls = node.GetScript().VariantType != Variant.Type.Nil ? node.GetType().Name : node.GetClass();
        if (!byClass.TryGetValue(cls, out var t)) byClass[cls] = t = new Tally();
        t.Nodes++;
        if (node.IsProcessing()) t.Process++;
        if (node.IsPhysicsProcessing()) t.PhysicsProcess++;
        if (node.IsProcessingInternal()) t.InternalProcess++;
        if (node.IsPhysicsProcessingInternal()) t.InternalPhysics++;
        switch (node)
        {
            case CanvasItem ci when ci.IsVisibleInTree():
                t.Visible++;
                visibleCanvas++;
                canvasOwners[owner] = canvasOwners.TryGetValue(owner, out int n) ? n + 1 : 1;
                break;
            case VisualInstance3D vi when vi.IsVisibleInTree():
                t.Visible++;
                visibleVisual++;
                break;
        }
        string childOwner = depth < OwnerDepth ? $"{owner}/{node.Name}" : owner;
        foreach (var child in node.GetChildren())
            Walk(child, childOwner, depth + 1, byClass, canvasOwners, ref visibleCanvas, ref visibleVisual);
    }
}
