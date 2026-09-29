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

    private static ImageTexture? _rimTexture, _discTexture;
    private readonly BlipBatch _rimmed = new(), _plain = new();
    private readonly List<Blip> _rings = new();

    private sealed class BlipBatch
    {
        private MultiMesh? _mesh;
        private float[] _data = new float[BlipStride * BlipInitialCapacity];
        private int _count;

        public void Begin() => _count = 0;

        public void Add(Vector2 at, float diameter, Color color)
        {
            if ((_count + 1) * BlipStride > _data.Length) System.Array.Resize(ref _data, _data.Length * 2);
            int i = _count * BlipStride;
            _data[i] = diameter; _data[i + 1] = 0f; _data[i + 2] = 0f; _data[i + 3] = at.X;
            _data[i + 4] = 0f; _data[i + 5] = diameter; _data[i + 6] = 0f; _data[i + 7] = at.Y;
            _data[i + 8] = color.R; _data[i + 9] = color.G; _data[i + 10] = color.B; _data[i + 11] = color.A;
            _count++;
        }

        public void Draw(Control canvas, Texture2D texture)
        {
            if (_count == 0) return;
            _mesh ??= new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = new QuadMesh { Size = Vector2.One },
            };
            int capacity = _data.Length / BlipStride;
            if (_mesh.InstanceCount != capacity) _mesh.InstanceCount = capacity;
            _mesh.Buffer = _data;
            _mesh.VisibleInstanceCount = _count;
            canvas.DrawMultimesh(_mesh, texture);
        }
    }

    private void DrawBlips(Control c, Vector2 ctr, float radius)
    {
        float pxw = PxPerWorld;
        float min = 7f, maxX = c.Size.X - 7f, maxY = c.Size.Y - 7f;
        _rimmed.Begin();
        _plain.Begin();
        _rings.Clear();
        foreach (var b in _blips)
        {
            float dx = (b.X - _koX) * pxw;
            float dy = -(b.Z - _koZ) * pxw;
            var p = ctr + new Vector2(dx, dy);
            bool clamped = p.X < min || p.X > maxX || p.Y < min || p.Y > maxY;
            if (clamped)
            {
                p = new Vector2(Mathf.Clamp(p.X, min, maxX), Mathf.Clamp(p.Y, min, maxY));
                _plain.Add(p, 2f * b.Radius * ClampedScale, new Color(b.Color, ClampedAlpha));
                continue;
            }
            if (b.Hollow) _rings.Add(b with { X = p.X, Z = p.Y });
            else _rimmed.Add(p, 2f * (b.Radius + BlipOutline), b.Color);
        }
        _plain.Draw(c, _discTexture ??= BlipTexture(rim: false));
        _rimmed.Draw(c, _rimTexture ??= BlipTexture(rim: true));
        foreach (var ring in _rings)
            c.DrawArc(new Vector2(ring.X, ring.Z), ring.Radius + 1.5f, 0, Mathf.Tau, 20, ring.Color, 2f, true);
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
