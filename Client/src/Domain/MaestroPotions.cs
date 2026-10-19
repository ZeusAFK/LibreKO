namespace LibreKO.Domain;

public static class MaestroPotions
{
    public const int Health = 810117000;
    public const int Mana = 810118000;
    public const int MinimumCoins = 100_000;

    public static bool Is(int itemId) => itemId is Health or Mana;

    public static int ItemFor(int directType) => directType switch
    {
        HealTarget.HpPurchase => Health,
        HealTarget.MpPurchase => Mana,
        _ => 0,
    };

    public static bool CanUse(int itemId, int gold) => !Is(itemId) || gold >= MinimumCoins;

    public static int PickStrongest(IEnumerable<int> itemIds, Func<int, int> healOf, int gold)
    {
        int best = 0, bestHeal = 0, maestro = 0, maestroHeal = 0;
        foreach (int id in itemIds)
        {
            if (id == 0) continue;
            int heal = healOf(id);
            if (!Is(id))
            {
                if (heal > bestHeal) (best, bestHeal) = (id, heal);
            }
            else if (CanUse(id, gold) && heal > maestroHeal)
            {
                (maestro, maestroHeal) = (id, heal);
            }
        }
        return best != 0 ? best : maestro;
    }
}
