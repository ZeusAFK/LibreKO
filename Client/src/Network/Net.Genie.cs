using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<string, bool>? GenieStatusEvent;
    public event Action<bool, int>? GenieClaimEvent;

    private void HandleGenie(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            string tip = p.ReadSByteString();
            bool rewardAvail = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            GenieStatusEvent?.Invoke(tip, rewardAvail);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int rewardGold = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            GenieClaimEvent?.Invoke(ok, rewardGold);
        }
    }

    public void SendGenieStatus()
    {
        var p = new Packet(GameOpcodes.GS_GENIE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendGenieClaim()
    {
        var p = new Packet(GameOpcodes.GS_GENIE);
        p.WriteByte(2);
        _conn.Send(p);
    }
}
