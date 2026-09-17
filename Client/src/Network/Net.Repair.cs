using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int>? RepairNpcEvent;

    public event Action<bool, int>? ItemRepairResultEvent;

    private void HandleRepairNpc(Packet p)
    {
        int group = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        RepairNpcEvent?.Invoke(group);
    }

    private void HandleItemRepair(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        bool ok = p.ReadByte() == 1;
        int money = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        if (money >= 0) GoldChangeEvent?.Invoke(money);
        ItemRepairResultEvent?.Invoke(ok, money);
    }

    public void SendRepair(byte positionType, byte slot, int npcId, int itemId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_REPAIR);
        p.WriteByte(positionType);
        p.WriteByte(slot);
        p.WriteInt(npcId);
        p.WriteInt(itemId);
        _conn.Send(p);
    }
}
