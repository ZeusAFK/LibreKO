using LibreKO.Quests.Binding;
using LibreKO.Quests.Text;

namespace LibreKO.Quests.Syntax;

public enum QuestTokenKind
{
    Declaration,
    Control,
    Effect,
    Presentation,
    EventName,
    Id,
    Text,
    Domain,
    Reader,
    Comment,
    Operator,
}

public readonly record struct ClassifiedSpan(int Start, int Length, QuestTokenKind Kind);

public static class QuestClassifier
{
    private static readonly HashSet<string> Declarations = new(StringComparer.OrdinalIgnoreCase)
    {
        "npc", "quest", "bind", "zone",
    };

    private const string EventWord = "event";

    private static readonly HashSet<string> Control = new(StringComparer.OrdinalIgnoreCase)
    {
        "on", "if", "else", "by", "goto", "do", "close", "choose",
    };

    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not",
    };

    private static readonly HashSet<string> Status = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed", "started", "unstarted",
    };

    private static readonly HashSet<string> StructureVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "kill", "title", "journal", "daily", "requires", "rewards",
    };

    private static readonly HashSet<string> StructureWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "roll", "needs", "any",
    };

    private static readonly HashSet<string> EffectVerbs = BuildVerbs(effects: true);
    private static readonly HashSet<string> PresentationVerbs = BuildVerbs(effects: false);
    private static readonly HashSet<string> Readers = BuildReaders();

    private static bool IsEffect(QuestActionKind kind) => kind is not (
        QuestActionKind.Say or QuestActionKind.Button or QuestActionKind.Announce
        or QuestActionKind.ShowMap or QuestActionKind.OpenStatSkillPanel
        or QuestActionKind.OpenRenamePanel or QuestActionKind.Goto);

    private static HashSet<string> BuildVerbs(bool effects)
    {
        var verbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in QuestVocabulary.Actions)
        {
            if (action.Kind == QuestActionKind.Goto)
                continue;
            if (IsEffect(action.Kind) == effects && action.Pattern.FirstWord.Length > 0)
                verbs.Add(action.Pattern.FirstWord);
        }
        return verbs;
    }

    private static HashSet<string> BuildReaders()
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var condition in QuestVocabulary.Conditions)
            foreach (var word in Literals(condition.Pattern.Parts))
                words.Add(word);

        foreach (var action in QuestVocabulary.Actions)
        {
            var first = true;
            foreach (var word in Literals(action.Pattern.Parts))
            {
                if (first)
                {
                    first = false;
                    continue;
                }
                words.Add(word);
            }
        }

        words.ExceptWith(Status);
        words.ExceptWith(Control);
        words.ExceptWith(Operators);
        return words;
    }

    private static IEnumerable<string> Literals(IEnumerable<PhrasePart> parts)
    {
        foreach (var part in parts)
        {
            switch (part)
            {
                case PhrasePart.Literal literal:
                    yield return literal.Word;
                    break;
                case PhrasePart.Optional optional:
                    foreach (var nested in Literals(optional.Parts))
                        yield return nested;
                    break;
            }
        }
    }

    public static IReadOnlyList<ClassifiedSpan> Classify(QuestCompilation compilation)
    {
        var source = compilation.Source;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).Tokenize();

        var eventNames = new HashSet<string>(compilation.Program.EventNames.Keys, StringComparer.OrdinalIgnoreCase);

        var spans = new List<ClassifiedSpan>();
        var lineStartsAt = -1;

        foreach (var token in tokens)
        {
            if (token.Kind is TokenKind.EndOfFile)
                break;

            var line = source.GetLineIndex(token.Span.Start);
            var firstOnLine = line != lineStartsAt;
            lineStartsAt = line;

            var kind = Classify(token, firstOnLine, eventNames);
            if (kind is null)
                continue;

            spans.Add(new ClassifiedSpan(token.Span.Start, token.Span.Length, kind.Value));
        }

        AddComments(source, spans);
        spans.Sort((left, right) => left.Start.CompareTo(right.Start));
        return spans;
    }

    private static QuestTokenKind? Classify(
        Token token,
        bool firstOnLine,
        HashSet<string> eventNames)
    {
        switch (token.Kind)
        {
            case TokenKind.Number:
                return QuestTokenKind.Id;

            case TokenKind.String:
                return eventNames.Contains(token.Text) ? QuestTokenKind.EventName : QuestTokenKind.Text;

            case TokenKind.Word:
                if (firstOnLine && (token.IsWord("transaction") || token.IsWord("for")))
                    return QuestTokenKind.Control;
                if (Operators.Contains(token.Text))
                    return QuestTokenKind.Operator;
                if (QuestVocabulary.ClassGroups.ContainsKey(token.Text)
                    || QuestVocabulary.Nations.ContainsKey(token.Text)
                    || QuestVocabulary.ClanRanks.ContainsKey(token.Text)
                    || QuestVocabulary.Weekdays.ContainsKey(token.Text)
                    || Status.Contains(token.Text))
                    return QuestTokenKind.Domain;
                if (EventWord.Equals(token.Text, StringComparison.OrdinalIgnoreCase))
                    return QuestTokenKind.EventName;
                if (firstOnLine && Declarations.Contains(token.Text))
                    return QuestTokenKind.Declaration;
                if (Control.Contains(token.Text))
                    return QuestTokenKind.Control;
                if (firstOnLine && (EffectVerbs.Contains(token.Text) || StructureVerbs.Contains(token.Text)))
                    return QuestTokenKind.Effect;
                if (firstOnLine && PresentationVerbs.Contains(token.Text))
                    return QuestTokenKind.Presentation;
                if (Readers.Contains(token.Text) || StructureWords.Contains(token.Text))
                    return QuestTokenKind.Reader;
                if (eventNames.Contains(token.Text))
                    return QuestTokenKind.EventName;
                return firstOnLine ? QuestTokenKind.Presentation : QuestTokenKind.Declaration;

            default:
                return null;
        }
    }

    private static void AddComments(SourceText source, List<ClassifiedSpan> spans)
    {
        var position = 0;
        bool Matches(string text) => position + text.Length <= source.Length
            && source.GetText(new TextSpan(position, text.Length)) == text;
        void Add(int start, int end)
        {
            if (end > start)
                spans.Add(new ClassifiedSpan(start, end - start, QuestTokenKind.Comment));
        }
        while (position < source.Length)
        {
            if (source[position] == '"')
            {
                position++;
                while (position < source.Length && source[position] != '"')
                    position += source[position] == '\\' && position + 1 < source.Length ? 2 : 1;
                position++;
                continue;
            }
            if (Matches("//"))
            {
                var start = position;
                while (position < source.Length && source[position] is not ('\n' or '\r'))
                    position++;
                Add(start, position);
                continue;
            }
            if (Matches("/*"))
            {
                var start = position;
                position += 2;
                while (position < source.Length && !Matches("*/"))
                {
                    if (source[position] is '\n' or '\r')
                    {
                        Add(start, position);
                        start = position + 1;
                    }
                    position++;
                }
                if (Matches("*/"))
                    position += 2;
                Add(start, position);
                continue;
            }
            position++;
        }
    }
}
