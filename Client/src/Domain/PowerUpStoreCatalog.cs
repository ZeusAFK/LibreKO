using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public sealed record PowerUpStoreEntry(
    int Id,
    int ItemId,
    string Name,
    string Description,
    int Category,
    int BasePrice,
    bool Featured = false,
    int DiscountPrice = 0,
    DateTime? DiscountEndsAt = null)
{
    public bool Discounted => DiscountPrice > 0 && DiscountPrice < BasePrice;
    public int Price => Discounted ? DiscountPrice : BasePrice;
}

public enum PowerUpStoreSort
{
    NameAToZ,
    NameZToA,
    PriceLowToHigh,
    PriceHighToLow,
}

public static class PowerUpStoreCatalog
{
    public const int FeaturedCategory = 0;
    public const PowerUpStoreSort DefaultSort = PowerUpStoreSort.NameAToZ;

    public static IReadOnlyList<PowerUpStoreSort> Sorts { get; } = Enum.GetValues<PowerUpStoreSort>();

    public static string Label(PowerUpStoreSort sort) => sort switch
    {
        PowerUpStoreSort.NameZToA => "Name: Z to A",
        PowerUpStoreSort.PriceLowToHigh => "Price: low to high",
        PowerUpStoreSort.PriceHighToLow => "Price: high to low",
        _ => "Name: A to Z",
    };

    public static List<PowerUpStoreEntry> View(IEnumerable<PowerUpStoreEntry> catalogue, int category, string search, PowerUpStoreSort sort)
    {
        var text = search.Trim();
        var shown = text.Length > 0
            ? catalogue.Where(e => e.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            : InCategory(catalogue, category);
        return (sort switch
        {
            PowerUpStoreSort.PriceLowToHigh => shown.OrderBy(e => e.Price).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            PowerUpStoreSort.PriceHighToLow => shown.OrderByDescending(e => e.Price).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            PowerUpStoreSort.NameZToA => shown.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => shown.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        }).ToList();
    }

    public static bool HasDiscount(IEnumerable<PowerUpStoreEntry> catalogue, int category) =>
        category != FeaturedCategory && catalogue.Any(e => e.Category == category && e.Discounted);

    private static IEnumerable<PowerUpStoreEntry> InCategory(IEnumerable<PowerUpStoreEntry> catalogue, int category) =>
        category == FeaturedCategory
            ? catalogue.Where(e => e.Featured)
            : catalogue.Where(e => e.Category == category);
}
