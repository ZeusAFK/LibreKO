using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Protocol;

public interface ISpecialAuctionService
{
    Task HandleAsync(IClient client, Packet packet);
    Task SettleDueAsync();
    Task PruneAsync();
    Task<bool> HasOpenAuctionAsync(int characterId);
}

public class SpecialAuctionService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IItemGrantService itemGrantService,
    ICharacterStatePersister statePersister,
    IOptions<GameServerSettings> settings,
    ILogger<SpecialAuctionService> logger,
    TimeProvider? clock = null) : ISpecialAuctionService
{
    public const int MythrilCheck = 1399299001;
    public const long CheckValue = 1_000_000_000;
    public const long CoinUnit = 1_000_000;
    public const long MaxCoins = 999 * CoinUnit;
    public const int RetentionDays = 14;
    public const int LogDays = SpecialAuctionSchedule.DaysPerGroup;
    public const int ResultRetentionDays = 30;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private TimeProvider Clock => clock ?? TimeProvider.System;
    private DateTime Now => Clock.GetUtcNow().UtcDateTime;
    private int Channel => settings.Value.ServerId;
    private int Group => settings.Value.SpecialAuctionGroup;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        switch (packet.ReadByte())
        {
            case SpecialAuctionPacketWriter.TodaySub:
                await TodayAsync(session);
                break;
            case SpecialAuctionPacketWriter.BidSub:
                await client.SendPacket(SpecialAuctionPacketWriter.BidResult(await BidAsync(session, packet)));
                break;
            case SpecialAuctionPacketWriter.CollectSub:
                await client.SendPacket(SpecialAuctionPacketWriter.CollectResult(await CollectAsync(session, packet)));
                break;
            case SpecialAuctionPacketWriter.MyInfoSub:
                await MyInfoAsync(session);
                break;
            case SpecialAuctionPacketWriter.ClaimSub:
                await client.SendPacket(SpecialAuctionPacketWriter.ClaimResult(await ClaimAsync(session, packet)));
                break;
            case SpecialAuctionPacketWriter.LogSub:
                await LogAsync(session);
                break;
        }
    }

    private IReadOnlyList<SpecialAuctionLotData> LotsOf(int serial) =>
        Group <= 0 ? [] : gameDataService.SpecialAuctionLotsByRow[SpecialAuctionSchedule.Row(Group, SpecialAuctionSchedule.DayOf(serial))]
            .OrderBy(l => l.Slot).ToList();

    private async Task TodayAsync(UserSession session)
    {
        var moment = SpecialAuctionSchedule.At(Now);
        await SettleDueAsync();
        var lots = LotsOf(moment.Serial);
        if (lots.Count == 0)
        {
            await session.Client.SendPacket(SpecialAuctionPacketWriter.Today(SpecialAuctionPacketWriter.NothingToBid, Group, 0, moment.Day, []));
            return;
        }

        var status = moment.Phase switch
        {
            AuctionPhase.Open => SpecialAuctionPacketWriter.Bidding,
            AuctionPhase.Settlement => SpecialAuctionPacketWriter.Settling,
            _ => SpecialAuctionPacketWriter.CollectOnly,
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tops = await db.AuctionBids.AsNoTracking()
            .Where(b => b.Channel == Channel && b.Serial == moment.Serial && b.Status == AuctionBidStatus.Top)
            .ToListAsync();
        var views = lots.Select(lot =>
        {
            var top = tops.FirstOrDefault(b => b.Slot == lot.Slot);
            return new AuctionLotView(lot.Slot, lot.ItemId, top?.Total(CheckValue) ?? 0, top?.CharacterName ?? string.Empty);
        }).ToList();
        await session.Client.SendPacket(SpecialAuctionPacketWriter.Today(status, Group, moment.SecondsLeft, moment.Day, views));
    }

    private async Task<short> BidAsync(UserSession session, Packet packet)
    {
        var slot = packet.ReadByte();
        var itemId = packet.ReadInt();
        var count = packet.ReadShort();
        var checkCount = packet.ReadByte();
        var checkSlots = new List<int>();
        var checksValid = true;
        for (var i = 0; i < checkCount; i++)
        {
            var checkId = packet.ReadInt();
            var bagSlot = packet.ReadByte();
            checksValid &= checkId == MythrilCheck && bagSlot < InventoryConstants.HaveMax;
            checkSlots.Add(InventoryConstants.SlotMax + bagSlot);
        }
        var coins = (long)packet.ReadUInt();
        var total = packet.ReadLong();

        var moment = SpecialAuctionSchedule.At(Now);
        if (moment.Phase != AuctionPhase.Open)
            return SpecialAuctionPacketWriter.NotBiddingTime;

        var lot = LotsOf(moment.Serial).FirstOrDefault(l => l.Slot == slot);
        if (lot == null || lot.ItemId != itemId || lot.Count != count)
            return SpecialAuctionPacketWriter.NoSuchItem;

        if (!checksValid || checkSlots.Distinct().Count() != checkSlots.Count
            || checkSlots.Any(s => session.Inventory[s].ItemId != MythrilCheck || !session.Inventory[s].IsTradable))
            return SpecialAuctionPacketWriter.WrongChecks;

        if (coins % CoinUnit != 0 || coins > MaxCoins || coins > session.Money)
            return SpecialAuctionPacketWriter.NotEnoughBalance;

        if (total != coins + checkSlots.Count * CheckValue)
            return SpecialAuctionPacketWriter.PriceNotAccurate;

        if (!await _lock.WaitAsync(0))
            return SpecialAuctionPacketWriter.Busy;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lotBids = await db.AuctionBids
                .Where(b => b.Channel == Channel && b.Serial == moment.Serial && b.Slot == slot && b.ClosedAt == null)
                .ToListAsync();
            var top = lotBids.FirstOrDefault(b => b.Status == AuctionBidStatus.Top);
            if (top?.CharacterId == session.CharacterId)
                return SpecialAuctionPacketWriter.AlreadyTopBidder;
            if (lotBids.Any(b => b.CharacterId == session.CharacterId))
                return SpecialAuctionPacketWriter.CollectFirst;
            if (total < (top?.Total(CheckValue) ?? lot.StartPrice) + lot.Step)
                return SpecialAuctionPacketWriter.NotEnoughBalance;

            session.Money -= (int)coins;
            foreach (var bagSlot in checkSlots)
            {
                session.Inventory[bagSlot].Clear();
                await userNotificationService.SendStackChangeAsync(session, (byte)bagSlot, 0, 0, 0);
            }
            if (top != null)
                top.Status = AuctionBidStatus.Outbid;
            db.AuctionBids.Add(new AuctionBid
            {
                Channel = Channel,
                Serial = moment.Serial,
                Day = (byte)moment.Day,
                Slot = slot,
                ItemId = itemId,
                Count = count,
                CharacterId = session.CharacterId,
                CharacterName = session.Name,
                Coins = coins,
                Checks = checkSlots.Count,
                Status = AuctionBidStatus.Top,
                PlacedAt = Now,
            });
            await db.SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }

        if (coins > 0)
            await userNotificationService.SendGoldLossAsync(session, (int)coins);
        if (checkSlots.Count > 0)
            await userNotificationService.SendWeightChangeAsync(session);
        await statePersister.SaveAsync(session);
        logger.LogInformation("{Name} bid {Total} on auction lot {Slot} ({ItemId})", session.Name, total, slot, itemId);
        return SpecialAuctionPacketWriter.Success;
    }

    private async Task<short> CollectAsync(UserSession session, Packet packet)
    {
        var channel = packet.ReadInt();
        packet.ReadByte();
        var slot = packet.ReadByte();
        var itemId = packet.ReadInt();
        var count = packet.ReadShort();
        var serial = packet.ReadInt();

        if (channel != Channel)
            return SpecialAuctionPacketWriter.WrongChannel;

        await _lock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bid = await db.AuctionBids.FirstOrDefaultAsync(b => b.Channel == Channel && b.CharacterId == session.CharacterId
                && b.Serial == serial && b.Slot == slot && b.ItemId == itemId && b.Count == count && b.ClosedAt == null);
            if (bid == null)
                return SpecialAuctionPacketWriter.NoHistory;
            if (bid.Status is AuctionBidStatus.Top or AuctionBidStatus.Won)
                return SpecialAuctionPacketWriter.AlreadyTopBidder;
            if (session.Money + bid.Coins > ExchangePacketConstants.CoinMax)
                return SpecialAuctionPacketWriter.CoinsFull;
            if (bid.Checks > 0 && (gameDataService.GetItem(MythrilCheck) == null || FreeBagSlots(session) < bid.Checks))
                return SpecialAuctionPacketWriter.NoRoomForChecks;

            bid.ClosedAt = Now;
            await db.SaveChangesAsync();

            session.Money += (int)bid.Coins;
            if (bid.Coins > 0)
                await userNotificationService.SendGoldGainAsync(session, (int)bid.Coins);
            if (bid.Checks > 0)
                await itemGrantService.GrantAsync(session, gameDataService.GetItem(MythrilCheck)!, bid.Checks);
        }
        finally
        {
            _lock.Release();
        }

        await statePersister.SaveAsync(session);
        return SpecialAuctionPacketWriter.Success;
    }

    private static int FreeBagSlots(UserSession session)
    {
        var free = 0;
        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
            if (session.Inventory[i].IsEmpty)
                free++;
        return free;
    }

    private async Task MyInfoAsync(UserSession session)
    {
        var moment = SpecialAuctionSchedule.At(Now);
        if (moment.Phase == AuctionPhase.Settlement)
        {
            await session.Client.SendPacket(SpecialAuctionPacketWriter.MyInfo(SpecialAuctionPacketWriter.MyInfoSettling, []));
            return;
        }

        await SettleDueAsync();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bids = await db.AuctionBids.AsNoTracking()
            .Where(b => b.CharacterId == session.CharacterId && b.ClosedAt == null)
            .OrderBy(b => b.PlacedAt)
            .ToListAsync();
        var rows = bids.Select(b => new AuctionBidView(b.Channel, b.Day, b.Slot, b.ItemId, b.Count, b.Serial, b.Total(CheckValue), (byte)b.Status))
            .ToList();
        await session.Client.SendPacket(SpecialAuctionPacketWriter.MyInfo(SpecialAuctionPacketWriter.Success, rows));
    }

    private async Task<short> ClaimAsync(UserSession session, Packet packet)
    {
        var channel = packet.ReadInt();
        var day = packet.ReadByte();
        var slot = packet.ReadByte();
        var itemId = packet.ReadInt();
        var count = packet.ReadShort();

        if (channel != Channel)
            return SpecialAuctionPacketWriter.ClaimWrongChannel;

        await _lock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bid = await db.AuctionBids
                .Where(b => b.Channel == Channel && b.CharacterId == session.CharacterId && b.Status == AuctionBidStatus.Won
                    && b.Day == day && b.Slot == slot && b.ItemId == itemId && b.Count == count && b.ClosedAt == null)
                .OrderBy(b => b.Serial)
                .FirstOrDefaultAsync();
            if (bid == null || gameDataService.GetItem(itemId) is not { } item)
                return SpecialAuctionPacketWriter.NoWin;
            if (FreeBagSlots(session) == 0 || await itemGrantService.GrantAsync(session, item, count) < count)
                return SpecialAuctionPacketWriter.BagFull;

            bid.ClosedAt = Now;
            await db.SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }

        await statePersister.SaveAsync(session);
        return SpecialAuctionPacketWriter.Success;
    }

    private async Task LogAsync(UserSession session)
    {
        await SettleDueAsync();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var results = await db.AuctionResults.AsNoTracking().Where(r => r.Channel == Channel).ToListAsync();
        var days = results.GroupBy(r => r.Serial)
            .OrderByDescending(g => g.Key)
            .Take(LogDays)
            .Select(g =>
            {
                var block = new AuctionResultView[SpecialAuctionPacketWriter.LotsPerDay];
                foreach (var r in g.Where(r => r.Slot < SpecialAuctionPacketWriter.LotsPerDay))
                    block[r.Slot] = new AuctionResultView(r.ItemId, r.Price, (byte)r.Status);
                return (IReadOnlyList<AuctionResultView>)block;
            })
            .ToList();
        await session.Client.SendPacket(SpecialAuctionPacketWriter.Log(days));
    }

    public async Task SettleDueAsync()
    {
        var moment = SpecialAuctionSchedule.At(Now);
        var lastClosed = moment.Phase == AuctionPhase.Open ? moment.Serial - 1 : moment.Serial;
        List<int> pending;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pending = await db.AuctionBids.AsNoTracking()
                .Where(b => b.Channel == Channel && b.Status == AuctionBidStatus.Top && b.Serial < lastClosed)
                .Select(b => b.Serial)
                .Distinct()
                .ToListAsync();
        }
        foreach (var serial in pending.Order())
            await SettleAsync(serial);
        await SettleAsync(lastClosed);
    }

    private async Task SettleAsync(int serial)
    {
        var lots = LotsOf(serial);
        if (lots.Count == 0)
            return;

        List<AuctionResultView> ended;
        await _lock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.AuctionResults.AnyAsync(r => r.Channel == Channel && r.Serial == serial))
                return;

            var tops = await db.AuctionBids
                .Where(b => b.Channel == Channel && b.Serial == serial && b.Status == AuctionBidStatus.Top)
                .ToListAsync();
            ended = [];
            foreach (var lot in lots)
            {
                var top = tops.FirstOrDefault(b => b.Slot == lot.Slot);
                if (top != null)
                    top.Status = AuctionBidStatus.Won;
                var result = new AuctionResult
                {
                    Channel = Channel,
                    Serial = serial,
                    Slot = lot.Slot,
                    ItemId = lot.ItemId,
                    Price = top?.Total(CheckValue) ?? 0,
                    Status = top != null ? AuctionBidStatus.Won : AuctionBidStatus.Cancelled,
                    SettledAt = Now,
                };
                db.AuctionResults.Add(result);
                ended.Add(new AuctionResultView(result.ItemId, result.Price, (byte)result.Status));
            }
            await db.SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("Akara's Altar auction {Serial} settled: {Sold} of {Lots} lots sold", serial,
            ended.Count(r => r.Status == (byte)AuctionBidStatus.Won), ended.Count);
        var notice = SpecialAuctionPacketWriter.Ended(ended);
        foreach (var online in sessionManager.GetAll())
            await online.Client.SendPacket(notice);
    }

    public async Task PruneAsync()
    {
        var cutoff = Now.AddDays(-RetentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expired = await db.AuctionBids.Where(b => b.PlacedAt < cutoff).ToListAsync();
        var oldResults = await db.AuctionResults.Where(r => r.SettledAt < Now.AddDays(-ResultRetentionDays)).ToListAsync();
        if (expired.Count == 0 && oldResults.Count == 0)
            return;
        db.AuctionBids.RemoveRange(expired);
        db.AuctionResults.RemoveRange(oldResults);
        await db.SaveChangesAsync();
        logger.LogInformation("Pruned {Bids} auction bids and {Results} results", expired.Count, oldResults.Count);
    }

    public async Task<bool> HasOpenAuctionAsync(int characterId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuctionBids.AnyAsync(b => b.CharacterId == characterId && b.ClosedAt == null);
    }
}
