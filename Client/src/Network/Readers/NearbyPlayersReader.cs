namespace LibreKO.Network.Readers;

internal sealed class NearbyPlayersReader : IPacketReader
{
    private readonly NearbyPlayersSession nearbyPlayers;

    public NearbyPlayersReader(NearbyPlayersSession nearbyPlayers)
    {
        this.nearbyPlayers = nearbyPlayers;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<PlayerInspectSubOpcode>(GameOpcodes.GS_USER_INFO, HandleNearbyPlayers);
    }

    private void HandleNearbyPlayers(Packet p, PlayerInspectSubOpcode sub)
    {
        if (sub is not (PlayerInspectSubOpcode.Nearby or PlayerInspectSubOpcode.NearbyAll)) return;
        if (p.RemainingBytes < 6) return;
        p.ReadByte();
        p.ReadShort();
        p.ReadByte();
        int count = p.ReadUShort();
        if (NearbyPlayersWire.IsListHeader(sub, nearbyPlayers.Requested, count)) return;

        var list = new List<NearbyPlayer>(count);
        for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
        {
            string name = p.ReadSByteString();
            if (p.RemainingBytes < 15) break;
            int nation = p.ReadByte();
            byte signal = p.ReadByte();
            byte kind = p.ReadByte();
            bool online = (signal | kind) != 0;
            float mapX = p.ReadShort() / Coord.TenthsPerUnit;
            float mapZ = p.ReadShort() / Coord.TenthsPerUnit;
            int clanId = p.ReadInt();
            p.ReadShort();
            p.ReadShort();
            list.Add(new NearbyPlayer(name, nation, online, mapX, mapZ, clanId, Domain.UserInfoCard.NearbyState(signal, kind)));
        }

        nearbyPlayers.RaiseNearbyPlayers(list);
    }
}
