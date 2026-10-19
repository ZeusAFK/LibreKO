namespace LibreKO.Network.Readers;

internal sealed class SpecialAuctionReader : IPacketReader
{
    private readonly SpecialAuctionSession specialAuction;

    public SpecialAuctionReader(SpecialAuctionSession specialAuction)
    {
        this.specialAuction = specialAuction;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_AUCTION, HandleAuction);
    }

    private void HandleAuction(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((SpecialAuctionSubOpcode)p.ReadByte())
        {
            case SpecialAuctionSubOpcode.Today:
                HandleAuctionToday(p);
                break;
            case SpecialAuctionSubOpcode.Bid:
                specialAuction.RaiseAuctionBid(p.ReadShort());
                break;
            case SpecialAuctionSubOpcode.Collect:
                specialAuction.RaiseAuctionCollect(p.ReadShort());
                break;
            case SpecialAuctionSubOpcode.MyInfo:
                HandleAuctionMyInfo(p);
                break;
            case SpecialAuctionSubOpcode.Notice:
                HandleAuctionNotice(p);
                break;
            case SpecialAuctionSubOpcode.Claim:
                specialAuction.RaiseAuctionClaim(p.ReadShort());
                break;
            case SpecialAuctionSubOpcode.Log:
                HandleAuctionLog(p);
                break;
        }
    }

    private void HandleAuctionToday(Packet p)
    {
        short status = p.ReadShort();
        if (status == SpecialAuction.NothingToBid)
        {
            specialAuction.RaiseAuctionToday(new AuctionToday(status, 0, 0, 0, Array.Empty<AuctionOffer>()));
            return;
        }

        int group = p.ReadInt();
        p.ReadInt();
        p.ReadByte();
        int seconds = p.ReadInt();
        int day = p.ReadByte();
        var offers = new List<AuctionOffer>();
        if (status == SpecialAuction.Bidding)
        {
            int count = p.ReadShort();
            for (int i = 0; i < count && p.RemainingBytes > 0; i++)
            {
                int slot = p.ReadByte();
                int itemId = p.ReadInt();
                long current = p.ReadLong();
                offers.Add(new AuctionOffer(slot, itemId, current, p.ReadSByteString()));
            }
        }
        specialAuction.RaiseAuctionToday(new AuctionToday(status, group, seconds, day, offers));
    }

    private void HandleAuctionMyInfo(Packet p)
    {
        short result = p.ReadShort();
        var rows = new List<AuctionBidRow>();
        if (result == SpecialAuction.Success)
        {
            int count = p.ReadByte();
            for (int i = 0; i < count && p.RemainingBytes > 0; i++)
            {
                int channel = p.ReadInt();
                byte day = p.ReadByte();
                byte slot = p.ReadByte();
                int itemId = p.ReadInt();
                short itemCount = p.ReadShort();
                int serial = p.ReadInt();
                byte sequence = p.ReadByte();
                long price = p.ReadLong();
                var status = (AuctionBidStatus)p.ReadByte();
                if (itemId != 0)
                    rows.Add(new AuctionBidRow(channel, day, slot, itemId, itemCount, serial, sequence, price, status));
            }
        }
        specialAuction.RaiseAuctionMyInfo(result, rows);
    }

    private void HandleAuctionNotice(Packet p)
    {
        switch ((SpecialAuctionNoticeKind)p.ReadByte())
        {
            case SpecialAuctionNoticeKind.Miscarried:
                specialAuction.RaiseAuctionMiscarried(p.ReadByte() != 0);
                break;
            case SpecialAuctionNoticeKind.Ended:
                p.ReadShort();
                p.ReadByte();
                int count = Math.Min((int)p.ReadByte(), SpecialAuction.LotsPerDay);
                var lines = new List<AuctionResultLine>();
                for (int i = 0; i < count && p.RemainingBytes > 0; i++)
                    lines.Add(ReadAuctionResult(p));
                specialAuction.RaiseAuctionEnded(lines);
                break;
        }
    }

    private void HandleAuctionLog(Packet p)
    {
        int days = p.ReadShort();
        var log = new List<IReadOnlyList<AuctionResultLine>>();
        for (int d = 0; d < days && p.RemainingBytes > 0; d++)
        {
            var block = new List<AuctionResultLine>();
            for (int slot = 0; slot < SpecialAuction.LotsPerDay; slot++)
                block.Add(ReadAuctionResult(p));
            log.Add(block);
        }
        specialAuction.RaiseAuctionLog(log);
    }

    private static AuctionResultLine ReadAuctionResult(Packet p)
    {
        int itemId = p.ReadInt();
        long price = p.ReadLong();
        return new AuctionResultLine(itemId, price, (AuctionBidStatus)p.ReadByte());
    }
}
