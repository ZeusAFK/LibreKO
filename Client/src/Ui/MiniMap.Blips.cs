using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class MiniMap
{
    private const int BlipTextureSize = 32;
    private const float BlipRimInner = 0.7f;
    private const float BlipRimAlpha = 0.7f;
    private const float BlipOutline = 1.2f;
    private const float ClampedScale = 0.8f;
    private const float ClampedAlpha = 0.65f;
    private const int BlipStride = 12;
    private const int BlipInitialCapacity = 64;

    private static ImageTexture? rimTexture, discTexture;
    private readonly BlipBatch rimmed = new(), plain = new();
    private readonly List<Blip> rings = new();
    private readonly List<Blip> diamonds = new();
    private static readonly Color DiamondEdge = new(0f, 0f, 0f, 0.8f);
    private const float DiamondEdgeWidth = 1f;

    private sealed class BlipBatch
    {
        private MultiMesh? mesh;
        private float[] data = new float[BlipStride * BlipInitialCapacity];
        private int count;

        public void Begin() => count = 0;

        public void Add(Vector2 at, float diameter, Color color)
        {
            if ((count + 1) * BlipStride > data.Length) System.Array.Resize(ref data, data.Length * 2);
            int i = count * BlipStride;
            data[i] = diameter; data[i + 1] = 0f; data[i + 2] = 0f; data[i + 3] = at.X;
            data[i + 4] = 0f; data[i + 5] = diameter; data[i + 6] = 0f; data[i + 7] = at.Y;
            data[i + 8] = color.R; data[i + 9] = color.G; data[i + 10] = color.B; data[i + 11] = color.A;
            count++;
        }

        public void Draw(Control canvas, Texture2D texture)
        {
            if (count == 0) return;
            mesh ??= new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = new QuadMesh { Size = Vector2.One },
            };
            int capacity = data.Length / BlipStride;
            if (mesh.InstanceCount != capacity) mesh.InstanceCount = capacity;
            mesh.Buffer = data;
            mesh.VisibleInstanceCount = count;
            canvas.DrawMultimesh(mesh, texture);
        }
    }

    private void DrawBlips(Control c, Vector2 ctr, float radius)
    {
        float pxw = PxPerWorld;
        float min = 7f, maxX = c.Size.X - 7f, maxY = c.Size.Y - 7f;
        rimmed.Begin();
        plain.Begin();
        rings.Clear();
        diamonds.Clear();
        foreach (var b in blips)
        {
            float dx = (b.X - mapX) * pxw;
            float dy = -(b.Z - mapZ) * pxw;
            var p = ctr + new Vector2(dx, dy);
            bool clamped = p.X < min || p.X > maxX || p.Y < min || p.Y > maxY;
            if (b.Diamond)
            {
                p = new Vector2(Mathf.Clamp(p.X, min, maxX), Mathf.Clamp(p.Y, min, maxY));
                diamonds.Add(b with { X = p.X, Z = p.Y });
                continue;
            }
            if (clamped)
            {
                p = new Vector2(Mathf.Clamp(p.X, min, maxX), Mathf.Clamp(p.Y, min, maxY));
                plain.Add(p, 2f * b.Radius * ClampedScale, new Color(b.Color, ClampedAlpha));
                continue;
            }
            if (b.Hollow) rings.Add(b with { X = p.X, Z = p.Y });
            else rimmed.Add(p, 2f * (b.Radius + BlipOutline), b.Color);
        }
        plain.Draw(c, discTexture ??= BlipTexture(rim: false));
        rimmed.Draw(c, rimTexture ??= BlipTexture(rim: true));
        foreach (var ring in rings)
            c.DrawArc(new Vector2(ring.X, ring.Z), ring.Radius + 1.5f, 0, Mathf.Tau, 20, ring.Color, 2f, true);
        foreach (var diamond in diamonds)
            DrawDiamond(c, new Vector2(diamond.X, diamond.Z), diamond.Radius, diamond.Color);
    }

    private static void DrawDiamond(Control c, Vector2 at, float radius, Color color)
    {
        var points = new[]
        {
            at + new Vector2(0f, -radius), at + new Vector2(radius, 0f),
            at + new Vector2(0f, radius), at + new Vector2(-radius, 0f),
        };
        c.DrawColoredPolygon(points, color);
        c.DrawPolyline(new[] { points[0], points[1], points[2], points[3], points[0] }, DiamondEdge, DiamondEdgeWidth, true);
    }

    private static ImageTexture BlipTexture(bool rim)
    {
        const float half = BlipTextureSize / 2f;
        using var image = Image.CreateEmpty(BlipTextureSize, BlipTextureSize, false, Image.Format.Rgba8);
        for (int y = 0; y < BlipTextureSize; y++)
            for (int x = 0; x < BlipTextureSize; x++)
            {
                float d = new Vector2(x + 0.5f - half, y + 0.5f - half).Length() / half;
                float outer = Mathf.Clamp((1f - d) * half + 0.5f, 0f, 1f);
                if (!rim)
                {
                    image.SetPixel(x, y, new Color(1f, 1f, 1f, outer));
                    continue;
                }
                float fill = Mathf.Clamp((BlipRimInner - d) * half + 0.5f, 0f, 1f);
                image.SetPixel(x, y, new Color(fill, fill, fill, fill + (1f - fill) * BlipRimAlpha * outer));
            }
        return ImageTexture.CreateFromImage(image);
    }
}
