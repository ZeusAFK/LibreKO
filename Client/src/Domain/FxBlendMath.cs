using System;

namespace LibreKO.Domain;

public static class FxBlendMath
{
    public const int Zero = 1;
    public const int One = 2;
    public const int SrcColor = 3;
    public const int InvSrcColor = 4;
    public const int SrcAlpha = 5;
    public const int InvSrcAlpha = 6;

    public const int MaxLayers = 8;
    private const float CoverEpsilon = 1e-4f;
    public const int HalfFloatExactLimit = 2048;
    private const int PackFactorMask = 0x7;
    private const int PackDestShift = 3;
    private const int PackOpaqueBit = 1 << 6;
    private const int PackLayersShift = 7;
    private const int PackLayersMask = 0xF;

    public readonly record struct Rgb(float R, float G, float B);

    public readonly record struct Premultiplied(float R, float G, float B, float Cover);

    public static float SourceWeight(int factor, float channel, float alpha) => factor switch
    {
        Zero => 0f,
        One => 1f,
        SrcColor => channel,
        InvSrcColor => 1f - channel,
        SrcAlpha => alpha,
        InvSrcAlpha => 1f - alpha,
        _ => 1f,
    };

    public static float DestinationKeep(int factor, Rgb src, float alpha) => factor switch
    {
        Zero => 0f,
        One => 1f,
        SrcColor => (src.R + src.G + src.B) / 3f,
        InvSrcColor => 1f - (src.R + src.G + src.B) / 3f,
        SrcAlpha => alpha,
        InvSrcAlpha => 1f - alpha,
        _ => 0f,
    };

    public static Premultiplied Shade(int srcFactor, int destFactor, bool blended, Rgb src, float alpha, int layers)
    {
        if (!blended) return new Premultiplied(src.R, src.G, src.B, 1f);
        var r = src.R * SourceWeight(srcFactor, src.R, alpha);
        var g = src.G * SourceWeight(srcFactor, src.G, alpha);
        var b = src.B * SourceWeight(srcFactor, src.B, alpha);
        float keep = Math.Clamp(DestinationKeep(destFactor, src, alpha), 0f, 1f);
        int n = Math.Clamp(layers, 1, MaxLayers);
        if (n == 1) return new Premultiplied(r, g, b, 1f - keep);
        float cover = 1f - keep;
        float keepAll = MathF.Pow(keep, n);
        float gain = cover > CoverEpsilon ? (1f - keepAll) / cover : n;
        return new Premultiplied(r * gain, g * gain, b * gain, 1f - keepAll);
    }

    public static Rgb Over(Premultiplied fx, Rgb dst) => new(
        fx.R + dst.R * (1f - fx.Cover),
        fx.G + dst.G * (1f - fx.Cover),
        fx.B + dst.B * (1f - fx.Cover));

    public static int Pack(int srcFactor, int destFactor, bool blended, int layers) =>
        (srcFactor & PackFactorMask) | (destFactor & PackFactorMask) << PackDestShift
        | (blended ? 0 : PackOpaqueBit) | Math.Clamp(layers, 1, MaxLayers) << PackLayersShift;

    public static (int Src, int Dest, bool Blended, int Layers) Unpack(int packed) =>
        (packed & PackFactorMask, packed >> PackDestShift & PackFactorMask,
         (packed & PackOpaqueBit) == 0,
         packed >> PackLayersShift & PackLayersMask);
}
