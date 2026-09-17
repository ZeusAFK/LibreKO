using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<MarketAd>>? MarketAdListEvent;
    public event Action<bool, int>? MarketRegisterEvent;
    public event Action<bool>? MarketDeleteEvent;

    private void HandleMarketBbs(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 4:
            {
                if (p.RemainingBytes >= 1) p.ReadByte();
                var ads = new List<MarketAd>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                {
                    var ad = new MarketAd { AdId = p.ReadInt(), SellerId = p.ReadInt(), Seller = p.ReadSByteString(),
                        ItemId = p.ReadInt(), Price = p.ReadInt(), Count = p.ReadUShort(),
                        BuyType = (byte)p.ReadByte(), RemainingDays = p.ReadInt() };
                    ads.Add(ad);
                }
                MarketAdListEvent?.Invoke(ads);
                break;
            }
            case 1:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int adId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                MarketRegisterEvent?.Invoke(ok, adId);
                break;
            }
            case 2:
                MarketDeleteEvent?.Invoke((p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1);
                break;
        }
    }

    public void SendMarketOpen(byte filter = 0)
    {
        var p = new Packet(GameOpcodes.GS_MARKET_BBS);
        p.WriteByte(4); p.WriteByte(filter);
        _conn.Send(p);
    }

    public void SendMarketRegister(int itemId, int price, int count, byte buyType, string memo)
    {
        var p = new Packet(GameOpcodes.GS_MARKET_BBS);
        p.WriteByte(1); p.WriteInt(itemId); p.WriteInt(price); p.WriteUShort((ushort)count);
        p.WriteByte(buyType); p.WriteSByteString(memo ?? "");
        _conn.Send(p);
    }

    public void SendMarketDelete(int adId)
    {
        var p = new Packet(GameOpcodes.GS_MARKET_BBS);
        p.WriteByte(2); p.WriteInt(adId);
        _conn.Send(p);
    }

    public void SendMarketReport(int adId)
    {
        var p = new Packet(GameOpcodes.GS_MARKET_BBS);
        p.WriteByte(3); p.WriteInt(adId);
        _conn.Send(p);
    }
}

public struct MarketAd
{
    public int AdId;
    public int SellerId;
    public string Seller;
    public int ItemId;
    public int Price;
    public int Count;
    public byte BuyType;
    public int RemainingDays;
}
