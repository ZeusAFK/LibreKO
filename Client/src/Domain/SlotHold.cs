using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class SlotHold
{
    private readonly Dictionary<int, ItemSlot> held = new();

    public bool Active => held.Count > 0;

    public void Hold(int abs, ItemSlot shown) => held.TryAdd(abs, shown);

    public ItemSlot Shown(int abs, ItemSlot live) => held.TryGetValue(abs, out var heldSlot) ? heldSlot : live;

    public void Release() => held.Clear();
}
