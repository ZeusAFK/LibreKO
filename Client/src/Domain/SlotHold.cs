using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class SlotHold
{
    private readonly Dictionary<int, ItemSlot> _held = new();

    public bool Active => _held.Count > 0;

    public void Hold(int abs, ItemSlot shown) => _held.TryAdd(abs, shown);

    public ItemSlot Shown(int abs, ItemSlot live) => _held.TryGetValue(abs, out var held) ? held : live;

    public void Release() => _held.Clear();
}
