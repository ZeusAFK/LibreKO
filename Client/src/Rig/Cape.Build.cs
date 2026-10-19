using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private void Build(GridMesh grid, int capeId, Color dye, bool highDetail, Node bodyNode)
    {
        family = table[capeId].Family;
        restBasis = BodyBasis(skel);
        var rideRest = skel.GetBoneGlobalRest(rideBone);
        var neckRest = skel.GetBoneGlobalRest(neckBone);
        var anchorRest = new Transform3D(restBasis, neckRest.Origin);
        anchorLocal = rideRest.AffineInverse() * anchorRest;

        float scale = neckRest.Origin.Y > 0.2f ? neckRest.Origin.Y / RefNeckY : 1f;
        rigScale = scale;
        float neckY = Mathf.Max(neckRest.Origin.Y, 0.2f);
        MeasureBody(bodyNode, neckY);
        ResolveCapsuleRadii(neckY);

        int ar = rows, ac = cols;
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
        gridColumns = Mathf.Clamp(Mathf.RoundToInt(hemWidth / quad) + 1, 5, maxC);
        gridRows = Mathf.Clamp(Mathf.RoundToInt(length / quad) + 1, 7, maxR);
        int n = gridColumns * gridRows;

        rest = BuildDrape(wid, cum, length, scale, neckY);
        FitAttachment(bodyNode, neckRest.Origin);
        var tris = new List<int>((gridColumns - 1) * (gridRows - 1) * 6);
        for (int j = 0; j < gridRows - 1; j++)
            for (int c = 0; c < gridColumns - 1; c++)
            {
                int a = j * gridColumns + c, b = a + 1, d = a + gridColumns, e = d + 1;
                tris.Add(a); tris.Add(d); tris.Add(b);
                tris.Add(b); tris.Add(d); tris.Add(e);
            }
        idx = tris.ToArray();

        pos = new Vector3[n];
        targets = new Vector3[n];
        pred = new Vector3[n];
        previous = new Vector3[n];
        renderPos = new Vector3[n];
        vel = new Vector3[n];
        inverseMass = new float[n];
        contact = new bool[n];
        tether = new float[n];
        areaW = new float[n];
        var share = new float[n];
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
            float a3 = (rest[i1] - rest[i0]).Cross(rest[i2] - rest[i0]).Length() / 6f;
            share[i0] += a3; share[i1] += a3; share[i2] += a3;
        }
        for (int i = 0; i < n; i++) areaW[i] = share[i] > 1e-8f ? 1f / share[i] : 0f;

        float averageArea = 0f;
        foreach (float area in share) averageArea += area / n;
        for (int j = 0; j < gridRows; j++)
            for (int c = 0; c < gridColumns; c++)
            {
                int i = j * gridColumns + c;
                inverseMass[i] = j == 0 ? 0f : Mathf.Clamp(averageArea / Mathf.Max(share[i], 1e-6f), 0.25f, 4f);
                tether[i] = j == 0 ? 0 : tether[i - gridColumns] + rest[i].DistanceTo(rest[i - gridColumns]);
            }
        BuildLinks();
        var oldMesh = mesh;
        mesh = new ArrayMesh();
        Mesh = mesh;
        oldMesh?.Dispose();
        int subdivisions = highDetail ? 2 : 1;
        surfaceCols = (gridColumns - 1) * subdivisions + 1;
        surfaceRows = (gridRows - 1) * subdivisions + 1;
        int surfaceCount = surfaceCols * surfaceRows;
        surfacePos = new Vector3[surfaceCount];
        surfaceEdgeBounds = new Aabb[surfaceRows - 1];
        nrm = new Vector3[surfaceCount];
        var surfaceUv = new Vector2[surfaceCount];
        var surfaceIndices = new List<int>();
        for (int j = 0; j < surfaceRows; j++)
            for (int c = 0; c < surfaceCols; c++)
            {
                surfaceUv[j * surfaceCols + c] = new Vector2(c / (surfaceCols - 1f), j / (surfaceRows - 1f));
                if (j == surfaceRows - 1 || c == surfaceCols - 1) continue;
                int a = j * surfaceCols + c, b = a + 1, d = a + surfaceCols, e = d + 1;
                surfaceIndices.Add(a); surfaceIndices.Add(d); surfaceIndices.Add(b);
                surfaceIndices.Add(b); surfaceIndices.Add(d); surfaceIndices.Add(e);
            }
        surfaceIdx = surfaceIndices.ToArray();
        surfaceSamples = new int[surfaceCount * 16];
        surfaceWeights = new float[surfaceCount * 16];
        float Weight(int index, float t) => index switch
        {
            0 => -0.5f * t + t * t - 0.5f * t * t * t,
            1 => 1 - 2.5f * t * t + 1.5f * t * t * t,
            2 => 0.5f * t + 2 * t * t - 1.5f * t * t * t,
            _ => -0.5f * t * t + 0.5f * t * t * t,
        };
        for (int j = 0; j < surfaceRows; j++)
            for (int c = 0; c < surfaceCols; c++)
            {
                float u = c * (gridColumns - 1f) / (surfaceCols - 1);
                float v = j * (gridRows - 1f) / (surfaceRows - 1);
                int x = (int)u, y = (int)v, start = (j * surfaceCols + c) * 16;
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 4; col++)
                    {
                        int at = start + row * 4 + col;
                        surfaceSamples[at] = Mathf.Clamp(y + row - 1, 0, gridRows - 1) * gridColumns
                            + Mathf.Clamp(x + col - 1, 0, gridColumns - 1);
                        surfaceWeights[at] = Weight(row, v - y) * Weight(col, u - x);
                    }
            }
        SmoothSurface(rest);
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        Array.Fill(nrm, Vector3.Back);
        arrays[(int)Mesh.ArrayType.Vertex] = surfacePos;
        arrays[(int)Mesh.ArrayType.Normal] = nrm;
        arrays[(int)Mesh.ArrayType.TexUV] = surfaceUv;
        arrays[(int)Mesh.ArrayType.Index] = surfaceIdx;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
            flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
        var format = (RenderingServer.ArrayFormat)mesh.SurfaceGetFormat(0);
        normalOffset = (int)RenderingServer.MeshSurfaceGetFormatOffset(format, surfaceCount, (int)Mesh.ArrayType.Normal);
        normalStride = (int)RenderingServer.MeshSurfaceGetFormatNormalTangentStride(format, surfaceCount);
        var templateKey = ((long)format, surfaceCount);
        if (!VertexTemplates.TryGetValue(templateKey, out var template))
        {
            using var surface = RenderingServer.MeshGetSurface(mesh.GetRid(), 0);
            VertexTemplates[templateKey] = template = surface["vertex_data"].AsByteArray();
        }
        vertexBytes = (byte[])template.Clone();
        mat ??= new ShaderMaterial { Shader = ClothShader };
        MaterialOverride = mat;
        float extent = length + hemWidth * 0.5f + 0.5f;
        CustomAabb = new Aabb(new Vector3(-extent, -extent, -extent), Vector3.One * extent * 2);
        if (visibility == null)
        {
            visibility = new VisibleOnScreenNotifier3D();
            AddChild(visibility);
        }
        visibility.Aabb = CustomAabb;
        SetCape(capeId, dye);
        primed = false;
        frozen = false;

        if (Debug)
        {
            GD.Print($"[cape] {gridColumns}x{gridRows} ({n} verts, {links.Length} links, {idx.Length / 3} tris) "
                   + $"fabric {hemWidth:0.00}m wide x {length:0.00}m  scale={scale:0.###} "
                   + $"facing={BodyBasis(skel).Z}");
            var prof = new System.Text.StringBuilder("[cape]   body profile ");
            prof.Append(fitted != null ? "(fitted): " : "(fallback): ");
            for (int i = 0; i < ProfileBins; i++)
                prof.Append($"{BodyRadiusAt(i / (float)(ProfileBins - 1)):0.000} ");
            GD.Print(prof.ToString());
            var dr = new System.Text.StringBuilder("[cape]   drape radius: ");
            for (int j = 0; j < gridRows; j += Mathf.Max(1, gridRows / 8))
                dr.Append($"{new Vector2(rest[j * gridColumns + gridColumns / 2].X, rest[j * gridColumns + gridColumns / 2].Z).Length():0.000} ");
            GD.Print(dr.ToString());
        }
    }

    private Vector3[] BuildDrape(float[] wid, float[] cum, float length, float scale, float neckY)
    {
        int segmentCount = gridColumns - 1;
        var outPos = new Vector3[gridColumns * gridRows];

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

        var radius = new float[gridRows];
        var theta = new float[gridRows];
        var pleat = new float[gridRows];
        var rowW = new float[gridRows];
        float step = length / (gridRows - 1);
        float cap = MaxDrapeRadius * scale;

        for (int j = 0; j < gridRows; j++)
        {
            rowW[j] = WidthAt(step * j);
            float drop = Mathf.Clamp(step * j / neckY, 0f, 1f);
            float depth = (BodyRadiusAt(drop) + ClearMargin + FlareOut * Mathf.Pow(drop, 1.5f)) * scale;
            float b = Mathf.Lerp(CollarRadius * scale, depth,
                                 Mathf.SmoothStep(0f, CollarBlendDrop, drop));

            float r = b, th = 0f;
            for (int k = 0; k < 4; k++)
            {
                th = Mathf.Min(ThetaMax, 2f * segmentCount * Mathf.Asin(
                         Mathf.Clamp(rowW[j] / (2f * segmentCount * Mathf.Max(r, 1e-4f)), -1f, 1f)));
                float s = Mathf.Sin(th * 0.5f), c = Mathf.Cos(th * 0.5f);
                r = Mathf.Min(b * Mathf.Sqrt(aspect * aspect * s * s + c * c), cap);
            }
            radius[j] = r;
            theta[j] = th;

            float taut = 2f * segmentCount * r * Mathf.Sin(th / (2f * segmentCount));
            float usable = Mathf.Min(rowW[j], taut * MaxGather);
            float minAmp = PleatDepth * Mathf.SmoothStep(0f, PleatFadeIn, drop);
            pleat[j] = SolvePleat(usable, r, th, segmentCount, minAmp);
        }

        float y = 0f;
        for (int j = 0; j < gridRows; j++)
        {
            if (j > 0)
            {
                float dR = radius[j] - radius[j - 1];
                y -= Mathf.Sqrt(Mathf.Max(step * step - dR * dR, step * step * 0.04f));
            }
            for (int c = 0; c < gridColumns; c++)
            {
                float a = (0.5f - c / (float)segmentCount) * theta[j];
                float rr = radius[j] * (1f + pleat[j] * PleatWave(c, segmentCount));
                outPos[j * gridColumns + c] = new Vector3(rr * Mathf.Sin(a), y, -rr * Mathf.Cos(a));
            }
        }
        return outPos;
    }

    private static float PleatWave(int c, int segmentCount)
        => 0.5f + 0.5f * Mathf.Cos(c / (float)segmentCount * PleatCount * Mathf.Tau);

    private static float SolvePleat(float fabric, float r, float th, int segmentCount, float minAmp)
    {
        float Len(float amp)
        {
            float total = 0f;
            var prev = Vector2.Zero;
            for (int c = 0; c <= segmentCount; c++)
            {
                float a = (0.5f - c / (float)segmentCount) * th;
                float rr = r * (1f + amp * PleatWave(c, segmentCount));
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
        var linkList = new List<Link>();
        rowLinkStart = new int[gridRows + 1];
        rowBounds = new Aabb[gridRows];
        void Add(int j0, int c0, int j1, int c1, float k)
        {
            if (j0 < 0 || j0 >= gridRows || c0 < 0 || c0 >= gridColumns
                || j1 < 0 || j1 >= gridRows || c1 < 0 || c1 >= gridColumns) return;
            int a = j0 * gridColumns + c0, b = j1 * gridColumns + c1;
            if (inverseMass[a] == 0f && inverseMass[b] == 0f) return;
            linkList.Add(new Link { A = a, B = b, Length = rest[a].DistanceTo(rest[b]), Compliance = k });
        }
        for (int j = 0; j < gridRows; j++)
        {
            rowLinkStart[j] = linkList.Count;
            for (int c = 0; c < gridColumns; c++)
            {
                Add(j, c, j, c + 1, StretchCompliance);
                Add(j, c, j + 1, c, StretchCompliance);
                Add(j, c, j + 1, c + 1, ShearCompliance);
                Add(j, c + 1, j + 1, c, ShearCompliance);
                Add(j, c, j, c + 2, BendCompliance);
                Add(j, c, j + 2, c, BendCompliance);
            }
        }
        rowLinkStart[gridRows] = linkList.Count;
        links = linkList.ToArray();
    }

    private void MeasureBody(Node root, float neckY)
    {
        fitted = null;
        aspect = 1f;
        var fallback = new float[ProfileBins];
        for (int i = 0; i < ProfileBins; i++) fallback[i] = BodyRadiusAt(i / (float)(ProfileBins - 1));

        var limbs = new[]
        {
            (upLegL, loLegL), (loLegL, ftLegL), (upLegR, loLegR), (loLegR, ftLegR),
            (upArmL, loArmL), (loArmL, handL), (upArmR, loArmR), (loArmR, handR),
        };
        var limbA = new Vector3[limbs.Length];
        var limbB = new Vector3[limbs.Length];
        Array.Clear(limbRadius);
        Array.Clear(limbTipRadius);
        Array.Clear(limbWidth);
        for (int i = 0; i < limbs.Length; i++)
        {
            if (limbs[i].Item1 < 0 || limbs[i].Item2 < 0) continue;
            limbA[i] = skel.GetBoneGlobalRest(limbs[i].Item1).Origin;
            limbB[i] = skel.GetBoneGlobalRest(limbs[i].Item2).Origin;
        }
        float lateral = 0f;
        foreach (int b in new[] { upArmL, upArmR })
            if (b >= 0) lateral = Mathf.Max(lateral, Mathf.Abs(skel.GetBoneGlobalRest(b).Origin.X));
        lateral = lateral > 0.01f ? lateral * 1.25f : float.MaxValue;

        var fwdLocal = BodyBasis(skel).Z;

        var maxBack = new float[ProfileBins];
        var maxLat = new float[ProfileBins];
        int sampled = 0, meshes = 0, seen = 0;

        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is Cape or BoneAttachment3D or FxInstance or FxWeaponGlow or ItemShineDriver) continue;
                if (c is Node3D nd && nd.Name.ToString().StartsWith("Hair")) continue;
                if (c is MeshInstance3D mi && mi.Mesh is { } sourceMesh && mi.Visible)
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
                            skinBones[i] = name.IsEmpty ? skin.GetBindBone(i) : skel.FindBone(name);
                            restSkin[i] = skel.GetBoneGlobalRest(skinBones[i]) * skin.GetBindPose(i);
                        }
                    }
                    var toSkel = skel.GlobalTransform.AffineInverse() * mi.GlobalTransform;
                    for (int s = 0; s < sourceMesh.GetSurfaceCount(); s++)
                    {
                        var cached = MeshArraysCache.Get(sourceMesh, s);
                        if (cached.Vertices.Length == 0) continue;
                        var verts = cached.Vertices;
                        var bones = cached.Bones;
                        var weights = cached.Weights;
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
                                float depth = radial.Dot(restBasis.Z);
                                float radius = Mathf.Abs(depth);
                                limbWidth[limb] = Mathf.Max(limbWidth[limb],
                                    (radial - restBasis.Z * depth).Length() + 0.045f * rigScale);
                                if (along < 0.5f)
                                    limbRadius[limb] = Mathf.Max(limbRadius[limb], radius + 0.045f * rigScale);
                                else
                                    limbTipRadius[limb] = Mathf.Max(limbTipRadius[limb], radius + 0.045f * rigScale);
                            }
                            if (p.Y >= neckY) continue;
                            int bin = Mathf.Clamp(
                                Mathf.RoundToInt((neckY - p.Y) / neckY * (ProfileBins - 1)), 0, ProfileBins - 1);
                            if (Mathf.Abs(p.X) > lateral) continue;
                            float ahead = p.X * fwdLocal.X + p.Z * fwdLocal.Z;
                            if (ahead > 0.02f) continue;
                            float back = -ahead / rigScale;
                            float side = Mathf.Abs(p.X * fwdLocal.Z - p.Z * fwdLocal.X) / rigScale;
                            
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
        if (Debug) GD.Print($"[cape] limbs={string.Join(", ", limbRadius)}");
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
        aspect = sumBack > 1e-4f ? Mathf.Clamp(sumLat / sumBack, 1f, 2.4f) : 1f;
        fitted = fit;
        if (Debug) GD.Print($"[cape]   body aspect (width/depth) = {aspect:0.00}");
    }
}
