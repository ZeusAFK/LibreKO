using LibreKO.Quests.Syntax;

namespace LibreKO.Quests.Binding;

public enum SlotKind
{
    Int,
    Count,
    ItemId,
    TalkTextId,
    MenuTextId,
    QuestId,
    ExchangeId,
    MapId,
    ZoneId,
    SkillId,
    EffectId,
    KillGroup,
    CompareOp,
    ClassGroup,
    Nation,
    ClanRank,
    PremiumType,
    EventRef,
    NpcId,
    RewardId,
    Weekday,
}

public abstract record PhrasePart
{
    public sealed record Literal(string Word) : PhrasePart;

    public sealed record Slot(string Name, SlotKind Kind) : PhrasePart;

    public sealed record Optional(IReadOnlyList<PhrasePart> Parts) : PhrasePart;
}

public sealed class PhrasePattern
{
    public string Text { get; }
    public IReadOnlyList<PhrasePart> Parts { get; }
    public IReadOnlyList<PhrasePart.Slot> Slots { get; }
    public string FirstWord { get; }

    private PhrasePattern(string text, IReadOnlyList<PhrasePart> parts)
    {
        Text = text;
        Parts = parts;
        Slots = [.. Flatten(parts).OfType<PhrasePart.Slot>()];
        FirstWord = Flatten(parts).OfType<PhrasePart.Literal>().FirstOrDefault()?.Word ?? string.Empty;
    }

    public int RequiredWordCount => Parts.OfType<PhrasePart.Literal>().Count();

    public static PhrasePattern Parse(string pattern)
    {
        var parts = new List<PhrasePart>();
        var index = 0;
        ParseInto(pattern, ref index, parts, stopAtBracket: false);
        return new PhrasePattern(pattern, parts);
    }

    private static void ParseInto(string pattern, ref int index, List<PhrasePart> parts, bool stopAtBracket)
    {
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (c == ' ')
            {
                index++;
                continue;
            }

            if (c == ']')
            {
                if (stopAtBracket)
                    return;
                throw new FormatException($"Unbalanced ']' in phrase '{pattern}'.");
            }

            if (c == '[')
            {
                index++;
                var inner = new List<PhrasePart>();
                ParseInto(pattern, ref index, inner, stopAtBracket: true);
                if (index >= pattern.Length || pattern[index] != ']')
                    throw new FormatException($"Missing ']' in phrase '{pattern}'.");
                index++;
                parts.Add(new PhrasePart.Optional(inner));
                continue;
            }

            if (c == '{')
            {
                var close = pattern.IndexOf('}', index);
                if (close < 0)
                    throw new FormatException($"Missing '}}' in phrase '{pattern}'.");
                var body = pattern[(index + 1)..close];
                var colon = body.IndexOf(':');
                if (colon < 0)
                    throw new FormatException($"Slot '{body}' in phrase '{pattern}' needs a kind.");
                var name = body[..colon];
                var kindText = body[(colon + 1)..];
                if (!Enum.TryParse<SlotKind>(kindText, ignoreCase: true, out var kind))
                    throw new FormatException($"Unknown slot kind '{kindText}' in phrase '{pattern}'.");
                parts.Add(new PhrasePart.Slot(name, kind));
                index = close + 1;
                continue;
            }

            var start = index;
            while (index < pattern.Length && pattern[index] is not (' ' or '[' or ']' or '{'))
                index++;
            parts.Add(new PhrasePart.Literal(pattern[start..index]));
        }
    }

    private static IEnumerable<PhrasePart> Flatten(IEnumerable<PhrasePart> parts)
    {
        foreach (var part in parts)
        {
            if (part is PhrasePart.Optional optional)
            {
                foreach (var nested in Flatten(optional.Parts))
                    yield return nested;
                continue;
            }
            yield return part;
        }
    }

    public override string ToString() => Text;
}

public sealed record PhraseMatch(
    PhrasePattern Pattern,
    IReadOnlyDictionary<string, Token> Arguments,
    int TokensConsumed,
    int LiteralsMatched);

public static class PhraseMatcher
{
    public static PhraseMatch? TryMatch(PhrasePattern pattern, IReadOnlyList<Token> tokens, int start)
    {
        var arguments = new Dictionary<string, Token>(StringComparer.OrdinalIgnoreCase);
        var position = start;
        var literals = 0;
        if (!MatchParts(pattern.Parts, tokens, ref position, arguments, ref literals, required: true))
            return null;
        return new PhraseMatch(pattern, arguments, position - start, literals);
    }

    private static bool MatchParts(
        IReadOnlyList<PhrasePart> parts,
        IReadOnlyList<Token> tokens,
        ref int position,
        Dictionary<string, Token> arguments,
        ref int literals,
        bool required)
    {
        foreach (var part in parts)
        {
            switch (part)
            {
                case PhrasePart.Literal literal:
                    if (position >= tokens.Count || !tokens[position].IsWord(literal.Word))
                        return false;
                    position++;
                    literals++;
                    break;

                case PhrasePart.Slot slot:
                    if (position >= tokens.Count || !SlotAccepts(slot.Kind, tokens[position]))
                        return false;
                    arguments[slot.Name] = tokens[position];
                    position++;
                    break;

                case PhrasePart.Optional optional:
                    var probePosition = position;
                    var probeArguments = new Dictionary<string, Token>(arguments, StringComparer.OrdinalIgnoreCase);
                    var probeLiterals = literals;
                    if (MatchParts(optional.Parts, tokens, ref probePosition, probeArguments, ref probeLiterals, required: false))
                    {
                        position = probePosition;
                        literals = probeLiterals;
                        foreach (var pair in probeArguments)
                            arguments[pair.Key] = pair.Value;
                    }
                    break;
            }
        }

        return true;
    }

    private static bool SlotAccepts(SlotKind kind, Token token) => kind switch
    {
        SlotKind.CompareOp => token.Kind == TokenKind.Operator,
        SlotKind.ClassGroup or SlotKind.Nation or SlotKind.PremiumType or SlotKind.Weekday => token.Kind == TokenKind.Word,
        SlotKind.EventRef => token.Kind is TokenKind.String or TokenKind.Word,
        SlotKind.Int or SlotKind.Count or SlotKind.KillGroup => token.Kind == TokenKind.Number,
        SlotKind.MapId => token.Kind == TokenKind.Word,
        SlotKind.QuestId => token.Kind == TokenKind.Number
            || token.Kind == TokenKind.Word && !token.IsWord("and") && !token.IsWord("or"),
        SlotKind.TalkTextId or SlotKind.MenuTextId =>
            token.Kind is TokenKind.String or TokenKind.Number or TokenKind.Word,
        _ => token.Kind is TokenKind.Word or TokenKind.Number,
    };
}

public static class SlotKinds
{
    private static readonly Dictionary<SlotKind, string> Names = new()
    {
        [SlotKind.EventRef] = "event",
        [SlotKind.TalkTextId] = "dialogue",
        [SlotKind.MenuTextId] = "button label",
        [SlotKind.ItemId] = "item",
        [SlotKind.ExchangeId] = "exchange",
        [SlotKind.QuestId] = "quest",
        [SlotKind.MapId] = "map",
        [SlotKind.ZoneId] = "zone",
        [SlotKind.SkillId] = "skill",
        [SlotKind.EffectId] = "effect",
        [SlotKind.NpcId] = "npc",
        [SlotKind.RewardId] = "reward",
    };

    public static string Name(SlotKind kind) =>
        Names.TryGetValue(kind, out var name) ? name : kind.ToString();
}
