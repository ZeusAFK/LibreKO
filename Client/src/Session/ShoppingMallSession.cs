namespace LibreKO.Session;

public sealed class ShoppingMallSession
{
    public event Action<short, short>? ShoppingMallOpenEvent;

    internal void RaiseShoppingMallOpen(short arg1, short arg2) => ShoppingMallOpenEvent?.Invoke(arg1, arg2);

    public event Action<List<PowerUpStoreEntry>>? ShoppingMallCatalogEvent;

    internal void RaiseShoppingMallCatalog(List<PowerUpStoreEntry> list) => ShoppingMallCatalogEvent?.Invoke(list);

    public event Action<List<ShoppingMallCategory>>? ShoppingMallCategoriesEvent;

    internal void RaiseShoppingMallCategories(List<ShoppingMallCategory> list) => ShoppingMallCategoriesEvent?.Invoke(list);

    public event Action<int>? ShoppingMallBalanceEvent;

    internal void RaiseShoppingMallBalance(int value) => ShoppingMallBalanceEvent?.Invoke(value);

    public event Action<PowerUpStoreResult, int>? ShoppingMallPurchaseEvent;

    internal void RaiseShoppingMallPurchase(PowerUpStoreResult powerUpStoreResult, int arg2) => ShoppingMallPurchaseEvent?.Invoke(powerUpStoreResult, arg2);

    public event Action<PowerUpStoreResult, string, int, int>? ShoppingMallRecipientEvent;

    internal void RaiseShoppingMallRecipient(PowerUpStoreResult powerUpStoreResult, string arg2, int arg3, int arg4) => ShoppingMallRecipientEvent?.Invoke(powerUpStoreResult, arg2, arg3, arg4);
}
