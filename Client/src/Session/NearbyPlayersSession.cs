namespace LibreKO.Session;

public sealed class NearbyPlayersSession
{
    public PlayerInspectSubOpcode Requested { get; internal set; } = PlayerInspectSubOpcode.Nearby;

    public event Action<List<NearbyPlayer>>? NearbyPlayersEvent;

    public IReadOnlyList<NearbyPlayer> Players { get; private set; } = Array.Empty<NearbyPlayer>();

    internal void RaiseNearbyPlayers(List<NearbyPlayer> list)
    {
        Players = list;
        NearbyPlayersEvent?.Invoke(list);
    }
}
