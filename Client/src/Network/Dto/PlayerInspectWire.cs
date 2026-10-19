namespace LibreKO.Network;

public enum PlayerInspectSubOpcode : byte
{
    Nearby = 1,
    Detail = 2,
    NearbyAll = 3,
    Equipment = 6,
}

public enum PlayerInspectResult : byte
{
    Accepted = 1,
    NoUser = 2,
    NoUserAgain = 3,
}
