using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const byte RankTypePkZone = 1;
    public const byte RankTypeBorderWar = 2;
    public const byte RankTypeChaosDungeon = 3;

    public event Action<IReadOnlyList<RankEntry>, IReadOnlyList<RankEntry>, int, int>? RankEvent;

    private void HandleRank(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int rankType = p.ReadByte();
        if (rankType != RankTypePkZone)
        {
            RankEvent?.Invoke(Array.Empty<RankEntry>(), Array.Empty<RankEntry>(), 0, 0);
            return;
        }

        var karus = ReadRankNation(p);
        var elmorad = ReadRankNation(p);

        int myRank = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        int myLoyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        if (p.RemainingBytes >= 2) p.ReadUShort();

        RankEvent?.Invoke(karus, elmorad, myRank, myLoyalty);
    }

    private static List<RankEntry> ReadRankNation(Packet p)
    {
        var list = new List<RankEntry>();
        if (p.RemainingBytes < 2) return list;
        int count = p.ReadUShort();
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 1) break;
            string name = p.ReadSByteString();
            if (p.RemainingBytes < 5) break;
            int nation = p.ReadByte();
            int knightsId = p.ReadUShort();
            p.ReadUShort();
            if (p.RemainingBytes < 1) break;
            string clan = p.ReadSByteString();
            if (p.RemainingBytes < 4) break;
            int loyalty = p.ReadInt();
            if (p.RemainingBytes >= 2) p.ReadUShort();
            if (p.RemainingBytes >= 1) p.ReadByte();
            list.Add(new RankEntry
            {
                Name = name,
                Nation = nation,
                KnightsId = knightsId,
                ClanName = clan,
                Loyalty = loyalty,
                Place = i + 1,
            });
        }
        return list;
    }

    public void SendRankRequest(byte rankType)
    {
        var p = new Packet(GameOpcodes.GS_RANK);
        p.WriteByte(rankType);
        _conn.Send(p);
    }
}
