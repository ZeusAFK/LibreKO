using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public readonly record struct Reaction(int CharacterId, string Name, string Emoji);

public readonly record struct ReactionCount(string Emoji, int Count);

public sealed class MessageReactions
{
    private readonly List<Reaction> all = [];

    public IReadOnlyList<Reaction> All => all;

    public int Total => all.Count;

    public string Of(int characterId) => all.FirstOrDefault(r => r.CharacterId == characterId).Emoji ?? "";

    public bool Set(int characterId, string name, string emoji)
    {
        int at = all.FindIndex(r => r.CharacterId == characterId);
        if (emoji.Length == 0)
        {
            if (at < 0) return false;
            all.RemoveAt(at);
            return true;
        }
        if (at >= 0)
        {
            if (all[at].Emoji == emoji) return false;
            all.RemoveAt(at);
        }
        all.Add(new Reaction(characterId, name, emoji));
        return true;
    }

    public IReadOnlyList<ReactionCount> Tally()
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reaction in all)
        {
            if (!counts.TryAdd(reaction.Emoji, 1)) counts[reaction.Emoji]++;
            else order.Add(reaction.Emoji);
        }
        return order
            .Select((emoji, index) => (Count: new ReactionCount(emoji, counts[emoji]), Index: index))
            .OrderByDescending(e => e.Count.Count)
            .ThenBy(e => e.Index)
            .Select(e => e.Count)
            .ToList();
    }

    public override bool Equals(object? obj) => obj is MessageReactions other && all.SequenceEqual(other.all);

    public override int GetHashCode() => all.Count;

    public IReadOnlyList<Reaction> Listed(string emoji, int self) =>
        all.Where(r => emoji.Length == 0 || r.Emoji == emoji)
            .OrderByDescending(r => r.CharacterId == self)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class ReactionRules
{
    public const int PillMax = 3;
    public const int MaxLength = 32;

    public static readonly IReadOnlyList<string> QuickPicks = ["👍", "❤️", "😂", "😮", "😢", "🙏"];

    public static string Count(int total) => total > 1 ? total.ToString() : "";

    public static string Title(int total) => total == 1 ? "1 reaction" : $"{total} reactions";

    public static string Toggle(string current, string picked) => current == picked ? "" : picked;

    public static bool Valid(string emoji) => emoji.Length is > 0 and <= MaxLength && !emoji.Any(char.IsControl);
}
