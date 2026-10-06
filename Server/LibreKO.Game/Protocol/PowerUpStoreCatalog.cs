using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.Protocol;

public readonly record struct PowerUpStorePrice(int Price, int BasePrice, DateTime? DiscountEndsAt)
{
    public bool Discounted => Price < BasePrice;
}

public static class PowerUpStoreCatalog
{
    public static PowerUpStorePrice PriceOf(PusItemData item, IEnumerable<PusDiscountData> discounts, DateTime now)
    {
        var best = discounts
            .Where(d => d.PusItemId == item.Id && d.Price > 0 && d.Price < item.Price && d.ActiveAt(now))
            .OrderBy(d => d.Price)
            .ThenBy(d => d.EndsAt ?? DateTime.MaxValue)
            .FirstOrDefault();
        return best == null
            ? new PowerUpStorePrice(item.Price, item.Price, null)
            : new PowerUpStorePrice(best.Price, item.Price, best.EndsAt);
    }

    public static List<string> SeedProblems(
        IEnumerable<PusItemData> items, IEnumerable<PusDiscountData> discounts, Func<int, bool> itemExists)
    {
        var problems = new List<string>();
        var storeItems = new HashSet<int>();
        foreach (var item in items)
        {
            storeItems.Add(item.Id);
            if (!itemExists(item.ItemId))
                problems.Add($"Power-Up Store item {item.Id} sells item {item.ItemId}, which is not in the item table; it is left out of the store");
        }

        foreach (var discount in discounts)
        {
            if (!storeItems.Contains(discount.PusItemId))
                problems.Add($"Power-Up Store discount {discount.Id} is for store item {discount.PusItemId}, which does not exist");
        }

        return problems;
    }
}
