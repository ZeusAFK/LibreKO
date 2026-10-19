using System.Collections.Generic;
using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public partial class Terrain : Node3D
{
    private const int ChunkCells = 32;
    public const float RenderDistanceFull = 900f;

    public float RenderDistance = RenderDistanceFull * Config.ViewDistance;

    private static readonly Color LowColor = new(0.36f, 0.50f, 0.30f);
    private static readonly Color HighColor = new(0.62f, 0.60f, 0.55f);

    private readonly List<MeshInstance3D> chunks = new();
    private Heightmap? heightmap;
    private GroundTextures? groundTex;
    private Texture2DArray? tileArray;

    public bool HasTileSurface => groundTex != null;

    public string ZoneStem { get; private set; } = "";

    public float WorldExtent => heightmap != null ? heightmap.WorldExtent : 0f;

    public Vector3 MapCenterGodot()
    {
        if (heightmap == null) return Vector3.Zero;
        float c = (heightmap.MapSize - 1) * heightmap.CellSize * 0.5f;
        return Coord.ToGodot(c, 0, c);
    }

    public const int DefaultRot = 0;
    public const bool DefaultMirX = false;
    public const bool DefaultMirZ = false;

    public void SetDebugOrientation(int rotQuarters, bool mirX, bool mirZ)
    {
        var pivot = MapCenterGodot();
        var basis = Basis.Identity.Scaled(new Vector3(mirX ? -1f : 1f, 1f, mirZ ? -1f : 1f));
        basis = new Basis(Vector3.Up, Mathf.DegToRad(-90f * rotQuarters)) * basis;
        Transform = new Transform3D(basis, pivot - basis * pivot);
    }

    public Vector3 MapToWorld(float mapX, float mapY, float mapZ)
        => Transform * Coord.ToGodot(mapX, mapY, mapZ);

    public (float x, float z) WorldToMap(Vector3 world)
    {
        var local = Transform.AffineInverse() * world;
        return Coord.ToMap(local.X, local.Z);
    }

    public bool Build(int zoneId)
    {
        var stem = ZoneCatalog.Stem(zoneId);
        return stem != null && BuildStem(stem);
    }

    public bool BuildStem(string stem)
    {
        ZoneStem = stem;
        heightmap = Heightmap.LoadPng(stem);
        if (heightmap == null || heightmap.MapSize < 2)
            return false;

        groundTex = GroundTextures.LoadPng(stem);
        if (groundTex != null && groundTex.MapSize != heightmap.MapSize)
        {
            GD.PrintErr($"[terrain] {stem} splat mapSize {groundTex.MapSize} != hmap {heightmap.MapSize}; ignoring tiles");
            groundTex = null;
        }
        tileArray = null;

        Material material = groundTex != null ? MakeTileMaterial(groundTex) : MakeMaterial();
        groundMat = material;
        Active = this;
        int n = heightmap.MapSize;
        int chunksPerSide = (n - 1 + ChunkCells - 1) / ChunkCells;
        for (int cz = 0; cz < chunksPerSide; cz++)
            for (int cx = 0; cx < chunksPerSide; cx++)
                AddChunk(cx, cz, material);

        SetDebugOrientation(DefaultRot, DefaultMirX, DefaultMirZ);

        int tileCount = groundTex?.TileCount ?? 0;
        GD.Print($"[terrain] {stem}: {(tileCount > 0 ? $"{tileCount} tile layers" : "height-tint fallback")}");
        return true;
    }

    public bool SampleHeight(float worldX, float worldZ, out float y)
    {
        y = 0f;
        if (heightmap == null) return false;

        float cell = heightmap.CellSize;
        float gx = worldX / cell;
        float gz = worldZ / cell;
        int x0 = Mathf.FloorToInt(gx);
        int z0 = Mathf.FloorToInt(gz);
        int n = heightmap.MapSize;
        if (x0 < 0 || z0 < 0 || x0 >= n - 1 || z0 >= n - 1)
            return false;

        float fx = gx - x0;
        float fz = gz - z0;
        float h00 = heightmap.HeightAt(x0, z0);
        float h10 = heightmap.HeightAt(x0 + 1, z0);
        float h01 = heightmap.HeightAt(x0, z0 + 1);
        float h11 = heightmap.HeightAt(x0 + 1, z0 + 1);
        float a = Mathf.Lerp(h00, h10, fx);
        float b = Mathf.Lerp(h01, h11, fx);
        y = Mathf.Lerp(a, b, fz);
        return true;
    }

    private void AddChunk(int cx, int cz, Material material)
    {
        int n = heightmap!.MapSize;
        int x0 = cx * ChunkCells;
        int z0 = cz * ChunkCells;
        int x1 = Mathf.Min(x0 + ChunkCells, n - 1);
        int z1 = Mathf.Min(z0 + ChunkCells, n - 1);
        if (x1 <= x0 || z1 <= z0)
            return;

        float cell = heightmap.CellSize;

        var chunkOrigin = Coord.ToGodot(x0 * cell, 0, z0 * cell);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        if (HasTileSurface)
            st.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);

        for (int z = z0; z < z1; z++)
        {
            for (int x = x0; x < x1; x++)
            {
                EmitVertex(st, x,     z,     x, z, chunkOrigin, cell);
                EmitVertex(st, x,     z + 1, x, z, chunkOrigin, cell);
                EmitVertex(st, x + 1, z,     x, z, chunkOrigin, cell);

                EmitVertex(st, x + 1, z,     x, z, chunkOrigin, cell);
                EmitVertex(st, x,     z + 1, x, z, chunkOrigin, cell);
                EmitVertex(st, x + 1, z + 1, x, z, chunkOrigin, cell);
            }
        }

        var mesh = st.Commit();

        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            Layers = 1u | ItemShineLight.SceneryLayer,
            MaterialOverride = material,
            Position = chunkOrigin,
            VisibilityRangeEnd = RenderDistance,
            VisibilityRangeEndMargin = 60f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        AddChild(mi);
        chunks.Add(mi);
    }

    public void SetDistanceCull(bool enabled)
    {
        foreach (var c in chunks)
            c.VisibilityRangeEnd = enabled ? RenderDistance : 0f;
    }

    public void ApplyViewDistance()
    {
        RenderDistance = RenderDistanceFull * Config.ViewDistance;
        foreach (var c in chunks)
            if (c.VisibilityRangeEnd > 0f) c.VisibilityRangeEnd = RenderDistance;
    }

    private void EmitVertex(SurfaceTool st, int x, int z, int cellX, int cellZ, Vector3 chunkOrigin, float cell)
    {
        float h = heightmap!.HeightAt(x, z);
        if (HasTileSurface)
        {
            int ci = MapCornerIndex(x - cellX, z - cellZ);
            int d1 = groundTex!.Dir1At(cellX, cellZ);
            int d2 = groundTex.Dir2At(cellX, cellZ);
            st.SetUV(new Vector2(GroundTextures.TileDirU[d1, ci], GroundTextures.TileDirV[d1, ci]));
            st.SetUV2(new Vector2(GroundTextures.TileDirU[d2, ci], GroundTextures.TileDirV[d2, ci]));
            int l1 = groundTex.LayerAt(cellX, cellZ);
            int l2 = groundTex.Layer2At(cellX, cellZ);
            bool has2 = l2 != GroundTextures.NoTile;
            st.SetCustom(0, new Color(l1 == GroundTextures.NoTile ? 0 : l1,
                                      has2 ? l2 : 0, has2 ? 1f : 0f, 0f));
        }
        else
        {
            st.SetUV(new Vector2(x - cellX, z - cellZ));
        }
        float span = heightmap.MaxHeight - heightmap.MinHeight;
        float t = span > 0f ? Mathf.Clamp((h - heightmap.MinHeight) / span, 0f, 1f) : 0f;
        st.SetColor(LowColor.Lerp(HighColor, t));
        st.SetNormal(GridNormal(x, z, cell));
        st.AddVertex(Coord.ToGodot(x * cell, h, z * cell) - chunkOrigin);
    }

    private static int MapCornerIndex(int dx, int dz)
        => (dx, dz) switch { (0, 0) => 2, (0, 1) => 0, (1, 1) => 1, _ => 3 };

    private Vector3 GridNormal(int x, int z, float cell)
    {
        int n = heightmap!.MapSize;
        int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, n - 1);
        int zm = Mathf.Max(z - 1, 0), zp = Mathf.Min(z + 1, n - 1);
        float dhdx = (heightmap.HeightAt(xp, z) - heightmap.HeightAt(xm, z)) / ((xp - xm) * cell);
        float dhdz = (heightmap.HeightAt(x, zp) - heightmap.HeightAt(x, zm)) / ((zp - zm) * cell);
        return Coord.DirToGodot(-dhdx, 1f, -dhdz).Normalized();
    }

    private static Material MakeMaterial()
    {
        return new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    

    private ShaderMaterial MakeTileMaterial(GroundTextures tex)
    {
        tileArray = tex.BuildTextureArray();
        var mat = Shaders.Material("terrain_tile");
        mat.SetShaderParameter("tiles", tileArray);
        mat.SetShaderParameter("cell_size", heightmap!.CellSize);
        return mat;
    }

    public static Terrain? Active { get; private set; }

    private Material? groundMat;

    public override void _ExitTree()
    {
        if (Active == this) Active = null;
        tileArray = null;
        foreach (var c in chunks)
            c.QueueFree();
        chunks.Clear();
    }
}
