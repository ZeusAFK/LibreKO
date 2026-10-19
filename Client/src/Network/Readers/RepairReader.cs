namespace LibreKO.Network.Readers;

internal sealed class RepairReader : IPacketReader
{
    private readonly ProgressSession progress;
    private readonly RepairSession repair;

    public RepairReader(ProgressSession progress, RepairSession repair)
    {
        this.progress = progress;
        this.repair = repair;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_REPAIR_NPC, HandleRepairNpc);
        dispatcher.On(GameOpcodes.GS_ITEM_REPAIR, HandleItemRepair);
    }

    private void HandleRepairNpc(Packet p)
    {
        int group = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        repair.RaiseRepairNpc(group);
    }

    private void HandleItemRepair(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        bool ok = p.ReadByte() == 1;
        int money = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        if (money >= 0) progress.ApplyGold(money);
        repair.RaiseItemRepairResult(ok, money);
    }
}
