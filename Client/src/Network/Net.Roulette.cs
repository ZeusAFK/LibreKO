using System;

namespace LibreKO.Network;

public readonly record struct RoulettePrize(int ItemId, int Quantity, int UnixTime);

public partial class Net
{
    public const int RoulettePrizeLogMax = 20;

    public const byte EventBoardRouletteOpen = 6;
    public const byte EventBoardRouletteSpin = 8;
    public const byte EventBoardRoulettePrizeList = 9;

    public event Action<int>? RouletteStatusEvent;
    public event Action<bool, int, int>? RouletteSpinEvent;
    public event Action<RoulettePrize[]>? RoulettePrizeLogEvent;

    private void HandleRoulette(Packet p, byte sub)
    {
        if (sub == EventBoardRouletteOpen)
        {
            int coins = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            RouletteStatusEvent?.Invoke(coins);
        }
        else if (sub == EventBoardRoulettePrizeList)
        {
            if (p.RemainingBytes < 12) return;
            p.ReadInt();
            if (p.ReadInt() != 1) return;
            int count = p.ReadInt();
            if (count < 0 || count > RoulettePrizeLogMax) return;

            var entries = new RoulettePrize[count];
            for (int i = 0; i < count; i++)
            {
                if (p.RemainingBytes < 12) return;
                entries[i] = new RoulettePrize(p.ReadInt(), p.ReadInt(), p.ReadInt());
            }

            RoulettePrizeLogEvent?.Invoke(entries);
        }
        else if (sub == EventBoardRouletteSpin)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int prizeItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            int prizeGold = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            RouletteSpinEvent?.Invoke(ok, prizeItemId, prizeGold);
        }
    }

    public void SendRouletteStatus()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte(EventBoardRouletteOpen);
        _conn.Send(p);
    }

    public void SendRoulettePrizeLog()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte(EventBoardRoulettePrizeList);
        p.WriteInt(0);
        _conn.Send(p);
    }

    public void SendRouletteSpin()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte(EventBoardRouletteSpin);
        _conn.Send(p);
    }
}
