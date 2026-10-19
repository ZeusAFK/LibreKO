using System.Collections.Generic;

namespace LibreKO.Domain;

public static class Jackpot
{
    public const int ExpText = 6094;
    public const int CoinText = 6093;

    private static readonly Dictionary<int, int> ExpEffects = new()
    {
        [2] = 30727, [10] = 30726, [50] = 30725, [100] = 30724, [500] = 30723, [1000] = 30722,
    };

    private static readonly Dictionary<int, int> CoinEffects = new()
    {
        [2] = 30905, [10] = 30904, [50] = 30903, [100] = 30902, [500] = 30901, [1000] = 30900,
    };

    public static int ExpEffect(int times) => ExpEffects.TryGetValue(times, out int fx) ? fx : 0;

    public static int CoinEffect(int times) => CoinEffects.TryGetValue(times, out int fx) ? fx : 0;
}
