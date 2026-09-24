using System;

namespace LibreKO.Domain;

public static class BlinkPath
{
    public const int TenthsPerMetre = 10;
    private const int DataLength = 7;
    private const float HalfTurnDegrees = 180f;

    public static (float X, float Z) KoDirection(float rotationYDegrees)
    {
        float heading = (HalfTurnDegrees - rotationYDegrees) * MathF.PI / HalfTurnDegrees;
        return (MathF.Sin(heading), MathF.Cos(heading));
    }

    public static short[] Data(float koX, float koY, float koZ)
    {
        var data = new short[DataLength];
        data[0] = Tenths(koX);
        data[1] = Tenths(koY);
        data[2] = Tenths(koZ);
        return data;
    }

    private static short Tenths(float metres) => unchecked((short)(int)MathF.Round(metres * TenthsPerMetre));
}
