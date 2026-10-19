namespace LibreKO.Features.Windows;

internal enum PowerUpStorePurchase { None, Cart, BuyNow }

internal sealed record PowerUpStoreRecipient(string Name, int Level, int Class)
{
    internal string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Level > 0) parts.Add($"Level {Level}");
            if (Class > 0)
            {
                parts.Add(CharacterClassCatalog.DisplayName(Class));
                parts.Add(Nations.Name(Nations.OfClass(Class)));
            }
            return string.Join(" · ", parts);
        }
    }
}

internal sealed class PowerUpStoreModel
{
    internal List<PowerUpStoreEntry> Catalog { get; } = new();

    internal void ReplaceCatalog(IEnumerable<PowerUpStoreEntry> entries)
    {
        Catalog.Clear();
        Catalog.AddRange(entries);
    }

    internal List<ShoppingMallCategory> Categories { get; } = new();

    internal PowerUpStoreCart Cart { get; } = new();

    internal PowerUpStoreRecipient? GiftRecipient { get; set; }

    internal PowerUpStorePurchase Pending { get; set; }

    internal bool Busy => Pending != PowerUpStorePurchase.None;

    internal bool Loaded { get; set; }

    internal string StoreError { get; set; } = "";

    internal string CategoryName(int id) => Categories.Find(c => c.Id == id).Name ?? "";
}
