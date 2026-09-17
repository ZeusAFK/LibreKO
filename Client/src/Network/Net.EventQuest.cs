using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<EventQuestEntry>>? EventQuestListEvent;
    public event Action<int, bool>? EventQuestAcceptEvent;
    public event Action<int, bool>? EventQuestClaimEvent;

    private void HandleEventQuest(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<EventQuestEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            {
                int id = p.ReadInt();
                string title = p.ReadSByteString();
                bool accepted = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
                bool claimable = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
                list.Add(new EventQuestEntry { Id = id, Title = title, Accepted = accepted, Claimable = claimable });
            }
            EventQuestListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int questId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            EventQuestAcceptEvent?.Invoke(questId, ok);
        }
        else if (sub == 3)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int questId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            EventQuestClaimEvent?.Invoke(questId, ok);
        }
    }

    public void SendEventQuestList()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_QUEST);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendEventQuestAccept(int questId)
    {
        var p = new Packet(GameOpcodes.GS_EVENT_QUEST);
        p.WriteByte(2);
        p.WriteInt(questId);
        _conn.Send(p);
    }

    public void SendEventQuestClaim(int questId)
    {
        var p = new Packet(GameOpcodes.GS_EVENT_QUEST);
        p.WriteByte(3);
        p.WriteInt(questId);
        _conn.Send(p);
    }
}

public struct EventQuestEntry
{
    public int Id;
    public string Title;
    public bool Accepted;
    public bool Claimable;
}
