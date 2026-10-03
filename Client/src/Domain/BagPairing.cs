namespace LibreKO.Domain;

public sealed class BagPairing<T> where T : class
{
    private T? _current;
    private bool _openedByCompanion;

    public T? Attach(T companion, bool inventoryWasOpen)
    {
        if (ReferenceEquals(companion, _current)) return null;
        var previous = _current;
        _current = companion;
        if (previous == null) _openedByCompanion = !inventoryWasOpen;
        return previous;
    }

    public bool Detach(T companion)
    {
        if (!ReferenceEquals(companion, _current)) return false;
        _current = null;
        bool close = _openedByCompanion;
        _openedByCompanion = false;
        return close;
    }

    public void PlayerTouched() => _openedByCompanion = false;
}
