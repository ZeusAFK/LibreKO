using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

[Flags]
public enum GiftContactSource
{
    None = 0,
    Friend = 1,
    Clan = 2,
}

public sealed record GiftContact(string Name, int Level, int Class, bool Online, GiftContactSource Source);

public static class GiftRecipients
{
    public const int NameMaxLength = 20;

    public static List<GiftContact> Merge(IEnumerable<GiftContact> contacts) =>
        contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First() with
            {
                Level = g.Max(c => c.Level),
                Class = g.Select(c => c.Class).FirstOrDefault(c => c != 0),
                Online = g.Any(c => c.Online),
                Source = g.Aggregate(GiftContactSource.None, (all, c) => all | c.Source),
            })
            .ToList();

    public static List<GiftContact> Suggest(IEnumerable<GiftContact> contacts, string typed, string self)
    {
        var text = typed.Trim();
        return contacts
            .Where(c => !string.Equals(c.Name, self, StringComparison.OrdinalIgnoreCase))
            .Where(c => text.Length == 0 || c.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => Rank(c.Name, text))
            .ThenByDescending(c => c.Online)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static GiftContact? Exact(IEnumerable<GiftContact> contacts, string typed)
    {
        var text = typed.Trim();
        return contacts.FirstOrDefault(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
    }

    private static int Rank(string name, string text) =>
        text.Length == 0 ? 0
        : string.Equals(name, text, StringComparison.OrdinalIgnoreCase) ? 0
        : name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
        : 2;
}
