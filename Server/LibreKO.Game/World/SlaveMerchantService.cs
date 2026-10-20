using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

public class SlaveMerchantService : ISlaveMerchantService, IDisposable
{
    private readonly SessionManager sessionManager;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IGameDataService gameDataService;
    private readonly IUserNotificationService userNotificationService;
    private readonly ILogger<SlaveMerchantService> logger;

    private readonly ConcurrentDictionary<int, SlaveMerchantBotSession> activeSlavesById = new();
    private readonly ConcurrentDictionary<int, SlaveMerchantBotSession> activeSlavesByOwner = new();
    private readonly CancellationTokenSource cts = new();
    private const int BotCharacterIdBase = 850_000;
    private int nextSlaveBotCharacterId = BotCharacterIdBase;

    public const int VoucherOfflineMerchant = 700111000;
    public const int ItemOfflineMerchant = 924041913;
    public const int ItemSlaveScroll = 751730000;

    public IReadOnlyCollection<SlaveMerchantBotSession> ActiveSlaves => activeSlavesById.Values.ToList();

    public SlaveMerchantService(
        SessionManager sessionManager,
        IServiceScopeFactory scopeFactory,
        IGameDataService gameDataService,
        IUserNotificationService userNotificationService,
        ILogger<SlaveMerchantService> logger)
    {
        this.sessionManager = sessionManager;
        this.scopeFactory = scopeFactory;
        this.gameDataService = gameDataService;
        this.userNotificationService = userNotificationService;
        this.logger = logger;

        // Background loop to check for 1-hour duration expiration
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, cts.Token);
                    var now = DateTime.UtcNow;
                    foreach (var bot in activeSlavesById.Values)
                    {
                        if (now >= bot.ExpiresAt)
                        {
                            await ExpireSlaveAsync(bot);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Slave Merchant] Error in expiration check loop");
                }
            }
        });
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }

    public SlaveMerchantBotSession? GetActiveSlaveByOwner(int ownerCharacterId) =>
        activeSlavesByOwner.TryGetValue(ownerCharacterId, out var bot) ? bot : null;

    public async Task<SlaveMerchantBotSession?> CreateSlaveMerchantAsync(UserSession session, string advertMessage)
    {
        if (activeSlavesByOwner.ContainsKey(session.CharacterId))
        {
            await SendNoticeAsync(session, "[Slave Merchant] You already have an active Slave Merchant. Close it first using +slaveclose.");
            return null;
        }

        var stagedCount = session.Trade.MerchantItems.Count(item => item is { IsEmpty: false });
        if (stagedCount == 0)
        {
            await SendNoticeAsync(session, "[Slave Merchant] No items staged for merchant sale.");
            return null;
        }

        // Verify player has Voucher of Offline Merchant (700111000)
        byte voucherSlot = 0;
        bool hasVoucher = false;
        for (byte slot = (byte)InventoryConstants.InventoryStart; slot < (byte)InventoryConstants.CospreStart; slot++)
        {
            var invItem = session.Inventory[slot];
            if (invItem != null && !invItem.IsEmpty &&
                (invItem.ItemId == VoucherOfflineMerchant || invItem.ItemId == ItemOfflineMerchant || invItem.ItemId == ItemSlaveScroll) &&
                invItem.Count > 0)
            {
                voucherSlot = slot;
                hasVoucher = true;
                break;
            }
        }

        if (!hasVoucher)
        {
            await SendNoticeAsync(session, "[Slave Merchant] You need 1x Voucher of Offline Merchant to open a Slave Merchant.");
            return null;
        }

        var botId = Interlocked.Increment(ref nextSlaveBotCharacterId);
        var botName = session.Name.Length <= 18 ? $"[S]{session.Name}" : $"[S]{session.Name[..18]}";

        var client = new BotClient
        {
            CharacterId = botId,
            AccountId = -botId,
        };

        var bot = new SlaveMerchantBotSession(client, botId, -botId)
        {
            OwnerCharacterId = session.CharacterId,
            OwnerAccountId = session.AccountId,
            OwnerName = session.Name,
            Name = botName,
            Nation = session.Nation,
            Race = session.Race,
            Class = session.Class,
            Level = session.Level > 0 ? session.Level : BotSession.DefaultBotLevel,
            Face = session.Face,
            Hair = session.Hair,
            ZoneId = session.ZoneId,
            X = session.X,
            Y = session.Y,
            Z = session.Z,
            Direction = session.Direction,
            Room = session.Room,
            Hp = BotSession.DefaultBotHp,
            MaxHp = BotSession.DefaultBotHp,
            Mp = BotSession.DefaultBotMp,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        };

        // Copy equipment visuals
        var visualsList = new List<StoredVisual>();
        for (byte slot = 0; slot < InventoryConstants.VisualSlots.Length; slot++)
        {
            var equipped = session.GetEquippedItem(slot);
            if (!equipped.IsEmpty)
            {
                bot.Inventory[slot].ItemId = equipped.ItemId;
                bot.Inventory[slot].Count = equipped.Count;
                bot.Inventory[slot].Durability = equipped.Durability;
                bot.Inventory[slot].Flag = equipped.Flag;

                visualsList.Add(new StoredVisual
                {
                    Slot = slot,
                    ItemId = equipped.ItemId,
                    Durability = equipped.Durability,
                    Flag = equipped.Flag,
                });
            }
        }

        // Deduct staged items from player inventory and transfer to bot
        var itemsList = new List<StoredItem>();
        for (byte i = 0; i < session.Trade.MerchantItems.Length; i++)
        {
            var src = session.Trade.MerchantItems[i];
            if (src != null && !src.IsEmpty)
            {
                var bagSlot = InventoryConstants.SlotMax + i;
                bot.Inventory[bagSlot].ItemId = src.ItemId;
                bot.Inventory[bagSlot].Count = src.Count;
                bot.Inventory[bagSlot].Durability = src.Durability;

                bot.Trade.MerchantItems[i] = new MerchantItem
                {
                    ItemId = src.ItemId,
                    Count = src.Count,
                    Price = src.Price,
                    Durability = src.Durability,
                    OriginalSlot = (byte)bagSlot,
                };

                itemsList.Add(new StoredItem
                {
                    Slot = i,
                    ItemId = src.ItemId,
                    Count = (ushort)src.Count,
                    Price = src.Price,
                    Durability = src.Durability,
                });

                // Deduct from owner inventory
                var ownerSlot = session.Inventory[src.OriginalSlot];
                if (ownerSlot.ItemId == src.ItemId)
                {
                    ownerSlot.Count -= Math.Min(src.Count, ownerSlot.Count);
                    var isEmpty = ownerSlot.Count <= 0;
                    if (isEmpty) ownerSlot.Clear();
                    await userNotificationService.SendStackChangeAsync(
                        session, src.OriginalSlot, ownerSlot.ItemId, ownerSlot.Count, ownerSlot.Durability, isEmpty);
                }
            }
        }

        // Deduct 1 voucher item from player inventory
        var voucherItem = session.Inventory[voucherSlot];
        voucherItem.Count--;
        var isVoucherEmpty = voucherItem.Count <= 0;
        if (isVoucherEmpty) voucherItem.Clear();
        await userNotificationService.SendStackChangeAsync(
            session, voucherSlot, voucherItem.ItemId, voucherItem.Count, voucherItem.Durability, isVoucherEmpty);

        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);

        bot.Trade.MerchantState = MerchantMode.Selling;
        bot.Trade.PremiumMerchant = session.Trade.PremiumMerchant;
        bot.Trade.MerchantAdvert = string.IsNullOrWhiteSpace(advertMessage) ? $"{session.Name}'s Store" : advertMessage;
        bot.IsSitting = true;

        // Persist to database
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await EnsureTableCreatedAsync(db);
            var data = new SlaveMerchantData
            {
                OwnerCharacterId = session.CharacterId,
                OwnerAccountId = session.AccountId,
                OwnerName = session.Name,
                BotName = bot.Name,
                Nation = session.Nation,
                Race = session.Race,
                Class = session.Class,
                Face = session.Face,
                Hair = (byte)session.Hair,
                Level = session.Level,
                ZoneId = session.ZoneId,
                X = session.X,
                Y = session.Y,
                Z = session.Z,
                Direction = session.Direction,
                MerchantType = bot.Trade.PremiumMerchant ? MerchantType.PremiumSelling : MerchantType.Selling,
                StallTitle = bot.Trade.MerchantAdvert,
                ItemsJson = JsonSerializer.Serialize(itemsList),
                EquipmentJson = JsonSerializer.Serialize(visualsList),
                EarnedNoah = 0,
                IsActive = true,
                CreatedAt = bot.CreatedAt,
                ExpiresAt = bot.ExpiresAt,
            };

            db.SlaveMerchants.Add(data);
            await db.SaveChangesAsync();
            bot.SlaveDatabaseId = data.Id;
        }

        // Register session & region
        sessionManager.RegisterSession(bot);
        sessionManager.Regions.AddToRegion(bot);
        activeSlavesById[bot.CharacterId] = bot;
        activeSlavesByOwner[session.CharacterId] = bot;

        // Broadcast UserInOut to surrounding players
        var inPacket = BuildUserInOutPacket(bot, InOutType.In);
        await sessionManager.Regions.SendToRegion(bot, inPacket, excludeSender: true);

        // Broadcast StallInserted
        var displayItemIds = bot.Trade.MerchantItems
            .Where(item => item is { IsEmpty: false })
            .Select(item => item.ItemId)
            .ToArray();

        var stallPacket = MerchantPacketWriter.StallInserted(
            MerchantPacketWriter.Succeeded,
            bot.Trade.MerchantAdvert,
            bot.CharacterId,
            (byte)(bot.Trade.PremiumMerchant ? 1 : 0),
            displayItemIds);

        await sessionManager.Regions.SendToRegion(bot, stallPacket, excludeSender: true);

        logger.LogInformation("Slave merchant '{BotName}' (ID {BotId}) spawned for owner '{OwnerName}' for 1 hour at ({X:F1}, {Z:F1})",
            bot.Name, bot.CharacterId, session.Name, bot.X, bot.Z);

        await SendNoticeAsync(session,
            $"[Slave Merchant] Slave merchant '{bot.Name}' deployed for 1 hour! You are now free to explore, change zones, or log out.");

        return bot;
    }

    public async Task RecordSaleAsync(SlaveMerchantBotSession bot, byte slot, int itemId, int count, long totalCost, string buyerName)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var data = await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId);
            if (data != null)
            {
                data.EarnedNoah += totalCost;

                // Update items in DB
                var remainingItems = new List<StoredItem>();
                for (byte i = 0; i < bot.Trade.MerchantItems.Length; i++)
                {
                    var it = bot.Trade.MerchantItems[i];
                    if (it != null && !it.IsEmpty)
                    {
                        remainingItems.Add(new StoredItem
                        {
                            Slot = i,
                            ItemId = it.ItemId,
                            Count = (ushort)it.Count,
                            Price = it.Price,
                            Durability = it.Durability,
                        });
                    }
                }
                data.ItemsJson = JsonSerializer.Serialize(remainingItems);

                var allSoldOut = remainingItems.Count == 0;
                if (allSoldOut)
                {
                    data.IsActive = false;
                    data.FinishedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
            }
        }

        var itemData = gameDataService.GetItem(itemId);
        var itemName = itemData?.Name ?? $"Item #{itemId}";

        // Notify owner if online
        var owner = sessionManager.GetByCharacterId(bot.OwnerCharacterId);
        if (owner != null)
        {
            await SendNoticeAsync(owner,
                $"[Slave Merchant] {buyerName} purchased {itemName} x{count} for {totalCost:N0} Noah!");
        }

        // Check if all items are sold out
        if (bot.Trade.MerchantItems.All(entry => entry == null || entry.IsEmpty))
        {
            var closePacket = MerchantPacketWriter.StallClosed(bot.CharacterId);
            await sessionManager.Regions.SendToRegion(bot, closePacket, excludeSender: true);

            var outPacket = VisibilityPacketWriter.UserOut(bot.CharacterId);
            await sessionManager.Regions.SendToRegion(bot, outPacket, excludeSender: true);

            sessionManager.RemoveSession(bot);
            activeSlavesById.TryRemove(bot.CharacterId, out _);
            activeSlavesByOwner.TryRemove(bot.OwnerCharacterId, out _);

            if (owner != null)
            {
                await SendNoticeAsync(owner,
                    "[Slave Merchant] All your stall items have been sold! Use +slaveclaim to collect your earnings.");
            }

            logger.LogInformation("Slave merchant '{BotName}' finished selling all items and closed", bot.Name);
        }
    }

    public async Task<long> ClaimEarningsAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var records = await db.SlaveMerchants
            .Where(s => s.OwnerCharacterId == session.CharacterId && s.EarnedNoah > 0)
            .ToListAsync();

        if (records.Count == 0)
        {
            await SendNoticeAsync(session, "[Slave Merchant] No sales earnings available to claim.");
            return 0;
        }

        long totalNoah = records.Sum(s => s.EarnedNoah);
        foreach (var r in records)
        {
            r.EarnedNoah = 0;
        }

        await db.SaveChangesAsync();

        session.Money += (int)totalNoah;
        await userNotificationService.SendGoldGainAsync(session, (int)totalNoah);
        await SendNoticeAsync(session, $"[Slave Merchant] Successfully claimed {totalNoah:N0} Noah!");

        return totalNoah;
    }

    public async Task<bool> CloseSlaveAsync(UserSession session)
    {
        if (!activeSlavesByOwner.TryGetValue(session.CharacterId, out var bot))
        {
            // Also check DB for unclosed records just in case
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var activeRecord = await db.SlaveMerchants
                .FirstOrDefaultAsync(s => s.OwnerCharacterId == session.CharacterId && s.IsActive);
            if (activeRecord != null)
            {
                activeRecord.IsActive = false;
                activeRecord.FinishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            await SendNoticeAsync(session, "[Slave Merchant] You do not have an active Slave Merchant.");
            return false;
        }

        // Close stall and remove bot
        var closePacket = MerchantPacketWriter.StallClosed(bot.CharacterId);
        await sessionManager.Regions.SendToRegion(bot, closePacket, excludeSender: true);

        var outPacket = VisibilityPacketWriter.UserOut(bot.CharacterId);
        await sessionManager.Regions.SendToRegion(bot, outPacket, excludeSender: true);

        sessionManager.RemoveSession(bot);
        activeSlavesById.TryRemove(bot.CharacterId, out _);
        activeSlavesByOwner.TryRemove(bot.OwnerCharacterId, out _);

        // Return unsold items to owner inventory
        var returnedCount = 0;
        for (int i = 0; i < bot.Trade.MerchantItems.Length; i++)
        {
            var it = bot.Trade.MerchantItems[i];
            if (it != null && !it.IsEmpty)
            {
                var destination = session.FindSlotForItem(it.ItemId, gameDataService, (ushort)it.Count);
                if (destination >= 0)
                {
                    var destSlot = session.Inventory[destination];
                    var isNew = destSlot.IsEmpty;
                    if (isNew)
                    {
                        destSlot.ItemId = it.ItemId;
                        destSlot.Count = (ushort)it.Count;
                        destSlot.Durability = it.Durability;
                    }
                    else
                    {
                        destSlot.Count += (ushort)it.Count;
                    }

                    await userNotificationService.SendStackChangeAsync(
                        session, (byte)destination, destSlot.ItemId, destSlot.Count, destSlot.Durability, isNew);
                    returnedCount++;
                }
                else
                {
                    logger.LogWarning("Owner inventory full when returning slave merchant item {ItemId} x{Count}",
                        it.ItemId, it.Count);
                }
            }
        }

        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);

        // Mark DB record inactive and claim any earned noah
        long claimedNoah = 0;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var data = await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId);
            if (data != null)
            {
                data.IsActive = false;
                data.FinishedAt = DateTime.UtcNow;
                data.ItemsJson = "[]";
                claimedNoah = data.EarnedNoah;
                data.EarnedNoah = 0;
                await db.SaveChangesAsync();
            }
        }

        if (claimedNoah > 0)
        {
            session.Money += (int)claimedNoah;
            await userNotificationService.SendGoldGainAsync(session, (int)claimedNoah);
        }

        // Notify owner client directly that stall is closed and empty
        await session.Client.SendPacket(closePacket);
        var emptyStallOnClose = new List<MerchantPacketWriter.StallItem?>(MerchantPacketConstants.StallSlots);
        for (var i = 0; i < MerchantPacketConstants.StallSlots; i++) emptyStallOnClose.Add(null);
        await session.Client.SendPacket(MerchantPacketWriter.StallContents(MerchantSubOpcode.ItemList, 0, emptyStallOnClose));

        await SendNoticeAsync(session,
            $"[Slave Merchant] Your Slave Merchant has been closed. {returnedCount} item(s) and {claimedNoah:N0} Noah returned.");

        return true;
    }

    public async Task<string> GetStatusAsync(UserSession session)
    {
        if (activeSlavesByOwner.TryGetValue(session.CharacterId, out var bot))
        {
            var remainingItems = bot.Trade.MerchantItems.Count(it => it != null && !it.IsEmpty);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var data = await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId);
            var earned = data?.EarnedNoah ?? 0;

            var remaining = bot.ExpiresAt - DateTime.UtcNow;
            var remainingMinutes = Math.Max(0, (int)remaining.TotalMinutes);

            return $"[Slave Merchant] Status: ACTIVE in Zone {bot.ZoneId} ({bot.X / 10:F0}, {bot.Z / 10:F0}). Time left: {remainingMinutes}m. Earnings: {earned:N0} Noah. Items: {remainingItems}. Use +slaveclose to close, +slaveclaim to claim.";
        }

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pendingNoah = await db.SlaveMerchants
                .Where(s => s.OwnerCharacterId == session.CharacterId && s.EarnedNoah > 0)
                .SumAsync(s => s.EarnedNoah);

            if (pendingNoah > 0)
            {
                return $"[Slave Merchant] No active bot, but you have {pendingNoah:N0} unclaimed Noah! Type +slaveclaim to collect.";
            }
        }

        return "[Slave Merchant] No active bot. Deploy a Slave Merchant from the merchant menu using Voucher of Offline Merchant.";
    }

    public async Task SendSlaveStatusAsync(UserSession session)
    {
        if (activeSlavesByOwner.TryGetValue(session.CharacterId, out var bot))
        {
            var remainingItems = bot.Trade.MerchantItems.Count(it => it != null && !it.IsEmpty);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var data = await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId);
            var earned = data?.EarnedNoah ?? 0;

            var remaining = bot.ExpiresAt - DateTime.UtcNow;
            var remainingMinutes = Math.Max(0, (int)remaining.TotalMinutes);

            var notice = $"[Slave Merchant] Status: ACTIVE in Zone {bot.ZoneId} ({bot.X / 10:F0}, {bot.Z / 10:F0}). Time left: {remainingMinutes}m. Earnings: {earned:N0} Noah. Items: {remainingItems}. Use +slaveclose to close, +slaveclaim to claim.";
            await SendNoticeAsync(session, notice);

            // Send StallContents packet so client opens and populates the merchant stall UI bar!
            var stall = new List<MerchantPacketWriter.StallItem?>(MerchantPacketConstants.StallSlots);
            for (var i = 0; i < bot.Trade.MerchantItems.Length; i++)
            {
                var item = bot.Trade.MerchantItems[i];
                stall.Add(item != null && !item.IsEmpty
                    ? new MerchantPacketWriter.StallItem(item.ItemId, item.Count, item.Durability, item.Price)
                    : null);
            }

            await session.Client.SendPacket(MerchantPacketWriter.StallContents(
                MerchantSubOpcode.ItemList, bot.CharacterId, stall));
            return;
        }

        // When no active bot, clear the client stall display by sending empty stall contents
        var emptyStall = new List<MerchantPacketWriter.StallItem?>(MerchantPacketConstants.StallSlots);
        for (var i = 0; i < MerchantPacketConstants.StallSlots; i++) emptyStall.Add(null);
        await session.Client.SendPacket(MerchantPacketWriter.StallContents(MerchantSubOpcode.ItemList, 0, emptyStall));

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pendingNoah = await db.SlaveMerchants
                .Where(s => s.OwnerCharacterId == session.CharacterId && s.EarnedNoah > 0)
                .SumAsync(s => s.EarnedNoah);

            if (pendingNoah > 0)
            {
                await SendNoticeAsync(session, $"[Slave Merchant] No active bot, but you have {pendingNoah:N0} unclaimed Noah! Type +slaveclaim to collect.");
                return;
            }
        }

        await SendNoticeAsync(session, "[Slave Merchant] No active bot. Deploy a Slave Merchant from the merchant menu using Voucher of Offline Merchant.");
    }

    private async Task ExpireSlaveAsync(SlaveMerchantBotSession bot)
    {
        try
        {
            var closePacket = MerchantPacketWriter.StallClosed(bot.CharacterId);
            await sessionManager.Regions.SendToRegion(bot, closePacket, excludeSender: true);

            var outPacket = VisibilityPacketWriter.UserOut(bot.CharacterId);
            await sessionManager.Regions.SendToRegion(bot, outPacket, excludeSender: true);

            sessionManager.RemoveSession(bot);
            activeSlavesById.TryRemove(bot.CharacterId, out _);
            activeSlavesByOwner.TryRemove(bot.OwnerCharacterId, out _);

            var owner = sessionManager.GetByCharacterId(bot.OwnerCharacterId);
            var returnedCount = 0;
            if (owner != null)
            {
                for (int i = 0; i < bot.Trade.MerchantItems.Length; i++)
                {
                    var it = bot.Trade.MerchantItems[i];
                    if (it != null && !it.IsEmpty)
                    {
                        var destination = owner.FindSlotForItem(it.ItemId, gameDataService, (ushort)it.Count);
                        if (destination >= 0)
                        {
                            var destSlot = owner.Inventory[destination];
                            var isNew = destSlot.IsEmpty;
                            if (isNew)
                            {
                                destSlot.ItemId = it.ItemId;
                                destSlot.Count = (ushort)it.Count;
                                destSlot.Durability = it.Durability;
                            }
                            else
                            {
                                destSlot.Count += (ushort)it.Count;
                            }

                            await userNotificationService.SendStackChangeAsync(
                                owner, (byte)destination, destSlot.ItemId, destSlot.Count, destSlot.Durability, isNew);
                            returnedCount++;
                        }
                    }
                }

                owner.RecalculateStatsWithBuffs(gameDataService);
                await userNotificationService.SendWeightChangeAsync(owner);

                await owner.Client.SendPacket(closePacket);
                var emptyStallOnExpire = new List<MerchantPacketWriter.StallItem?>(MerchantPacketConstants.StallSlots);
                for (var i = 0; i < MerchantPacketConstants.StallSlots; i++) emptyStallOnExpire.Add(null);
                await owner.Client.SendPacket(MerchantPacketWriter.StallContents(MerchantSubOpcode.ItemList, 0, emptyStallOnExpire));
            }

            long claimedNoah = 0;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var data = await db.SlaveMerchants.FindAsync(bot.SlaveDatabaseId);
                if (data != null)
                {
                    data.IsActive = false;
                    data.FinishedAt = DateTime.UtcNow;
                    if (owner != null)
                    {
                        claimedNoah = data.EarnedNoah;
                        data.EarnedNoah = 0;
                        data.ItemsJson = "[]";
                    }
                    await db.SaveChangesAsync();
                }
            }

            if (owner != null)
            {
                if (claimedNoah > 0)
                {
                    owner.Money += (int)claimedNoah;
                    await userNotificationService.SendGoldGainAsync(owner, (int)claimedNoah);
                }

                await SendNoticeAsync(owner,
                    $"[Slave Merchant] Your 1-hour Slave Merchant duration has expired. Stall closed, {returnedCount} item(s) and {claimedNoah:N0} Noah returned.");
            }

            logger.LogInformation("Slave merchant '{BotName}' duration expired and closed cleanly.", bot.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while expiring slave merchant '{BotName}'", bot.Name);
        }
    }

    private async Task EnsureTableCreatedAsync(AppDbContext db)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS `SlaveMerchants` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `OwnerCharacterId` int NOT NULL,
  `OwnerAccountId` int NOT NULL,
  `OwnerName` varchar(32) NOT NULL,
  `BotName` varchar(32) NOT NULL,
  `Nation` tinyint NOT NULL,
  `Race` tinyint unsigned NOT NULL,
  `Class` smallint NOT NULL,
  `Face` tinyint unsigned NOT NULL,
  `Hair` tinyint unsigned NOT NULL,
  `Level` tinyint unsigned NOT NULL,
  `ZoneId` tinyint unsigned NOT NULL,
  `X` float NOT NULL,
  `Y` float NOT NULL,
  `Z` float NOT NULL,
  `Direction` smallint NOT NULL,
  `MerchantType` int NOT NULL,
  `StallTitle` varchar(128) NOT NULL,
  `ItemsJson` varchar(4000) NOT NULL,
  `EquipmentJson` varchar(2000) NOT NULL,
  `EarnedNoah` bigint NOT NULL DEFAULT 0,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `CreatedAt` datetime(6) NOT NULL,
  `FinishedAt` datetime(6) DEFAULT NULL,
  `ExpiresAt` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_SlaveMerchants_IsActive` (`IsActive`),
  KEY `IX_SlaveMerchants_OwnerCharacterId` (`OwnerCharacterId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Slave Merchant] EnsureTableCreatedAsync: {Message}", ex.Message);
        }
    }

    public async Task<int> LoadActiveSlaveMerchantsAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await EnsureTableCreatedAsync(db);

            var list = await db.SlaveMerchants.Where(b => b.IsActive).ToListAsync();
            var loaded = 0;

            foreach (var data in list)
            {
                var expiresAt = data.ExpiresAt ?? data.CreatedAt.AddHours(1);
                if (DateTime.UtcNow >= expiresAt)
                {
                    data.IsActive = false;
                    data.FinishedAt = DateTime.UtcNow;
                    continue;
                }

                var botId = Interlocked.Increment(ref nextSlaveBotCharacterId);
                var client = new BotClient
                {
                    CharacterId = botId,
                    AccountId = -botId,
                };

                var bot = new SlaveMerchantBotSession(client, botId, -botId)
                {
                    OwnerCharacterId = data.OwnerCharacterId,
                    OwnerAccountId = data.OwnerAccountId,
                    OwnerName = data.OwnerName,
                    SlaveDatabaseId = data.Id,
                    Name = data.BotName,
                    Nation = data.Nation,
                    Race = data.Race,
                    Class = data.Class,
                    Level = data.Level > 0 ? data.Level : BotSession.DefaultBotLevel,
                    Face = data.Face,
                    Hair = data.Hair,
                    ZoneId = data.ZoneId,
                    X = data.X,
                    Y = data.Y,
                    Z = data.Z,
                    Direction = data.Direction,
                    Hp = BotSession.DefaultBotHp,
                    MaxHp = BotSession.DefaultBotHp,
                    Mp = BotSession.DefaultBotMp,
                    CreatedAt = data.CreatedAt,
                    ExpiresAt = expiresAt,
                };

                // Restore equipment
                if (!string.IsNullOrEmpty(data.EquipmentJson))
                {
                    try
                    {
                        var visuals = JsonSerializer.Deserialize<List<StoredVisual>>(data.EquipmentJson);
                        if (visuals != null)
                        {
                            foreach (var v in visuals)
                            {
                                if (v.Slot < bot.Inventory.Length)
                                {
                                    bot.Inventory[v.Slot].ItemId = v.ItemId;
                                    bot.Inventory[v.Slot].Durability = v.Durability;
                                    bot.Inventory[v.Slot].Flag = v.Flag;
                                    bot.Inventory[v.Slot].Count = 1;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize slave bot equipment");
                    }
                }

                // Restore merchant items
                if (!string.IsNullOrEmpty(data.ItemsJson))
                {
                    try
                    {
                        var items = JsonSerializer.Deserialize<List<StoredItem>>(data.ItemsJson);
                        if (items != null)
                        {
                            foreach (var it in items)
                            {
                                if (it.Slot < bot.Trade.MerchantItems.Length)
                                {
                                    var bagSlot = InventoryConstants.SlotMax + it.Slot;
                                    bot.Inventory[bagSlot].ItemId = it.ItemId;
                                    bot.Inventory[bagSlot].Count = it.Count;
                                    bot.Inventory[bagSlot].Durability = it.Durability;

                                    bot.Trade.MerchantItems[it.Slot] = new MerchantItem
                                    {
                                        ItemId = it.ItemId,
                                        Count = it.Count,
                                        Price = it.Price,
                                        Durability = it.Durability,
                                        OriginalSlot = (byte)bagSlot,
                                    };
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to deserialize slave bot merchant items");
                    }
                }

                bot.Trade.MerchantState = MerchantMode.Selling;
                bot.Trade.MerchantAdvert = data.StallTitle;
                bot.Trade.PremiumMerchant = data.MerchantType == MerchantType.PremiumSelling;
                bot.IsSitting = true;

                sessionManager.RegisterSession(bot);
                sessionManager.Regions.AddToRegion(bot);
                activeSlavesById[bot.CharacterId] = bot;
                activeSlavesByOwner[data.OwnerCharacterId] = bot;

                var inPacket = BuildUserInOutPacket(bot, InOutType.In);
                await sessionManager.Regions.SendToRegion(bot, inPacket, excludeSender: true);

                var displayItemIds = bot.Trade.MerchantItems
                    .Where(item => item is { IsEmpty: false })
                    .Select(item => item.ItemId)
                    .ToArray();

                var stallPacket = MerchantPacketWriter.StallInserted(
                    MerchantPacketWriter.Succeeded,
                    bot.Trade.MerchantAdvert,
                    bot.CharacterId,
                    (byte)(bot.Trade.PremiumMerchant ? 1 : 0),
                    displayItemIds);

                await sessionManager.Regions.SendToRegion(bot, stallPacket, excludeSender: true);
                loaded++;
            }

            await db.SaveChangesAsync();

            logger.LogInformation("Loaded and spawned {Count} active slave merchant bots from database", loaded);
            return loaded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Slave Merchant] Failed to load active slave merchants from database.");
            return 0;
        }
    }

    private static Task SendNoticeAsync(UserSession session, string message) =>
        session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, message));

    private static Packet BuildUserInOutPacket(UserSession session, InOutType type) =>
        UserInfoPacketWriter.InOut(
            (byte)type,
            session.CharacterId,
            type == InOutType.Out ? null : UserStateOf(session));

    private static UserInfoPacketWriter.UserState UserStateOf(UserSession session)
    {
        var visuals = new List<UserInfoPacketWriter.VisualItem>(InventoryConstants.VisualSlotCount);
        foreach (var slot in InventoryConstants.VisualSlots)
        {
            var item = session.GetEquippedItem(slot);
            visuals.Add(new UserInfoPacketWriter.VisualItem(item.ItemId, item.Durability, item.Flag));
        }

        return new UserInfoPacketWriter.UserState(
            session.Name,
            (byte)session.Nation,
            session.KnightsId,
            session.KnightsFame,
            null,
            0,
            session.Level,
            session.Race,
            session.Class,
            session.GetPosX,
            session.GetPosZ,
            session.GetPosY,
            session.Face,
            session.Hair,
            (byte)UserPoseState.Sitting,
            NeedParty: false,
            session.IsGM,
            session.IsPartyLeader,
            (byte)session.Invisibility,
            session.Direction,
            session.ZoneId,
            session.IsHidingHelmet,
            session.DisplayTitleId,
            visuals);
    }

    private class StoredItem
    {
        public byte Slot { get; set; }
        public int ItemId { get; set; }
        public ushort Count { get; set; }
        public int Price { get; set; }
        public short Durability { get; set; }
    }

    private class StoredVisual
    {
        public byte Slot { get; set; }
        public int ItemId { get; set; }
        public short Durability { get; set; }
        public byte Flag { get; set; }
    }
}
