using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private ItemSearchPanel _admItemSearch = null!;
    private bool _admItemsLoaded;

    private Control BuildAdminItemsTab()
    {
        _admItemSearch = new ItemSearchPanel(
            tradeableOnly: false,
            actionText: "Add",
            onAction: (hit, count) => OnAdminGiveItem(hit.Id, count),
            showTooltip: itemId => ShowItemTooltip(-1, TooltipItem(itemId)),
            hideTooltip: HideItemTooltip);
        return _admItemSearch;
    }

    private void OnAdminGiveItem(int itemId, int count)
    {
        Net.I.SendAdminGiveItem(itemId, count);
        SetAdminStatus($"Requesting {ItemData.DisplayName(itemId)} x{count}…", false);
    }
}
