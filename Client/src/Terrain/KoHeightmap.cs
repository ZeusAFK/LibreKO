using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public sealed class KoHeightmap
{
    public const float KoCellSize = 4.0f;

    public string ZoneName = "";
    public int MapSize;
    public float CellSize = KoCellSize;
    public float MinHeight;
    public float MaxHeight;
    public float[] Heights = System.Array.Empty<float>();

    public float WorldExtent => (MapSize - 1) * CellSize;
    public float HeightAt(int x, int z) => Heights[z * MapSize + x];

    public static KoHeightmap? LoadPng(string stem)
    {
        using var f = FileAccess.Open($"res://assets/terrain/{stem}/heightmap.png", FileAccess.ModeFlags.Read);
        if (f == null)
            return null;
        var img = new Image();
        if (img.LoadPngFromBuffer(f.GetBuffer((long)f.GetLength())) != Error.Ok)
            return null;
        if (img.GetFormat() != Image.Format.Rgba8)
            return null;
        int n = img.GetWidth();
        if (n < 2 || img.GetHeight() != n)
            return null;

        var data = img.GetData();
        if (data.Length < n * n * 4)
            return null;
        var heights = new float[n * n];
        System.Buffer.BlockCopy(data, 0, heights, 0, n * n * 4);

        float mn = float.MaxValue, mx = float.MinValue;
        foreach (var h in heights) { if (h < mn) mn = h; if (h > mx) mx = h; }
        return new KoHeightmap
        {
            ZoneName = stem, MapSize = n, CellSize = KoCellSize,
            MinHeight = mn, MaxHeight = mx, Heights = heights,
        };
    }
}
