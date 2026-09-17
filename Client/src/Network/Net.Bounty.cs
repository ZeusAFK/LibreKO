using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<BountyEntry>>? BountyListEvent;
    public event Action<bool>? BountyPostEvent;
    public event Action<int, bool>? BountyClaimEvent;

    private void HandleBounty(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<BountyEntry>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
            {
                int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                string target = p.ReadSByteString();
                string poster = p.ReadSByteString();
                int reward = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                list.Add(new BountyEntry { Id = id, Target = target, Poster = poster, Reward = reward });
            }
            BountyListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            BountyPostEvent?.Invoke(ok);
        }
        else if (sub == 3)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int bountyId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            BountyClaimEvent?.Invoke(bountyId, ok);
        }
    }

    public void SendBountyList()
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendBountyPost(string target, int reward)
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte(2);
        p.WriteSByteString(target ?? "");
        p.WriteInt(reward);
        _conn.Send(p);
    }

    public void SendBountyClaim(int bountyId)
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte(3);
        p.WriteInt(bountyId);
        _conn.Send(p);
    }
}

public struct BountyEntry
{
    public int Id;
    public string Target;
    public string Poster;
    public int Reward;
}
