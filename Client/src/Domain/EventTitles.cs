using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class EventTitles
{
    private readonly List<EventTitle> entries = new();

    public IReadOnlyList<EventTitle> Entries => entries;
    public bool Expanded { get; private set; }

    public void Expand() => Expanded = true;

    public void Collapse() => Expanded = false;

    public void ToggleExpanded() => Expanded = !Expanded;

    public int ShownCount => entries.Count == 0 ? 0 : Expanded ? entries.Count : 1;
    public bool HasToggle => entries.Count > 1;

    public void Show(string title, string message)
    {
        int at = entries.FindIndex(entry => entry.Title == title);
        if (at >= 0) entries[at] = new EventTitle(title, message);
        else entries.Add(new EventTitle(title, message));
    }

    public bool Remove(string title) => entries.RemoveAll(entry => entry.Title == title) > 0;

    public void Clear()
    {
        entries.Clear();
        Expanded = false;
    }
}

public readonly record struct EventTitle(string Title, string Message);
