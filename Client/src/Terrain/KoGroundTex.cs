using FileAccess = Godot.FileAccess;
using Godot;

namespace LibreKO;

public sealed class KoGroundTex
{
    public const int NoTile = 0xFFFF;
    private const byte NoTileByte = 255;
    private const int DirMask = 7;

    public int MapSize;
    public int TileCount;
    public int TilePx;
    public byte[] Atlas = System.Array.Empty<byte>();
    public int[] CellLayer = System.Array.Empty<int>();
    public int[] CellLayer2 = System.Array.Empty<int>();
    public byte[] Dir1 = System.Array.Empty<byte>();
    public byte[] Dir2 = System.Array.Empty<byte>();

    public static KoGroundTex? LoadPng(string stem)
    {
        string root = $"res://assets/terrain/{stem}";
        using var sf = FileAccess.Open($"{root}/splat.png", FileAccess.ModeFlags.Read);
        if (sf == null) return null;

        var simg = new Image();
        if (simg.LoadPngFromBuffer(sf.GetBuffer((long)sf.GetLength())) != Error.Ok ||
            simg.GetFormat() != Image.Format.Rgba8)
            return null;

        int n = simg.GetWidth();
        if (n < 2 || simg.GetHeight() != n) return null;
        byte[] sd = simg.GetData();
        if (sd.Length < n * n * 4) return null;

        int cells = n * n;
        var l1 = new int[cells];
        var l2 = new int[cells];
        var d1 = new byte[cells];
        var d2 = new byte[cells];
        for (int i = 0; i < cells; i++)
        {
            byte r = sd[i * 4], g = sd[i * 4 + 1], b = sd[i * 4 + 2], a = sd[i * 4 + 3];
            int hi1 = b >> 3, hi2 = a >> 3;
            l1[i] = r == NoTileByte && hi1 == 0 ? NoTile : r | (hi1 << 8);
            l2[i] = g == NoTileByte && hi2 == 0 ? NoTile : g | (hi2 << 8);
            d1[i] = (byte)(b & DirMask);
            d2[i] = (byte)(a & DirMask);
        }

        using var tf = FileAccess.Open($"{root}/tiles.png", FileAccess.ModeFlags.Read);
        if (tf == null) return null;

        var timg = new Image();
        if (timg.LoadPngFromBuffer(tf.GetBuffer((long)tf.GetLength())) != Error.Ok ||
            timg.GetFormat() != Image.Format.Rgba8)
            return null;

        int px = timg.GetWidth();
        if (px <= 0 || timg.GetHeight() % px != 0) return null;
        int count = timg.GetHeight() / px;
        if (count <= 0) return null;

        return new KoGroundTex
        {
            MapSize = n,
            TileCount = count,
            TilePx = px,
            Atlas = timg.GetData(),
            CellLayer = l1,
            CellLayer2 = l2,
            Dir1 = d1,
            Dir2 = d2,
        };
    }

    public Texture2DArray BuildTextureArray()
    {
        var images = new Godot.Collections.Array<Image>();
        int layerBytes = TilePx * TilePx * 4;
        for (int i = 0; i < TileCount; i++)
        {
            var slice = new byte[layerBytes];
            System.Buffer.BlockCopy(Atlas, i * layerBytes, slice, 0, layerBytes);
            var img = Image.CreateFromData(TilePx, TilePx, false, Image.Format.Rgba8, slice);
            img.GenerateMipmaps();
            images.Add(img);
        }

        var array = new Texture2DArray();
        array.CreateFromImages(images);
        return array;
    }

    public int LayerAt(int x, int z) => CellLayer[z * MapSize + x];
    public int Layer2At(int x, int z) => CellLayer2[z * MapSize + x];
    public byte Dir1At(int x, int z) => Dir1[z * MapSize + x];
    public byte Dir2At(int x, int z) => Dir2[z * MapSize + x];

    public static readonly float[,] TileDirU =
    {
        { 0f, 1f, 0f, 1f }, { 0f, 0f, 1f, 1f }, { 1f, 0f, 1f, 0f }, { 1f, 1f, 0f, 0f },
        { 1f, 0f, 1f, 0f }, { 0f, 0f, 1f, 1f }, { 0f, 1f, 0f, 1f }, { 1f, 1f, 0f, 0f },
    };
    public static readonly float[,] TileDirV =
    {
        { 0f, 0f, 1f, 1f }, { 1f, 0f, 1f, 0f }, { 1f, 1f, 0f, 0f }, { 0f, 1f, 0f, 1f },
        { 0f, 0f, 1f, 1f }, { 0f, 1f, 0f, 1f }, { 1f, 1f, 0f, 0f }, { 1f, 0f, 1f, 0f },
    };
}
