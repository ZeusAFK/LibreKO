namespace LibreKO.Network.Readers;

internal sealed class ExchangeReader : IPacketReader
{
    private readonly ExchangeSession exchange;
    private readonly PetSession pet;
    private readonly ProgressSession progress;

    public ExchangeReader(ExchangeSession exchange, PetSession pet, ProgressSession progress)
    {
        this.exchange = exchange;
        this.pet = pet;
        this.progress = progress;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_EXCHANGE, HandleExchange);
    }

    private void HandleExchange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ExchangeSubOpcode)p.ReadByte();
        switch (sub)
        {
            case ExchangeSubOpcode.Request:
                exchange.RaiseExchangeRequest(p.ReadInt());
                break;
            case ExchangeSubOpcode.Agree:
                exchange.RaiseExchangeAgree(p.ReadByte() == 1);
                break;
            case ExchangeSubOpcode.Add:
                exchange.RaiseExchangeAddResult(p.ReadByte() == 1);
                break;
            case ExchangeSubOpcode.OtherAdd:
                {
                    int itemId = p.ReadInt();
                    int count = p.ReadInt();
                    short dura = p.ReadShort();
                    int uniqueId = PetWire.ReadUniqueId(p, out var petInfo);
                    pet.Remember(petInfo);
                    exchange.RaiseExchangeOtherAdd(itemId, count, dura, uniqueId);
                    break;
                }
            case ExchangeSubOpcode.OtherDecide:
                exchange.RaiseExchangeOtherDecide();
                break;
            case ExchangeSubOpcode.Done:
                {
                    byte ok = p.ReadByte();
                    var received = new List<(byte, ItemSlot)>();
                    int money = 0;
                    if (ok == 1)
                    {
                        money = p.ReadInt();
                        int n = p.ReadUShort();
                        for (int i = 0; i < n && p.RemainingBytes >= 14; i++)
                        {
                            byte dstPos = p.ReadByte();
                            int itemId = p.ReadInt();
                            int cnt = p.ReadUShort();
                            short dura = p.ReadShort();
                            p.ReadByte();
                            int uniqueId = PetWire.ReadUniqueId(p, out var petInfo);
                            pet.Remember(petInfo);
                            received.Add((dstPos, new ItemSlot { ItemId = itemId, Count = (short)cnt, Durability = dura, UniqueId = uniqueId }));
                        }
                        progress.ApplyGold(money);
                    }
                    exchange.RaiseExchangeDone(ok == 1, money, received);
                    break;
                }
            case ExchangeSubOpcode.Cancel:
                exchange.RaiseExchangeCancel();
                break;
            case ExchangeSubOpcode.Duplicate:
                exchange.RaiseExchangeDuplicate();
                break;
        }
    }
}
