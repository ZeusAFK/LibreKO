using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public const byte AuctionTodaySub = 1;
    public const byte AuctionBidSub = 2;
    public const byte AuctionCollectSub = 3;
    public const byte AuctionMyInfoSub = 4;
    public const byte AuctionNoticeSub = 5;
    public const byte AuctionClaimSub = 6;
    public const byte AuctionLogSub = 7;

    private const byte AuctionMiscarriedNotice = 0;
    private const byte AuctionEndedNotice = 1;

    public event Action<AuctionToday>? AuctionTodayEvent;
    public event Action<short>? AuctionBidEvent;
    public event Action<short>? AuctionCollectEvent;
    public event Action<short, IReadOnlyList<AuctionBidRow>>? AuctionMyInfoEvent;
    public event Action<IReadOnlyList<AuctionResultLine>>? AuctionEndedEvent;
    public event Action<bool>? AuctionMiscarriedEvent;
    public event Action<short>? AuctionClaimEvent;
    public event Action<IReadOnlyList<IReadOnlyList<AuctionResultLine>>>? AuctionLogEvent;

    public void SendAuctionRequest(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(sub);
        _conn.Send(p);
    }

    public void SendAuctionBid(AuctionLot lot, IReadOnlyList<int> checkSlots, int millions)
    {
        long coins = millions * SpecialAuction.CoinUnit;
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(AuctionBidSub);
        p.WriteByte((byte)lot.Slot);
        p.WriteInt(lot.ItemId);
        p.WriteShort((short)lot.Count);
        p.WriteByte((byte)checkSlots.Count);
        foreach (int abs in checkSlots)
        {
            p.WriteInt(SpecialAuction.MythrilCheck);
            p.WriteByte((byte)(abs - Inventory.GridStart));
        }
        p.WriteUInt((uint)coins);
        p.WriteLong(coins + checkSlots.Count * SpecialAuction.CheckValue);
        _conn.Send(p);
    }

    public void SendAuctionRetract(AuctionBidRow row)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(AuctionCollectSub);
        WriteAuctionRowKey(p, row);
        p.WriteInt(row.Serial);
        p.WriteByte(row.Sequence);
        _conn.Send(p);
    }

    public void SendAuctionReceive(AuctionBidRow row)
    {
        var p = new Packet(GameOpcodes.GS_AUCTION);
        p.WriteByte(AuctionClaimSub);
        WriteAuctionRowKey(p, row);
        _conn.Send(p);
    }

    private static void WriteAuctionRowKey(Packet p, AuctionBidRow row)
    {
        p.WriteInt(row.Channel);
        p.WriteByte(row.Day);
        p.WriteByte(row.Slot);
        p.WriteInt(row.ItemId);
        p.WriteShort(row.Count);
    }

    private void HandleAuction(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch (p.ReadByte())
        {
            case AuctionTodaySub:
                HandleAuctionToday(p);
                break;
            case AuctionBidSub:
                AuctionBidEvent?.Invoke(p.ReadShort());
                break;
            case AuctionCollectSub:
                AuctionCollectEvent?.Invoke(p.ReadShort());
                break;
            case AuctionMyInfoSub:
                HandleAuctionMyInfo(p);
                break;
            case AuctionNoticeSub:
                HandleAuctionNotice(p);
                break;
            case AuctionClaimSub:
                AuctionClaimEvent?.Invoke(p.ReadShort());
                break;
            case AuctionLogSub:
                HandleAuctionLog(p);
                break;
        }
    }

    private void HandleAuctionToday(Packet p)
    {
        short status = p.ReadShort();
        if (status == SpecialAuction.NothingToBid)
        {
            AuctionTodayEvent?.Invoke(new AuctionToday(status, 0, 0, 0, Array.Empty<AuctionOffer>()));
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
        AuctionTodayEvent?.Invoke(new AuctionToday(status, group, seconds, day, offers));
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
                byte status = p.ReadByte();
                if (itemId != 0)
                    rows.Add(new AuctionBidRow(channel, day, slot, itemId, itemCount, serial, sequence, price, status));
            }
        }
        AuctionMyInfoEvent?.Invoke(result, rows);
    }

    private void HandleAuctionNotice(Packet p)
    {
        switch (p.ReadByte())
        {
            case AuctionMiscarriedNotice:
                AuctionMiscarriedEvent?.Invoke(p.ReadByte() != 0);
                break;
            case AuctionEndedNotice:
                p.ReadShort();
                p.ReadByte();
                int count = Math.Min((int)p.ReadByte(), SpecialAuction.LotsPerDay);
                var lines = new List<AuctionResultLine>();
                for (int i = 0; i < count && p.RemainingBytes > 0; i++)
                    lines.Add(ReadAuctionResult(p));
                AuctionEndedEvent?.Invoke(lines);
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
        AuctionLogEvent?.Invoke(log);
    }

    private static AuctionResultLine ReadAuctionResult(Packet p)
    {
        int itemId = p.ReadInt();
        long price = p.ReadLong();
        return new AuctionResultLine(itemId, price, p.ReadByte());
    }
}
