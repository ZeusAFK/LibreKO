namespace LibreKO.Domain;

public static class MaestroPotions
{
    public const int Health = 810117000;
    public const int Mana = 810118000;
    public const int MinimumCoins = 100_000;

    public static bool Is(int itemId) => itemId is Health or Mana;

    public static bool CanUse(int itemId, int gold) => !Is(itemId) || gold >= MinimumCoins;
}
