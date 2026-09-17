using Godot;

namespace LibreKO;

public static class Coord
{
    public static Vector3 ToGodot(float koX, float koY, float koZ) => new(-koX, koY, koZ);

    public static Vector3 DirToGodot(float koX, float koY, float koZ) => new(-koX, koY, koZ);

    public static (float x, float z) ToKo(float godotX, float godotZ) => (-godotX, godotZ);

    public static float KoHeading(float koDx, float koDz)
    {
        float h = Mathf.RadToDeg(Mathf.Atan2(koDx, koDz));
        return h < 0 ? h + 360f : h;
    }

    private const float HeadingWireScale = 100f;

    public static short HeadingToWire(float koHeadingDegrees) =>
        (short)Mathf.RoundToInt(Mathf.DegToRad(Mathf.Wrap(koHeadingDegrees, -180f, 180f)) * HeadingWireScale);

    public static float HeadingFromWire(short wire)
    {
        float degrees = Mathf.RadToDeg(wire / HeadingWireScale);
        return degrees < 0f ? degrees + 360f : degrees;
    }
}
