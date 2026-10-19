namespace LibreKO.Domain;

public static class ExpSeal
{
    public const int EmptyJar = 810354000;
    public const int SmallJar = 810355000;
    public const int LargeJar = 810356000;
    public const int RebirthJar = 810357000;
    public const double Capacity = 1_000_000_000.0;
    public const byte NoBagPos = 255;

    public static readonly long[] Amounts = { 100_000_000, 500_000_000, 1_000_000_000 };
    public const long MinimumStore = 100_000_000;

    public const int TooLittleText = 33350;
    public const int HelpText = 33351;
    public const int WhileOnText = 33352;
    public const int NoJarText = 33353;
    public const int NoAmountText = 33354;
    public const int GainText = 33356;
    public const int RebirthLimitText = 33357;
    public const int JarFailedText = 33358;
    public const int ObtainText = 33359;
    public const int EmptyRefusalText = 33361;

    public static int? StoreRefusal(ExpSealResult code) => code switch
    {
        ExpSealResult.TooLittle => TooLittleText,
        ExpSealResult.WhileOn => WhileOnText,
        _ => null,
    };

    public static (int Id, bool WithCode)? UseRefusal(ExpSealResult code) => code switch
    {
        ExpSealResult.Succeeded => null,
        ExpSealResult.WhileOn => (WhileOnText, false),
        ExpSealResult.Empty => (EmptyRefusalText, false),
        ExpSealResult.RebirthLimit => (RebirthLimitText, false),
        _ => (JarFailedText, true),
    };

    public static int? LocalRefusal(bool jarPlaced, long sealedTotal, long amount) =>
        !jarPlaced ? NoJarText
        : sealedTotal < MinimumStore ? TooLittleText
        : amount <= 0 ? NoAmountText
        : null;

    public static int JarForFlag(NpcDialogStyle flag) => flag switch
    {
        NpcDialogStyle.SmallJar => SmallJar,
        NpcDialogStyle.LargeJar => LargeJar,
        NpcDialogStyle.RebirthJar => RebirthJar,
        _ => 0,
    };

    public static double Percent(long total) => total / Capacity * 100.0;
}
