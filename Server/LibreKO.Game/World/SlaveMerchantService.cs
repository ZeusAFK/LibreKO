using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class SlaveMerchantService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ICharacterStatePersister statePersister,
    IMailService mailService,
    ILogger<SlaveMerchantService> logger) : ISlaveMerchantService
{
    public const int OneDayCertificate = 751730000;
    public const int ThirtyDayCertificate = 891063000;
    public const int OneDayHours = 24;
    public const int ThirtyDayHours = 720;
    public const int MaxHours = ushort.MaxValue;
    public const string NamePrefix = "[S]";
    public const int NameLimit = 20;
    public const string AwayLetterSubject = "Your Slave Merchant";

    private const int BotCharacterIdBase = 850_000;
    private const int MoreLineLength = 16;
    private static readonly TimeSpan PaidSpan = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<int, SlaveMerchantBotSession> activeByOwner = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private int nextBotCharacterId = BotCharacterIdBase;

    public static int HoursOf(int itemId) => itemId switch
    {
        OneDayCertificate => OneDayHours,
        ThirtyDayCertificate => ThirtyDayHours,
        _ => 0,
    };

    public static string SlaveName(string ownerName)
    {
        var room = NameLimit - NamePrefix.Length;
        return NamePrefix + (ownerName.Length <= room ? ownerName : ownerName[..room]);
    }

    public async Task<bool> DeployAsync(UserSession owner, string advert)
    {
        await gate.WaitAsync();
        try
        {
            var result = await TryDeployAsync(owner, advert);
            await owner.Client.SendPacket(SlaveMerchantPacketWriter.Deploy(result));
            return result == SlaveMerchantDeployResult.Deployed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<SlaveMerchantDeployResult> TryDeployAsync(UserSession owner, string advert)
    {
        if (activeByOwner.ContainsKey(owner.CharacterId) || await HoldsAnythingAsync(owner.CharacterId))
            return SlaveMerchantDeployResult.AlreadyOpen;
        if (await HoursAsync(owner.CharacterId) < 1)
            return SlaveMerchantDeployResult.NoTime;

        var staged = owner.WithLock(StagedItems);
        if (staged == null)
            return SlaveMerchantDeployResult.ItemsMoved;

        var now = DateTime.UtcNow;
        var bot = NewBot(owner.CharacterId, owner.AccountId, owner.Name, now + PaidSpan);
        bot.Name = SlaveName(owner.Name);
        bot.Nation = owner.Nation;
        bot.Race = owner.Race;
        bot.Class = owner.Class;
        bot.Level = owner.Level > 0 ? owner.Level : BotSession.DefaultBotLevel;
        bot.Face = owner.Face;
        bot.Hair = owner.Hair;
        bot.ZoneId = owner.ZoneId;
        bot.X = owner.X;
        bot.Y = owner.Y;
        bot.Z = owner.Z;
        bot.Direction = owner.Direction;
        bot.Trade.PremiumMerchant = owner.Trade.PremiumMerchant;
        bot.Trade.MerchantAdvert = advert;

        var visuals = owner.WithLock(VisualsOf);
        WearVisuals(bot, visuals);
        foreach (var (stallSlot, item) in staged)
            PutOnStall(bot, stallSlot, item);

        var data = new SlaveMerchantData
        {
            OwnerCharacterId = owner.CharacterId,
            OwnerAccountId = owner.AccountId,
            OwnerName = owner.Name,
            BotName = bot.Name,
            Nation = owner.Nation,
            Race = owner.Race,
            Class = owner.Class,
            Face = owner.Face,
            Hair = owner.Hair,
            Level = bot.Level,
            ZoneId = owner.ZoneId,
            X = owner.X,
            Y = owner.Y,
            Z = owner.Z,
            Direction = owner.Direction,
            MerchantType = bot.Trade.PremiumMerchant ? MerchantType.PremiumSelling : MerchantType.Selling,
            StallTitle = advert,
            ItemsJson = JsonSerializer.Serialize(StallItems(bot)),
            EquipmentJson = JsonSerializer.Serialize(visuals),
            IsActive = true,
            CreatedAt = now,
            PaidUntil = bot.PaidUntil,
            HeartbeatAt = now,
        };

        var journal = new InventoryJournal(owner);
        var hoursLeft = 0;
        var outcome = await statePersister.CommitAsync(owner, async db =>
        {
            var time = await db.SlaveMerchantTimes.FindAsync(owner.CharacterId);
            if (time == null || time.Hours < 1 || !owner.WithLock(active => Take(active, staged, journal)))
                return false;

            time.Hours--;
            hoursLeft = time.Hours;
            db.SlaveMerchants.Add(data);
            return true;
        }, () => owner.WithLock(_ => journal.Restore()));

        if (outcome != StateCommit.Saved)
            return outcome == StateCommit.Failed ? SlaveMerchantDeployResult.Failed : SlaveMerchantDeployResult.ItemsMoved;

        bot.SlaveDatabaseId = data.Id;
        foreach (var slot in journal.TouchedSlots)
        {
            var source = owner.Inventory[slot];
            await userNotificationService.SendStackChangeAsync(owner, (byte)slot, source.ItemId, source.Count, source.Durability);
        }

        owner.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(owner);

        await SpawnAsync(bot);
        await owner.Client.SendPacket(SlaveMerchantPacketWriter.Time(hoursLeft));
        logger.LogInformation("Slave merchant {BotName} opened for {Owner} with {Count} items, {Hours} hours left",
            bot.Name, owner.Name, staged.Count, hoursLeft);
        return SlaveMerchantDeployResult.Deployed;
    }

    private static List<(int StallSlot, StoredItem Item)>? StagedItems(UserSession owner)
    {
        var staged = new List<(int, StoredItem)>();
        var taken = new Dictionary<int, int>();
        for (var i = 0; i < owner.Trade.MerchantItems.Length; i++)
        {
            var entry = owner.Trade.MerchantItems[i];
            if (entry == null || entry.IsEmpty)
                continue;

            var source = owner.Inventory[entry.OriginalSlot];
            var already = taken.GetValueOrDefault(entry.OriginalSlot);
            if (source.ItemId != entry.ItemId || source.Count < already + entry.Count || !source.IsTradable)
                return null;

            taken[entry.OriginalSlot] = already + entry.Count;
            staged.Add((i, new StoredItem(entry.ItemId, entry.Count, entry.Price,
                source.Durability, source.Flag, source.ExpiresAt, source.UniqueId)));
        }

        return staged.Count == 0 ? null : staged;
    }

    private static bool Take(UserSession owner, List<(int StallSlot, StoredItem Item)> staged, InventoryJournal journal)
    {
        var current = StagedItems(owner);
        if (current == null || !current.SequenceEqual(staged))
            return false;

        foreach (var (stallSlot, item) in staged)
        {
            var source = journal.Slot(owner.Trade.MerchantItems[stallSlot].OriginalSlot);
            source.Count -= item.Count;
            if (source.Count == 0)
                source.Clear();
        }

        return true;
    }

    private static List<StoredVisual> VisualsOf(UserSession owner)
    {
        var visuals = new List<StoredVisual>();
        foreach (var slot in InventoryConstants.VisualSlots)
        {
            var equipped = owner.GetEquippedItem(slot);
            if (!equipped.IsEmpty)
                visuals.Add(new StoredVisual(slot, equipped.ItemId, equipped.Durability, equipped.Flag));
        }

        return visuals;
    }

    public async Task<bool> SellAsync(SlaveMerchantBotSession bot, MerchantPurchase purchase, Func<Task> announce)
    {
        await gate.WaitAsync();
        try
        {
            if (!IsOpen(bot))
                return false;

            var outcome = await statePersister.CommitAsync(purchase.Buyer, async db =>
            {
                var applied = false;
                UserSession.WithBoth(purchase.Buyer, bot, (_, _) => applied = purchase.TryApply(gameDataService));
                if (!applied)
                    return false;

                var data = await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId)
                    ?? throw new InvalidOperationException($"slave merchant record {bot.SlaveDatabaseId} is not stored");
                data.EarnedNoah = bot.Money;
                data.ItemsJson = JsonSerializer.Serialize(StallItems(bot));
                if (sessionManager.GetByCharacterId(bot.OwnerCharacterId) == null)
                    data.AwayReportJson = WriteReport(ReadReport(data.AwayReportJson).WithSale(purchase.ItemId, purchase.Count, purchase.Cost));
                return true;
            }, () => UserSession.WithBoth(purchase.Buyer, bot, (_, _) => purchase.Undo()));

            if (outcome != StateCommit.Saved)
                return false;

            await announce();
            if (sessionManager.GetByCharacterId(bot.OwnerCharacterId) is { } owner)
                await owner.Client.SendPacket(SlaveMerchantPacketWriter.Sold(purchase.ItemId, purchase.Count, purchase.Cost));
            if (purchase.StallIsEmpty)
                await CloseAsync(bot, SlaveMerchantClosedReason.SoldOut);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SendStatusAsync(UserSession owner)
    {
        var hours = await HoursAsync(owner.CharacterId);
        if (activeByOwner.TryGetValue(owner.CharacterId, out var bot))
        {
            await owner.Client.SendPacket(SlaveMerchantPacketWriter.Status(open: true, hours, bot.Money, Lines(StallItems(bot))));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var held = await db.SlaveMerchants.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerCharacterId == owner.CharacterId);
        var items = held == null ? new List<StoredItem>() : ReadItems(held.ItemsJson);
        await owner.Client.SendPacket(SlaveMerchantPacketWriter.Status(open: false, hours, held?.EarnedNoah ?? 0, Lines(items)));
    }

    public async Task CollectAsync(UserSession owner)
    {
        await gate.WaitAsync();
        try
        {
            if (activeByOwner.TryGetValue(owner.CharacterId, out var bot) && !await CloseAsync(bot, closing: null))
            {
                await owner.Client.SendPacket(SlaveMerchantPacketWriter.CollectFailed());
                return;
            }

            var journal = new InventoryJournal(owner);
            var tally = new CollectTally();
            var outcome = await statePersister.CommitAsync(owner, async db =>
            {
                var held = await db.SlaveMerchants.Where(s => s.OwnerCharacterId == owner.CharacterId).ToListAsync();
                if (held.Count == 0)
                    return false;

                owner.WithLock(active => TakeHome(active, held, journal, tally, db));
                return true;
            }, () => owner.WithLock(_ => journal.Restore()));

            if (outcome == StateCommit.Failed)
            {
                await owner.Client.SendPacket(SlaveMerchantPacketWriter.CollectFailed());
                return;
            }

            foreach (var slot in journal.TouchedSlots)
            {
                var returned = owner.Inventory[slot];
                await userNotificationService.SendStackChangeAsync(
                    owner, (byte)slot, returned.ItemId, returned.Count, returned.Durability, journal.WasEmpty(slot));
            }

            if (tally.Paid > 0)
                await userNotificationService.SendGoldGainAsync(owner, (int)tally.Paid);
            if (tally.Returned > 0)
            {
                owner.RecalculateStatsWithBuffs(gameDataService);
                await userNotificationService.SendWeightChangeAsync(owner);
            }

            await owner.Client.SendPacket(SlaveMerchantPacketWriter.Collected(tally.Paid, tally.Returned, tally.Kept, tally.CoinsKept));
            if (tally.Paid > 0 || tally.Returned > 0)
                logger.LogInformation("{Name} collected {Coins} coins and {Items} items from their slave merchant",
                    owner.Name, tally.Paid, tally.Returned);
        }
        finally
        {
            gate.Release();
        }

        await SendStatusAsync(owner);
    }

    private void TakeHome(UserSession owner, List<SlaveMerchantData> held, InventoryJournal journal, CollectTally tally, AppDbContext db)
    {
        foreach (var data in held)
        {
            var room = Math.Max(0L, ExchangePacketConstants.CoinMax - (long)owner.Money);
            var pay = Math.Min(data.EarnedNoah, room);
            journal.AddMoney(pay);
            tally.Paid += pay;
            data.EarnedNoah -= pay;
            tally.CoinsKept += data.EarnedNoah;

            var left = new List<StoredItem>();
            foreach (var item in ReadItems(data.ItemsJson))
            {
                if (GiveBack(owner, item, journal))
                    tally.Returned++;
                else
                    left.Add(item);
            }

            tally.Kept += left.Count;
            data.ItemsJson = JsonSerializer.Serialize(left);
            if (left.Count == 0 && data.EarnedNoah == 0)
                db.SlaveMerchants.Remove(data);
        }
    }

    private bool GiveBack(UserSession owner, StoredItem item, InventoryJournal journal)
    {
        var destination = owner.FindSlotForItem(item.ItemId, gameDataService, item.Count);
        if (destination < 0)
            return false;

        var slot = journal.Slot(destination);
        if (slot.IsEmpty)
        {
            slot.ItemId = item.ItemId;
            slot.Count = item.Count;
            slot.Durability = item.Durability;
            slot.Flag = item.Flag;
            slot.ExpiresAt = item.ExpiresAt;
            slot.UniqueId = item.UniqueId;
        }
        else
        {
            slot.Count += item.Count;
        }

        return true;
    }

    public async Task RedeemAsync(UserSession owner, byte bagPosition, int itemId)
    {
        await gate.WaitAsync();
        try
        {
            var added = HoursOf(itemId);
            var slotIndex = InventoryConstants.InventoryStart + bagPosition;
            var journal = new InventoryJournal(owner);
            var hours = 0;
            var outcome = added == 0 || bagPosition >= InventoryConstants.HaveMax
                ? StateCommit.Unchanged
                : await statePersister.CommitAsync(owner, async db =>
                {
                    if (!owner.WithLock(_ => TakeOne(owner, slotIndex, itemId, journal)))
                        return false;

                    hours = await AddHoursAsync(db, owner.CharacterId, added);
                    return true;
                }, () => owner.WithLock(_ => journal.Restore()));

            if (outcome != StateCommit.Saved)
            {
                await owner.Client.SendPacket(SlaveMerchantPacketWriter.Redeemed(
                    SlaveMerchantRedeemResult.Failed, await HoursAsync(owner.CharacterId)));
                return;
            }

            var slot = owner.Inventory[slotIndex];
            await userNotificationService.SendStackChangeAsync(owner, (byte)slotIndex, slot.ItemId, slot.Count, slot.Durability);
            owner.RecalculateStatsWithBuffs(gameDataService);
            await userNotificationService.SendWeightChangeAsync(owner);
            await owner.Client.SendPacket(SlaveMerchantPacketWriter.Redeemed(SlaveMerchantRedeemResult.Added, hours));
            logger.LogInformation("{Name} used certificate {ItemId}: {Hours} slave merchant hours", owner.Name, itemId, hours);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool TakeOne(UserSession owner, int slotIndex, int itemId, InventoryJournal journal)
    {
        if (owner.Inventory[slotIndex].ItemId != itemId || owner.Inventory[slotIndex].Count == 0)
            return false;

        var slot = journal.Slot(slotIndex);
        slot.Count--;
        if (slot.Count == 0)
            slot.Clear();
        return true;
    }

    public async Task<int?> GiveHoursAsync(string characterName, int hours)
    {
        var characterId = sessionManager.GetByName(characterName)?.CharacterId;
        if (characterId == null)
        {
            using var scope = scopeFactory.CreateScope();
            characterId = (await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetByName(characterName))?.Id;
        }

        if (characterId == null)
            return null;

        await gate.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var total = await AddHoursAsync(db, characterId.Value, hours);
            await db.SaveChangesAsync();
            if (sessionManager.GetByCharacterId(characterId.Value) is { } online)
                await online.Client.SendPacket(SlaveMerchantPacketWriter.Time(total));
            return total;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReportAwayAsync(UserSession owner)
    {
        await gate.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reported = await db.SlaveMerchants
                .Where(s => s.OwnerCharacterId == owner.CharacterId && s.AwayReportJson != string.Empty)
                .ToListAsync();
            if (reported.Count == 0)
                return;

            foreach (var data in reported)
            {
                mailService.StageSystemMail(db, owner.CharacterId, AwayLetterSubject, AwayLetter(ReadReport(data.AwayReportJson)));
                data.AwayReportJson = string.Empty;
            }

            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The away report of {Name} could not be sent", owner.Name);
        }
        finally
        {
            gate.Release();
        }
    }

    private string AwayLetter(AwayReport report)
    {
        var footer = new StringBuilder();
        if (report.Closed is { } closed)
            footer.Append(closed == SlaveMerchantClosedReason.SoldOut
                ? "It closed: everything was sold.\n"
                : "It closed: its Slave Merchant time ran out.\n");
        footer.Append("Collect your coins and items in Merchant > Slave Merchant.");

        var body = new StringBuilder(report.Sales.Count == 0
            ? "While you were away, nothing was sold.\n"
            : "While you were away, your Slave Merchant sold:\n");
        for (var i = 0; i < report.Sales.Count; i++)
        {
            var sale = report.Sales[i];
            var name = gameDataService.GetItem(sale.ItemId)?.Name ?? sale.ItemId.ToString(CultureInfo.InvariantCulture);
            var line = string.Format(CultureInfo.InvariantCulture, "{0} x{1} for {2:N0} coins\n", name, sale.Count, sale.Coins);
            if (body.Length + line.Length + footer.Length + MoreLineLength > MailLimits.BodyMax)
            {
                body.Append(CultureInfo.InvariantCulture, $"and {report.Sales.Count - i} more\n");
                break;
            }

            body.Append(line);
        }

        return body.Append(footer).ToString();
    }

    public async Task<bool> DeleteOwnerAsync(int characterId, AppDbContext unitOfWork, Func<Task> commit)
    {
        await gate.WaitAsync();
        try
        {
            unitOfWork.SlaveMerchants.RemoveRange(
                await unitOfWork.SlaveMerchants.Where(s => s.OwnerCharacterId == characterId).ToListAsync());
            if (await unitOfWork.SlaveMerchantTimes.FindAsync(characterId) is { } time)
                unitOfWork.SlaveMerchantTimes.Remove(time);

            try
            {
                await commit();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not delete character {CharacterId} with its slave merchant", characterId);
                return false;
            }

            if (activeByOwner.TryGetValue(characterId, out var bot))
                await DespawnAsync(bot);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task TickAsync(DateTime utcNow)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var bot in activeByOwner.Values.ToList())
            {
                try
                {
                    await TickAsync(bot, utcNow);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Slave merchant {BotName} of {Owner} could not be charged", bot.Name, bot.OwnerName);
                }
            }

            try
            {
                await BeatAsync(utcNow);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The slave merchants' heartbeat could not be saved");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task BeatAsync(DateTime utcNow)
    {
        var ids = activeByOwner.Values.Select(bot => bot.SlaveDatabaseId).ToList();
        if (ids.Count == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var data in await db.SlaveMerchants.Where(s => ids.Contains(s.Id)).ToListAsync())
            data.HeartbeatAt = utcNow;
        await db.SaveChangesAsync();
    }

    private async Task TickAsync(SlaveMerchantBotSession bot, DateTime utcNow)
    {
        if (bot.Trade.MerchantItems.All(entry => entry == null || entry.IsEmpty))
        {
            await CloseAsync(bot, SlaveMerchantClosedReason.SoldOut);
            return;
        }

        if (utcNow < bot.PaidUntil)
            return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var time = await db.SlaveMerchantTimes.FindAsync(bot.OwnerCharacterId);
        if (time == null || time.Hours < 1)
        {
            await CloseAsync(bot, SlaveMerchantClosedReason.OutOfTime);
            return;
        }

        var paidUntil = bot.PaidUntil + PaidSpan;
        time.Hours--;
        if (await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId) is { } data)
            data.PaidUntil = paidUntil;
        await db.SaveChangesAsync();
        bot.PaidUntil = paidUntil;

        if (sessionManager.GetByCharacterId(bot.OwnerCharacterId) is { } owner)
            await owner.Client.SendPacket(SlaveMerchantPacketWriter.Time(time.Hours));
    }

    public async Task<int> LoadActiveAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var loaded = 0;
        foreach (var data in await db.SlaveMerchants.Where(s => s.IsActive).ToListAsync())
        {
            var paidLeft = data.PaidUntil - (data.HeartbeatAt == default ? now : data.HeartbeatAt);
            data.PaidUntil = now + (paidLeft < TimeSpan.Zero ? TimeSpan.Zero : paidLeft > PaidSpan ? PaidSpan : paidLeft);
            data.HeartbeatAt = now;

            var bot = NewBot(data.OwnerCharacterId, data.OwnerAccountId, data.OwnerName, data.PaidUntil);
            bot.SlaveDatabaseId = data.Id;
            bot.Name = data.BotName;
            bot.Nation = data.Nation;
            bot.Race = data.Race;
            bot.Class = data.Class;
            bot.Level = data.Level > 0 ? data.Level : BotSession.DefaultBotLevel;
            bot.Face = data.Face;
            bot.Hair = data.Hair;
            bot.ZoneId = data.ZoneId;
            bot.X = data.X;
            bot.Y = data.Y;
            bot.Z = data.Z;
            bot.Direction = data.Direction;
            bot.Money = (int)Math.Min(data.EarnedNoah, ExchangePacketConstants.CoinMax);
            bot.Trade.PremiumMerchant = data.MerchantType == MerchantType.PremiumSelling;
            bot.Trade.MerchantAdvert = data.StallTitle;

            WearVisuals(bot, ReadVisuals(data.EquipmentJson));
            var items = ReadItems(data.ItemsJson);
            for (var i = 0; i < items.Count && i < bot.Trade.MerchantItems.Length; i++)
                PutOnStall(bot, i, items[i]);

            await SpawnAsync(bot);
            loaded++;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Loaded {Count} slave merchants", loaded);
        return loaded;
    }

    private bool IsOpen(SlaveMerchantBotSession bot) =>
        activeByOwner.TryGetValue(bot.OwnerCharacterId, out var open) && ReferenceEquals(open, bot) && bot.Trade.IsMerchanting;

    private SlaveMerchantBotSession NewBot(int ownerId, int ownerAccountId, string ownerName, DateTime paidUntil)
    {
        var botId = Interlocked.Increment(ref nextBotCharacterId);
        var client = new BotClient { CharacterId = botId, AccountId = -botId };
        return new SlaveMerchantBotSession(client, botId, -botId)
        {
            OwnerCharacterId = ownerId,
            OwnerAccountId = ownerAccountId,
            OwnerName = ownerName,
            PaidUntil = paidUntil,
            Hp = BotSession.DefaultBotHp,
            MaxHp = BotSession.DefaultBotHp,
            Mp = BotSession.DefaultBotMp,
            MaxMp = BotSession.DefaultBotMp,
            IsSitting = true,
        };
    }

    private static void WearVisuals(SlaveMerchantBotSession bot, IEnumerable<StoredVisual> visuals)
    {
        foreach (var visual in visuals)
        {
            if (visual.Slot < 0 || visual.Slot >= bot.Inventory.Length)
                continue;
            var slot = bot.Inventory[visual.Slot];
            slot.ItemId = visual.ItemId;
            slot.Count = 1;
            slot.Durability = visual.Durability;
            slot.Flag = visual.Flag;
        }
    }

    private static void PutOnStall(SlaveMerchantBotSession bot, int stallSlot, StoredItem item)
    {
        var bagSlot = InventoryConstants.InventoryStart + stallSlot;
        var slot = bot.Inventory[bagSlot];
        slot.ItemId = item.ItemId;
        slot.Count = item.Count;
        slot.Durability = item.Durability;
        slot.Flag = item.Flag;
        slot.ExpiresAt = item.ExpiresAt;
        slot.UniqueId = item.UniqueId;

        bot.Trade.MerchantItems[stallSlot] = new MerchantItem
        {
            ItemId = item.ItemId,
            Count = item.Count,
            Price = item.Price,
            Durability = item.Durability,
            OriginalSlot = (byte)bagSlot,
        };
    }

    private static List<StoredItem> StallItems(SlaveMerchantBotSession bot)
    {
        var items = new List<StoredItem>();
        foreach (var entry in bot.Trade.MerchantItems)
        {
            if (entry == null || entry.IsEmpty)
                continue;
            var slot = bot.Inventory[entry.OriginalSlot];
            items.Add(new StoredItem(entry.ItemId, entry.Count, entry.Price,
                slot.Durability, slot.Flag, slot.ExpiresAt, slot.UniqueId));
        }

        return items;
    }

    private static List<SlaveMerchantLine> Lines(IEnumerable<StoredItem> items) =>
        items.Select(item => new SlaveMerchantLine(item.ItemId, item.Count, item.Price)).ToList();

    private async Task SpawnAsync(SlaveMerchantBotSession bot)
    {
        bot.Trade.MerchantState = MerchantMode.Selling;
        sessionManager.RegisterSession(bot);
        sessionManager.Regions.AddToRegion(bot);
        activeByOwner[bot.OwnerCharacterId] = bot;

        await sessionManager.Regions.SendToRegion(
            bot, MerchantBotService.BuildUserInOutPacket(bot, InOutType.In), excludeSender: true);
        await sessionManager.Regions.SendToRegion(bot, MerchantPacketWriter.StallInserted(
            MerchantPacketWriter.Succeeded, bot.Trade.MerchantAdvert, bot.CharacterId,
            MerchantLifecycleService.StallFlags(bot), MerchantLifecycleService.DisplayItemIds(bot)), excludeSender: true);
    }

    private async Task<bool> CloseAsync(SlaveMerchantBotSession bot, SlaveMerchantClosedReason? closing)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId) is { } data)
            {
                data.IsActive = false;
                data.FinishedAt = DateTime.UtcNow;
                data.EarnedNoah = bot.Money;
                data.ItemsJson = JsonSerializer.Serialize(StallItems(bot));
                if (closing.HasValue && sessionManager.GetByCharacterId(bot.OwnerCharacterId) == null)
                    data.AwayReportJson = WriteReport(ReadReport(data.AwayReportJson) with { Closed = closing });
                await db.SaveChangesAsync();
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slave merchant {BotName} of {Owner} could not be closed", bot.Name, bot.OwnerName);
            return false;
        }

        await DespawnAsync(bot);
        if (closing.HasValue && sessionManager.GetByCharacterId(bot.OwnerCharacterId) is { } owner)
            await owner.Client.SendPacket(SlaveMerchantPacketWriter.Closed(closing.Value));

        logger.LogInformation("Slave merchant {BotName} of {Owner} closed ({Closing})", bot.Name, bot.OwnerName, closing);
        return true;
    }

    private async Task DespawnAsync(SlaveMerchantBotSession bot)
    {
        activeByOwner.TryRemove(new KeyValuePair<int, SlaveMerchantBotSession>(bot.OwnerCharacterId, bot));
        bot.Trade.MerchantState = MerchantMode.None;
        await sessionManager.Regions.SendToRegion(bot, MerchantPacketWriter.StallClosed(bot.CharacterId), excludeSender: true);
        await sessionManager.Regions.SendToRegion(bot, VisibilityPacketWriter.UserOut(bot.CharacterId), excludeSender: true);
        sessionManager.RemoveSession(bot);
    }

    private async Task<bool> HoldsAnythingAsync(int characterId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SlaveMerchants.AnyAsync(s => s.OwnerCharacterId == characterId);
    }

    private async Task<int> HoursAsync(int characterId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.SlaveMerchantTimes.AsNoTracking().FirstOrDefaultAsync(t => t.CharacterId == characterId))?.Hours ?? 0;
    }

    private static async Task<int> AddHoursAsync(AppDbContext db, int characterId, int hours)
    {
        var time = await db.SlaveMerchantTimes.FindAsync(characterId);
        if (time == null)
        {
            time = new SlaveMerchantTimeData { CharacterId = characterId };
            db.SlaveMerchantTimes.Add(time);
        }

        time.Hours = (int)Math.Clamp((long)time.Hours + hours, 0L, MaxHours);
        return time.Hours;
    }

    private static AwayReport ReadReport(string json) =>
        string.IsNullOrEmpty(json) ? new AwayReport([], null) : JsonSerializer.Deserialize<AwayReport>(json) ?? new AwayReport([], null);

    private static string WriteReport(AwayReport report) =>
        report.Sales.Count == 0 && report.Closed == null ? string.Empty : JsonSerializer.Serialize(report);

    private static List<StoredItem> ReadItems(string json) =>
        string.IsNullOrEmpty(json) ? new List<StoredItem>() : JsonSerializer.Deserialize<List<StoredItem>>(json) ?? new List<StoredItem>();

    private static List<StoredVisual> ReadVisuals(string json) =>
        string.IsNullOrEmpty(json) ? new List<StoredVisual>() : JsonSerializer.Deserialize<List<StoredVisual>>(json) ?? new List<StoredVisual>();

    private sealed record StoredItem(int ItemId, ushort Count, int Price, short Durability, byte Flag, long ExpiresAt, int UniqueId);

    private sealed record StoredVisual(int Slot, int ItemId, short Durability, byte Flag);

    private sealed record AwaySale(int ItemId, int Count, long Coins);

    private sealed record AwayReport(List<AwaySale> Sales, SlaveMerchantClosedReason? Closed)
    {
        public AwayReport WithSale(int itemId, int count, long coins)
        {
            var earlier = Sales.FirstOrDefault(sale => sale.ItemId == itemId);
            var sales = Sales.Where(sale => sale.ItemId != itemId).ToList();
            sales.Add(new AwaySale(itemId, count + (earlier?.Count ?? 0), coins + (earlier?.Coins ?? 0)));
            return this with { Sales = sales };
        }
    }

    private sealed class CollectTally
    {
        public long Paid { get; set; }
        public long CoinsKept { get; set; }
        public int Returned { get; set; }
        public int Kept { get; set; }
    }
}
