namespace LibreKO.Domain;

public static class TouchScreenRules
{
    public const float PhoneShortSide = 1080f;

    public static float PhoneScale(int shortSide) =>
        shortSide > 0 ? shortSide / PhoneShortSide : 1f;
}
