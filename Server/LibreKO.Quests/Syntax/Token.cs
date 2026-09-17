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

    private int _position;
    private bool _blockComment;
    private int _commentStart;

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

        if (_position >= source.Length)
            return new Token(TokenKind.EndOfFile, string.Empty, new TextSpan(source.Length, 0));

        var start = _position;
        var current = source[_position];

        if (current is '\n' or '\r')
        {
            if (current == '\r' && _position + 1 < source.Length && source[_position + 1] == '\n')
                _position++;
            _position++;
            return new Token(TokenKind.LineBreak, "\n", TextSpan.FromBounds(start, _position));
        }

        if (current == ',')
        {
            _position++;
            return new Token(TokenKind.Comma, ",", TextSpan.FromBounds(start, _position));
        }

        if (current == ':')
        {
            _position++;
            return new Token(TokenKind.Colon, ":", TextSpan.FromBounds(start, _position));
        }

        if (current == '(')
        {
            _position++;
            return new Token(TokenKind.OpenParen, "(", TextSpan.FromBounds(start, _position));
        }

        if (current == ')')
        {
            _position++;
            return new Token(TokenKind.CloseParen, ")", TextSpan.FromBounds(start, _position));
        }

        if (current == '"')
            return ReadString(start);

        foreach (var op in MultiCharOperators)
        {
            if (!MatchesAt(_position, op))
                continue;
            _position += op.Length;
            return new Token(TokenKind.Operator, op, TextSpan.FromBounds(start, _position));
        }

        if (current is '>' or '<' or '=')
        {
            _position++;
            return new Token(TokenKind.Operator, current.ToString(), TextSpan.FromBounds(start, _position));
        }

        if (char.IsAsciiDigit(current) || (current == '-' && _position + 1 < source.Length && char.IsAsciiDigit(source[_position + 1])))
            return ReadNumber(start);

        if (IsWordStart(current))
            return ReadWord(start);

        _position++;
        var span = TextSpan.FromBounds(start, _position);
        diagnostics.Error(DiagnosticId.UnexpectedCharacter, span, $"Unexpected character '{current}'.");
        return new Token(TokenKind.Bad, current.ToString(), span);
    }

    private void SkipTrivia()
    {
        while (_position < source.Length)
        {
            if (_blockComment)
            {
                while (_position < source.Length && source[_position] is not ('\n' or '\r')
                       && !MatchesAt(_position, "*/"))
                    _position++;
                if (MatchesAt(_position, "*/"))
                {
                    _position += 2;
                    _blockComment = false;
                    continue;
                }
                if (_position == source.Length)
                {
                    diagnostics.Error(DiagnosticId.UnexpectedCharacter,
                        TextSpan.FromBounds(_commentStart, _position), "This block comment needs '*/'.");
                    _blockComment = false;
                }
                return;
            }
            if (MatchesAt(_position, "/*"))
            {
                _commentStart = _position;
                _position += 2;
                _blockComment = true;
                continue;
            }
            var current = source[_position];
            if (current is ' ' or '\t')
            {
                _position++;
                continue;
            }

            if (MatchesAt(_position, "//"))
            {
                while (_position < source.Length && source[_position] is not ('\n' or '\r'))
                    _position++;
                continue;
            }

            break;
        }
        if (_blockComment)
        {
            diagnostics.Error(DiagnosticId.UnexpectedCharacter,
                TextSpan.FromBounds(_commentStart, _position), "This block comment needs '*/'.");
            _blockComment = false;
        }
    }

    private Token ReadString(int start)
    {
        _position++;
        var builder = new System.Text.StringBuilder();
        while (_position < source.Length && source[_position] != '"')
        {
            if (source[_position] is '\n' or '\r')
                break;
            if (source[_position] == '\\' && _position + 1 < source.Length)
            {
                builder.Append(Unescape(source[_position + 1]));
                _position += 2;
                continue;
            }
            builder.Append(source[_position]);
            _position++;
        }

        var span = TextSpan.FromBounds(start, Math.Min(_position + 1, source.Length));
        if (_position >= source.Length || source[_position] != '"')
        {
            diagnostics.Error(DiagnosticId.UnexpectedCharacter, span, "Unterminated text.");
            return new Token(TokenKind.String, builder.ToString(), span);
        }

        _position++;
        return new Token(TokenKind.String, builder.ToString(), TextSpan.FromBounds(start, _position));
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
                    TextSpan.FromBounds(_position, _position + 2),
                    $"\"\\{escape}\" is not an escape this language knows.",
                    "Write \\n for a line break, \\t for a tab, \\\\ for a backslash or \\\" for a quote.");
                return escape;
        }
    }

    private Token ReadNumber(int start)
    {
        if (source[_position] == '-')
            _position++;
        while (_position < source.Length && (char.IsAsciiDigit(source[_position]) || source[_position] == '_'))
            _position++;

        var span = TextSpan.FromBounds(start, _position);
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
        while (_position < source.Length && IsWordPart(source[_position]))
            _position++;
        var span = TextSpan.FromBounds(start, _position);
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
