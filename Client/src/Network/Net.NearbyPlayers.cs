using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public readonly struct NearbyPlayer
    {
        public readonly string Name;
        public readonly int Nation;
        public readonly bool Online;
        public readonly float KoX, KoZ;
        public readonly int ClanId;

        public NearbyPlayer(string name, int nation, bool online, float koX, float koZ, int clanId)
        {
            Name = name; Nation = nation; Online = online; KoX = koX; KoZ = koZ; ClanId = clanId;
        }
    }

    public const byte NearbyPlayersSignSub = 1;
    public const byte NearbyPlayersRefreshSub = 3;

    public event Action<List<NearbyPlayer>>? NearbyPlayersEvent;

    private void HandleNearbyPlayers(Packet p, byte sub)
    {
        if (sub is not (NearbyPlayersSignSub or NearbyPlayersRefreshSub)) return;
        if (p.RemainingBytes < 6) return;
        p.ReadByte();
        p.ReadShort();
        p.ReadByte();
        int count = p.ReadUShort();

        var list = new List<NearbyPlayer>(count);
        for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
        {
            string name = p.ReadSByteString();
            if (p.RemainingBytes < 15) break;
            int nation = p.ReadByte();
            bool online = p.ReadShort() != 0;
            float koX = p.ReadShort() / 10f;
            float koZ = p.ReadShort() / 10f;
            int clanId = p.ReadInt();
            p.ReadShort();
            p.ReadShort();
            list.Add(new NearbyPlayer(name, nation, online, koX, koZ, clanId));
        }

        NearbyPlayersEvent?.Invoke(list);
    }

    public void SendNearbyPlayersRequest(bool first = false)
    {
        var p = new Packet(GameOpcodes.GS_USER_INFO);
        p.WriteByte(first ? NearbyPlayersSignSub : NearbyPlayersRefreshSub);
        _conn.Send(p);
    }
}
