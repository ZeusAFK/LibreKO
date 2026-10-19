using LibreKO.Quests.Text;

namespace LibreKO.Quests.Syntax;

public enum TokenKind
{
    Word,
    Number,
    String,
    Operator,
    Comma,
    Colon,
    OpenParen,
    CloseParen,
    LineBreak,
    EndOfFile,
    Bad,
}

public readonly record struct Token(TokenKind Kind, string Text, TextSpan Span, long Value = 0)
{
    public bool IsWord(string text) =>
        Kind == TokenKind.Word && string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Kind switch
    {
        TokenKind.LineBreak => "end of line",
        TokenKind.EndOfFile => "end of file",
        _ => $"'{Text}'",
    };
}

public sealed class Lexer(SourceText source, DiagnosticBag diagnostics)
{
    private static readonly string[] MultiCharOperators = [">=", "<=", "==", "!=", "<>"];

    private int position;
    private bool blockComment;
    private int commentStart;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var token = Next();
            if (token.Kind == TokenKind.LineBreak
                && (tokens.Count == 0 || tokens[^1].Kind == TokenKind.LineBreak))
                continue;
            tokens.Add(token);
            if (token.Kind == TokenKind.EndOfFile)
                break;
        }
        return tokens;
    }

    private Token Next()
    {
        SkipTrivia();

        if (position >= source.Length)
            return new Token(TokenKind.EndOfFile, string.Empty, new TextSpan(source.Length, 0));

        var start = position;
        var current = source[position];

        if (current is '\n' or '\r')
        {
            if (current == '\r' && position + 1 < source.Length && source[position + 1] == '\n')
                position++;
            position++;
            return new Token(TokenKind.LineBreak, "\n", TextSpan.FromBounds(start, position));
        }

        if (current == ',')
        {
            position++;
            return new Token(TokenKind.Comma, ",", TextSpan.FromBounds(start, position));
        }

        if (current == ':')
        {
            position++;
            return new Token(TokenKind.Colon, ":", TextSpan.FromBounds(start, position));
        }

        if (current == '(')
        {
            position++;
            return new Token(TokenKind.OpenParen, "(", TextSpan.FromBounds(start, position));
        }

        if (current == ')')
        {
            position++;
            return new Token(TokenKind.CloseParen, ")", TextSpan.FromBounds(start, position));
        }

        if (current == '"')
            return ReadString(start);

        foreach (var op in MultiCharOperators)
        {
            if (!MatchesAt(position, op))
                continue;
            position += op.Length;
            return new Token(TokenKind.Operator, op, TextSpan.FromBounds(start, position));
        }

        if (current is '>' or '<' or '=')
        {
            position++;
            return new Token(TokenKind.Operator, current.ToString(), TextSpan.FromBounds(start, position));
        }

        if (char.IsAsciiDigit(current) || (current == '-' && position + 1 < source.Length && char.IsAsciiDigit(source[position + 1])))
            return ReadNumber(start);

        if (IsWordStart(current))
            return ReadWord(start);

        position++;
        var span = TextSpan.FromBounds(start, position);
        diagnostics.Error(DiagnosticId.UnexpectedCharacter, span, $"Unexpected character '{current}'.");
        return new Token(TokenKind.Bad, current.ToString(), span);
    }

    private void SkipTrivia()
    {
        while (position < source.Length)
        {
            if (blockComment)
            {
                while (position < source.Length && source[position] is not ('\n' or '\r')
                       && !MatchesAt(position, "*/"))
                    position++;
                if (MatchesAt(position, "*/"))
                {
                    position += 2;
                    blockComment = false;
                    continue;
                }
                if (position == source.Length)
                {
                    diagnostics.Error(DiagnosticId.UnexpectedCharacter,
                        TextSpan.FromBounds(commentStart, position), "This block comment needs '*/'.");
                    blockComment = false;
                }
                return;
            }
            if (MatchesAt(position, "/*"))
            {
                commentStart = position;
                position += 2;
                blockComment = true;
                continue;
            }
            var current = source[position];
            if (current is ' ' or '\t')
            {
                position++;
                continue;
            }

            if (MatchesAt(position, "//"))
            {
                while (position < source.Length && source[position] is not ('\n' or '\r'))
                    position++;
                continue;
            }

            break;
        }
        if (blockComment)
        {
            diagnostics.Error(DiagnosticId.UnexpectedCharacter,
                TextSpan.FromBounds(commentStart, position), "This block comment needs '*/'.");
            blockComment = false;
        }
    }

    private Token ReadString(int start)
    {
        position++;
        var builder = new System.Text.StringBuilder();
        while (position < source.Length && source[position] != '"')
        {
            if (source[position] is '\n' or '\r')
                break;
            if (source[position] == '\\' && position + 1 < source.Length)
            {
                builder.Append(Unescape(source[position + 1]));
                position += 2;
                continue;
            }
            builder.Append(source[position]);
            position++;
        }

        var span = TextSpan.FromBounds(start, Math.Min(position + 1, source.Length));
        if (position >= source.Length || source[position] != '"')
        {
            diagnostics.Error(DiagnosticId.UnexpectedCharacter, span, "Unterminated text.");
            return new Token(TokenKind.String, builder.ToString(), span);
        }

        position++;
        return new Token(TokenKind.String, builder.ToString(), TextSpan.FromBounds(start, position));
    }

    private char Unescape(char escape)
    {
        switch (escape)
        {
            case 'n': return '\n';
            case 't': return '\t';
            case '\\': return '\\';
            case '"': return '"';
            default:
                diagnostics.Error(DiagnosticId.UnexpectedCharacter,
                    TextSpan.FromBounds(position, position + 2),
                    $"\"\\{escape}\" is not an escape this language knows.",
                    "Write \\n for a line break, \\t for a tab, \\\\ for a backslash or \\\" for a quote.");
                return escape;
        }
    }

    private Token ReadNumber(int start)
    {
        if (source[position] == '-')
            position++;
        while (position < source.Length && (char.IsAsciiDigit(source[position]) || source[position] == '_'))
            position++;

        var span = TextSpan.FromBounds(start, position);
        var text = source.GetText(span);
        if (!long.TryParse(text.Replace("_", string.Empty), out var value))
        {
            diagnostics.Error(DiagnosticId.NumberOutOfRange, span, $"'{text}' is not a whole number this language can hold.");
            return new Token(TokenKind.Number, text, span);
        }

        return new Token(TokenKind.Number, text, span, value);
    }

    private Token ReadWord(int start)
    {
        while (position < source.Length && IsWordPart(source[position]))
            position++;
        var span = TextSpan.FromBounds(start, position);
        return new Token(TokenKind.Word, source.GetText(span), span);
    }

    private bool MatchesAt(int position, string text)
    {
        if (position + text.Length > source.Length)
            return false;
        for (var i = 0; i < text.Length; i++)
        {
            if (source[position + i] != text[i])
                return false;
        }
        return true;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c is '_' or '.';

    private static bool IsWordPart(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '\'' or '/' or '.';
}
