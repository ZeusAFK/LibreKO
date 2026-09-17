using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0060
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Scripting;

public class ScriptItemService(
    UserSession session,
    IGameDataService gameData,
    List<Packet> queuedPackets,
    ILogger logger,
    ScriptCharacterService characterService,
    QuestScriptContext context)
{
    private const ushort MaxItemCount = 9999;

    public bool HasCoins(int _uid, int amount) => session.Money >= amount;

    public bool HasLoyalty(int _uid, int amount) => session.Loyalty >= amount;

    public int HowmuchItem(int _uid, int itemId)
    {
        if (itemId == InventoryConstants.ItemGold)
            return session.Money;
        if (itemId == InventoryConstants.ItemQuestCount || itemId == InventoryConstants.ItemLadderPoint)
            return session.Loyalty;
        // ITEM_HUNT and ITEM_CHAT are gates that quest scripts use as
        // "always-true" preconditions. Treating them as inventory lookups
        // returns 0 and silently fails the gate.
        if (itemId == InventoryConstants.ItemHunt || itemId == InventoryConstants.ItemChat)
            return int.MaxValue;

        var total = 0;
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length; index++)
        {
            if (session.Inventory[index].ItemId == itemId)
                total += session.Inventory[index].Count;
        }

        return total;
    }

    public bool CheckExistItem(int uid, int itemId, int requiredCount) =>
        HowmuchItem(uid, itemId) >= requiredCount;

    public int IsRoomForItem(int _uid, int itemId, int stackSize = 1)
    {
        if (stackSize <= 0)
            return -1;

        if (itemId == InventoryConstants.ItemGold)
            return InventoryConstants.InventoryStart;

        return session.FindSlotForItem(itemId, gameData, (ushort)Math.Min(stackSize, ushort.MaxValue));
    }

    public bool CheckGiveSlot(int _uid, int requiredSlots)
    {
        if (requiredSlots <= 0)
            return true;

        if (session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering)
            return false;

        var freeSlots = 0;
        for (var index = InventoryConstants.InventoryStart;
             index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
             index++)
        {
            if (!session.Inventory[index].IsEmpty)
                continue;

            freeSlots++;
            if (freeSlots >= requiredSlots)
                break;
        }

        if (freeSlots >= requiredSlots)
            return true;

        queuedPackets.Add(QuestPacketWriter.RewardRefused(QuestRewardRefusal.InventoryFull));
        return false;
    }

    public int GetCountEmptySlot(int _uid)
    {
        var freeSlots = 0;
        for (var index = InventoryConstants.InventoryStart;
             index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
             index++)
        {
            if (session.Inventory[index].IsEmpty)
                freeSlots++;
        }

        return freeSlots;
    }

    public bool GiveItem(int _uid, int itemId, int count, int rentalHours = 0)
    {
        // Once something in this script run failed, the rest of the transaction must not proceed:
        // the retail scripts have no rollback, so a half-applied reward is worse than none.
        if (count <= 0 || context.ActionFailed)
            return false;

        if (TryApplyVirtualReward(_uid, itemId, count))
            return true;

        if (itemId == InventoryConstants.ItemGold)
        {
            GoldGain(_uid, count);
            return true;
        }

        var itemData = gameData.GetItem(itemId);
        if (itemData == null)
            return false;

        var slotIndex = session.FindSlotForItem(itemId, gameData, (ushort)Math.Min(count, ushort.MaxValue));
        if (slotIndex < 0)
        {
            context.FailAction("You need a free inventory slot for the reward.");
            return false;
        }

        var slot = session.Inventory[slotIndex];
        var isNewItem = slot.IsEmpty;

        if (isNewItem)
        {
            slot.ItemId = itemId;
            slot.Durability = itemData.Duration;
            slot.Count = 0;
            slot.Flag = 0;
            slot.ExpiresAt = 0;
        }

        slot.Count = (ushort)Math.Min(MaxItemCount, slot.Count + count);
        slot.Durability = itemData.Duration;
        slot.ExpireInHours(rentalHours, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (itemData.Kind == 255)
            slot.Count = (ushort)Math.Min(MaxItemCount, itemData.Duration);

        QueueStackChange((byte)slotIndex, slot.ItemId, slot.Count, slot.Durability, isNewItem);
        QueueWeightChange();
        return true;
    }

    public bool RobItem(int _uid, int itemId, int count)
    {
        if (count <= 0 || context.ActionFailed)
            return false;

        if (itemId == InventoryConstants.ItemQuestCount || itemId == InventoryConstants.ItemLadderPoint)
        {
            if (session.Loyalty < count)
            {
                context.FailAction("You do not have enough National Points.");
                return false;
            }

            characterService.RobLoyalty(_uid, count);
            return true;
        }

        if (itemId == InventoryConstants.ItemGold)
        {
            if (session.Money < count)
            {
                context.FailAction("You do not have enough coins.");
                return false;
            }

            GoldLose(_uid, count);
            return true;
        }

        var itemData = gameData.GetItem(itemId);
        if (itemData == null)
            return false;

        var remaining = count;
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length && remaining > 0; index++)
        {
            var slot = session.Inventory[index];
            if (slot.ItemId != itemId)
                continue;

            if (itemData.Kind == 255 && slot.Durability > 0)
            {
                var taken = Math.Min(slot.Durability, remaining);
                slot.Durability -= (short)taken;
                slot.Count = (ushort)Math.Max(0, (int)slot.Durability);
                remaining -= taken;
            }
            else
            {
                var taken = Math.Min(slot.Count, remaining);
                slot.Count -= (ushort)taken;
                remaining -= taken;
            }

            if (slot.Count == 0)
                slot.Clear();

            QueueStackChange((byte)index, slot.ItemId, slot.Count, slot.Durability);
        }

        if (remaining > 0)
        {
            context.FailAction("You do not have the items this quest asks for.");
            return false;
        }

        QueueWeightChange();
        return true;
    }

    public void GoldGain(int _uid, int amount)
    {
        session.Money = Math.Min(session.Money + amount, int.MaxValue);
        QueueGoldChange(1, amount);
    }

    public void GoldLose(int _uid, int amount)
    {
        if (context.ActionFailed)
            return;

        if (amount > session.Money)
        {
            context.FailAction("You do not have enough coins.");
            return;
        }

        session.Money -= amount;
        QueueGoldChange(2, amount);
    }

    public void GiveCash(int _uid, int amount)
    {
        if (amount == 0) return;
        session.KnightCash = (int)Math.Clamp((long)session.KnightCash + amount, 0L, int.MaxValue);
    }

    public void GiveBalance(int _uid, int amount) => GiveCash(_uid, amount);

    public bool CheckExchange(int uid, int exchangeIndex)
    {
        var exchange = gameData.GetItemExchange(exchangeIndex);
        if (exchange == null)
            return false;

        foreach (var (itemId, count) in exchange.GetOriginItems())
        {
            if (itemId == 0 || count <= 0)
                continue;

            if (!CheckExistItem(uid, itemId, count))
                return false;
        }

        return TryResolveRewards(exchange, out _);
    }

    public bool RunExchange(int uid, int exchangeIndex)
    {
        var exchange = gameData.GetItemExchange(exchangeIndex);
        if (exchange == null || !TryResolveRewards(exchange, out var rewards))
            return false;

        var originItems = exchange.GetOriginItems()
            .Where(entry => entry.itemId != 0 && entry.count > 0)
            .ToArray();

        foreach (var (itemId, count) in originItems)
        {
            if (!CheckExistItem(uid, itemId, count))
                return false;
        }

        foreach (var (itemId, count) in originItems)
        {
            if (!RobItem(uid, itemId, count))
                return false;
        }

        foreach (var (itemId, count) in rewards)
        {
            if (!GiveItem(uid, itemId, count))
                return false;
        }

        return true;
    }

    public bool ApplyScriptReward(
        IReadOnlyList<(int itemId, int count)> take,
        IReadOnlyList<(int itemId, int count, int rentalHours)> give)
    {
        var granted = give.Select(e => (e.itemId, e.count)).ToArray();
        if (context.ActionFailed || take.Count + give.Count == 0
            || give.Any(e => e.itemId is not (InventoryConstants.ItemGold or InventoryConstants.ItemExperience
                or InventoryConstants.ItemQuestCount or InventoryConstants.ItemLadderPoint) && gameData.GetItem(e.itemId) is null)
            || take.Concat(granted).Any(e => e.itemId <= 0 || e.count <= 0)
            || !CanInsertRewards(granted))
            return false;
        foreach (var group in take.GroupBy(e => e.itemId == InventoryConstants.ItemQuestCount
                     ? InventoryConstants.ItemLadderPoint : e.itemId))
        {
            var total = group.Sum(e => (long)e.count);
            if (total > int.MaxValue)
                return false;
            var available = group.Key switch
            {
                InventoryConstants.ItemGold => session.Money,
                InventoryConstants.ItemQuestCount or InventoryConstants.ItemLadderPoint => session.Loyalty,
                _ => session.Inventory.Skip(InventoryConstants.InventoryStart)
                    .Where(slot => slot.ItemId == group.Key)
                    .Sum(slot => gameData.GetItem(group.Key)?.Kind == 255 && slot.Durability > 0
                        ? (long)slot.Durability : slot.Count)
            };
            if (available < total || (group.Key is not (InventoryConstants.ItemGold
                or InventoryConstants.ItemQuestCount or InventoryConstants.ItemLadderPoint)
                && gameData.GetItem(group.Key) is null))
                return false;
        }
        foreach (var currency in new[] { InventoryConstants.ItemGold, InventoryConstants.ItemLadderPoint })
        {
            bool Matches(int item) => item == currency
                || currency == InventoryConstants.ItemLadderPoint && item == InventoryConstants.ItemQuestCount;
            var gained = give.Where(e => Matches(e.itemId)).Sum(e => (long)e.count);
            var spent = take.Where(e => Matches(e.itemId)).Sum(e => (long)e.count);
            var balance = currency == InventoryConstants.ItemGold ? session.Money : session.Loyalty;
            if (balance - spent + gained > int.MaxValue
                || currency == InventoryConstants.ItemLadderPoint && session.MonthlyLoyalty + gained > int.MaxValue)
                return false;
        }
        foreach (var (itemId, count) in take)
            if (!RobItem(0, itemId, count))
                return false;
        foreach (var (itemId, count, rentalHours) in give)
            if (!GiveItem(0, itemId, count, rentalHours))
                return false;
        return true;
    }

    public bool RunQuestExchange(int uid, int exchangeIndex, int selectedAward = -1)
    {
        var exchange = gameData.GetItemExchange(exchangeIndex);
        if (exchange == null || !TryResolveQuestRewards(exchange, selectedAward, out var rewards))
            return false;

        var originItems = exchange.GetOriginItems()
            .Where(entry => entry.itemId != 0 && entry.count > 0)
            .ToArray();

        foreach (var (itemId, count) in originItems)
        {
            if (!HasQuestOriginRequirement(uid, itemId, count))
                return false;
        }

        foreach (var (itemId, count) in originItems)
        {
            if (!ConsumeQuestOriginRequirement(uid, itemId, count))
                return false;
        }

        foreach (var (itemId, count) in rewards)
        {
            if (!GiveItem(uid, itemId, count))
                return false;
        }

        return true;
    }

    public bool GenieExchange(int _uid, int itemId, int hours)
    {
        if (itemId <= 0 || hours <= 0 || !CheckExistItem(0, itemId, 1) || !RobItem(0, itemId, 1))
            return false;

        var now = DateTime.UtcNow;
        var standing = session.GenieExpiry > now ? session.GenieExpiry!.Value : now;
        session.GenieExpiry = standing.AddHours(hours);
        return true;
    }

    public bool RunMiningExchange(int uid, int oreType)
    {
        const short pitmanNpcId = 31511;
        var entries = gameData.MiningExchangesByOreNpc[((byte)oreType, pitmanNpcId)].ToArray();
        if (entries.Length == 0)
        {
            logger.LogDebug("RunMiningExchange: no entries for oreType={OreType}", oreType);
            return false;
        }

        // each entry occupies (SuccessRate / 5) slots. Unfilled slots stay at
        // entry's item. We reproduce both behaviors.
        const int wheelSize = 10000;
        var wheel = new int[wheelSize];
        var offset = 0;
        foreach (var entry in entries)
        {
            if (offset >= wheelSize) break;
            var slots = Math.Max(0, entry.SuccessRate / 5);
            var take = Math.Min(slots, wheelSize - offset);
            for (var i = 0; i < take; i++)
                wheel[offset + i] = entry.GiveItemNum;
            offset += take;
        }

        var fallbackItemId = entries[0].GiveItemNum;
        var rewardItemId = wheel[Random.Shared.Next(0, wheelSize)];
        if (rewardItemId == 0)
            rewardItemId = fallbackItemId;

        var matchingEntry = entries.FirstOrDefault(e => e.GiveItemNum == rewardItemId) ?? entries[0];
        var originItemId = matchingEntry.OriginItemNum;
        var giveCount = matchingEntry.GiveItemCount > 0 ? (int)matchingEntry.GiveItemCount : 1;

        if (gameData.GetItem(rewardItemId) == null)
        {
            logger.LogWarning("RunMiningExchange: reward item {ItemId} missing from item table", rewardItemId);
            return false;
        }

        if (FindEmptyInventorySlot(rewardItemId) < 0)
            return false;

        // Consume one ore, grant the reward.
        if (!ConsumeQuestOriginRequirement(uid, originItemId, 1))
            return false;
        if (!GiveItem(uid, rewardItemId, giveCount))
            return false;

        if (matchingEntry.GiveEffect == 1)
        {
            const ushort miningResultSuccess = 1;
            const byte miningAttempt = 2;
            const ushort effectItem = 13081;
            var pkt = MiningPacketWriter.AttemptSucceeded(
                miningAttempt, miningResultSuccess, session.CharacterId, effectItem);
            queuedPackets.Add(pkt);
        }

        logger.LogInformation("Mining exchange: {Name} ore={OreType} {Origin}→{Reward}×{Count}",
            session.Name, oreType, originItemId, rewardItemId, giveCount);
        return true;
    }

    private int FindEmptyInventorySlot(int rewardItemId)
    {
        var itemData = gameData.GetItem(rewardItemId);
        var stackable = itemData?.Countable == 1;
        for (var i = InventoryConstants.InventoryStart;
             i < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
             i++)
        {
            var slot = session.Inventory[i];
            if (slot.IsEmpty) return i;
            if (stackable && slot.ItemId == rewardItemId && slot.Count < MaxItemCount) return i;
        }
        return -1;
    }

    public bool RunCountExchange(int uid, int exchangeIndex, int count)
    {
        if (count <= 0)
            return false;

        for (var index = 0; index < count; index++)
        {
            if (!RunExchange(uid, exchangeIndex))
                return false;
        }

        return true;
    }

    public int GetMaxExchange(int uid, int exchangeIndex)
    {
        var exchange = gameData.GetItemExchange(exchangeIndex);
        if (exchange == null)
            return 0;

        var byOriginItems = int.MaxValue;
        foreach (var (itemId, count) in exchange.GetOriginItems())
        {
            if (itemId == 0 || count <= 0)
                continue;

            byOriginItems = Math.Min(byOriginItems, HowmuchItem(uid, itemId) / count);
        }

        var rewardWeight = 0;
        foreach (var (itemId, _) in exchange.GetExchangeItems())
        {
            if (itemId == 0 || itemId == InventoryConstants.ItemGold)
                continue;

            var itemData = gameData.GetItem(itemId);
            if (itemData != null)
                rewardWeight += itemData.Weight;
        }

        var byWeight = rewardWeight > 0
            ? Math.Max(0, (session.Stats.MaxWeight - session.Stats.ItemWeight) / rewardWeight)
            : int.MaxValue;

        var result = Math.Min(byOriginItems, byWeight);
        return result == int.MaxValue ? 0 : result;
    }

    private bool TryResolveRewards(ItemExchangeData exchange, out List<(int itemId, int count)> rewards)
    {
        rewards = [];

        if (exchange.RandomFlag > ItemExchangeFlags.Highest)
        {
            logger.LogWarning(
                "Exchange {ExchangeIndex} uses unsupported random flag {RandomFlag}",
                exchange.Index,
                exchange.RandomFlag);
            return false;
        }

        var slots = exchange.GetExchangeItems();
        if (ItemExchangeFlags.ExchangeGivesEveryReward(exchange.RandomFlag))
        {
            rewards.AddRange(slots.Where(entry => entry.itemId != 0 && entry.count > 0));
        }
        else if (exchange.RandomFlag == ItemExchangeFlags.WeightedPickOne)
        {
            if (!TryAddWeightedReward(exchange.Index, slots, rewards))
                return false;
        }
        else
        {
            var slot = Random.Shared.Next(0, ItemExchangeFlags.RollScale * exchange.RandomFlag + 1)
                / ItemExchangeFlags.RollScale;
            if (slot == ItemExchangeFlags.RewardSlotCount)
                slot = ItemExchangeFlags.RewardSlotCount - 1;
            if (slot < ItemExchangeFlags.RewardSlotCount
                && slots[slot].itemId != 0
                && slots[slot].count > 0)
                rewards.Add(slots[slot]);
        }

        return rewards.Count == 0 || CanInsertRewards(rewards);
    }

    private bool TryResolveQuestRewards(ItemExchangeData exchange, int selectedAward, out List<(int itemId, int count)> rewards)
    {
        rewards = [];

        if (exchange.RandomFlag > ItemExchangeFlags.Highest)
        {
            logger.LogWarning(
                "Quest exchange {ExchangeIndex} uses unsupported random flag {RandomFlag}",
                exchange.Index,
                exchange.RandomFlag);
            return false;
        }

        if (!TryCollectQuestRewards(
                exchange.Index, exchange.RandomFlag, exchange.GetExchangeItems(), selectedAward, rewards))
            return false;

        if (exchange.RandomFlag != ItemExchangeFlags.WeightedPickOne)
        {
            var bonus = gameData.GetItemExchangeExp(exchange.Index);
            if (bonus != null
                && !TryCollectQuestRewards(
                    exchange.Index, bonus.RandomFlag, bonus.GetExchangeItems(), selectedAward, rewards))
                return false;
        }

        return rewards.Count > 0 && CanInsertRewards(rewards);
    }

    private bool TryCollectQuestRewards(
        int exchangeIndex,
        byte flag,
        (int itemId, int count)[] slots,
        int selectedAward,
        List<(int itemId, int count)> rewards)
    {
        if (ItemExchangeFlags.QuestGivesEveryReward(flag))
        {
            rewards.AddRange(slots.Where(entry => entry.itemId != 0 && entry.count > 0));
            return true;
        }

        if (ItemExchangeFlags.LetsPlayerPick(flag))
        {
            if (selectedAward < 0)
                return true;

            if (selectedAward >= slots.Length)
            {
                logger.LogWarning(
                    "Quest exchange {ExchangeIndex} selected reward slot {SelectedReward} is out of range",
                    exchangeIndex,
                    selectedAward);
                return false;
            }

            var chosen = slots[selectedAward];
            if (chosen.itemId == 0 || chosen.count <= 0)
            {
                logger.LogWarning(
                    "Quest exchange {ExchangeIndex} selected reward slot {SelectedReward} is empty",
                    exchangeIndex,
                    selectedAward);
                return false;
            }

            rewards.Add(chosen);
            return true;
        }

        if (ItemExchangeFlags.IsPremiumExperience(flag))
        {
            var slot = session.PremiumType > 0
                ? ItemExchangeFlags.PremiumExperienceSlot
                : ItemExchangeFlags.BaseExperienceSlot;
            if (slot < slots.Length
                && slots[slot].itemId == InventoryConstants.ItemExperience
                && slots[slot].count > 0)
                rewards.Add(slots[slot]);
            return true;
        }

        if (flag == ItemExchangeFlags.WeightedPickOne)
            return TryAddWeightedReward(exchangeIndex, slots, rewards);

        logger.LogWarning(
            "Quest exchange {ExchangeIndex} uses unhandled random flag {RandomFlag}",
            exchangeIndex,
            flag);
        return true;
    }

    private bool TryAddWeightedReward(
        int exchangeIndex,
        (int itemId, int count)[] slots,
        List<(int itemId, int count)> rewards)
    {
        var totalRate = slots.Sum(entry => Math.Max(0, entry.count));
        if (totalRate > ItemExchangeFlags.WeightedRateTotal)
        {
            logger.LogWarning(
                "Exchange {ExchangeIndex} has invalid weighted rates totaling {TotalPercent}",
                exchangeIndex,
                totalRate);
            return false;
        }

        if (totalRate == 0)
            return false;

        var roll = Random.Shared.Next(ItemExchangeFlags.WeightedRateTotal);
        var offset = 0;
        foreach (var (itemId, count) in slots)
        {
            if (itemId == 0 || count <= 0)
                continue;

            offset += count;
            if (roll >= offset)
                continue;

            rewards.Add((itemId, ItemExchangeFlags.WeightedRewardCount));
            return true;
        }

        var (fallbackId, _) = slots.FirstOrDefault(entry => entry.itemId != 0 && entry.count > 0);
        if (fallbackId != 0)
            rewards.Add((fallbackId, ItemExchangeFlags.WeightedRewardCount));
        return true;
    }

    private bool HasQuestOriginRequirement(int uid, int itemId, int count)
    {
        if (count <= 0 || itemId == 0 || IsQuestVirtualOrigin(itemId))
            return true;

        if (itemId == InventoryConstants.ItemGold)
            return session.Money >= count;

        if (itemId == InventoryConstants.ItemQuestCount || itemId == InventoryConstants.ItemLadderPoint)
            return session.Loyalty >= count;

        return CheckExistItem(uid, itemId, count);
    }

    private bool ConsumeQuestOriginRequirement(int uid, int itemId, int count)
    {
        if (count <= 0 || itemId == 0 || IsQuestVirtualOrigin(itemId))
            return true;

        if (itemId == InventoryConstants.ItemGold)
        {
            GoldLose(uid, count);
            return true;
        }

        if (itemId == InventoryConstants.ItemQuestCount || itemId == InventoryConstants.ItemLadderPoint)
            return RobItem(uid, itemId, count);

        return RobItem(uid, itemId, count);
    }

    private static bool IsQuestVirtualOrigin(int itemId)
    {
        return Array.IndexOf(InventoryConstants.QuestVirtualRewardIds, itemId) >= 0;
    }

    private bool TryApplyVirtualReward(int uid, int itemId, int count)
    {
        switch (itemId)
        {
            case InventoryConstants.ItemGold:
                GoldGain(uid, count);
                return true;

            case InventoryConstants.ItemExperience:
                characterService.ExpChange(uid, count);
                return true;

            case InventoryConstants.ItemQuestCount:
            case InventoryConstants.ItemLadderPoint:
                characterService.GiveLoyalty(uid, count);
                return true;

            default:
                return false;
        }
    }

    private bool CanInsertRewards(IEnumerable<(int itemId, int count)> rewards)
    {
        var snapshot = session.Inventory
            .Select(slot => new ItemSlot
            {
                ItemId = slot.ItemId,
                Count = slot.Count,
                Durability = slot.Durability,
                Flag = slot.Flag
            })
            .ToArray();

        foreach (var (itemId, count) in rewards)
        {
            if (TryInsertItem(snapshot, itemId, count, applyChanges: true) < 0)
                return false;
        }

        return true;
    }

    private int TryInsertItem(ItemSlot[] inventory, int itemId, int count, bool applyChanges)
    {
        if ((itemId == InventoryConstants.ItemGold || itemId == InventoryConstants.ItemExperience || itemId == InventoryConstants.ItemQuestCount || itemId == InventoryConstants.ItemLadderPoint) && count > 0)
            return 0;

        var itemData = gameData.GetItem(itemId);
        if (itemData == null || count <= 0)
            return -1;

        if (itemData.Countable != 0)
        {
            for (var index = InventoryConstants.InventoryStart;
                 index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
                 index++)
            {
                var slot = inventory[index];
                if (slot.ItemId != itemId)
                    continue;

                var newCount = slot.Count + count;
                if (newCount > MaxItemCount)
                    continue;

                if (applyChanges)
                    slot.Count = (ushort)newCount;

                return index;
            }
        }

        for (var index = InventoryConstants.InventoryStart;
             index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax;
             index++)
        {
            var slot = inventory[index];
            if (!slot.IsEmpty)
                continue;

            if (applyChanges)
            {
                slot.ItemId = itemId;
                slot.Count = (ushort)count;
                slot.Durability = itemData.Duration;
                slot.Flag = 0;
            }

            return index;
        }

        return -1;
    }

    private void QueueGoldChange(byte changeType, int amount)
    {
        queuedPackets.Add(GoldChangePacketWriter.Change(changeType, amount, session.Money));
    }

    private void QueueStackChange(byte position, int itemId, ushort count, short durability, bool isNewItem = false)
    {
        var normalizedPos = position >= InventoryConstants.InventoryStart
            ? (byte)(position - InventoryConstants.InventoryStart)
            : position;

        var packet = new ItemCountChangePacketWriter()
            .Add(normalizedPos, itemId, count, durability, isNewItem)
            .Build();
        queuedPackets.Add(packet);
    }

    private void QueueWeightChange()
    {
        var coefficient = gameData.GetCoefficient(session.Class);
        if (coefficient != null)
            session.RecalculateStats(coefficient, gameData);

        var packet = ProgressionPacketWriter.WeightChange(session.Stats.ItemWeight);
        queuedPackets.Add(packet);
    }
}
#pragma warning restore IDE0060
