namespace LibreKO.Features;

internal interface IInventoryView
{
    event Action? Refreshed;

    bool MoveBusy { get; }

    IReadOnlyList<ItemCell> BagCells { get; }

    IReadOnlyList<ItemCell> MagicBagCells { get; }

    IReadOnlyDictionary<int, ItemCell> Cells { get; }

    int CarriedWeight();

    void Refresh();

    void RefreshCash();

    void RefreshArrangeButton();

    void Hover(ItemCell cell, bool entered);

    void EnqueuePetMove(ItemMoveDirection dir, int itemId, byte src, byte dst, int bagAbs, int petPos);

    void Activate(int absSlot);

    void RefreshFooter();
}
