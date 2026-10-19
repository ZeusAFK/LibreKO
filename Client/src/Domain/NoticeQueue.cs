using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class NoticeQueue
{
    public const int Capacity = 8;
    private readonly Queue<string> waiting = new();

    public string? Current { get; private set; }

    public bool Enqueue(string text)
    {
        if (text.Length == 0 || text == Current || waiting.Contains(text)) return false;
        if (Current == null)
        {
            Current = text;
            return true;
        }
        if (waiting.Count < Capacity) waiting.Enqueue(text);
        return false;
    }

    public string? Advance()
    {
        Current = waiting.Count > 0 ? waiting.Dequeue() : null;
        return Current;
    }
}
