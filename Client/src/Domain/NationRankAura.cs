namespace LibreKO.Domain;

public static class NationRankAura
{
    public const int Unranked = 255;
    public const int NoFx = 0;
    public const int GoldFxId = 23410;
    public const int SilverFxId = 23411;
    public const int MirageFxId = 23412;

    private const int GoldPlace = 1;
    private const int SilverLastPlace = 4;
    private const int MirageLastPlace = 10;

    private static readonly int[] HiddenZones = { 29, 37, 38, 39, 45, 57, 58, 59, 60, 76, 85, 89 };

    public static int FxIdFor(int personalRank) => personalRank switch
    {
        GoldPlace => GoldFxId,
        > GoldPlace and <= SilverLastPlace => SilverFxId,
        > SilverLastPlace and <= MirageLastPlace => MirageFxId,
        _ => NoFx,
    };

    public static bool HiddenIn(int zone) => System.Array.IndexOf(HiddenZones, zone) >= 0;
}
