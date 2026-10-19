using Godot;

namespace LibreKO.Presentation;

internal static class SceneryRig
{
    internal static string DescribeFxParts(Node node)
    {
        var sb = new System.Text.StringBuilder();
        void Visit(Node n)
        {
            if (n is MeshInstance3D mi)
            {
                sb.Append($" | {n.Name} vis={mi.Visible} aabb={mi.GetAabb().Size.X:F2}x{mi.GetAabb().Size.Y:F2}");
                if (mi.MaterialOverride is StandardMaterial3D m)
                    sb.Append($" blend={m.BlendMode} tr={m.Transparency} depth={m.DepthDrawMode} " +
                              $"noZTest={m.NoDepthTest} prio={m.RenderPriority} albedo={m.AlbedoColor} " +
                              $"tex={(m.AlbedoTexture != null ? m.AlbedoTexture.GetSize().ToString() : "none")}");
            }
            foreach (Node c in n.GetChildren()) Visit(c);
        }
        Visit(node);
        return sb.ToString();
    }

    internal static void ConfigureAnimatedObject(Node root)
    {
        foreach (var mi in ModelNodes.FindAll<MeshInstance3D>(root))
        {
            mi.VisibilityRangeEnd = LargeObjectCullDist;
            mi.VisibilityRangeEndMargin = ModelNodes.ObjectCullFade;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
    }

    internal const float LargeObjectCullDist = 700f;

    internal static void ConfigureObjectMaterials(Mesh mesh)
    {
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            if (mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D imported)
                continue;
            string name = imported.ResourceName;
            int marker = name.LastIndexOf("__ko_f", StringComparison.Ordinal);
            if (marker < 0)
                continue;
            int sourceMarker = name.IndexOf("_s", marker + 6, StringComparison.Ordinal);
            int destMarker = sourceMarker < 0 ? -1 : name.IndexOf("_d", sourceMarker + 2, StringComparison.Ordinal);
            int animMarker = destMarker < 0 ? -1 : name.IndexOf("_a", destMarker + 2, StringComparison.Ordinal);
            int destEnd = animMarker < 0 ? name.Length : animMarker;
            if (sourceMarker < 0 || destMarker < 0 ||
                !uint.TryParse(name.AsSpan(marker + 6, sourceMarker - marker - 6),
                    System.Globalization.NumberStyles.HexNumber, null, out uint flags) ||
                !int.TryParse(name.AsSpan(sourceMarker + 2, destMarker - sourceMarker - 2), out int srcBlend) ||
                !int.TryParse(name.AsSpan(destMarker + 2, destEnd - destMarker - 2), out int destBlend))
                continue;

            var material = (StandardMaterial3D)imported.Duplicate();
            material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
            bool alphaBlend = (flags & 0x001) != 0;
            bool noLight = (flags & 0x040) != 0;
            bool additive = srcBlend == 2 && destBlend == 2;
            bool noZWrite = (flags & 0x100) != 0;
            // An additive surface has no opaque interior for a depth pre-pass to stand for: it would
            // write the whole quad and punch the world out behind it — FX_CONTINUATION_HANDOFF §4r.
            if (alphaBlend)
                material.Transparency = noZWrite || additive
                    ? BaseMaterial3D.TransparencyEnum.Alpha
                    : BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
            if (additive)
            {
                material.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                material.DisableFog = true;
            }
            if ((flags & 0x004) != 0)
                material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            if ((flags & 0x008) != 0)
                material.BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY;
            if ((flags & 0x010) != 0)
                material.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
            if (noLight)
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            if (noZWrite)
                material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
            if ((flags & 0x400) != 0)
                material.NoDepthTest = true;

            var (fps, frameCount) = TextureAnim.Parse(name, destMarker + 2);
            if (fps > 0f) TextureAnim.Register(material, fps, frameCount);

            mesh.SurfaceSetMaterial(surface, material);
        }
    }

    internal static void ConfigureObjectMaterials(Node node)
    {
        if (node is MeshInstance3D mi && mi.Mesh != null)
            ConfigureObjectMaterials(mi.Mesh);
        foreach (Node child in node.GetChildren())
            ConfigureObjectMaterials(child);
    }

    internal static bool RequiresCompleteObjectScene(Node node)
    {
        int meshCount = 0;
        bool pivotedMesh = false;
        void Visit(Node current)
        {
            if (current is MeshInstance3D mi)
            {
                meshCount++;
                if (!mi.Position.IsEqualApprox(Vector3.Zero))
                    pivotedMesh = true;
            }
            foreach (Node child in current.GetChildren())
                Visit(child);
        }
        Visit(node);
        return meshCount > 1 || pivotedMesh;
    }
}
