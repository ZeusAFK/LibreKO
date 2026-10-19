namespace LibreKO.Network.Readers;

internal sealed class RouletteReader : IPacketReader
{
    private readonly RouletteSession roulette;

    public RouletteReader(RouletteSession roulette)
    {
        this.roulette = roulette;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_EVENT_BOARD, EventBoardSubOpcode.RouletteOpen, HandleStatus);
        dispatcher.On(GameOpcodes.GS_EVENT_BOARD, EventBoardSubOpcode.RoulettePrizeList, HandlePrizeList);
        dispatcher.On(GameOpcodes.GS_EVENT_BOARD, EventBoardSubOpcode.RouletteSpin, HandleSpin);
    }

    private void HandleStatus(Packet p)
    {
        int coins = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        roulette.RaiseRouletteStatus(coins);
    }

    private void HandlePrizeList(Packet p)
    {
        if (p.RemainingBytes < 12) return;
        p.ReadInt();
        if (p.ReadInt() != 1) return;
        int count = p.ReadInt();
        if (count < 0 || count > RouletteWire.PrizeLogMax) return;

        var entries = new RoulettePrize[count];
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 12) return;
            entries[i] = new RoulettePrize(p.ReadInt(), p.ReadInt(), p.ReadInt());
        }

        roulette.RaiseRoulettePrizeLog(entries);
    }

    private void HandleSpin(Packet p)
    {
        bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
        int prizeItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        int prizeGold = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        roulette.RaiseRouletteSpin(ok, prizeItemId, prizeGold);
    }
}
