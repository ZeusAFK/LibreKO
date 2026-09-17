using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte RebirthSubActivate = 1;
    public const byte RebirthSubResult   = 2;
    public const byte RebirthSubProgress = 3;
    public const byte RebirthSubComplete = 4;

    public const int RebirthGoldCost    = 100_000_000;
    public const int RebirthLoyaltyCost = 10_000;

    public event Action? RebirthActivateEvent;
    public event Action<int>? RebirthResultEvent;
    public event Action<int, int, int>? RebirthProgressEvent;
    public event Action<int>? RebirthCompleteEvent;

    private void HandleRebirth(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int sub = p.ReadByte();
        switch (sub)
        {
            case RebirthSubActivate:
                RebirthActivateEvent?.Invoke();
                break;
            case RebirthSubResult:
                RebirthResultEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;
            case RebirthSubProgress:
                if (p.RemainingBytes >= 12)
                {
                    int levelOffset = p.ReadInt();
                    int current = p.ReadInt();
                    int max = p.ReadInt();
                    RebirthProgressEvent?.Invoke(levelOffset, current, max);
                }
                break;
            case RebirthSubComplete:
                RebirthCompleteEvent?.Invoke(p.RemainingBytes >= 4 ? p.ReadInt() : 0);
                break;
        }
    }

    public void SendRebirthRequest()
    {
        var p = new Packet(GameOpcodes.GS_REBIRTH);
        p.WriteByte(RebirthSubActivate);
        _conn.Send(p);
    }
}
