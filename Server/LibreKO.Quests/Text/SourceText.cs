namespace LibreKO.Quests.Text;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end) => new(start, end - start);

    public TextSpan Union(TextSpan other) =>
        FromBounds(Math.Min(Start, other.Start), Math.Max(End, other.End));
}

public readonly record struct LinePosition(int Line, int Character)
{
    public override string ToString() => $"{Line + 1}:{Character + 1}";
}

public sealed class SourceText
{
    private readonly int[] _lineStarts;

    public string FileName { get; }
    public string Content { get; }
    public int LineCount => _lineStarts.Length;

    public SourceText(string content, string fileName)
    {
        Content = content;
        FileName = fileName;
        _lineStarts = ComputeLineStarts(content);
    }

    public char this[int index] => Content[index];
    public int Length => Content.Length;

    public LinePosition GetLinePosition(int position)
    {
        var line = GetLineIndex(position);
        return new LinePosition(line, position - _lineStarts[line]);
    }

    public int GetPosition(LinePosition position)
    {
        if (position.Line < 0)
            return 0;
        if (position.Line >= _lineStarts.Length)
            return Content.Length;
        return Math.Min(_lineStarts[position.Line] + position.Character, Content.Length);
    }

    public int GetLineIndex(int position)
    {
        var low = 0;
        var high = _lineStarts.Length - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (_lineStarts[mid] == position)
                return mid;
            if (_lineStarts[mid] < position)
                low = mid + 1;
            else
                high = mid - 1;
        }
        return Math.Max(0, low - 1);
    }

    public string GetLineText(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lineStarts.Length)
            return string.Empty;
        var start = _lineStarts[lineIndex];
        var end = lineIndex + 1 < _lineStarts.Length ? _lineStarts[lineIndex + 1] : Content.Length;
        return Content[start..end].TrimEnd('\r', '\n');
    }

    public string GetText(TextSpan span) =>
        Content.Substring(span.Start, Math.Min(span.Length, Content.Length - span.Start));

    private static int[] ComputeLineStarts(string content)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '\n')
                continue;
            starts.Add(i + 1);
        }
        return [.. starts];
    }
}
