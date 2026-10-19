namespace LibreKO.Network.Readers;

internal sealed class GuardPetReader : IPacketReader
{
    private readonly GuardPetSession guardPet;

    public GuardPetReader(GuardPetSession guardPet)
    {
        this.guardPet = guardPet;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_GUARD_PET, HandleGuardPet);
    }

    private void HandleGuardPet(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (GuardPetSubOpcode)p.ReadByte();
        if (sub != GuardPetSubOpcode.Status) return;

        if (p.RemainingBytes < 1) return;
        bool active = p.ReadByte() != 0;
        int hp = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        int maxHp = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        guardPet.RaiseGuardPetStatus(new GuardPetStatus { Active = active, Hp = hp, MaxHp = maxHp });
    }
}
