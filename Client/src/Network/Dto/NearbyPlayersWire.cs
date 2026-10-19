namespace LibreKO.Network;

public static class NearbyPlayersWire
{

    public static bool IsListHeader(PlayerInspectSubOpcode sub, PlayerInspectSubOpcode requested, int count) => count == 0 && sub != requested;
}
