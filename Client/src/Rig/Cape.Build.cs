using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private void Build(GridMesh grid, int capeId, Color dye, bool highDetail, Node bodyRoot)
    {
        _family = _table[capeId].Family;
        _restBasis = BodyBasis(_skel);
        var rideRest = _skel.GetBoneGlobalRest(_rideBone);
        var neckRest = _skel.GetBoneGlobalRest(_neckBone);
        var anchorRest = new Transform3D(_restBasis, neckRest.Origin);
        _anchorLocal = rideRest.AffineInverse() * anchorRest;

        float scale = neckRest.Origin.Y > 0.2f ? neckRest.Origin.Y / RefNeckY : 1f;
        _rigScale = scale;
        float neckY = Mathf.Max(neckRest.Origin.Y, 0.2f);
        MeasureBody(bodyRoot, neckY);
        ResolveCapsuleRadii(neckY);

        int ar = _rows, ac = _cols;
        var cum = new float[ar];
        var wid = new float[ar];
        for (int k = 0; k < ar; k++)
        {
            int r = ar - 1 - k;
            for (int c = 0; c < ac - 1; c++)
                wid[k] += grid.Pos[r * ac + c].DistanceTo(grid.Pos[r * ac + c + 1]) * scale;
            if (k > 0)
                cum[k] = cum[k - 1] + grid.Pos[(r + 1) * ac + 2].DistanceTo(grid.Pos[r * ac + 2]) * scale;
        }
        float length = Mathf.Max(cum[ar - 1], 0.2f);
        float hemWidth = Mathf.Max(wid[ar - 1], 0.2f);

        float quad = (highDetail ? QuadHigh : QuadLow) * scale;
        int maxC = highDetail ? MaxColsHigh : MaxColsLow;
        int maxR = highDetail ? MaxRowsHigh : MaxRowsLow;
        _nc = Mathf.Clamp(Mathf.RoundToInt(hemWidth / quad) + 1, 5, maxC);
        _nr = Mathf.Clamp(Mathf.RoundToInt(length / quad) + 1, 7, maxR);
        int n = _nc * _nr;

        _rest = BuildDrape(wid, cum, length, scale, neckY);
        FitAttachment(bodyRoot, neckRest.Origin);
        var tris = new List<int>((_nc - 1) * (_nr - 1) * 6);
        for (int j = 0; j < _nr - 1; j++)
            for (int c = 0; c < _nc - 1; c++)
            {
                int a = j * _nc + c, b = a + 1, d = a + _nc, e = d + 1;
                tris.Add(a); tris.Add(d); tris.Add(b);
                tris.Add(b); tris.Add(d); tris.Add(e);
            }
        _idx = tris.ToArray();

        _pos = new Vector3[n];
        _targets = new Vector3[n];
        _pred = new Vector3[n];
        _previous = new Vector3[n];
        _renderPos = new Vector3[n];
        _vel = new Vector3[n];
        _w = new float[n];
        _contact = new bool[n];
        _tether = new float[n];
        _areaW = new float[n];
        var share = new float[n];
        for (int t = 0; t + 2 < _idx.Length; t += 3)
        {
            int i0 = _idx[t], i1 = _idx[t + 1], i2 = _idx[t + 2];
            float a3 = (_rest[i1] - _rest[i0]).Cross(_rest[i2] - _rest[i0]).Length() / 6f;
            share[i0] += a3; share[i1] += a3; share[i2] += a3;
        }
        for (int i = 0; i < n; i++) _areaW[i] = share[i] > 1e-8f ? 1f / share[i] : 0f;

        float averageArea = 0f;
        foreach (float area in share) averageArea += area / n;
        for (int j = 0; j < _nr; j++)
            for (int c = 0; c < _nc; c++)
            {
                int i = j * _nc + c;
                _w[i] = j == 0 ? 0f : Mathf.Clamp(averageArea / Mathf.Max(share[i], 1e-6f), 0.25f, 4f);
                _tether[i] = j == 0 ? 0 : _tether[i - _nc] + _rest[i].DistanceTo(_rest[i - _nc]);
            }
        BuildLinks();
        var oldMesh = _mesh;
        _mesh = new ArrayMesh();
        Mesh = _mesh;
        oldMesh?.Dispose();
        int subdivisions = highDetail ? 2 : 1;
        _surfaceCols = (_nc - 1) * subdivisions + 1;
        _surfaceRows = (_nr - 1) * subdivisions + 1;
        int surfaceCount = _surfaceCols * _surfaceRows;
        _surfacePos = new Vector3[surfaceCount];
        _surfaceEdgeBounds = new Aabb[_surfaceRows - 1];
        _nrm = new Vector3[surfaceCount];
        var surfaceUv = new Vector2[surfaceCount];
        var surfaceIndices = new List<int>();
        for (int j = 0; j < _surfaceRows; j++)
            for (int c = 0; c < _surfaceCols; c++)
            {
                surfaceUv[j * _surfaceCols + c] = new Vector2(c / (_surfaceCols - 1f), j / (_surfaceRows - 1f));
                if (j == _surfaceRows - 1 || c == _surfaceCols - 1) continue;
                int a = j * _surfaceCols + c, b = a + 1, d = a + _surfaceCols, e = d + 1;
                surfaceIndices.Add(a); surfaceIndices.Add(d); surfaceIndices.Add(b);
                surfaceIndices.Add(b); surfaceIndices.Add(d); surfaceIndices.Add(e);
            }
        _surfaceIdx = surfaceIndices.ToArray();
        _surfaceSamples = new int[surfaceCount * 16];
        _surfaceWeights = new float[surfaceCount * 16];
        float Weight(int index, float t) => index switch
        {
            0 => -0.5f * t + t * t - 0.5f * t * t * t,
            1 => 1 - 2.5f * t * t + 1.5f * t * t * t,
            2 => 0.5f * t + 2 * t * t - 1.5f * t * t * t,
            _ => -0.5f * t * t + 0.5f * t * t * t,
        };
        for (int j = 0; j < _surfaceRows; j++)
            for (int c = 0; c < _surfaceCols; c++)
            {
                float u = c * (_nc - 1f) / (_surfaceCols - 1);
                float v = j * (_nr - 1f) / (_surfaceRows - 1);
                int x = (int)u, y = (int)v, start = (j * _surfaceCols + c) * 16;
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 4; col++)
                    {
                        int at = start + row * 4 + col;
                        _surfaceSamples[at] = Mathf.Clamp(y + row - 1, 0, _nr - 1) * _nc
                            + Mathf.Clamp(x + col - 1, 0, _nc - 1);
                        _surfaceWeights[at] = Weight(row, v - y) * Weight(col, u - x);
                    }
            }
        SmoothSurface(_rest);
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        Array.Fill(_nrm, Vector3.Back);
        arrays[(int)Mesh.ArrayType.Vertex] = _surfacePos;
        arrays[(int)Mesh.ArrayType.Normal] = _nrm;
        arrays[(int)Mesh.ArrayType.TexUV] = surfaceUv;
        arrays[(int)Mesh.ArrayType.Index] = _surfaceIdx;
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
            flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
        var format = (RenderingServer.ArrayFormat)_mesh.SurfaceGetFormat(0);
        _normalOffset = (int)RenderingServer.MeshSurfaceGetFormatOffset(format, surfaceCount, (int)Mesh.ArrayType.Normal);
        _normalStride = (int)RenderingServer.MeshSurfaceGetFormatNormalTangentStride(format, surfaceCount);
        using var surface = RenderingServer.MeshGetSurface(_mesh.GetRid(), 0);
        _vertexBytes = surface["vertex_data"].AsByteArray();
        _mat ??= new ShaderMaterial { Shader = ClothShader };
        MaterialOverride = _mat;
        float extent = length + hemWidth * 0.5f + 0.5f;
        CustomAabb = new Aabb(new Vector3(-extent, -extent, -extent), Vector3.One * extent * 2);
        if (_visibility == null)
        {
            _visibility = new VisibleOnScreenNotifier3D();
            AddChild(_visibility);
        }
        _visibility.Aabb = CustomAabb;
        SetCape(capeId, dye);
        _primed = false;
        _frozen = false;

        if (Debug)
        {
            GD.Print($"[cape] {_nc}x{_nr} ({n} verts, {_links.Length} links, {_idx.Length / 3} tris) "
                   + $"fabric {hemWidth:0.00}m wide x {length:0.00}m  scale={scale:0.###} "
                   + $"facing={BodyBasis(_skel).Z}");
            var prof = new System.Text.StringBuilder("[cape]   body profile ");
            prof.Append(_fitted != null ? "(fitted): " : "(fallback): ");
            for (int i = 0; i < ProfileBins; i++)
                prof.Append($"{BodyRadiusAt(i / (float)(ProfileBins - 1)):0.000} ");
            GD.Print(prof.ToString());
            var dr = new System.Text.StringBuilder("[cape]   drape radius: ");
            for (int j = 0; j < _nr; j += Mathf.Max(1, _nr / 8))
                dr.Append($"{new Vector2(_rest[j * _nc + _nc / 2].X, _rest[j * _nc + _nc / 2].Z).Length():0.000} ");
            GD.Print(dr.ToString());
        }
    }

    private Vector3[] BuildDrape(float[] wid, float[] cum, float length, float scale, float neckY)
    {
        int segs = _nc - 1;
        var outPos = new Vector3[_nc * _nr];

        float WidthAt(float arc)
        {
            for (int k = 1; k < cum.Length; k++)
            {
                if (arc > cum[k]) continue;
                float span = cum[k] - cum[k - 1];
                return Mathf.Lerp(wid[k - 1], wid[k], span < 1e-6f ? 0f : (arc - cum[k - 1]) / span);
            }
            return wid[^1];
        }

        var radius = new float[_nr];
        var theta = new float[_nr];
        var pleat = new float[_nr];
        var rowW = new float[_nr];
        float step = length / (_nr - 1);
        float cap = MaxDrapeRadius * scale;

        for (int j = 0; j < _nr; j++)
        {
            rowW[j] = WidthAt(step * j);
            float drop = Mathf.Clamp(step * j / neckY, 0f, 1f);
            float depth = (BodyRadiusAt(drop) + ClearMargin + FlareOut * Mathf.Pow(drop, 1.5f)) * scale;
            float b = Mathf.Lerp(CollarRadius * scale, depth,
                                 Mathf.SmoothStep(0f, CollarBlendDrop, drop));

            float r = b, th = 0f;
            for (int k = 0; k < 4; k++)
            {
                th = Mathf.Min(ThetaMax, 2f * segs * Mathf.Asin(
                         Mathf.Clamp(rowW[j] / (2f * segs * Mathf.Max(r, 1e-4f)), -1f, 1f)));
                float s = Mathf.Sin(th * 0.5f), c = Mathf.Cos(th * 0.5f);
                r = Mathf.Min(b * Mathf.Sqrt(_aspect * _aspect * s * s + c * c), cap);
            }
            radius[j] = r;
            theta[j] = th;

            float taut = 2f * segs * r * Mathf.Sin(th / (2f * segs));
            float usable = Mathf.Min(rowW[j], taut * MaxGather);
            float minAmp = PleatDepth * Mathf.SmoothStep(0f, PleatFadeIn, drop);
            pleat[j] = SolvePleat(usable, r, th, segs, minAmp);
        }

        float y = 0f;
        for (int j = 0; j < _nr; j++)
        {
            if (j > 0)
            {
                float dR = radius[j] - radius[j - 1];
                y -= Mathf.Sqrt(Mathf.Max(step * step - dR * dR, step * step * 0.04f));
            }
            for (int c = 0; c < _nc; c++)
            {
                float a = (0.5f - c / (float)segs) * theta[j];
                float rr = radius[j] * (1f + pleat[j] * PleatWave(c, segs));
                outPos[j * _nc + c] = new Vector3(rr * Mathf.Sin(a), y, -rr * Mathf.Cos(a));
            }
        }
        return outPos;
    }

    private static float PleatWave(int c, int segs)
        => 0.5f + 0.5f * Mathf.Cos(c / (float)segs * PleatCount * Mathf.Tau);

    private static float SolvePleat(float fabric, float r, float th, int segs, float minAmp)
    {
        float Len(float amp)
        {
            float total = 0f;
            var prev = Vector2.Zero;
            for (int c = 0; c <= segs; c++)
            {
                float a = (0.5f - c / (float)segs) * th;
                float rr = r * (1f + amp * PleatWave(c, segs));
                var p = new Vector2(rr * Mathf.Sin(a), -rr * Mathf.Cos(a));
                if (c > 0) total += (p - prev).Length();
                prev = p;
            }
            return total;
        }
        if (Len(minAmp) >= fabric) return minAmp;
        float lo = minAmp, hi = MaxPleat;
        if (Len(hi) <= fabric) return hi;
        for (int i = 0; i < 18; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Len(mid) < fabric) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    private void BuildLinks()
    {
        var links = new List<Link>();
        _rowLinkStart = new int[_nr + 1];
        _rowBounds = new Aabb[_nr];
        void Add(int j0, int c0, int j1, int c1, float k)
        {
            if (j0 < 0 || j0 >= _nr || c0 < 0 || c0 >= _nc
                || j1 < 0 || j1 >= _nr || c1 < 0 || c1 >= _nc) return;
            int a = j0 * _nc + c0, b = j1 * _nc + c1;
            if (_w[a] == 0f && _w[b] == 0f) return;
            links.Add(new Link { A = a, B = b, Length = _rest[a].DistanceTo(_rest[b]), Compliance = k });
        }
        for (int j = 0; j < _nr; j++)
        {
            _rowLinkStart[j] = links.Count;
            for (int c = 0; c < _nc; c++)
            {
                Add(j, c, j, c + 1, StretchCompliance);
                Add(j, c, j + 1, c, StretchCompliance);
                Add(j, c, j + 1, c + 1, ShearCompliance);
                Add(j, c + 1, j + 1, c, ShearCompliance);
                Add(j, c, j, c + 2, BendCompliance);
                Add(j, c, j + 2, c, BendCompliance);
            }
        }
        _rowLinkStart[_nr] = links.Count;
        _links = links.ToArray();
    }

    private void MeasureBody(Node root, float neckY)
    {
        _fitted = null;
        _aspect = 1f;
        var fallback = new float[ProfileBins];
        for (int i = 0; i < ProfileBins; i++) fallback[i] = BodyRadiusAt(i / (float)(ProfileBins - 1));

        var limbs = new[]
        {
            (_upLegL, _loLegL), (_loLegL, _ftLegL), (_upLegR, _loLegR), (_loLegR, _ftLegR),
            (_upArmL, _loArmL), (_loArmL, _handL), (_upArmR, _loArmR), (_loArmR, _handR),
        };
        var limbA = new Vector3[limbs.Length];
        var limbB = new Vector3[limbs.Length];
        Array.Clear(_limbRadius);
        Array.Clear(_limbTipRadius);
        Array.Clear(_limbWidth);
        for (int i = 0; i < limbs.Length; i++)
        {
            if (limbs[i].Item1 < 0 || limbs[i].Item2 < 0) continue;
            limbA[i] = _skel.GetBoneGlobalRest(limbs[i].Item1).Origin;
            limbB[i] = _skel.GetBoneGlobalRest(limbs[i].Item2).Origin;
        }
        float lateral = 0f;
        foreach (int b in new[] { _upArmL, _upArmR })
            if (b >= 0) lateral = Mathf.Max(lateral, Mathf.Abs(_skel.GetBoneGlobalRest(b).Origin.X));
        lateral = lateral > 0.01f ? lateral * 1.25f : float.MaxValue;

        var fwdLocal = BodyBasis(_skel).Z;

        var maxBack = new float[ProfileBins];
        var maxLat = new float[ProfileBins];
        int sampled = 0, meshes = 0, seen = 0;

        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Cape or BoneAttachment3D or FxInstance or FxWeaponGlow or ItemShineDriver) continue;
                if (c is Node3D nd && nd.Name.ToString().StartsWith("Hair")) continue;
                if (c is MeshInstance3D mi && mi.Mesh is { } mesh && mi.Visible)
                {
                    meshes++;
                    int[] skinBones = Array.Empty<int>();
                    var restSkin = Array.Empty<Transform3D>();
                    if (mi.Skin is { } skin)
                    {
                        skinBones = new int[skin.GetBindCount()];
                        restSkin = new Transform3D[skinBones.Length];
                        for (int i = 0; i < skinBones.Length; i++)
                        {
                            var name = skin.GetBindName(i);
                            skinBones[i] = name.IsEmpty ? skin.GetBindBone(i) : _skel.FindBone(name);
                            restSkin[i] = _skel.GetBoneGlobalRest(skinBones[i]) * skin.GetBindPose(i);
                        }
                    }
                    var toSkel = _skel.GlobalTransform.AffineInverse() * mi.GlobalTransform;
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                    {
                        using var arr = mesh.SurfaceGetArrays(s);
                        if (arr.Count <= (int)Mesh.ArrayType.Vertex) continue;
                        var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var bones = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                        var weights = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                        int influences = verts.Length > 0 ? bones.Length / verts.Length : 0;
                        int stride = Mathf.Max(1, verts.Length / 4000);
                        for (int v = 0; v < verts.Length; v += stride)
                        {
                            seen++;
                            var p = toSkel * verts[v];
                            if (restSkin.Length > 0 && influences > 0)
                            {
                                p = Vector3.Zero;
                                for (int w = 0; w < influences; w++)
                                {
                                    int at = v * influences + w;
                                    if (weights[at] > 0) p += (restSkin[bones[at]] * verts[v]) * weights[at];
                                }
                            }
                            for (int limb = 0; limb < limbs.Length; limb++)
                            {
                                var axis = limbB[limb] - limbA[limb];
                                float length2 = axis.LengthSquared();
                                if (length2 < 1e-8f) continue;
                                float along = (p - limbA[limb]).Dot(axis) / length2;
                                if (along < -0.15f || along > 1.15f) continue;
                                float influence = 0;
                                for (int w = 0; w < influences; w++)
                                {
                                    int at = v * influences + w, bind = bones[at];
                                    int bone = bind < skinBones.Length ? skinBones[bind] : -1;
                                    if (bone == limbs[limb].Item1 || bone == limbs[limb].Item2)
                                        influence += weights[at];
                                }
                                if (influence < 0.25f) continue;
                                var radial = p - limbA[limb] - axis * Mathf.Clamp(along, 0, 1);
                                float depth = radial.Dot(_restBasis.Z);
                                float radius = Mathf.Abs(depth);
                                _limbWidth[limb] = Mathf.Max(_limbWidth[limb],
                                    (radial - _restBasis.Z * depth).Length() + 0.045f * _rigScale);
                                if (along < 0.5f)
                                    _limbRadius[limb] = Mathf.Max(_limbRadius[limb], radius + 0.045f * _rigScale);
                                else
                                    _limbTipRadius[limb] = Mathf.Max(_limbTipRadius[limb], radius + 0.045f * _rigScale);
                            }
                            if (p.Y >= neckY) continue;
                            int bin = Mathf.Clamp(
                                Mathf.RoundToInt((neckY - p.Y) / neckY * (ProfileBins - 1)), 0, ProfileBins - 1);
                            if (Mathf.Abs(p.X) > lateral) continue;
                            float ahead = p.X * fwdLocal.X + p.Z * fwdLocal.Z;
                            if (ahead > 0.02f) continue;
                            float back = -ahead / _rigScale;
                            float side = Mathf.Abs(p.X * fwdLocal.Z - p.Z * fwdLocal.X) / _rigScale;
                            
                            if (back > maxBack[bin]) maxBack[bin] = back;
                            if (side > maxLat[bin]) maxLat[bin] = side;
                            sampled++;
                        }
                    }
                }
                Walk(c);
            }
        }
        Walk(root);
        if (Debug) GD.Print($"[cape] limbs={string.Join(", ", _limbRadius)}");
        if (Debug)
            GD.Print($"[cape]   measured {meshes} meshes, {seen} verts seen, {sampled} kept, "
                   + $"lateral cutoff {(lateral == float.MaxValue ? -1f : lateral):0.000}");
        if (sampled < 50) return;

        var fit = new float[ProfileBins];
        float sumBack = 0f, sumLat = 0f;
        for (int i = 0; i < ProfileBins; i++)
        {
            float a = maxBack[Mathf.Max(i - 1, 0)], b = maxBack[i], c2 = maxBack[Mathf.Min(i + 1, ProfileBins - 1)];
            fit[i] = Mathf.Max(Mathf.Max(b, (a + c2) * 0.5f), fallback[i] * 0.7f);
            sumBack += fit[i];
            sumLat += maxLat[i];
        }
        _aspect = sumBack > 1e-4f ? Mathf.Clamp(sumLat / sumBack, 1f, 2.4f) : 1f;
        _fitted = fit;
        if (Debug) GD.Print($"[cape]   body aspect (width/depth) = {_aspect:0.00}");
    }
}
