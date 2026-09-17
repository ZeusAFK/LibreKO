using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<AuctionLot>>? AuctionListEvent;
    public event Action<int, bool>? AuctionRegisterEvent;
    public event Action<int, int, bool>? AuctionBidEvent;
    public event Action<int, int, bool>? AuctionBuyoutEvent;
    public event Action<int, bool>? AuctionCancelEvent;

    private void HandleAuction(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 1:
            {
                var list = new List<AuctionLot>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 8; i++)
                {
                    var lot = new AuctionLot
                    {
                        AuctionId = p.ReadInt(),
                        SellerId = p.ReadInt(),
                        Seller = p.ReadSByteString(),
                        ItemId = p.ReadInt(),
                        Count = p.ReadInt(),
                        CurrentBid = p.ReadInt(),
                        Buyout = p.ReadInt(),
                    };
                    list.Add(lot);
                }
                AuctionListEvent?.Invoke(list);
                break;
            }
            case 2:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                AuctionRegisterEvent?.Invoke(id, ok);
                break;
            }
            case 3:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int currentBid = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                AuctionBidEvent?.Invoke(id, currentBid, ok);
                break;
            }
            case 4:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int buyout = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                AuctionBuyoutEvent?.Invoke(id, buyout, ok);
                break;
            }
            case 5:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int id = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                AuctionCancelEvent?.Invoke(id, ok);
                break;
            }
        }
    }

    public void SendAuctionList()
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendAuctionRegister(int itemId, int startPrice, int buyout, int count)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(2);
        p.WriteInt(itemId);
        p.WriteInt(startPrice);
        p.WriteInt(buyout);
        p.WriteInt(count);
        _conn.Send(p);
    }

    public void SendAuctionBid(int auctionId, int bid)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(3);
        p.WriteInt(auctionId);
        p.WriteInt(bid);
        _conn.Send(p);
    }

    public void SendAuctionBuyout(int auctionId)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(4);
        p.WriteInt(auctionId);
        _conn.Send(p);
    }

    public void SendAuctionCancel(int auctionId)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(5);
        p.WriteInt(auctionId);
        _conn.Send(p);
    }
}

public struct AuctionLot
{
    public int AuctionId;
    public int SellerId;
    public string Seller;
    public int ItemId;
    public int Count;
    public int CurrentBid;
    public int Buyout;
}
