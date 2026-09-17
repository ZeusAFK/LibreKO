using Godot;

namespace LibreKO;

public static class CloudField
{
    private const int Size = 256;

    private static ImageTexture? _tex;
    private static byte[]? _px;

    public static ImageTexture Texture
    {
        get
        {
            if (_tex == null) Bake();
            return _tex!;
        }
    }

    

    public static float CoverAt(Vector2 world, Vector2 pan, float scale, float cover)
    {
        float bse = Tap((world - pan) * scale, 0);
        float det = Tap((world - pan * 2f) * (scale * 3f), 1);
        return Mathf.Clamp((float)Mathf.SmoothStep(0f, 0.22f, bse * 0.82f + det * 0.18f - (1f - cover)), 0f, 1f);
    }

    public static Vector2 DeckHit(Vector3 from, Vector3 dir, float deckHeight)
    {
        float t = Mathf.Max(deckHeight - from.Y, 0f) / Mathf.Max(dir.Y, 0.15f);
        return new Vector2(from.X + dir.X * t, from.Z + dir.Z * t);
    }

    public static Vector2 RayStep(Vector3 dir)
    {
        float up = Mathf.Max(dir.Y, 0.15f);
        return new Vector2(dir.X / up, dir.Z / up);
    }

    private static float Tap(Vector2 uv, int channel)
    {
        if (_px == null) Bake();
        var p = _px!;
        float fx = (uv.X - Mathf.Floor(uv.X)) * Size - 0.5f;
        float fy = (uv.Y - Mathf.Floor(uv.Y)) * Size - 0.5f;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        int x1 = (x0 + 1) & (Size - 1), y1 = (y0 + 1) & (Size - 1);
        x0 &= Size - 1; y0 &= Size - 1;
        float a = p[(y0 * Size + x0) * 4 + channel], b = p[(y0 * Size + x1) * 4 + channel];
        float c = p[(y1 * Size + x0) * 4 + channel], d = p[(y1 * Size + x1) * 4 + channel];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty) / 255f;
    }

    private static void Bake()
    {
        int n = Size * Size;
        var bse = new float[n];
        var det = new float[n];
        var cir = new float[n];
        for (int y = 0; y < Size; y++)
        {
            float v = (float)y / Size;
            for (int x = 0; x < Size; x++)
            {
                float u = (float)x / Size;
                int i = y * Size + x;
                float wu = u + (Fbm(u, v, 3, 3, 2, 811) - 0.5f) * 0.16f;
                float wv = v + (Fbm(u, v, 3, 3, 2, 929) - 0.5f) * 0.16f;
                bse[i] = Fbm(wu, wv, 4, 4, 4, 101);
                det[i] = Fbm(wu, wv, 16, 16, 3, 233);
                cir[i] = Fbm(u + (wu - u) * 0.5f, v, 5, 14, 4, 577);
            }
        }
        Normalize(bse); Normalize(det); Normalize(cir);

        var data = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            data[i * 4] = To8(bse[i]);
            data[i * 4 + 1] = To8(det[i]);
            data[i * 4 + 2] = To8(cir[i]);
            data[i * 4 + 3] = 255;
        }
        _px = data;
        var img = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        _tex = ImageTexture.CreateFromImage(img);
    }

    private static void Normalize(float[] v)
    {
        const int Bins = 512;
        var hist = new int[Bins];
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (float f in v) { if (f < lo) lo = f; if (f > hi) hi = f; }
        if (hi - lo < 1e-6f) return;
        foreach (float f in v)
            hist[Mathf.Clamp((int)((f - lo) / (hi - lo) * (Bins - 1)), 0, Bins - 1)]++;
        int want = v.Length / 100, run = 0, p1 = 0, p99 = Bins - 1;
        for (int i = 0; i < Bins; i++) { run += hist[i]; if (run >= want) { p1 = i; break; } }
        run = 0;
        for (int i = Bins - 1; i >= 0; i--) { run += hist[i]; if (run >= want) { p99 = i; break; } }
        float a = lo + (hi - lo) * p1 / (Bins - 1f);
        float b = lo + (hi - lo) * p99 / (Bins - 1f);
        if (b - a < 1e-6f) return;
        for (int i = 0; i < v.Length; i++) v[i] = Mathf.Clamp((v[i] - a) / (b - a), 0f, 1f);
    }

    private static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

    private static float Fbm(float u, float v, int px, int py, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Value(u * px, v * py, px, py, seed + o * 7919);
            norm += amp;
            amp *= 0.5f;
            px <<= 1; py <<= 1;
        }
        return sum / norm;
    }

    private static float Value(float x, float y, int px, int py, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(xi, yi, px, py, seed);
        float b = Hash(xi + 1, yi, px, py, seed);
        float c = Hash(xi, yi + 1, px, py, seed);
        float d = Hash(xi + 1, yi + 1, px, py, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Hash(int x, int y, int px, int py, int seed)
    {
        x = ((x % px) + px) % px;
        y = ((y % py) + py) % py;
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }
}
