namespace LibreKO.Network;

public readonly struct NearbyPlayer
{
    public readonly string Name;
    public readonly int Nation;
    public readonly bool Online;
    public readonly float MapX, MapZ;
    public readonly int ClanId;
    public readonly int State;

    public NearbyPlayer(string name, int nation, bool online, float mapX, float mapZ, int clanId, int state = 0)
    {
        Name = name; Nation = nation; Online = online; MapX = mapX; MapZ = mapZ; ClanId = clanId; State = state;
    }
}
