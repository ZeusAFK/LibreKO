namespace LibreKO.Session;

public sealed class FishingHallSession
{
    public event Action<List<FishingHallEntry>>? FishingHallListEvent;

    internal void RaiseFishingHallList(List<FishingHallEntry> list) => FishingHallListEvent?.Invoke(list);
}
