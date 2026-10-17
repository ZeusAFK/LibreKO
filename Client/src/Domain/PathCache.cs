using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class PathCache<T> where T : class
{
    private readonly Dictionary<string, T?> _kept = new();

    public int Count => _kept.Count;

    public T? Get(string path, Func<string, T?> load)
    {
        if (_kept.TryGetValue(path, out var kept)) return kept;
        kept = load(path);
        _kept[path] = kept;
        return kept;
    }

    public void Clear() => _kept.Clear();
}
