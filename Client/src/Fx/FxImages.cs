using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal static class FxImages
{
    private const int CtexHeaderBytes = 36;
    private const int CtexImageHeaderBytes = 16;
    private const uint CtexPng = 1;
    private const uint CtexWebp = 2;
    private static readonly byte[] CtexMagic = { (byte)'G', (byte)'S', (byte)'T', (byte)'2' };

    private static readonly Dictionary<ulong, Image> derived = Shutdown.Track(new Dictionary<ulong, Image>());

    internal static void Remember(Texture2D texture, Image image) => derived[texture.GetRid().Id] = image;

    internal static Image? Read(Texture2D texture)
    {
        if (FxImages.derived.TryGetValue(texture.GetRid().Id, out var derivedImage)) return (Image)derivedImage.Duplicate();
        if (texture is CompressedTexture2D compressed && FromCtex(compressed.LoadPath) is { } decoded) return decoded;
        return texture.GetImage();
    }

    private static readonly Dictionary<string, bool> opaqueFrames = Shutdown.Track(new Dictionary<string, bool>());

    internal static Image? ReadImported(string resPath) =>
        DecodeImported(resPath) ?? (ResourceLoader.Load<Texture2D>(resPath) is { } texture ? Read(texture) : null);

    private static string? ImportedPath(string resPath)
    {
        var import = new ConfigFile();
        if (import.Load(resPath + ".import") != Error.Ok) return null;
        string path = import.GetValue("remap", "path", "").AsString();
        return path.Length > 0 ? path : null;
    }

    internal static (Godot.Collections.Array<Image> Images, Image[] Created)? SequenceLayers(
        IReadOnlyList<string?> framePaths, bool noteOpacity)
    {
        string? first = null;
        foreach (var path in framePaths) if (path != null) { first = path; break; }
        if (first == null) return null;
        var files = new List<string>();
        var slot = new Dictionary<string, int>();
        foreach (var listed in framePaths)
        {
            string path = listed ?? first;
            if (slot.TryAdd(path, files.Count)) files.Add(path);
        }
        var decoded = new Image?[files.Count];
        var opaque = new bool[files.Count];
        ForEachFile(files.Count, i =>
        {
            var image = DecodeImported(files[i]);
            if (image != null && image.IsCompressed()) image.Decompress();
            if (image != null && noteOpacity) opaque[i] = image.DetectAlpha() == Image.AlphaMode.None;
            decoded[i] = image;
        });
        for (int i = 0; i < files.Count; i++)
        {
            if (decoded[i] == null && ResourceLoader.Load<Texture2D>(files[i]) is { } texture && Read(texture) is { } read)
            {
                if (read.IsCompressed()) read.Decompress();
                if (noteOpacity) opaque[i] = read.DetectAlpha() == Image.AlphaMode.None;
                decoded[i] = read;
            }
            if (decoded[i] != null && noteOpacity) opaqueFrames.TryAdd(files[i], opaque[i]);
        }
        var size = decoded[0]?.GetSize() ?? Vector2I.One;
        var layers = new Image[files.Count];
        ForEachFile(files.Count, i =>
        {
            var layer = decoded[i] ?? Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
            layer.ClearMipmaps();
            layer.Convert(Image.Format.Rgba8);
            if (layer.GetSize() != size) layer.Resize(size.X, size.Y);
            layer.GenerateMipmaps();
            layers[i] = layer;
        });
        var images = new Godot.Collections.Array<Image>();
        foreach (var listed in framePaths) images.Add(layers[slot[listed ?? first]]);
        return (images, layers);
    }

    private static void ForEachFile(int count, Action<int> work)
    {
        if (count == 1) work(0);
        else System.Threading.Tasks.Parallel.For(0, count, work);
    }

    // Runs on worker threads: it reads the pack and decodes, nothing else.
    private static Image? DecodeImported(string resPath) =>
        ImportedPath(resPath) is { } imported ? FromCtex(imported) : null;

    internal static bool FrameOpaque(string resPath)
    {
        if (opaqueFrames.TryGetValue(resPath, out bool opaque)) return opaque;
        using var scope = Perf.Measure(Perf.Section.SpawnOpacity);
        using var image = ReadImported(resPath);
        if (image == null) return false;
        if (image.IsCompressed()) image.Decompress();
        opaque = image.DetectAlpha() == Image.AlphaMode.None;
        opaqueFrames[resPath] = opaque;
        return opaque;
    }

    private static Image? FromCtex(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null || file.GetLength() < CtexHeaderBytes + CtexImageHeaderBytes) return null;
        if (!file.GetBuffer(CtexMagic.Length).AsSpan().SequenceEqual(CtexMagic)) return null;
        file.Seek(CtexHeaderBytes);
        uint dataFormat = file.Get32();
        file.Seek(CtexHeaderBytes + CtexImageHeaderBytes);
        if (dataFormat != CtexPng && dataFormat != CtexWebp) return null;
        long size = file.Get32();
        if (size <= 0 || file.GetPosition() + (ulong)size > file.GetLength()) return null;
        var bytes = file.GetBuffer(size);
        var image = new Image();
        var error = dataFormat == CtexPng ? image.LoadPngFromBuffer(bytes) : image.LoadWebpFromBuffer(bytes);
        return error == Error.Ok ? image : null;
    }
}
