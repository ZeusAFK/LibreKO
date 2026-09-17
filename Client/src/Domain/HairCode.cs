using Godot;

namespace LibreKO.Domain;

public static class HairCode
{
    public const int MaxStyle = 6;

    public static int Pack(int style, Color colour) =>
        (Mathf.Clamp(style, 0, MaxStyle) << 24)
        | (Mathf.Clamp((int)(colour.R * 255f), 0, 255) << 16)
        | (Mathf.Clamp((int)(colour.G * 255f), 0, 255) << 8)
        | Mathf.Clamp((int)(colour.B * 255f), 0, 255);

    public static int StyleOf(int hair) => Mathf.Min((hair >> 24) & 0xFF, MaxStyle);

    public static Color ColourOf(int hair) => new(
        ((hair >> 16) & 0xFF) / 255f,
        ((hair >> 8) & 0xFF) / 255f,
        (hair & 0xFF) / 255f);

    public static Color? TintOf(int hair) => (hair & 0xFFFFFF) == 0 ? null : ColourOf(hair);
}
