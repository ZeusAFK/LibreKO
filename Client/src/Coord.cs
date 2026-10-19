using Godot;

namespace LibreKO;

public static class Coord
{
    public static Vector3 ToGodot(float mapX, float mapY, float mapZ) => new(-mapX, mapY, mapZ);

    public static Vector3 DirToGodot(float mapX, float mapY, float mapZ) => new(-mapX, mapY, mapZ);

    public static (float x, float z) ToMap(float godotX, float godotZ) => (-godotX, godotZ);

    public static float MapHeading(float mapDx, float mapDz)
    {
        float h = Mathf.RadToDeg(Mathf.Atan2(mapDx, mapDz));
        return h < 0 ? h + 360f : h;
    }

    public const float TenthsPerUnit = 10f;
    private const float HeadingWireScale = 100f;

    public static short HeadingToWire(float mapHeadingDegrees) =>
        (short)Mathf.RoundToInt(Mathf.DegToRad(Mathf.Wrap(mapHeadingDegrees, -180f, 180f)) * HeadingWireScale);

    public static float HeadingFromWire(short wire)
    {
        float degrees = Mathf.RadToDeg(wire / HeadingWireScale);
        return degrees < 0f ? degrees + 360f : degrees;
    }

    public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
