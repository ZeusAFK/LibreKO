using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class PathCache<T> where T : class
{
    private readonly Dictionary<string, T?> byPath = new();

    public int Count => byPath.Count;

    public T? Get(string path, Func<string, T?> load)
    {
        if (byPath.TryGetValue(path, out var found)) return found;
        found = load(path);
        byPath[path] = found;
        return found;
    }

    public void Clear() => byPath.Clear();
}
