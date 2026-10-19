using System;
using System.Globalization;

namespace LibreKO.Domain;

public readonly struct ItemQuery
{
    private readonly string text;
    private readonly bool byId;
    private readonly int id;

    public ItemQuery(string query)
    {
        text = query.Trim();
        byId = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    public bool IsEmpty => text.Length == 0;

    public bool Matches(int itemId, Func<int, string> name) =>
        itemId != 0 && !IsEmpty
        && ((byId && itemId == id) || name(itemId).Contains(text, StringComparison.OrdinalIgnoreCase));
}
