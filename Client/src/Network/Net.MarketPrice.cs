using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<MarketPriceReply>? MarketPriceEvent;

    public bool HasPremium => PremiumAccountStatus != 0 && PremiumHours > 0;

    public void SendMarketPrice(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_MARKET_PRICE);
        p.WriteInt(itemId);
        _conn.Send(p);
    }

    private void HandleMarketPrice(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int result = p.ReadByte();
        if (result != MarketPrice.History)
        {
            MarketPriceEvent?.Invoke(new MarketPriceReply(result, 0, Array.Empty<MarketPriceDay>(), 0, default));
            return;
        }

        int itemId = p.ReadInt();
        var days = new MarketPriceDay[MarketPrice.DaysShown];
        for (int i = 0; i < days.Length; i++)
        {
            long average = p.ReadLong();
            long max = p.ReadLong();
            long min = p.ReadLong();
            days[i] = new MarketPriceDay(average, max, min);
        }
        int trades = p.ReadInt();
        var lastUpdate = DateTimeOffset.FromUnixTimeSeconds(p.ReadUInt()).UtcDateTime;
        MarketPriceEvent?.Invoke(new MarketPriceReply(result, itemId, days, trades, lastUpdate));
    }
}
