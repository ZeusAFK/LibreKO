using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<DailyQuestEntry>>? DailyQuestListEvent;
    public event Action<int, bool>? DailyQuestClaimEvent;

    private void HandleDailyQuest(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<DailyQuestEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 6; i++)
            {
                var e = new DailyQuestEntry { Id = p.ReadInt(), Available = p.ReadByte() != 0, Claimed = p.ReadByte() != 0, Title = p.ReadSByteString() };
                list.Add(e);
            }
            DailyQuestListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            DailyQuestClaimEvent?.Invoke(id, ok);
        }
    }

    public void SendDailyQuestList() { var p = new Packet(GameOpcodes.GS_DAILY_QUEST); p.WriteByte(1); _conn.Send(p); }
    public void SendDailyQuestClaim(int id) { var p = new Packet(GameOpcodes.GS_DAILY_QUEST); p.WriteByte(2); p.WriteInt(id); _conn.Send(p); }
}

public struct DailyQuestEntry { public int Id; public bool Available; public bool Claimed; public string Title; }
