using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public static class NewestFirst
{
    public static bool Upsert<T>(List<T> items, T item, Func<T, int> key)
    {
        int id = key(item);
        int index = items.FindIndex(existing => key(existing) == id);
        if (index >= 0)
        {
            items[index] = item;
            return false;
        }
        items.Insert(0, item);
        return true;
    }
}
