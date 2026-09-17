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

    private readonly List<MeshInstance3D> _chunks = new();
    private KoHeightmap? _hm;
    private KoGroundTex? _groundTex;
    private Texture2DArray? _tileArray;

    public bool HasTerrain => _hm != null;
    public bool HasTileSurface => _groundTex != null;

    public string ZoneStem { get; private set; } = "";

    public float WorldExtent => _hm != null ? _hm.WorldExtent : 0f;

    public Vector3 MapCenterGodot()
    {
        if (_hm == null) return Vector3.Zero;
        float c = (_hm.MapSize - 1) * _hm.CellSize * 0.5f;
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

    public Vector3 KoToWorld(float koX, float koY, float koZ)
        => Transform * Coord.ToGodot(koX, koY, koZ);

    public (float x, float z) WorldToKo(Vector3 world)
    {
        var local = Transform.AffineInverse() * world;
        return Coord.ToKo(local.X, local.Z);
    }

    public bool Build(int zoneId)
    {
        var stem = ZoneCatalog.Stem(zoneId);
        return stem != null && BuildStem(stem);
    }

    public bool BuildStem(string stem)
    {
        ZoneStem = stem;
        _hm = KoHeightmap.LoadPng(stem);
        if (_hm == null || _hm.MapSize < 2)
            return false;

        _groundTex = KoGroundTex.LoadPng(stem);
        if (_groundTex != null && _groundTex.MapSize != _hm.MapSize)
        {
            GD.PrintErr($"[terrain] {stem} splat mapSize {_groundTex.MapSize} != hmap {_hm.MapSize}; ignoring tiles");
            _groundTex = null;
        }
        _tileArray = null;

        Material material = _groundTex != null ? MakeTileMaterial(_groundTex) : MakeMaterial();
        _groundMat = material;
        Active = this;
        int n = _hm.MapSize;
        int chunksPerSide = (n - 1 + ChunkCells - 1) / ChunkCells;
        for (int cz = 0; cz < chunksPerSide; cz++)
            for (int cx = 0; cx < chunksPerSide; cx++)
                AddChunk(cx, cz, material);

        SetDebugOrientation(DefaultRot, DefaultMirX, DefaultMirZ);

        int tileCount = _groundTex?.TileCount ?? 0;
        GD.Print($"[terrain] {stem}: {(tileCount > 0 ? $"{tileCount} tile layers" : "height-tint fallback")}");
        return true;
    }

    public bool SampleHeight(float worldX, float worldZ, out float y)
    {
        y = 0f;
        if (_hm == null) return false;

        float cell = _hm.CellSize;
        float gx = worldX / cell;
        float gz = worldZ / cell;
        int x0 = Mathf.FloorToInt(gx);
        int z0 = Mathf.FloorToInt(gz);
        int n = _hm.MapSize;
        if (x0 < 0 || z0 < 0 || x0 >= n - 1 || z0 >= n - 1)
            return false;

        float fx = gx - x0;
        float fz = gz - z0;
        float h00 = _hm.HeightAt(x0, z0);
        float h10 = _hm.HeightAt(x0 + 1, z0);
        float h01 = _hm.HeightAt(x0, z0 + 1);
        float h11 = _hm.HeightAt(x0 + 1, z0 + 1);
        float a = Mathf.Lerp(h00, h10, fx);
        float b = Mathf.Lerp(h01, h11, fx);
        y = Mathf.Lerp(a, b, fz);
        return true;
    }

    private void AddChunk(int cx, int cz, Material material)
    {
        int n = _hm!.MapSize;
        int x0 = cx * ChunkCells;
        int z0 = cz * ChunkCells;
        int x1 = Mathf.Min(x0 + ChunkCells, n - 1);
        int z1 = Mathf.Min(z0 + ChunkCells, n - 1);
        if (x1 <= x0 || z1 <= z0)
            return;

        float cell = _hm.CellSize;

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
        _chunks.Add(mi);
    }

    public void SetDistanceCull(bool enabled)
    {
        foreach (var c in _chunks)
            c.VisibilityRangeEnd = enabled ? RenderDistance : 0f;
    }

    public void ApplyViewDistance()
    {
        RenderDistance = RenderDistanceFull * Config.ViewDistance;
        foreach (var c in _chunks)
            if (c.VisibilityRangeEnd > 0f) c.VisibilityRangeEnd = RenderDistance;
    }

    private void EmitVertex(SurfaceTool st, int x, int z, int cellX, int cellZ, Vector3 chunkOrigin, float cell)
    {
        float h = _hm!.HeightAt(x, z);
        if (HasTileSurface)
        {
            int ci = KoCornerIndex(x - cellX, z - cellZ);
            int d1 = _groundTex!.Dir1At(cellX, cellZ);
            int d2 = _groundTex.Dir2At(cellX, cellZ);
            st.SetUV(new Vector2(KoGroundTex.TileDirU[d1, ci], KoGroundTex.TileDirV[d1, ci]));
            st.SetUV2(new Vector2(KoGroundTex.TileDirU[d2, ci], KoGroundTex.TileDirV[d2, ci]));
            int l1 = _groundTex.LayerAt(cellX, cellZ);
            int l2 = _groundTex.Layer2At(cellX, cellZ);
            bool has2 = l2 != KoGroundTex.NoTile;
            st.SetCustom(0, new Color(l1 == KoGroundTex.NoTile ? 0 : l1,
                                      has2 ? l2 : 0, has2 ? 1f : 0f, 0f));
        }
        else
        {
            st.SetUV(new Vector2(x - cellX, z - cellZ));
        }
        float span = _hm.MaxHeight - _hm.MinHeight;
        float t = span > 0f ? Mathf.Clamp((h - _hm.MinHeight) / span, 0f, 1f) : 0f;
        st.SetColor(LowColor.Lerp(HighColor, t));
        st.SetNormal(GridNormal(x, z, cell));
        st.AddVertex(Coord.ToGodot(x * cell, h, z * cell) - chunkOrigin);
    }

    private static int KoCornerIndex(int dx, int dz)
        => (dx, dz) switch { (0, 0) => 2, (0, 1) => 0, (1, 1) => 1, _ => 3 };

    private Vector3 GridNormal(int x, int z, float cell)
    {
        int n = _hm!.MapSize;
        int xm = Mathf.Max(x - 1, 0), xp = Mathf.Min(x + 1, n - 1);
        int zm = Mathf.Max(z - 1, 0), zp = Mathf.Min(z + 1, n - 1);
        float dhdx = (_hm.HeightAt(xp, z) - _hm.HeightAt(xm, z)) / ((xp - xm) * cell);
        float dhdz = (_hm.HeightAt(x, zp) - _hm.HeightAt(x, zm)) / ((zp - zm) * cell);
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

    

    private ShaderMaterial MakeTileMaterial(KoGroundTex tex)
    {
        _tileArray = tex.BuildTextureArray();
        var mat = Shaders.Material("terrain_tile");
        mat.SetShaderParameter("tiles", _tileArray);
        mat.SetShaderParameter("cell_size", _hm!.CellSize);
        return mat;
    }

    public static Terrain? Active { get; private set; }

    private Material? _groundMat;

    public override void _ExitTree()
    {
        if (Active == this) Active = null;
        _tileArray = null;
        foreach (var c in _chunks)
            c.QueueFree();
        _chunks.Clear();
    }
}
