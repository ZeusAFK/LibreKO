namespace LibreKO.Network.Readers;

internal sealed class ClanWarehouseReader : IPacketReader
{
    private readonly ClanWarehouseSession clanWarehouse;

    public ClanWarehouseReader(ClanWarehouseSession clanWarehouse)
    {
        this.clanWarehouse = clanWarehouse;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CLAN_WAREHOUSE, HandleClanWarehouse);
    }

    private void HandleClanWarehouse(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (WarehouseSubOpcode)p.ReadByte();
        switch (sub)
        {
            case WarehouseSubOpcode.Open:
                {
                    var result = p.RemainingBytes >= 1 ? (ClanWarehouseResult)p.ReadByte() : ClanWarehouseResult.Failed;
                    if (result != ClanWarehouseResult.Succeeded) { clanWarehouse.RaiseClanWhResult(sub, result); break; }
                    int money = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    var slots = new ItemSlot[ClanWarehouseWire.NetSlots];
                    for (int i = 0; i < ClanWarehouseWire.NetSlots && p.RemainingBytes >= 17; i++)
                    {
                        int itemId = p.ReadInt();
                        short dur = WireNumbers.ClampToShort(p.ReadUShort());
                        short count = WireNumbers.ClampToShort(p.ReadUShort());
                        byte flag = p.ReadByte();
                        p.Skip(8);
                        slots[i] = itemId == 0 ? default : new ItemSlot { ItemId = itemId, Durability = dur, Count = count, Flag = flag };
                    }
                    clanWarehouse.RaiseClanWhContents(money, slots);
                    break;
                }
            case WarehouseSubOpcode.Input:
            case WarehouseSubOpcode.Output:
            case WarehouseSubOpcode.Move:
            case WarehouseSubOpcode.InventoryMove:
                clanWarehouse.RaiseClanWhResult(sub, p.RemainingBytes >= 1 ? (ClanWarehouseResult)p.ReadByte() : ClanWarehouseResult.Failed);
                break;
        }
    }
}
