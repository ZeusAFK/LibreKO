namespace LibreKO.Session;

public sealed class EntitiesSession
{
    private readonly Dictionary<int, EntitySnapshot> known = new();

    public IEnumerable<EntitySnapshot> All => known.Values;

    public int Count => known.Count;

    public bool TryGet(int id, out EntitySnapshot entity) => known.TryGetValue(id, out entity!);

    public bool IsKnownAttackable(int id) => known.TryGetValue(id, out var snapshot) && snapshot.Attackable;

    public void Replay(Action<EntitySnapshot> onSpawn)
    {
        foreach (var e in known.Values)
            onSpawn(e);
    }

    internal void Add(EntitySnapshot entity) => known[entity.Id] = entity;

    internal bool Remove(int id) => known.Remove(id);

    internal void Clear() => known.Clear();

    public event Action<EntitySnapshot>? EntitySpawnEvent;

    internal void RaiseEntitySpawn(EntitySnapshot entitySnapshot) => EntitySpawnEvent?.Invoke(entitySnapshot);

    public event Action<EntityMove>? EntityMoveEvent;

    internal void RaiseEntityMove(EntityMove move) => EntityMoveEvent?.Invoke(move);

    public event Action<int, float>? EntityRotateEvent;

    internal void RaiseEntityRotate(int arg1, float arg2) => EntityRotateEvent?.Invoke(arg1, arg2);

    public event Action<int>? EntityOutEvent;

    internal void RaiseEntityOut(int value) => EntityOutEvent?.Invoke(value);

    public event Action<int, int, int, int>? EntityHpEvent;

    internal void RaiseEntityHp(int arg1, int arg2, int arg3, int arg4) => EntityHpEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action<int, int, int>? EntityHpSyncEvent;

    internal void RaiseEntityHpSync(int arg1, int arg2, int arg3) => EntityHpSyncEvent?.Invoke(arg1, arg2, arg3);
}
