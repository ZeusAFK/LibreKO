namespace LibreKO.Session;

public sealed class CollectionRaceSession
{
    public EventWindowMemory Window { get; } = new();

    public event Action<CollectionRaceState>? CollectionRaceStateEvent;

    internal void RaiseCollectionRaceState(CollectionRaceState collectionRaceState) => CollectionRaceStateEvent?.Invoke(collectionRaceState);

    public event Action<int, int[]>? CollectionRaceProgressEvent;

    internal void RaiseCollectionRaceProgress(int arg1, int[] arg2) => CollectionRaceProgressEvent?.Invoke(arg1, arg2);

    public event Action<string>? CollectionRaceCompletedEvent;

    internal void RaiseCollectionRaceCompleted(string value) => CollectionRaceCompletedEvent?.Invoke(value);

    public event Action? CollectionRaceCloseEvent;

    internal void RaiseCollectionRaceClose() => CollectionRaceCloseEvent?.Invoke();

    public event Action<int, int, int>? CollectionRaceWinnersEvent;

    internal void RaiseCollectionRaceWinners(int arg1, int arg2, int arg3) => CollectionRaceWinnersEvent?.Invoke(arg1, arg2, arg3);
}
