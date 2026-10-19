namespace LibreKO.Domain;

public static class YutGame
{
    public const int BetPlacedText = 16401;
    public const string BetPlacedFallback = "Betting of 50000 has been set.";
    public const int NoCoinsText = 1702;
    public const string NoCoinsFallback = "You don't have enough Coins.";
    public const int WonFx = 40106;
    public const int TiedFx = 40107;
    public const int LostFx = 40108;
    public const int BoardFx = 40119;
    public const double ResultDelaySeconds = 0.1;
    public const double CooldownSeconds = 0.5;

    private const int PlayerDieFxBase = 40108;
    private const int NpcDieFxBase = 40113;
    private const int FirstRoll = 1;
    private const int LastRoll = 5;

    public static bool Refused(YutResult result) => result is YutResult.NotEnoughCoins or YutResult.NoCoins;

    public static int? PlayerDieFx(int roll) => roll is >= FirstRoll and <= LastRoll ? PlayerDieFxBase + roll : null;

    public static int? NpcDieFx(int roll) => roll is >= FirstRoll and <= LastRoll ? NpcDieFxBase + roll : null;

    public static int? ResultFx(YutResult result) => result switch
    {
        YutResult.Won => WonFx,
        YutResult.Tied => TiedFx,
        YutResult.Lost => LostFx,
        _ => null,
    };

    public static (int Id, string Fallback)? ResultText(YutResult result) => result switch
    {
        YutResult.Won => (16402, "Player %s has obtained 3 Golden Yut."),
        YutResult.Tied => (16403, "From the Draw, obtained 2 Golden Yut."),
        YutResult.Lost => (16404, "Player %s has obtained 1 Golden Yut."),
        _ => null,
    };
}
