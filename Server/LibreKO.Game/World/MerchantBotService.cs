using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class MerchantBotService : IMerchantBotService
{
    private readonly SessionManager _sessionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MerchantBotService> _logger;

    private readonly ConcurrentDictionary<int, BotSession> _activeBots = new();
    private const int BotCharacterIdBase = 800_000;
    private int _nextBotCharacterId = BotCharacterIdBase;

    public IReadOnlyCollection<BotSession> ActiveBots => _activeBots.Values.ToList();

    public MerchantBotService(
        SessionManager sessionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<MerchantBotService> logger)
    {
        _sessionManager = sessionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<BotSession?> CloneFromGmAsync(UserSession gmSession, string advertMessage)
    {
        var botId = System.Threading.Interlocked.Increment(ref _nextBotCharacterId);
        var botName = await AcquireBotNameAsync(gmSession.Nation, gmSession.Name, botId);

        var bot = BotSession.Create(
            botId,
            botName,
            gmSession.Nation,
            gmSession.Race,
            gmSession.Class,
            gmSession.Level,
            gmSession.Face,
            gmSession.Hair,
            gmSession.ZoneId,
            gmSession.X,
            gmSession.Y,
            gmSession.Z,
            gmSession.Direction);

        bot.Room = gmSession.Room;

        // Copy equipment visuals
        for (int slot = 0; slot < InventoryConstants.VisualSlots.Length; slot++)
        {
            var equipped = gmSession.GetEquippedItem(slot);
            if (!equipped.IsEmpty)
            {
                bot.Inventory[slot].ItemId = equipped.ItemId;
                bot.Inventory[slot].Count = equipped.Count;
                bot.Inventory[slot].Durability = equipped.Durability;
                bot.Inventory[slot].Flag = equipped.Flag;
            }
        }

        // Copy merchant items
        var hasItems = false;
        for (int i = 0; i < gmSession.Trade.MerchantItems.Length; i++)
        {
            var src = gmSession.Trade.MerchantItems[i];
            if (src != null && !src.IsEmpty)
            {
                hasItems = true;
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
            }
        }

        if (!hasItems)
        {
            _logger.LogWarning("CloneFromGmAsync: GM has no merchant items staged");
            return null;
        }

        bot.Trade.MerchantState = MerchantMode.Selling;
        bot.Trade.PremiumMerchant = gmSession.Trade.PremiumMerchant;
        bot.Trade.MerchantAdvert = string.IsNullOrWhiteSpace(advertMessage) ? $"{gmSession.Name}'s Store" : advertMessage;
        bot.IsSitting = true;

        // Register in session & region managers
        _sessionManager.RegisterSession(bot);
        _sessionManager.Regions.AddToRegion(bot);
        _activeBots[bot.CharacterId] = bot;

        // Broadcast UserInOut to surrounding players
        var inPacket = BuildUserInOutPacket(bot, InOutType.In);
        await _sessionManager.Regions.SendToRegion(bot, inPacket, excludeSender: true);

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

        await _sessionManager.Regions.SendToRegion(bot, stallPacket, excludeSender: true);

        _logger.LogInformation("Merchant bot '{BotName}' (ID {BotId}) spawned at ({X:F1}, {Z:F1})",
            bot.Name, bot.CharacterId, bot.X, bot.Z);

        await SendNoticeAsync(gmSession,
            $"[Bot Merchant] Cloned stall to bot '{bot.Name}' at ({bot.X:F1}, {bot.Z:F1})! Type +savemerchantbots to save.");

        return bot;
    }

    public async Task<int> SaveActiveBotsAsync(UserSession? gmSession = null)
    {
        if (_activeBots.IsEmpty)
        {
            if (gmSession != null)
                await SendNoticeAsync(gmSession, "[Bot Merchant] No active merchant bots to save.");
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var count = 0;
        var savedEntries = new List<(BotSession Bot, BotMerchantData Data)>();

        foreach (var bot in _activeBots.Values)
        {
            var items = new List<StoredBotItem>();
            for (byte i = 0; i < bot.Trade.MerchantItems.Length; i++)
            {
                var it = bot.Trade.MerchantItems[i];
                if (it != null && !it.IsEmpty)
                {
                    items.Add(new StoredBotItem
                    {
                        Slot = i,
                        ItemId = it.ItemId,
                        Count = (ushort)it.Count,
                        Price = it.Price,
                        Durability = it.Durability,
                    });
                }
            }

            var visuals = new List<StoredBotVisual>();
            for (byte slot = 0; slot < InventoryConstants.VisualSlots.Length; slot++)
            {
                var eq = bot.GetEquippedItem(slot);
                if (!eq.IsEmpty)
                {
                    visuals.Add(new StoredBotVisual
                    {
                        Slot = slot,
                        ItemId = eq.ItemId,
                        Durability = eq.Durability,
                        Flag = eq.Flag,
                    });
                }
            }

            var itemsJson = JsonSerializer.Serialize(items);
            var eqJson = JsonSerializer.Serialize(visuals);

            BotMerchantData data;
            if (bot.BotDatabaseId.HasValue)
            {
                var existing = await db.BotMerchants.FindAsync(bot.BotDatabaseId.Value);
                if (existing != null)
                {
                    data = existing;
                }
                else
                {
                    data = new BotMerchantData { CreatedAt = DateTime.UtcNow };
                    db.BotMerchants.Add(data);
                }
            }
            else
            {
                data = new BotMerchantData { CreatedAt = DateTime.UtcNow };
                db.BotMerchants.Add(data);
            }

            data.BotName = bot.Name;
            data.Nation = bot.Nation;
            data.Race = bot.Race;
            data.Class = bot.Class;
            data.Face = bot.Face;
            data.Hair = (byte)bot.Hair;
            data.Level = bot.Level;
            data.ZoneId = bot.ZoneId;
            data.X = bot.X;
            data.Y = bot.Y;
            data.Z = bot.Z;
            data.Direction = bot.Direction;
            data.MerchantType = bot.Trade.PremiumMerchant ? MerchantType.PremiumSelling : MerchantType.Selling;
            data.StallTitle = bot.Trade.MerchantAdvert;
            data.ItemsJson = itemsJson;
            data.EquipmentJson = eqJson;
            data.IsActive = true;

            savedEntries.Add((bot, data));
            count++;
        }

        await db.SaveChangesAsync();

        foreach (var (bot, data) in savedEntries)
        {
            bot.BotDatabaseId = data.Id;
        }

        if (gmSession != null)
            await SendNoticeAsync(gmSession,
                $"[Bot Merchant] Successfully saved {count} merchant bots to database!");

        return count;
    }

    public async Task<int> LoadAllBotsAsync(UserSession? gmSession = null)
    {
        await ClearAllBotsAsync(gmSession: null);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var list = await db.BotMerchants.Where(b => b.IsActive).ToListAsync();

        var spawned = 0;
        foreach (var data in list)
        {
            var botId = System.Threading.Interlocked.Increment(ref _nextBotCharacterId);
            var bot = BotSession.Create(
                botId,
                data.BotName,
                data.Nation,
                data.Race,
                data.Class,
                data.Level > 0 ? data.Level : BotSession.DefaultBotLevel,
                data.Face,
                data.Hair,
                data.ZoneId,
                data.X,
                data.Y,
                data.Z,
                data.Direction);

            bot.BotDatabaseId = data.Id > 0 ? data.Id : null;

            // Restore equipment
            if (!string.IsNullOrEmpty(data.EquipmentJson))
            {
                try
                {
                    var visuals = JsonSerializer.Deserialize<List<StoredBotVisual>>(data.EquipmentJson);
                    if (visuals != null)
                    {
                        foreach (var v in visuals)
                        {
                            if (v.Slot < bot.Inventory.Length)
                            {
                                bot.Inventory[v.Slot].ItemId = v.ItemId;
                                bot.Inventory[v.Slot].Durability = (short)v.Durability;
                                bot.Inventory[v.Slot].Flag = v.Flag;
                                bot.Inventory[v.Slot].Count = 1;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize bot equipment");
                }
            }

            // Restore merchant items
            if (!string.IsNullOrEmpty(data.ItemsJson))
            {
                try
                {
                    var items = JsonSerializer.Deserialize<List<StoredBotItem>>(data.ItemsJson);
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
                    _logger.LogWarning(ex, "Failed to deserialize bot merchant items");
                }
            }

            bot.Trade.MerchantState = MerchantMode.Selling;
            bot.Trade.MerchantAdvert = data.StallTitle;
            bot.Trade.PremiumMerchant = data.MerchantType == MerchantType.PremiumSelling;
            bot.IsSitting = true;

            // Register
            _sessionManager.RegisterSession(bot);
            _sessionManager.Regions.AddToRegion(bot);
            _activeBots[bot.CharacterId] = bot;

            // Broadcast InOut
            var inPacket = BuildUserInOutPacket(bot, InOutType.In);
            await _sessionManager.Regions.SendToRegion(bot, inPacket, excludeSender: true);

            // Broadcast Stall
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

            await _sessionManager.Regions.SendToRegion(bot, stallPacket, excludeSender: true);

            spawned++;
        }

        _logger.LogInformation("Loaded and spawned {Count} merchant bots", spawned);
        if (gmSession != null)
            await SendNoticeAsync(gmSession,
                $"[Bot Merchant] Loaded and spawned {spawned} merchant bots from database.");

        return spawned;
    }

    public async Task ClearAllBotsAsync(UserSession? gmSession = null)
    {
        var count = _activeBots.Count;
        foreach (var bot in _activeBots.Values)
        {
            var closePacket = MerchantPacketWriter.StallClosed(bot.CharacterId);
            await _sessionManager.Regions.SendToRegion(bot, closePacket, excludeSender: true);

            var outPacket = VisibilityPacketWriter.UserOut(bot.CharacterId);
            await _sessionManager.Regions.SendToRegion(bot, outPacket, excludeSender: true);

            _sessionManager.RemoveSession(bot);
        }

        _activeBots.Clear();

        if (gmSession != null)
            await SendNoticeAsync(gmSession,
                $"[Bot Merchant] Cleared {count} active merchant bots.");
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

    private async Task<string> AcquireBotNameAsync(AccountNation nation, string fallbackBaseName, int botId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var onlineNames = new HashSet<string>(_sessionManager.GetAll().Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

            var availableBots = await db.UserBots
                .AsNoTracking()
                .Where(b => b.IsActive && (b.Nation == AccountNation.None || b.Nation == nation))
                .OrderBy(b => b.Id)
                .ToListAsync();

            foreach (var candidate in availableBots)
            {
                if (onlineNames.Contains(candidate.Name))
                    continue;

                var realNameTaken = await db.Characters.AnyAsync(c => c.Name == candidate.Name);
                if (!realNameTaken)
                {
                    return candidate.Name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query UserBots table for bot name, using fallback");
        }

        var baseName = fallbackBaseName.Length > 13 ? fallbackBaseName[..13] : fallbackBaseName;
        return $"[B]{baseName}_{botId % 1000}";
    }

    private class StoredBotItem
    {
        public byte Slot { get; set; }
        public int ItemId { get; set; }
        public ushort Count { get; set; }
        public int Price { get; set; }
        public short Durability { get; set; }
    }

    private class StoredBotVisual
    {
        public byte Slot { get; set; }
        public int ItemId { get; set; }
        public short Durability { get; set; }
        public byte Flag { get; set; }
    }
}
