using System.Collections.Generic;
using System.Text;

namespace LibreKO.Domain;

public sealed class ChatInputHistory
{
    public const int Capacity = 40;
    private const int NotBrowsing = -1;

    private readonly List<string> _lines = new();
    private int _cursor = NotBrowsing;
    private string _draft = "";

    public void Push(string line)
    {
        _cursor = NotBrowsing;
        if (line.Length == 0) return;
        if (_lines.Count > 0 && _lines[^1] == line) return;
        _lines.Add(line);
        if (_lines.Count > Capacity) _lines.RemoveAt(0);
    }

    public string? Older(string current)
    {
        if (_lines.Count == 0) return null;
        if (_cursor == NotBrowsing)
        {
            _draft = current;
            _cursor = _lines.Count - 1;
        }
        else if (_cursor > 0)
        {
            _cursor--;
        }
        return _lines[_cursor];
    }

    public string? Newer()
    {
        if (_cursor == NotBrowsing) return null;
        if (_cursor < _lines.Count - 1) return _lines[++_cursor];
        _cursor = NotBrowsing;
        return _draft;
    }

    public void StopBrowsing() => _cursor = NotBrowsing;

    public void Reset()
    {
        _lines.Clear();
        _cursor = NotBrowsing;
        _draft = "";
    }
}

public static class ChatInputText
{
    public const int MaxLength = 128;
    public const int CounterLead = 24;

    public static string? Counter(int length, int limit) =>
        length >= limit - CounterLead ? $"{length}/{limit}" : null;

    public static string Paste(string clipboard)
    {
        var text = new StringBuilder(clipboard.Length);
        bool pendingSpace = false;
        foreach (char c in clipboard)
        {
            if (c is '\r' or '\n' or '\t')
            {
                pendingSpace = text.Length > 0;
                continue;
            }
            if (char.IsControl(c)) continue;
            if (pendingSpace && c != ' ') text.Append(' ');
            pendingSpace = false;
            text.Append(c);
        }
        return text.ToString();
    }
}
