using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IAuctionPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class AuctionPacketCoordinator(
    SessionManager sessionManager,
    ILogger<AuctionPacketCoordinator> logger) : IAuctionPacketCoordinator
{
    private const byte AuctionSubList = 1;
    private const byte AuctionSubRegister = 2;
    private const byte AuctionSubBid = 3;
    private const byte AuctionSubBuyout = 4;
    private const byte AuctionSubCancel = 5;

    private sealed class AuctionLot
    {
        public int AuctionId;
        public int SellerId;
        public string SellerName = "";
        public int ItemId;
        public int Count;
        public int CurrentBid;     // 0 = no bid yet (starts at StartPrice via min-bid rule below)
        public int StartPrice;
        public int Buyout;
        public int TopBidderId;    // who holds the current bid (so we don't allow self-undercut weirdness)
    }

    // Single global lot book (in-memory; resets on server restart).
    private static readonly List<AuctionLot> auctions = new();
    private static int nextAuctionId = 1;
    private static readonly object auctionLock = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case AuctionSubList:
                await SendAuctionListAsync(session);
                break;
            case AuctionSubRegister:
                await HandleAuctionRegisterAsync(session, packet);
                break;
            case AuctionSubBid:
                await HandleAuctionBidAsync(session, packet);
                break;
            case AuctionSubBuyout:
                await HandleAuctionBuyoutAsync(session, packet);
                break;
            case AuctionSubCancel:
                await HandleAuctionCancelAsync(session, packet);
                break;
        }
    }

    private static Packet BuildAuctionListPacket()
    {
        List<AuctionPacketWriter.Lot> lots;
        lock (auctionLock)
        {
            lots = auctions
                .Select(lot => new AuctionPacketWriter.Lot(
                    lot.AuctionId, lot.SellerId, lot.SellerName,
                    lot.ItemId, lot.Count, lot.CurrentBid, lot.Buyout))
                .ToList();
        }

        return AuctionPacketWriter.Listing(AuctionSubList, lots);
    }

    private async Task SendAuctionListAsync(UserSession session)
    {
        await session.Client.SendPacket(BuildAuctionListPacket());
    }

    private async Task HandleAuctionRegisterAsync(UserSession session, Packet packet)
    {
        int itemId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        int startPrice = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        int buyout = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        int count = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        if (itemId <= 0 || count <= 0 || startPrice < 0 || (buyout > 0 && buyout < startPrice))
        {
            await session.Client.SendPacket(AuctionPacketWriter.Registered(
                AuctionSubRegister, AuctionPacketWriter.Failed, 0));
            return;
        }

        int newId;
        lock (auctionLock)
        {
            newId = nextAuctionId++;
            auctions.Add(new AuctionLot
            {
                AuctionId = newId,
                SellerId = session.CharacterId,
                SellerName = session.Name,
                ItemId = itemId,
                Count = count,
                CurrentBid = 0,
                StartPrice = startPrice,
                Buyout = buyout,
                TopBidderId = 0,
            });
        }

        logger.LogDebug("{Name} registered auction {Id} item {Item}x{Count} start {Start} buyout {Buyout}",
            session.Name, newId, itemId, count, startPrice, buyout);

        await session.Client.SendPacket(AuctionPacketWriter.Registered(
            AuctionSubRegister, AuctionPacketWriter.Succeeded, newId));
        await session.Client.SendPacket(BuildAuctionListPacket());
    }

    private async Task HandleAuctionBidAsync(UserSession session, Packet packet)
    {
        int auctionId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        int bid = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        bool ok = false;
        int currentBid = 0;
        lock (auctionLock)
        {
            var lot = auctions.Find(a => a.AuctionId == auctionId);
            if (lot != null && lot.SellerId != session.CharacterId)
            {
                int minBid = lot.CurrentBid > 0 ? lot.CurrentBid + 1 : lot.StartPrice;
                if (bid >= minBid && (lot.Buyout <= 0 || bid < lot.Buyout))
                {
                    lot.CurrentBid = bid;
                    lot.TopBidderId = session.CharacterId;
                    ok = true;
                }
                currentBid = lot.CurrentBid;
            }
        }

        if (ok)
            logger.LogDebug("{Name} bid {Bid} on auction {Id}", session.Name, bid, auctionId);

        await session.Client.SendPacket(AuctionPacketWriter.BidResult(
            AuctionSubBid, ok ? AuctionPacketWriter.Succeeded : AuctionPacketWriter.Failed,
            auctionId, currentBid));
        if (ok) await session.Client.SendPacket(BuildAuctionListPacket());
    }

    private async Task HandleAuctionBuyoutAsync(UserSession session, Packet packet)
    {
        int auctionId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        bool ok = false;
        int buyout = 0;
        lock (auctionLock)
        {
            var lot = auctions.Find(a => a.AuctionId == auctionId);
            if (lot != null && lot.Buyout > 0 && lot.SellerId != session.CharacterId)
            {
                buyout = lot.Buyout;
                auctions.Remove(lot);
                ok = true;
            }
        }

        if (ok)
            logger.LogDebug("{Name} bought out auction {Id} for {Buyout}", session.Name, auctionId, buyout);

        await session.Client.SendPacket(AuctionPacketWriter.BidResult(
            AuctionSubBuyout, ok ? AuctionPacketWriter.Succeeded : AuctionPacketWriter.Failed,
            auctionId, buyout));
        if (ok) await session.Client.SendPacket(BuildAuctionListPacket());
    }

    private async Task HandleAuctionCancelAsync(UserSession session, Packet packet)
    {
        int auctionId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        bool ok = false;
        lock (auctionLock)
        {
            var lot = auctions.Find(a => a.AuctionId == auctionId);
            // Only the seller may cancel, and not once someone has bid.
            if (lot != null && lot.SellerId == session.CharacterId && lot.CurrentBid <= 0)
            {
                auctions.Remove(lot);
                ok = true;
            }
        }

        if (ok)
            logger.LogDebug("{Name} cancelled auction {Id}", session.Name, auctionId);

        await session.Client.SendPacket(AuctionPacketWriter.Cancelled(
            AuctionSubCancel, ok ? AuctionPacketWriter.Succeeded : AuctionPacketWriter.Failed,
            auctionId));
        if (ok) await session.Client.SendPacket(BuildAuctionListPacket());
    }
}
