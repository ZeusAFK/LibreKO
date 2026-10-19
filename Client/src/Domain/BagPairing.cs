namespace LibreKO.Domain;

public sealed class BagPairing<T> where T : class
{
    private T? current;
    private bool openedByCompanion;

    public T? Attach(T companion, bool inventoryWasOpen)
    {
        if (ReferenceEquals(companion, current)) return null;
        var previous = current;
        current = companion;
        if (previous == null) openedByCompanion = !inventoryWasOpen;
        return previous;
    }

    public bool Detach(T companion)
    {
        if (!ReferenceEquals(companion, current)) return false;
        current = null;
        bool close = openedByCompanion;
        openedByCompanion = false;
        return close;
    }

    public void PlayerTouched() => openedByCompanion = false;
}
