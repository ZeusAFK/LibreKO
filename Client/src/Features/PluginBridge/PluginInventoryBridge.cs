using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginInventoryBridge(InventoryFeature inventory, ItemRoutingFeature itemRouting, ItemTooltipFeature itemTooltip, SelfSession self, IItemsRequests itemsRequests) : IGameInventory
{
    event Action? IGameInventory.Changed { add { } remove { } }

    public void RaiseInventory() => Callable.From(PluginHost.Game.RaiseInventory).CallDeferred();

    public int Length => self.Inventory.Length;

    public int GridStart => Inventory.GridStart;

    public int GridCount => Inventory.GridCount;

    public GameItem At(int slot)
    {
        if (slot < 0 || slot >= self.Inventory.Length) return GameItem.Empty(slot);
        var it = self.Inventory[slot];
        if (it.IsEmpty) return GameItem.Empty(slot);
        var def = ItemData.Get(it.ItemId);
        return new GameItem(slot, it.ItemId, ItemData.DisplayName(it.ItemId), ItemData.ShownCount(def, it),
            it.Durability, def?.UnbindStones ?? 0, def?.Kind ?? 0, ItemIcons.Get(it.ItemId),
            def != null && Inventory.IsTwoHandedSlotType(def.Slot), it.ExpiresAt, it.Flag);
    }

    public void Move(int from, int to) => inventory.MoveBetween(from, to);

    public void MoveAmount(int from, int to, int count) => inventory.MoveBetween(from, to, count);

    public void Use(int slot) => itemRouting.Context(slot);

    public void Drop(int slot) => inventory.AskDeleteItem(slot);

    public void Arrange() => itemsRequests.InventoryArrange();

    void IGameInventory.ShowTooltip(int slot)
    {
        if (slot < 0 || slot >= self.Inventory.Length || self.Inventory[slot].IsEmpty) return;
        itemTooltip.Show(slot, self.Inventory[slot]);
    }

    void IGameInventory.HideTooltip() => itemTooltip.Hide();
}
