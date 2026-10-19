namespace LibreKO.Network;

public enum ShoppingMallSubOpcode : byte
{
    Open = 1,
    Close = 2,
    Purchase = 8,
}

public enum ShoppingMallOpenKind : byte
{
    Catalog = 3,
    Categories = 4,
    Balance = 5,
}

public enum ShoppingMallPurchaseSubOpcode : byte
{
    Cart = 1,
    CheckRecipient = 2,
}

public readonly record struct ShoppingMallCategory(byte Id, string Name);
