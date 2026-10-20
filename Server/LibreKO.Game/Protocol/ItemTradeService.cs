using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IItemTradeService
{
    Task HandleRepairAsync(IClient client, Packet packet);
    Task HandleTradeAsync(IClient client, Packet packet);
}

public class ItemTradeService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IKingSystemRuntimeService kingSystemRuntimeService) : IItemTradeService
{
    private const int ItemNoTrade = 900000001;
    private const int LoyaltyMerchantSellingGroup = 249000;
    private const int SaleTypeFull = 1;
    private const int SaleTypeNoRepair = 2;
    private const int ItemBaseIdStep = 1000;
    private static readonly HashSet<int> UnsellableRaces = [12, 20, 50, 70, 71, 73, 74, 76];

    public async Task HandleRepairAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0)
            return;

        var positionType = packet.ReadByte();
        var slot = packet.ReadByte();
        var npcId = packet.ReadInt();
        var itemId = packet.ReadInt();

        var npc = sessionManager.Regions.GetNpc(npcId);
        if (npc == null || !npc.IsAlive || !IsInNpcRange(session, npc) || !RepairsItems(npc))
        {
            await SendRepairResponseAsync(session, ItemRepairResult.Failed);
            return;
        }

        int absolutePosition;
        if (positionType == 1)
        {
            if (slot >= InventoryConstants.SlotMax)
            {
                await SendRepairResponseAsync(session, ItemRepairResult.Failed);
                return;
            }

            absolutePosition = slot;
        }
        else if (positionType == 2)
        {
            if (slot >= InventoryConstants.HaveMax)
            {
                await SendRepairResponseAsync(session, ItemRepairResult.Failed);
                return;
            }

            absolutePosition = InventoryConstants.InventoryStart + slot;
        }
        else
        {
            await SendRepairResponseAsync(session, ItemRepairResult.Failed);
            return;
        }

        // A charge item's durability is its uses left, so a repair would refill its charges.
        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null || itemData.Duration <= 1 || itemData.SellPrice == SaleTypeNoRepair || itemData.IsChargeItem)
        {
            await SendRepairResponseAsync(session, ItemRepairResult.Failed);
            return;
        }

        var outcome = session.WithLock(s =>
        {
            var item = s.Inventory[absolutePosition];
            if (item.ItemId != itemId)
                return (Success: false, Money: 0);

            var quantity = itemData.Duration - item.Durability;
            if (quantity <= 0)
                return (Success: false, Money: 0);

            var repairCost = (int)(((itemData.BuyPrice - 10) / 10000.0f + Math.Pow(itemData.BuyPrice, 0.75f))
                * quantity / (double)itemData.Duration);
            if (repairCost < 0)
                repairCost = 0;

            var repairDiscount = gameDataService.GetPremiumProperty(s.PremiumType, PremiumPropertyType.RepairDiscount);
            if (repairDiscount > 0)
                repairCost = repairCost * (100 - repairDiscount) / 100;

            if (s.Money < repairCost)
                return (Success: false, Money: 0);

            s.Money -= repairCost;
            item.Durability = itemData.Duration;
            return (Success: true, Money: s.Money);
        });

        if (!outcome.Success)
        {
            await SendRepairResponseAsync(session, ItemRepairResult.Failed);
            return;
        }

        await client.SendPacket(ItemRepairPacketWriter.Repaired(outcome.Money));
    }

    public async Task HandleTradeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0)
            return;

        var type = packet.ReadByte();

        if (type == (byte)ItemTradeResult.Repurchase)
        {
            await HandleRepurchaseAsync(session, client, packet);
            return;
        }

        if (type == 1 || type == 2)
        {
            var sellingGroup = packet.ReadInt();
            var npcId = packet.ReadInt();

            var npc = sessionManager.Regions.GetNpc(npcId);
            if (npc == null || !npc.IsAlive || !IsInNpcRange(session, npc)
                || !OpensShop(npc) || npc.SellingGroup != sellingGroup)
            {
                await SendItemTradeErrorAsync(session, ItemTradeRefusal.CannotTrade);
                return;
            }
            if (session.IsBusy)
            {
                await SendItemTradeErrorAsync(session, ItemTradeRefusal.CannotTrade);
                return;
            }

            var itemCount = packet.ReadByte();
            if (itemCount == 0)
            {
                await SendItemTradeErrorAsync(session, ItemTradeRefusal.CannotTrade);
                return;
            }

            var entries = new List<NpcTradeEntry>(itemCount);
            for (var index = 0; index < itemCount; index++)
            {
                var tradeItemId = packet.ReadInt();
                var tradePosition = packet.ReadByte();
                var count = packet.ReadUShort();
                byte line = 0;
                byte listIndex = 0;
                if (type == 1)
                {
                    line = packet.ReadByte();
                    listIndex = packet.ReadByte();
                }

                entries.Add(new NpcTradeEntry(tradeItemId, tradePosition, count, line, listIndex));
            }

            var result = await HandleNpcTradeAsync(session, client, npc, type, entries);
            if (result != null)
                await client.SendPacket(result);
            return;
        }

        var itemId = packet.ReadInt();
        var position = packet.ReadByte();

        if (type == 3)
        {
            var destinationPosition = packet.ReadByte();
            if (session.IsBusy
                || position >= InventoryConstants.HaveMax || destinationPosition >= InventoryConstants.HaveMax)
            {
                await SendItemTradeErrorAsync(session, ItemTradeRefusal.InventoryFull);
                return;
            }

            var sourceIndex = InventoryConstants.InventoryStart + position;
            var destinationIndex = InventoryConstants.InventoryStart + destinationPosition;
            var swapped = session.WithLock(s =>
            {
                if (s.Inventory[sourceIndex].ItemId != itemId)
                    return false;
                s.Inventory[sourceIndex].SwapWith(s.Inventory[destinationIndex]);
                return true;
            });

            if (!swapped)
            {
                await SendItemTradeErrorAsync(session, ItemTradeRefusal.InventoryFull);
                return;
            }

            var moveResult = ItemTradePacketWriter.Moved();
            await client.SendPacket(moveResult);
        }
    }

    private const short RepurchaseGenericError = -1;
    private const short RepurchaseNoRoom = -4;
    private const short RepurchaseExpired = -6;
    private const short RepurchaseNoCoins = -8;

    private async Task HandleRepurchaseAsync(UserSession session, IClient client, Packet packet)
    {
        var op = packet.ReadByte();
        if (op is ItemTradePacketWriter.RepurchaseOpen or ItemTradePacketWriter.RepurchaseRefresh)
        {
            await client.SendPacket(ItemTradePacketWriter.RepurchaseList(op, RepurchaseRows(session)));
            return;
        }
        if (op != ItemTradePacketWriter.RepurchaseBuy)
            return;

        var row = packet.ReadByte();
        var itemId = packet.ReadInt();
        var npcId = packet.ReadInt();
        var npc = sessionManager.Regions.GetNpc(npcId);
        var itemData = gameDataService.GetItem(itemId);
        if (npc == null || !npc.IsAlive || !IsInNpcRange(session, npc) || itemData == null || session.IsBusy)
        {
            await client.SendPacket(ItemTradePacketWriter.RepurchaseRefused(itemId, RepurchaseGenericError));
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var outcome = session.WithLock(s =>
        {
            if (row >= s.Repurchases.Count || s.Repurchases[row].ItemId != itemId)
                return (Error: RepurchaseGenericError, Slot: 0, Count: (ushort)0, Durability: (short)0, Flag: (byte)0, Price: 0, IsNew: false);
            var entry = s.Repurchases[row];
            if (entry.DeletesAt <= now)
                return (Error: RepurchaseExpired, Slot: 0, Count: (ushort)0, Durability: (short)0, Flag: (byte)0, Price: 0, IsNew: false);
            var price = RepurchaseRules.Price(itemData.BuyPrice, entry.Count);
            if (s.Money < price)
                return (Error: RepurchaseNoCoins, Slot: 0, Count: (ushort)0, Durability: (short)0, Flag: (byte)0, Price: 0, IsNew: false);
            var bought = new ExchangeItem
            {
                ItemId = itemId, Count = entry.Count, Durability = entry.Durability, Flag = entry.Flag,
                Countable = itemData.Countable != 0,
            };
            var slotIndex = ItemPlacement.FindForDelivery(s.Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, bought);
            if (slotIndex < 0)
                return (Error: RepurchaseNoRoom, Slot: 0, Count: (ushort)0, Durability: (short)0, Flag: (byte)0, Price: 0, IsNew: false);

            var slot = s.Inventory[slotIndex];
            var isNew = slot.IsEmpty;
            ItemPlacement.Put(slot, bought);
            s.Money -= price;
            s.Repurchases.RemoveAt(row);
            s.RecalculateStatsWithBuffs(gameDataService);
            return (Error: (short)0, Slot: slotIndex, Count: slot.Count, Durability: slot.Durability, Flag: slot.Flag, Price: price, IsNew: isNew);
        });

        if (outcome.Error != 0)
        {
            await client.SendPacket(ItemTradePacketWriter.RepurchaseRefused(itemId, outcome.Error));
            return;
        }

        await userNotificationService.SendStackChangeAsync(session, (byte)outcome.Slot, itemId, outcome.Count, outcome.Durability, outcome.IsNew);
        await userNotificationService.SendGoldLossAsync(session, outcome.Price);
        await userNotificationService.SendWeightChangeAsync(session);
        await client.SendPacket(ItemTradePacketWriter.RepurchaseBought(
            (byte)(outcome.Slot - InventoryConstants.InventoryStart), itemId, outcome.Flag));
    }

    private List<ItemTradePacketWriter.RepurchaseRow> RepurchaseRows(UserSession session)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return session.WithLock(s =>
        {
            RepurchaseRules.Prune(s.Repurchases, now);
            var rows = new List<ItemTradePacketWriter.RepurchaseRow>(s.Repurchases.Count);
            for (var i = 0; i < s.Repurchases.Count; i++)
            {
                var entry = s.Repurchases[i];
                var buyPrice = gameDataService.GetItem(entry.ItemId)?.BuyPrice ?? 0;
                rows.Add(new ItemTradePacketWriter.RepurchaseRow(
                    (byte)i, entry.ItemId, RepurchaseRules.Price(buyPrice, entry.Count), (uint)entry.DeletesAt));
            }
            return rows;
        });
    }

    private async Task<Packet?> HandleNpcTradeAsync(
        UserSession session,
        IClient client,
        NpcInstance npc,
        byte type,
        List<NpcTradeEntry> entries)
    {
        var loyaltyShop = npc.SellingGroup == LoyaltyMerchantSellingGroup;
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var tariff = type == 1 && !loyaltyShop ? kingData?.TerritoryTariff ?? 0 : 0;
        var tax = 0;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var premiumSeller = NpcSellRules.SellsAtPremium(session,
            session.KnightsId > 0 && sessionManager.Knights.GetClan(session.KnightsId)?.HasPremium == true);
        static (ItemTradeRefusal Error, int Price, int Money, int Loyalty, byte SellingGroup, bool HasItems) Refuse(
            ItemTradeRefusal reason) => (reason, 0, 0, 0, 0, false);

        // Every line is checked before any is applied, and the gold is settled once.
        var outcome = session.WithLock(s =>
        {
            if (type == 2 && loyaltyShop)
                return Refuse(ItemTradeRefusal.CannotTrade);

            var lines = new List<(NpcTradeEntry Entry, ItemData Item)>(entries.Count);
            var usedPositions = new HashSet<byte>();
            var soldFrom = new Dictionary<byte, int>();
            long totalPrice = 0;
            long totalWeight = 0;

            foreach (var entry in entries)
            {
                var itemData = gameDataService.GetItem(entry.ItemId);
                if (itemData == null
                    || entry.Position >= InventoryConstants.HaveMax
                    || entry.Count == 0
                    || entry.Count > InventoryConstants.MaxStackCount
                    || (type == 1 && !usedPositions.Add(entry.Position))
                    || (type == 1 && !IsOnShopList(npc.SellingGroup, entry))
                    || (type == 1 && itemData.Countable == 0 && entry.Count != 1)
                    || (type == 2 && (entry.ItemId >= ItemNoTrade || UnsellableRaces.Contains(itemData.Race))))
                    return Refuse(ItemTradeRefusal.CannotTrade);

                var slot = s.Inventory[InventoryConstants.InventoryStart + entry.Position];
                if (type == 1)
                {
                    if (!slot.IsEmpty)
                    {
                        if (slot.ItemId != entry.ItemId || itemData.Countable == 0 || !slot.IsPlain)
                            return Refuse(ItemTradeRefusal.CannotTrade);

                        if (slot.Count + entry.Count > InventoryConstants.MaxStackCount)
                            return Refuse(ItemTradeRefusal.InventoryFull);
                    }

                    totalPrice += (long)itemData.BuyPrice * entry.Count;
                    totalWeight += (long)itemData.Weight * entry.Count;
                }
                else
                {
                    // Lines selling from the same stack add up against what it holds.
                    var sold = soldFrom.GetValueOrDefault(entry.Position) + entry.Count;
                    if (slot.ItemId != entry.ItemId || slot.Count < sold || !slot.IsTradable || slot.IsLinked || slot.Expires)
                        return Refuse(ItemTradeRefusal.CannotTrade);

                    soldFrom[entry.Position] = sold;
                    totalPrice += (long)SellUnitPrice(itemData, premiumSeller) * entry.Count;
                }

                lines.Add((entry, itemData));
            }

            if (lines.Count == 0)
                return Refuse(ItemTradeRefusal.None);

            var due = type == 1 ? NpcTariff.WithTax(totalPrice, tariff) : totalPrice;
            if (type == 1)
            {
                if ((loyaltyShop ? s.Loyalty : s.Money) < due)
                    return Refuse(ItemTradeRefusal.NotEnoughCoins);

                if (s.Stats.ItemWeight + totalWeight > s.Stats.MaxWeight)
                    return Refuse(ItemTradeRefusal.InventoryFull);
            }
            else if (s.Money + due > ExchangePacketConstants.CoinMax)
            {
                return Refuse(ItemTradeRefusal.CannotTrade);
            }

            foreach (var (entry, itemData) in lines)
            {
                var slot = s.Inventory[InventoryConstants.InventoryStart + entry.Position];
                if (type == 1)
                {
                    // An empty slot gets its count set, never added to whatever it held before.
                    if (slot.IsEmpty)
                    {
                        slot.Clear();
                        slot.ItemId = entry.ItemId;
                        slot.Count = entry.Count;
                    }
                    else
                    {
                        slot.Count += entry.Count;
                    }

                    slot.Durability = itemData.Duration;
                }
                else
                {
                    RepurchaseRules.Record(s.Repurchases, slot, entry.Count, now);
                    if (entry.Count >= slot.Count)
                        slot.Clear();
                    else
                        slot.Count -= entry.Count;
                }
            }

            tax = (int)(due - totalPrice);
            var price = (int)due;
            if (type == 1 && loyaltyShop)
                s.Loyalty -= price;
            else if (type == 1)
                s.Money -= price;
            else
                s.Money += price;

            s.RecalculateStatsWithBuffs(gameDataService);

            return (Error: ItemTradeRefusal.None, Price: price, Money: s.Money, Loyalty: s.Loyalty,
                SellingGroup: lines[^1].Item.SellingGroup, HasItems: true);
        });

        if (outcome.Error != ItemTradeRefusal.None)
        {
            await SendItemTradeErrorAsync(session, outcome.Error);
            return null;
        }

        if (!outcome.HasItems)
            return null;

        await userNotificationService.SendWeightChangeAsync(session);
        if (tax > 0)
            await AccrueTariffAsync(session, kingData, tax);

        return ItemTradePacketWriter.Traded(
            loyaltyShop ? outcome.Loyalty : outcome.Money,
            outcome.Price,
            loyaltyShop ? outcome.SellingGroup : null);
    }

    private async Task AccrueTariffAsync(UserSession session, KingSystemData? kingData, int tax)
    {
        var siege = gameDataService.SiegeWarfare;
        if (session.ZoneId == (byte)ZoneId.Moradon && siege != null)
        {
            siege.MoradonTax = (int)Math.Min((long)siege.MoradonTax + tax, ExchangePacketConstants.CoinMax);
            return;
        }
        if (session.ZoneId == (byte)ZoneId.Delos && siege != null)
        {
            siege.DellosTax = (int)Math.Min((long)siege.DellosTax + tax, ExchangePacketConstants.CoinMax);
            return;
        }
        if (kingData == null || string.IsNullOrWhiteSpace(kingData.KingName))
            return;
        var (treasury, territory) = NpcTariff.KingSplit(tax);
        kingData.NationalTreasury = (int)Math.Min((long)kingData.NationalTreasury + treasury, ExchangePacketConstants.CoinMax);
        kingData.TerritoryTax = (int)Math.Min((long)kingData.TerritoryTax + territory, ExchangePacketConstants.CoinMax);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.NationalTreasury);
        await kingSystemRuntimeService.PersistKingPropertyAsync(kingData, entry => entry.TerritoryTax);
    }

    private int SellUnitPrice(ItemData itemData, bool premium)
    {
        var baseItem = gameDataService.GetItem(itemData.Num / ItemBaseIdStep * ItemBaseIdStep);
        var saleType = baseItem?.SellPrice ?? itemData.SellPrice;
        return NpcSellRules.Unit(itemData.BuyPrice, saleType == SaleTypeFull, premium);
    }

    private static bool RepairsItems(NpcInstance npc) =>
        npc.NpcType is NpcData.TypeTradeMerchant or NpcData.TypeRepairMerchant;

    private static bool OpensShop(NpcInstance npc) =>
        npc.NpcType is NpcData.TypeTradeMerchant or NpcData.TypeRepairMerchant or NpcData.TypeFamiliarTamer;

    private bool IsOnShopList(int sellingGroup, NpcTradeEntry entry) =>
        gameDataService.ItemSellsByGroup[sellingGroup]
            .Any(sold => sold.Line == entry.Line && sold.Index == entry.Index && sold.ItemId == entry.ItemId);

    private static bool IsInNpcRange(UserSession session, NpcInstance npc)
    {
        if (npc.ZoneId != session.ZoneId)
            return false;

        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq;
    }

    private static async Task SendRepairResponseAsync(UserSession session, ItemRepairResult result)
    {
        await session.Client.SendPacket(
            ItemRepairPacketWriter.Completed(result, session.Money));
    }

    private static async Task SendItemTradeErrorAsync(UserSession session, ItemTradeRefusal reason)
    {
        await session.Client.SendPacket(ItemTradePacketWriter.Failed(reason));
    }


    private readonly record struct NpcTradeEntry(int ItemId, byte Position, ushort Count, byte Line, byte Index);

}
