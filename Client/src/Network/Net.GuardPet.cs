using System;

namespace LibreKO.Network;

public partial class Net
{
    private const byte GuardPetSubStatus = 1;

    public event Action<GuardPetStatus>? GuardPetStatusEvent;

    private void HandleGuardPet(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub != GuardPetSubStatus) return;

        if (p.RemainingBytes < 1) return;
        bool active = p.ReadByte() != 0;
        int hp    = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        int maxHp = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        GuardPetStatusEvent?.Invoke(new GuardPetStatus { Active = active, Hp = hp, MaxHp = maxHp });
    }

    public void SendGuardPetStatus()
    {
        var p = new Packet(GameOpcodes.GS_GUARD_PET);
        p.WriteByte(GuardPetSubStatus);
        _conn.Send(p);
    }
}

public struct GuardPetStatus
{
    public bool Active;
    public int Hp;
    public int MaxHp;
}
