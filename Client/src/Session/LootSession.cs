namespace LibreKO.Session;

public sealed class LootSession
{
    public event Action<int>? LootGoldEvent;

    internal void RaiseLootGold(int value) => LootGoldEvent?.Invoke(value);

    public event Action<int>? LootItemEvent;

    internal void RaiseLootItem(int value) => LootItemEvent?.Invoke(value);

    public event Action<string, int>? PartyLootEvent;

    internal void RaisePartyLoot(string arg1, int arg2) => PartyLootEvent?.Invoke(arg1, arg2);

    public event Action<int, bool>? TemporaryLootEvent;

    internal void RaiseTemporaryLoot(int arg1, bool arg2) => TemporaryLootEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? LootDropEvent;

    internal void RaiseLootDrop(int arg1, int arg2) => LootDropEvent?.Invoke(arg1, arg2);

    public event Action<int, List<LootEntry>>? LootContentsEvent;

    internal void RaiseLootContents(int arg1, List<LootEntry> list) => LootContentsEvent?.Invoke(arg1, list);

    public event Action<int, int, int>? LootTakenEvent;

    internal void RaiseLootTaken(int arg1, int arg2, int arg3) => LootTakenEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int>? LootFailEvent;

    internal void RaiseLootFail(int value) => LootFailEvent?.Invoke(value);

    public event Action<int, int>? LootRefusedEvent;

    internal void RaiseLootRefused(int arg1, int arg2) => LootRefusedEvent?.Invoke(arg1, arg2);
}
