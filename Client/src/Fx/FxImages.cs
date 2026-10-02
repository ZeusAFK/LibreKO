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

    private static readonly Dictionary<ulong, Image> _derived = Shutdown.Track(new Dictionary<ulong, Image>());

    internal static void Remember(Texture2D texture, Image image) => _derived[texture.GetRid().Id] = image;

    internal static Image? Read(Texture2D texture)
    {
        if (_derived.TryGetValue(texture.GetRid().Id, out var derived)) return (Image)derived.Duplicate();
        if (texture is CompressedTexture2D compressed && FromCtex(compressed.LoadPath) is { } decoded) return decoded;
        return texture.GetImage();
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
