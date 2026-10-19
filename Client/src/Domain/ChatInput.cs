using System.Collections.Generic;
using System.Text;

namespace LibreKO.Domain;

public sealed class ChatInputHistory
{
    public const int Capacity = 40;
    private const int NotBrowsing = -1;

    private readonly List<string> lines = new();
    private int cursor = NotBrowsing;
    private string draft = "";

    public void Push(string line)
    {
        cursor = NotBrowsing;
        if (line.Length == 0) return;
        if (lines.Count > 0 && lines[^1] == line) return;
        lines.Add(line);
        if (lines.Count > Capacity) lines.RemoveAt(0);
    }

    public string? Older(string current)
    {
        if (lines.Count == 0) return null;
        if (cursor == NotBrowsing)
        {
            draft = current;
            cursor = lines.Count - 1;
        }
        else if (cursor > 0)
        {
            cursor--;
        }
        return lines[cursor];
    }

    public string? Newer()
    {
        if (cursor == NotBrowsing) return null;
        if (cursor < lines.Count - 1) return lines[++cursor];
        cursor = NotBrowsing;
        return draft;
    }

    public void StopBrowsing() => cursor = NotBrowsing;

    public void Reset()
    {
        lines.Clear();
        cursor = NotBrowsing;
        draft = "";
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
