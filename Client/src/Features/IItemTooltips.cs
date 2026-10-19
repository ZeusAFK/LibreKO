using Godot;

namespace LibreKO.Features;

internal interface IItemTooltips
{
    void Show(int absSlot, ItemSlot item, string note = "", ShopPricing? shop = null);

    void Hide();

    Control Hover(Control row, int itemId);

    void ShowItem(int itemId);

    void Hover(WarehouseCell cell);
}
