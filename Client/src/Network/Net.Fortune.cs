using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<bool>? FortuneStatusEvent;
    public event Action<bool, int, int>? FortuneDrawEvent;

    private void HandleFortune(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            bool canDraw = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            FortuneStatusEvent?.Invoke(canDraw);
        }
        else if (sub == 2)
        {
            bool drew = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int rewardItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            int rewardGold = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            FortuneDrawEvent?.Invoke(drew, rewardItemId, rewardGold);
        }
    }

    public void SendFortuneStatus()
    {
        var p = new Packet(GameOpcodes.GS_FORTUNE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendFortuneDraw()
    {
        var p = new Packet(GameOpcodes.GS_FORTUNE);
        p.WriteByte(2);
        _conn.Send(p);
    }
}
