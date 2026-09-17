using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int>? RingUpgradeStatusEvent;
    public event Action<int, int>? RingUpgradeResultEvent;

    public const byte RingUpgradeResultSuccess = 1;
    public const byte RingUpgradeResultFailed = 0;
    public const byte RingUpgradeResultInvalid = 2;

    private void HandleRingUpgrade(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            int ratePct = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            RingUpgradeStatusEvent?.Invoke(ratePct);
        }
        else if (sub == 2)
        {
            int result = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            int newPlus = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
            RingUpgradeResultEvent?.Invoke(result, newPlus);
        }
    }

    public void SendRingUpgradeStatus(int invSlot)
    {
        var p = new Packet(GameOpcodes.GS_RING_UPGRADE);
        p.WriteByte(1);
        p.WriteByte((byte)invSlot);
        _conn.Send(p);
    }

    public void SendRingUpgrade(int invSlot)
    {
        var p = new Packet(GameOpcodes.GS_RING_UPGRADE);
        p.WriteByte(2);
        p.WriteByte((byte)invSlot);
        _conn.Send(p);
    }
}
