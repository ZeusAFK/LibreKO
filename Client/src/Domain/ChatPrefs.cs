using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LibreKO.Domain;

public sealed class ChatPrefs
{
    public const int DefaultFontSize = 1;
    public const int DefaultBackground = 1;
    public static readonly int[] FontSizes = { 12, 13, 15 };
    public static readonly float[] Backgrounds = { 0.9f, 0.5f, 0.2f, 0f };
    private const string FilterPrefix = "f.";

    private readonly Dictionary<string, ChatCategory> _filters = new();

    public string Tab { get; set; } = "all";
    public bool Timestamps { get; set; }
    public int FontSize { get; set; } = DefaultFontSize;
    public int Background { get; set; } = DefaultBackground;
    public bool Locked { get; set; }
    public bool NearbyShown { get; set; } = true;

    public ChatCategory Filter(ChatTabSpec tab) => _filters.TryGetValue(tab.Id, out var filter) ? filter : tab.Filter;

    public void SetFilter(ChatTabSpec tab, ChatCategory filter)
    {
        if (filter == tab.Filter) _filters.Remove(tab.Id);
        else _filters[tab.Id] = filter;
    }

    public void ResetFilters() => _filters.Clear();

    public string Format()
    {
        var text = new StringBuilder();
        text.Append("tab=").Append(Tab);
        text.Append(";ts=").Append(Timestamps ? 1 : 0);
        text.Append(";font=").Append(FontSize);
        text.Append(";bg=").Append(Background);
        text.Append(";lock=").Append(Locked ? 1 : 0);
        text.Append(";nearbylist=").Append(NearbyShown ? 1 : 0);
        foreach (var (id, filter) in _filters)
            text.Append(';').Append(FilterPrefix).Append(id).Append('=').Append((int)filter);
        return text.ToString();
    }

    public static ChatPrefs Parse(string text)
    {
        var prefs = new ChatPrefs();
        foreach (string part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq];
            string value = part[(eq + 1)..];
            bool number = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
            switch (key)
            {
                case "tab" when ChatTabs.IndexOf(value) >= 0: prefs.Tab = value; break;
                case "ts" when number: prefs.Timestamps = n != 0; break;
                case "font" when number && n >= 0 && n < FontSizes.Length: prefs.FontSize = n; break;
                case "bg" when number && n >= 0 && n < Backgrounds.Length: prefs.Background = n; break;
                case "lock" when number: prefs.Locked = n != 0; break;
                case "nearbylist" when number: prefs.NearbyShown = n != 0; break;
                default:
                    if (number && key.StartsWith(FilterPrefix, StringComparison.Ordinal)
                        && ChatTabs.IndexOf(key[FilterPrefix.Length..]) is int index and >= 0)
                        prefs.SetFilter(ChatTabs.All[index], (ChatCategory)n & ChatCategory.All);
                    break;
            }
        }
        return prefs;
    }
}
