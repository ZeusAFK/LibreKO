using System.Collections;

namespace LibreKO.Presentation;

public sealed class EntityRegistry : IReadOnlyDictionary<int, EntityView>
{
    private readonly Dictionary<int, EntityView> views = new();

    public EntityView this[int id]
    {
        get => views[id];
        set => views[id] = value;
    }

    public int Count => views.Count;

    public IEnumerable<int> Keys => views.Keys;

    public IEnumerable<EntityView> Values => views.Values;

    public bool ContainsKey(int id) => views.ContainsKey(id);

    public bool TryGetValue(int id, out EntityView view) => views.TryGetValue(id, out view!);

    public bool Remove(int id) => views.Remove(id);

    public void Clear() => views.Clear();

    public IEnumerator<KeyValuePair<int, EntityView>> GetEnumerator() => views.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
