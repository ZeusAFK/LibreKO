namespace LibreKO.Features.PluginBridge;

internal sealed class PluginServiceBridge(VendorFeature vendor, ItemTooltipFeature itemTooltip, WarehouseFeature warehouse, VipWarehouseFeature vipWarehouse, NpcQuestSession npcQuest) : IGameVendor, IGameWarehouse, IGameVipVault
{
    event Action? IGameVendor.Changed { add { } remove { } }

    event Action? IGameWarehouse.Changed { add { } remove { } }

    event Action? IGameVipVault.Changed { add { } remove { } }

    bool IGameVendor.IsOpen => vendor.IsOpen;

    string IGameVendor.Title => vendor.IsOpen ? npcQuest.LastNpcName : "";

    bool IGameVendor.PaysInNp => vendor.LoyaltyShop;

    int IGameVendor.Page => vendor.Page;

    int IGameVendor.Pages => vendor.Pages;

    IReadOnlyList<GameItem> IGameVendor.Items =>
        vendor.CellIds.Select((id, i) => id == 0 ? GameItem.Empty(i) : PluginBridgeFeature.PluginItem(i, UiKit.TooltipItem(id))).ToList();

    int IGameVendor.Price(int index) => VendorId(index) is { } id ? ItemData.BuyPrice(id) : 0;

    void IGameVendor.Buy(int index)
    {
        if (VendorId(index) is not { } id) return;
        vendor.SelectItem(id);
        vendor.AskBuy(id, -1);
    }

    void IGameVendor.Sell(int bagSlot) => vendor.TakeFromBag(bagSlot);

    void IGameVendor.SetPage(int page) => vendor.ShowPage(page);

    void IGameVendor.Search(string text)
    {
        vendor.Search.Text = text;
        vendor.OnSearch();
    }

    void IGameVendor.ShowTooltip(int index)
    {
        if (VendorId(index) is { } id) itemTooltip.Show(-1, UiKit.TooltipItem(id));
    }

    void IGameVendor.HideTooltip() => itemTooltip.Hide();

    void IGameVendor.Close() => vendor.Close();

    private int? VendorId(int index) =>
        vendor.IsOpen && index >= 0 && index < vendor.CellIds.Length && vendor.CellIds[index] != 0
            ? vendor.CellIds[index] : null;

    bool IGameWarehouse.IsOpen => warehouse.IsOpen;

    int IGameWarehouse.Page => warehouse.Page;

    int IGameWarehouse.Pages => WarehouseView.UiPages;

    int IGameWarehouse.Gold => warehouse.Money;

    IReadOnlyList<GameItem> IGameWarehouse.Items =>
        Enumerable.Range(0, WarehouseFeature.PageSize).Select(i => PluginBridgeFeature.PluginItem(i, warehouse.StoredItems[warehouse.Abs(i)])).ToList();

    void IGameWarehouse.Store(int bagSlot) => warehouse.TakeFromBag(bagSlot);

    void IGameWarehouse.Take(int index)
    {
        if (warehouse.IsOpen && index >= 0 && index < WarehouseFeature.PageSize && !warehouse.StoredItems[warehouse.Abs(index)].IsEmpty)
            warehouse.WithdrawSlot(warehouse.Abs(index));
    }

    void IGameWarehouse.SetPage(int page) => warehouse.TurnPage(page - warehouse.Page);

    void IGameWarehouse.ShowTooltip(int index)
    {
        if (index >= 0 && index < WarehouseFeature.PageSize && !warehouse.StoredItems[warehouse.Abs(index)].IsEmpty)
            itemTooltip.Show(-1, warehouse.StoredItems[warehouse.Abs(index)]);
    }

    void IGameWarehouse.HideTooltip() => itemTooltip.Hide();

    void IGameWarehouse.Close() => warehouse.Close();

    bool IGameVipVault.IsOpen => vipWarehouse.IsOpen;

    string IGameVipVault.Expiry => vipWarehouse.IsOpen ? vipWarehouse.ExpiryText() : "";

    IReadOnlyList<GameItem> IGameVipVault.Items => vipWarehouse.StoredItems.Select((slot, i) => PluginBridgeFeature.PluginItem(i, slot)).ToList();

    void IGameVipVault.Store(int bagSlot) => vipWarehouse.TakeFromBag(bagSlot);

    void IGameVipVault.Take(int index)
    {
        if (vipWarehouse.IsOpen && index >= 0 && index < vipWarehouse.StoredItems.Length && !vipWarehouse.StoredItems[index].IsEmpty)
            vipWarehouse.AskWithdraw(index, -1);
    }

    void IGameVipVault.ShowTooltip(int index)
    {
        if (index >= 0 && index < vipWarehouse.StoredItems.Length && !vipWarehouse.StoredItems[index].IsEmpty)
            itemTooltip.Show(-1, vipWarehouse.StoredItems[index]);
    }

    void IGameVipVault.HideTooltip() => itemTooltip.Hide();

    void IGameVipVault.Close() => vipWarehouse.Close();
}
