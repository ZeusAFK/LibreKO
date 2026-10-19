namespace LibreKO.Session;

public sealed class ItemUpgradeSession
{
    public event Action<int>? UpgradeOpenEvent;

    internal void RaiseUpgradeOpen(int value) => UpgradeOpenEvent?.Invoke(value);

    public event Action<UpgradeResult>? UpgradeResultEvent;

    internal void RaiseUpgradeResult(UpgradeResult upgradeResult) => UpgradeResultEvent?.Invoke(upgradeResult);

    public event Action<ItemSealType, ItemSealResult, int, int>? ItemSealEvent;

    internal void RaiseItemSeal(ItemSealType itemSealType, ItemSealResult itemSealResult, int arg3, int arg4) => ItemSealEvent?.Invoke(itemSealType, itemSealResult, arg3, arg4);
}
