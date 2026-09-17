using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<FishingHallEntry>>? FishingHallListEvent;

    private void HandleFishingHall(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<FishingHallEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
            {
                int rank = p.ReadUShort();
                string name = p.ReadSByteString();
                int score = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                list.Add(new FishingHallEntry { Rank = rank, Name = name, Score = score });
            }
            FishingHallListEvent?.Invoke(list);
        }
    }

    public void SendFishingHallList()
    {
        var p = new Packet(GameOpcodes.GS_FISHING_HALL);
        p.WriteByte(1);
        _conn.Send(p);
    }
}

public struct FishingHallEntry
{
    public int Rank;
    public string Name;
    public int Score;
}
