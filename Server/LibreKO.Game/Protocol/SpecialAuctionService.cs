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
    ICharacterStatePersister statePersister,
    IOptions<GameServerSettings> settings,
    ILogger<SpecialAuctionService> logger,
    TimeProvider? clock = null) : ISpecialAuctionService
{
    public const int MythrilCheck = 1399299001;
    public const int AltarNpcId = 31774;
    public const long CheckValue = 1_000_000_000;
    public const long CoinUnit = 1_000_000;
    public const long MaxCoins = 999 * CoinUnit;
    public const int RetentionDays = 14;
    public const int LogDays = SpecialAuctionSchedule.DaysPerGroup;
    public const int ResultRetentionDays = 30;

    private readonly SemaphoreSlim stateLock = new(1, 1);
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
                await client.SendPacket(SpecialAuctionPacketWriter.BidResult(AtTheAltar(session)
                    ? await BidAsync(session, packet)
                    : SpecialAuctionPacketWriter.Busy));
                break;
            case SpecialAuctionPacketWriter.CollectSub:
                await client.SendPacket(SpecialAuctionPacketWriter.CollectResult(AtTheAltar(session)
                    ? await CollectAsync(session, packet)
                    : SpecialAuctionPacketWriter.NoHistory));
                break;
            case SpecialAuctionPacketWriter.MyInfoSub:
                await MyInfoAsync(session);
                break;
            case SpecialAuctionPacketWriter.ClaimSub:
                await client.SendPacket(SpecialAuctionPacketWriter.ClaimResult(AtTheAltar(session)
                    ? await ClaimAsync(session, packet)
                    : SpecialAuctionPacketWriter.NoWin));
                break;
            case SpecialAuctionPacketWriter.LogSub:
                await LogAsync(session);
                break;
        }
    }

    // Gold and items change hands only at Akara's Altar; the lists can be read anywhere.
    private bool AtTheAltar(UserSession session) => NpcPresence.IsAt(sessionManager, session, AltarNpcId);

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

        if (session.Trade.IsTrading || session.Trade.IsMerchanting || session.Trade.IsMerchantPreparing)
            return SpecialAuctionPacketWriter.Busy;

        var lot = LotsOf(moment.Serial).FirstOrDefault(l => l.Slot == slot);
        if (lot == null || lot.ItemId != itemId || lot.Count != count)
            return SpecialAuctionPacketWriter.NoSuchItem;

        if (!checksValid || checkSlots.Distinct().Count() != checkSlots.Count
            || checkSlots.Any(s => !IsStakedCheck(session.Inventory[s])))
            return SpecialAuctionPacketWriter.WrongChecks;

        if (coins % CoinUnit != 0 || coins > MaxCoins || coins > session.Money)
            return SpecialAuctionPacketWriter.NotEnoughBalance;

        if (total != coins + checkSlots.Count * CheckValue)
            return SpecialAuctionPacketWriter.PriceNotAccurate;

        if (!await stateLock.WaitAsync(0))
            return SpecialAuctionPacketWriter.Busy;
        var journal = new InventoryJournal(session, logger);
        var answer = SpecialAuctionPacketWriter.Busy;
        try
        {
            // The coins, the checks and the bid are saved together.
            var outcome = await statePersister.CommitAsync(session, async db =>
            {
                var lotBids = await db.AuctionBids
                    .Where(b => b.Channel == Channel && b.Serial == moment.Serial && b.Slot == slot && b.ClosedAt == null)
                    .ToListAsync();
                var top = lotBids.FirstOrDefault(b => b.Status == AuctionBidStatus.Top);
                answer = top?.CharacterId == session.CharacterId ? SpecialAuctionPacketWriter.AlreadyTopBidder
                    : lotBids.Any(b => b.CharacterId == session.CharacterId) ? SpecialAuctionPacketWriter.CollectFirst
                    : total < (top?.Total(CheckValue) ?? lot.StartPrice) + lot.Step ? SpecialAuctionPacketWriter.NotEnoughBalance
                    : journal.Apply(s => TakeStake(s, journal, checkSlots, coins));
                if (answer != SpecialAuctionPacketWriter.Success)
                    return false;

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
                return true;
            }, () => session.WithLock(_ => journal.Restore()));
            if (outcome != StateCommit.Saved)
                return outcome == StateCommit.Unchanged ? answer : SpecialAuctionPacketWriter.Busy;
        }
        finally
        {
            stateLock.Release();
        }

        foreach (var bagSlot in journal.TouchedSlots)
            await userNotificationService.SendStackChangeAsync(session, (byte)bagSlot, 0, 0, 0);
        if (coins > 0)
            await userNotificationService.SendGoldLossAsync(session, (int)coins);
        if (checkSlots.Count > 0)
            await userNotificationService.SendWeightChangeAsync(session);
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

        await stateLock.WaitAsync();
        var journal = new InventoryJournal(session, logger);
        var answer = SpecialAuctionPacketWriter.NoHistory;
        long coins = 0;
        try
        {
            // The bid closes in the same save that brings its coins and checks back.
            var outcome = await statePersister.CommitAsync(session, async db =>
            {
                var bid = await db.AuctionBids.FirstOrDefaultAsync(b => b.Channel == Channel && b.CharacterId == session.CharacterId
                    && b.Serial == serial && b.Slot == slot && b.ItemId == itemId && b.Count == count && b.ClosedAt == null);
                answer = bid == null ? SpecialAuctionPacketWriter.NoHistory
                    : bid.Status is AuctionBidStatus.Top or AuctionBidStatus.Won ? SpecialAuctionPacketWriter.AlreadyTopBidder
                    : journal.Apply(s => GiveBack(s, journal, bid));
                if (answer != SpecialAuctionPacketWriter.Success)
                    return false;

                bid!.ClosedAt = Now;
                coins = bid.Coins;
                return true;
            }, () => session.WithLock(_ => journal.Restore()));
            if (outcome != StateCommit.Saved)
                return outcome == StateCommit.Unchanged ? answer : SpecialAuctionPacketWriter.Busy;
        }
        finally
        {
            stateLock.Release();
        }

        await SendDeliveredAsync(session, journal);
        if (coins > 0)
            await userNotificationService.SendGoldGainAsync(session, (int)coins);
        return SpecialAuctionPacketWriter.Success;
    }

    private static bool IsStakedCheck(ItemSlot slot) =>
        slot.ItemId == MythrilCheck && slot.Count > 0 && slot.IsTradable && !slot.Expires;

    // The stake leaves the bag only as it was checked.
    private static short TakeStake(UserSession session, InventoryJournal journal, List<int> checkSlots, long coins)
    {
        if (coins > session.Money)
            return SpecialAuctionPacketWriter.NotEnoughBalance;
        if (checkSlots.Any(index => !IsStakedCheck(session.Inventory[index])))
            return SpecialAuctionPacketWriter.WrongChecks;

        foreach (var index in checkSlots)
            journal.Slot(index).Clear();
        journal.AddMoney(-coins);
        return SpecialAuctionPacketWriter.Success;
    }

    private short GiveBack(UserSession session, InventoryJournal journal, AuctionBid bid)
    {
        if (session.Money + bid.Coins > ExchangePacketConstants.CoinMax)
            return SpecialAuctionPacketWriter.CoinsFull;
        if (bid.Checks > 0 && (gameDataService.GetItem(MythrilCheck) is not { } check || !journal.DeliverAll(check, bid.Checks)))
            return SpecialAuctionPacketWriter.NoRoomForChecks;

        journal.AddMoney(bid.Coins);
        return SpecialAuctionPacketWriter.Success;
    }

    private async Task SendDeliveredAsync(UserSession session, InventoryJournal journal)
    {
        foreach (var index in journal.TouchedSlots)
        {
            var slot = session.Inventory[index];
            await userNotificationService.SendStackChangeAsync(session, (byte)index, slot.ItemId, slot.Count, slot.Durability, journal.WasEmpty(index));
        }

        if (journal.TouchedSlots.Count > 0)
        {
            session.RecalculateStatsWithBuffs(gameDataService);
            await userNotificationService.SendWeightChangeAsync(session);
        }
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

        await stateLock.WaitAsync();
        var journal = new InventoryJournal(session, logger);
        var answer = SpecialAuctionPacketWriter.NoWin;
        try
        {
            // The whole lot reaches the bag in the same save that closes the bid, or nothing does.
            var outcome = await statePersister.CommitAsync(session, async db =>
            {
                var bid = await db.AuctionBids
                    .Where(b => b.Channel == Channel && b.CharacterId == session.CharacterId && b.Status == AuctionBidStatus.Won
                        && b.Day == day && b.Slot == slot && b.ItemId == itemId && b.Count == count && b.ClosedAt == null)
                    .OrderBy(b => b.Serial)
                    .FirstOrDefaultAsync();
                if (bid == null || gameDataService.GetItem(itemId) is not { } item)
                {
                    answer = SpecialAuctionPacketWriter.NoWin;
                    return false;
                }

                if (!journal.Apply(_ => journal.DeliverAll(item, count)))
                {
                    answer = SpecialAuctionPacketWriter.BagFull;
                    return false;
                }

                bid.ClosedAt = Now;
                return true;
            }, () => session.WithLock(_ => journal.Restore()));
            if (outcome != StateCommit.Saved)
                return outcome == StateCommit.Unchanged ? answer : SpecialAuctionPacketWriter.Busy;
        }
        finally
        {
            stateLock.Release();
        }

        await SendDeliveredAsync(session, journal);
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
        await stateLock.WaitAsync();
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
            stateLock.Release();
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
