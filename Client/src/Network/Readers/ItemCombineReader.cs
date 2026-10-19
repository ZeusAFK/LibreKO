namespace LibreKO.Network.Readers;

internal sealed class ItemCombineReader : IPacketReader
{
    private readonly ItemCombineSession itemCombine;

    public ItemCombineReader(ItemCombineSession itemCombine)
    {
        this.itemCombine = itemCombine;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_NPC_EVENT, HandleNpcEvent);
        dispatcher.On(GameOpcodes.GS_ITEM_UPGRADE, ItemUpgradeSubOpcode.Combine, HandleItemCombineReply);
    }

    private void HandleItemCombineReply(Packet p)
    {
        byte result = p.ReadByte();
        short row = 0;
        byte slot = 0;
        if (ItemCombine.IsDone(result))
        {
            row = p.ReadShort();
            if (result == ItemCombine.Succeeded)
            {
                p.ReadInt();
                slot = p.ReadByte();
            }
        }
        itemCombine.RaiseItemCombineReply(new CombineReply(result, row, slot));
    }

    private void HandleNpcEvent(Packet p)
    {
        if (p.RemainingBytes < 1 || (NpcEventSubOpcode)p.ReadByte() != NpcEventSubOpcode.CombineEffect) return;
        bool success = p.ReadByte() == ItemCombine.Succeeded;
        int npc = p.ReadShort();
        int row = p.ReadUShort();
        itemCombine.RaiseCombineEffect(new CombineEffect(success, npc, row));
    }
}
