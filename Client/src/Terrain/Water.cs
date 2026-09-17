using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Water : Node3D
{
    private readonly List<MeshInstance3D> _patches = new();
    private ShaderMaterial? _riverMat, _stillMat;

    public bool HasWater => _patches.Count > 0;

    public void SetParam(string name, Variant value)
    {
        _riverMat?.SetShaderParameter(name, value);
        _stillMat?.SetShaderParameter(name, value);
    }

    public bool Build(Terrain terrain, string stem)
    {
        var water = KoWater.LoadJson(stem);
        if (water == null)
            return false;

        _riverMat = MakeWaterMaterial(flowing: true);
        _stillMat = MakeWaterMaterial(flowing: false);

        int verts = 0;
        foreach (var p in water.Patches)
        {
            var mi = new MeshInstance3D
            {
                Mesh = BuildPatchMesh(terrain, p),
                MaterialOverride = IsStill(p.Tex) ? _stillMat : _riverMat,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            AddChild(mi);
            _patches.Add(mi);
            verts += p.Verts.Length;
        }

        return true;
    }

    private const float TargetQuad = 2.5f;
    private const int MaxSubdiv = 8;

    private static ArrayMesh BuildPatchMesh(Terrain terrain, KoWater.Patch p)
    {
        int cols = p.Cols, rows = p.Rows;
        var w = new Vector3[p.Verts.Length];
        for (int i = 0; i < w.Length; i++)
        {
            var ko = p.Verts[i];
            w[i] = terrain.KoToWorld(ko.X, ko.Y, ko.Z);
        }

        float edgeSum = 0f; int edgeCount = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                edgeSum += w[r * cols + c].DistanceTo(w[r * cols + c + 1]);
                edgeCount++;
            }
        float avgEdge = edgeCount > 0 ? edgeSum / edgeCount : TargetQuad;
        int s = Mathf.Clamp(Mathf.RoundToInt(avgEdge / TargetQuad), 1, MaxSubdiv);

        int nc = (cols - 1) * s + 1;
        int nr = (rows - 1) * s + 1;
        var verts = new Vector3[nc * nr];
        var uvs = new Vector2[nc * nr];
        var normals = new Vector3[nc * nr];
        for (int R = 0; R < nr; R++)
        {
            int rr = Mathf.Min(R / s, rows - 2);
            float fz = (R - rr * s) / (float)s;
            for (int C = 0; C < nc; C++)
            {
                int cc = Mathf.Min(C / s, cols - 2);
                float fx = (C - cc * s) / (float)s;
                var a = w[rr * cols + cc].Lerp(w[rr * cols + cc + 1], fx);
                var b = w[(rr + 1) * cols + cc].Lerp(w[(rr + 1) * cols + cc + 1], fx);
                var pos = a.Lerp(b, fz);
                int idx = R * nc + C;
                verts[idx] = pos;
                uvs[idx] = new Vector2(pos.X, pos.Z) * 0.25f;
                normals[idx] = Vector3.Up;
            }
        }

        var indices = new int[(nc - 1) * (nr - 1) * 6];
        int k = 0;
        for (int R = 0; R < nr - 1; R++)
        {
            for (int C = 0; C < nc - 1; C++)
            {
                int v00 = R * nc + C;
                int v10 = R * nc + C + 1;
                int v01 = (R + 1) * nc + C;
                int v11 = (R + 1) * nc + C + 1;
                indices[k++] = v00; indices[k++] = v01; indices[k++] = v10;
                indices[k++] = v10; indices[k++] = v01; indices[k++] = v11;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static bool IsStill(string tex)
        => tex.Contains("pond") || tex.Contains("nomaldark");

    private ShaderMaterial MakeWaterMaterial(bool flowing)
    {
        var mat = new ShaderMaterial { Shader = _waterShader ??= Shaders.Get("water") };
        var (nA, nB) = WaveNormals();
        mat.SetShaderParameter("wave_normal_a", nA);
        mat.SetShaderParameter("wave_normal_b", nB);
        mat.SetShaderParameter("wave_speed", 1.8f);
        mat.SetShaderParameter("wave_amp", 0.17f);
        mat.SetShaderParameter("scroll_a", flowing ? new Vector2(0.045f, 0.028f) : new Vector2(0.030f, 0.018f));
        mat.SetShaderParameter("scroll_b", flowing ? new Vector2(-0.032f, 0.040f) : new Vector2(-0.022f, 0.028f));
        mat.SetShaderParameter("nmap_scale", 0.025f);
        mat.SetShaderParameter("normal_strength", 0.08f);
        mat.SetShaderParameter("absorption", 0.08f);
        mat.SetShaderParameter("rough_min", 0.215f);
        mat.SetShaderParameter("spec_amt", 0.06f);
        mat.SetShaderParameter("foam_depth", 0.45f);
        mat.SetShaderParameter("foam_amount", 0.18f);
        mat.SetShaderParameter("deep_color", new Color(0.05f, 0.16f, 0.21f));
        mat.SetShaderParameter("shallow_color", new Color(0.10f, 0.30f, 0.36f));
        mat.SetShaderParameter("foam_color", new Color(0.92f, 0.96f, 0.98f));
        return mat;
    }

    private static Shader? _waterShader;
    private static (Texture2D, Texture2D)? _waveNormals;

    private static (Texture2D, Texture2D) WaveNormals()
    {
        if (_waveNormals.HasValue) return _waveNormals.Value;
        _waveNormals = (MakeWaterNormal(seed: 1337, strength: 2.2f),
                        MakeWaterNormal(seed: 7919, strength: 1.6f));
        return _waveNormals.Value;
    }

    private static ImageTexture MakeWaterNormal(int seed, float strength)
    {
        const int N = 512;
        const int Layers = 40;
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var kx = new float[Layers];
        var ky = new float[Layers];
        var amp = new float[Layers];
        var ph = new float[Layers];
        float ampSum = 0f;
        for (int i = 0; i < Layers; i++)
        {
            int harmonic = 1 + (i * 7) % 16;
            float ang = i * 2.39996323f + rng.RandfRange(-0.25f, 0.25f);
            kx[i] = Mathf.Round(harmonic * Mathf.Cos(ang));
            ky[i] = Mathf.Round(harmonic * Mathf.Sin(ang));
            amp[i] = 1f / harmonic;
            ph[i] = rng.RandfRange(0f, Mathf.Tau);
            ampSum += amp[i];
        }
        float k = strength / ampSum;

        var buf = new byte[N * N * 4];
        for (int y = 0; y < N; y++)
        {
            float v = (float)y / N;
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N;
                float dhdx = 0f, dhdy = 0f;
                for (int i = 0; i < Layers; i++)
                {
                    float c = Mathf.Cos(Mathf.Tau * (kx[i] * u + ky[i] * v) + ph[i]) * amp[i];
                    dhdx += kx[i] * c;
                    dhdy += ky[i] * c;
                }
                var n = new Vector3(-dhdx * k, -dhdy * k, 1f).Normalized();
                int o = (y * N + x) * 4;
                buf[o] = (byte)(n.X * 127.5f + 127.5f);
                buf[o + 1] = (byte)(n.Y * 127.5f + 127.5f);
                buf[o + 2] = (byte)(n.Z * 127.5f + 127.5f);
                buf[o + 3] = 255;
            }
        }
        var img = Image.CreateFromData(N, N, false, Image.Format.Rgba8, buf);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    

    public override void _ExitTree()
    {
        foreach (var mi in _patches)
            mi.QueueFree();
        _patches.Clear();
    }
}
