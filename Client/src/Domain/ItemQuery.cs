using System;
using System.Globalization;

namespace LibreKO.Domain;

public readonly struct ItemQuery
{
    private readonly string _text;
    private readonly bool _byId;
    private readonly int _id;

    public ItemQuery(string query)
    {
        _text = query.Trim();
        _byId = int.TryParse(_text, NumberStyles.None, CultureInfo.InvariantCulture, out _id);
    }

    public bool IsEmpty => _text.Length == 0;

    public bool Matches(int itemId, Func<int, string> name) =>
        itemId != 0 && !IsEmpty
        && ((_byId && itemId == _id) || name(itemId).Contains(_text, StringComparison.OrdinalIgnoreCase));
}
