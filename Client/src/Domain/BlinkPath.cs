using System;

namespace LibreKO.Domain;

public static class BlinkPath
{
    public const int TenthsPerMetre = 10;
    private const float HalfTurnDegrees = 180f;

    public static (float X, float Z) MapDirection(float rotationYDegrees)
    {
        float heading = (HalfTurnDegrees - rotationYDegrees) * MathF.PI / HalfTurnDegrees;
        return (MathF.Sin(heading), MathF.Cos(heading));
    }

    public static short[] Data(float mapX, float mapY, float mapZ)
    {
        var data = new short[MagicData.Length];
        data[0] = Tenths(mapX);
        data[1] = Tenths(mapY);
        data[2] = Tenths(mapZ);
        return data;
    }

    private static short Tenths(float metres) => unchecked((short)(int)MathF.Round(metres * TenthsPerMetre));
}
