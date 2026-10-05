using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class NoticeQueue
{
    public const int Capacity = 8;
    private readonly Queue<string> _waiting = new();

    public string? Current { get; private set; }

    public bool Enqueue(string text)
    {
        if (text.Length == 0 || text == Current || _waiting.Contains(text)) return false;
        if (Current == null)
        {
            Current = text;
            return true;
        }
        if (_waiting.Count < Capacity) _waiting.Enqueue(text);
        return false;
    }

    public string? Advance()
    {
        Current = _waiting.Count > 0 ? _waiting.Dequeue() : null;
        return Current;
    }
}
