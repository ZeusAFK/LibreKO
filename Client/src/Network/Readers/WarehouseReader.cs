namespace LibreKO.Network.Readers;

internal sealed class WarehouseReader : IPacketReader
{
    private readonly WarehouseSession warehouse;

    public WarehouseReader(WarehouseSession warehouse)
    {
        this.warehouse = warehouse;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_WAREHOUSE, HandleWarehouse);
    }

    private void HandleWarehouse(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 0x10:
                warehouse.RaiseWarehouseNpc();
                break;
            case 1:
                {
                    var opened = p.RemainingBytes >= 1 ? (WarehouseOpenResult)p.ReadByte() : WarehouseOpenResult.Opened;
                    if (opened != WarehouseOpenResult.Opened)
                    {
                        warehouse.RaiseWarehouseOpenRefused(opened);
                        break;
                    }
                    int money = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    var slots = new ItemSlot[WarehouseWire.Slots];
                    for (int i = 0; i < WarehouseWire.Slots && p.RemainingBytes >= 17; i++)
                    {
                        int itemId = p.ReadInt();
                        short dur = WireNumbers.ClampToShort(p.ReadUShort());
                        short count = WireNumbers.ClampToShort(p.ReadUShort());
                        byte flag = p.ReadByte();
                        p.Skip(8);
                        slots[i] = itemId == 0 ? default : new ItemSlot { ItemId = itemId, Durability = dur, Count = count, Flag = flag };
                    }
                    warehouse.RaiseWarehouseContents(money, slots);
                    break;
                }
            case 2:
            case 3:
            case 4:
            case 5:
                {
                    byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    warehouse.RaiseWarehouseResult(sub, result);
                    break;
                }
        }
    }
}
